/*
 * @author Valentin Simonov / http://va.lent.in/
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TouchScript.Devices.Display;
using TouchScript.Hit;
using TouchScript.InputSources;
using TouchScript.Layers;
using TouchScript.Utils;
#if TOUCHSCRIPT_DEBUG
using TouchScript.Utils.Debug;
#endif
using UnityEngine;

namespace TouchScript
{
    /// <summary>
    /// Default implementation of <see cref="ITouchManager"/>.
    /// </summary>
    internal sealed class TouchManagerInstance : DebuggableMonoBehaviour, ITouchManager
    {
        #region Events

        /// <inheritdoc />
        public event EventHandler FrameStarted
        {
            add { frameStartedInvoker += value; }
            remove { frameStartedInvoker -= value; }
        }

        /// <inheritdoc />
        public event EventHandler FrameFinished
        {
            add { frameFinishedInvoker += value; }
            remove { frameFinishedInvoker -= value; }
        }

        /// <inheritdoc />
        public event EventHandler<TouchEventArgs> TouchesBegan
        {
            add { touchesBeganInvoker += value; }
            remove { touchesBeganInvoker -= value; }
        }

        /// <inheritdoc />
        public event EventHandler<TouchEventArgs> TouchesMoved
        {
            add { touchesMovedInvoker += value; }
            remove { touchesMovedInvoker -= value; }
        }

        /// <inheritdoc />
        public event EventHandler<TouchEventArgs> TouchesEnded
        {
            add { touchesEndedInvoker += value; }
            remove { touchesEndedInvoker -= value; }
        }

        /// <inheritdoc />
        public event EventHandler<TouchEventArgs> TouchesCancelled
        {
            add { touchesCancelledInvoker += value; }
            remove { touchesCancelledInvoker -= value; }
        }

        // Needed to overcome iOS AOT limitations
        private EventHandler<TouchEventArgs> touchesBeganInvoker,
            touchesMovedInvoker,
            touchesEndedInvoker,
            touchesCancelledInvoker;

        private EventHandler frameStartedInvoker, frameFinishedInvoker;

        #endregion

        #region Public properties

        /// <inheritdoc />
        public static TouchManagerInstance Instance
        {
            get
            {
                if (shuttingDown) return null;
                if (instance == null)
                {
                    if (!Application.isPlaying) return null;
                    var objects = FindObjectsOfType<TouchManagerInstance>();
                    if (objects.Length == 0)
                    {
                        var go = new GameObject("TouchManager Instance");
                        instance = go.AddComponent<TouchManagerInstance>();
                    }
                    else if (objects.Length >= 1)
                    {
                        instance = objects[0];
                    }
                }
                return instance;
            }
        }

        /// <inheritdoc />
        public IDisplayDevice DisplayDevice
        {
            get
            {
                if (displayDevice == null)
                {
                    displayDevice = ScriptableObject.CreateInstance<GenericDisplayDevice>();
                }
                return displayDevice;
            }
            set
            {
                if (value == null)
                {
                    displayDevice = ScriptableObject.CreateInstance<GenericDisplayDevice>();
                }
                else
                {
                    displayDevice = value;
                }
                updateDPI();
            }
        }

        /// <inheritdoc />
        public float DPI
        {
            get { return dpi; }
        }

        /// <inheritdoc />
        public bool ShouldCreateCameraLayer
        {
            get { return shouldCreateCameraLayer; }
            set { shouldCreateCameraLayer = value; }
        }

        /// <inheritdoc />
        public bool ShouldCreateStandardInput
        {
            get { return shouldCreateStandardInput; }
            set { shouldCreateStandardInput = value; }
        }

        /// <inheritdoc />
        public IList<TouchLayer> Layers
        {
            get
            {
                if (readonlyLayers == null) readonlyLayers = new ReadOnlyCollection<TouchLayer>(layers);
                return readonlyLayers;
            }
        }

        /// <inheritdoc />
        public IList<IInputSource> Inputs
        {
            get
            {
                if (readonlyInputs == null) readonlyInputs = new ReadOnlyCollection<IInputSource>(inputs);
                return readonlyInputs;
            }
        }

        /// <inheritdoc />
        public float DotsPerCentimeter
        {
            get { return dotsPerCentimeter; }
        }

        /// <inheritdoc />
        public int NumberOfTouches
        {
            get { return touches.Count; }
        }

        /// <inheritdoc />
        public IList<ITouch> ActiveTouches
        {
            get { return touches.Cast<ITouch>().ToList(); }
        }

        #endregion

        #region Private variables

        private static bool shuttingDown = false;
        private static TouchManagerInstance instance;
        private bool shouldCreateCameraLayer = true;
        private bool shouldCreateStandardInput = true;

        private IDisplayDevice displayDevice;
        private float dpi = 96;
        private float dotsPerCentimeter = TouchManager.CM_TO_INCH*96;

        private List<TouchLayer> layers = new List<TouchLayer>(10);
        private ReadOnlyCollection<TouchLayer> readonlyLayers;
        private List<IInputSource> inputs = new List<IInputSource>(3);
        private ReadOnlyCollection<IInputSource> readonlyInputs;

        private List<TouchPoint> touches = new List<TouchPoint>(30);
        private Dictionary<int, TouchPoint> idToTouch = new Dictionary<int, TouchPoint>(30);

        // Upcoming changes
        private List<TouchPoint> touchesBegan = new List<TouchPoint>(10);
        private HashSet<int> touchesUpdated = new HashSet<int>();
        private HashSet<int> touchesEnded = new HashSet<int>();
        private HashSet<int> touchesCancelled = new HashSet<int>();
        private List<CancelledTouch> touchesManuallyCancelled = new List<CancelledTouch>(10);

        private static ObjectPool<TouchPoint> touchPointPool = new ObjectPool<TouchPoint>(10, null, null,
            (t) => t.INTERNAL_Reset());

        private static ObjectPool<List<ITouch>> touchListPool = new ObjectPool<List<ITouch>>(2,
            () => new List<ITouch>(10), null, (l) => l.Clear());

        private static ObjectPool<List<TouchPoint>> touchPointListPool = new ObjectPool<List<TouchPoint>>(1,
            () => new List<TouchPoint>(10), null, (l) => l.Clear());

        private static ObjectPool<List<int>> intListPool = new ObjectPool<List<int>>(1, () => new List<int>(10), null,
            (l) => l.Clear());

        private static ObjectPool<List<CancelledTouch>> cancelledListPool = new ObjectPool<List<CancelledTouch>>(1,
            () => new List<CancelledTouch>(10), null, (l) => l.Clear());

        private int nextTouchId = 0;

        #endregion

        #region Public methods

        /// <inheritdoc />
        public bool AddLayer(TouchLayer layer)
        {
            if (layer == null) return false;
            if (layers.Contains(layer)) return true;
            layers.Add(layer);
            return true;
        }

        /// <inheritdoc />
        public bool AddLayer(TouchLayer layer, int index)
        {
            if (layer == null) return false;
            if (index >= layers.Count) return AddLayer(layer);
            var i = layers.IndexOf(layer);
            if (i == -1)
            {
                layers.Insert(index, layer);
            }
            else
            {
                if (index == i || i == index - 1) return true;
                layers.RemoveAt(i);
                if (index < i) layers.Insert(index, layer);
                else layers.Insert(index - 1, layer);
            }
            return true;
        }

        /// <inheritdoc />
        public bool RemoveLayer(TouchLayer layer)
        {
            if (layer == null) return false;
            var result = layers.Remove(layer);
            return result;
        }

        /// <inheritdoc />
        public void ChangeLayerIndex(int at, int to)
        {
            if (at < 0 || at >= layers.Count) return;
            if (to < 0 || to >= layers.Count) return;
            var data = layers[at];
            layers.RemoveAt(at);
            layers.Insert(to, data);
        }

        /// <inheritdoc />
        public bool AddInput(IInputSource input)
        {
            if (input == null) return false;
            if (inputs.Contains(input)) return true;
            inputs.Add(input);
            return true;
        }

        /// <inheritdoc />
        public bool RemoveInput(IInputSource input)
        {
            if (input == null) return false;
            var result = inputs.Remove(input);
            return result;
        }

        /// <inheritdoc />
        public Transform GetHitTarget(Vector2 position)
        {
            TouchHit hit;
            TouchLayer layer;
            if (GetHitTarget(position, out hit, out layer)) return hit.Transform;
            return null;
        }

        /// <inheritdoc />
        public bool GetHitTarget(Vector2 position, out TouchHit hit)
        {
            TouchLayer layer;
            return GetHitTarget(position, out hit, out layer);
        }

        /// <inheritdoc />
        public bool GetHitTarget(Vector2 position, out TouchHit hit, out TouchLayer layer)
        {
            hit = default(TouchHit);
            layer = null;

            var count = layers.Count;
            for (var i = 0; i < count; i++)
            {
                var touchLayer = layers[i];
                if (touchLayer == null) continue;
                TouchHit _hit;
                if (touchLayer.Hit(position, out _hit) == TouchLayer.LayerHitResult.Hit)
                {
                    hit = _hit;
                    layer = touchLayer;
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void CancelTouch(int id, bool redispatch)
        {
            touchesManuallyCancelled.Add(new CancelledTouch(id, redispatch));
        }

        /// <inheritdoc />
        public void CancelTouch(int id)
        {
            CancelTouch(id, false);
        }

        #endregion

        #region Internal methods

        internal ITouch INTERNAL_BeginTouch(Vector2 position)
        {
            return INTERNAL_BeginTouch(position, null);
        }

        internal ITouch INTERNAL_BeginTouch(Vector2 position, Tags tags)
        {
            TouchPoint touch;
            lock (touchesBegan)
            {
                touch = touchPointPool.Get();
                touch.INTERNAL_Init(nextTouchId++, position, tags);
                touchesBegan.Add(touch);
            }
            return touch;
        }

        /// <summary>
        /// Update touch without moving it
        /// </summary>
        /// <param name="id">Touch id</param>
        internal void INTERNAL_UpdateTouch(int id)
        {
            lock (touchesUpdated)
            {
                if (idToTouch.ContainsKey(id))
                {
                    if (!touchesUpdated.Contains(id)) touchesUpdated.Add(id);
                }
#if TOUCHSCRIPT_DEBUG
                else Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to UPDATE but no touch with such id found.");
#endif
            }
        }

        internal void INTERNAL_MoveTouch(int id, Vector2 position)
        {
            lock (touchesUpdated)
            {
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
                    // This touch was added this frame
                    touch = touchesBegan.Find((t) => t.Id == id);
                    // No touch with such id
                    if (touch == null)
                    {
#if TOUCHSCRIPT_DEBUG
                        Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to MOVE to " + position + " but no touch with such id found.");
#endif
                        return;
                    }
                }

                touch.INTERNAL_SetPosition(position);
                if (!touchesUpdated.Contains(id)) touchesUpdated.Add(id);
            }
        }

        /// <inheritdoc />
        internal void INTERNAL_EndTouch(int id)
        {
            lock (touchesEnded)
            {
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
                    // This touch was added this frame
                    touch = touchesBegan.Find((t) => t.Id == id);
                    // No touch with such id
                    if (touch == null)
                    {
#if TOUCHSCRIPT_DEBUG
                        Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to END but no touch with such id found.");
#endif
                        return;
                    }
                }
                if (!touchesEnded.Contains(id)) touchesEnded.Add(id);
#if TOUCHSCRIPT_DEBUG
                else Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to END more than once this frame.");
#endif
            }
        }

        /// <inheritdoc />
        internal void INTERNAL_CancelTouch(int id)
        {
            lock (touchesCancelled)
            {
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
                    // This touch was added this frame
                    touch = touchesBegan.Find((t) => t.Id == id);
                    // No touch with such id
                    if (touch == null)
                    {
#if TOUCHSCRIPT_DEBUG
                        Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to CANCEL but no touch with such id found.");
#endif
                        return;
                    }
                }
                if (!touchesCancelled.Contains(id)) touchesCancelled.Add(touch.Id);
#if TOUCHSCRIPT_DEBUG
                else Debug.LogWarning("TouchScript > Touch with id [" + id + "] is requested to CANCEL more than once this frame.");
#endif
            }
        }

        #endregion

        #region Unity

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
            }
            else if (instance != this)
            {
                Destroy(this);
                return;
            }

            gameObject.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(gameObject);

            updateDPI();

            StopAllCoroutines();
            StartCoroutine(lateAwake());

            touchListPool.WarmUp(2);
            touchPointListPool.WarmUp(1);
            intListPool.WarmUp(1);
            cancelledListPool.WarmUp(1);

#if TOUCHSCRIPT_DEBUG
            DebugMode = true;
#endif
        }

        private void OnLevelWasLoaded(int value)
        {
            StopAllCoroutines();
            StartCoroutine(lateAwake());
        }

        private IEnumerator lateAwake()
        {
            yield return null;

            updateLayers();
            createCameraLayer();
            createTouchInput();
        }

        private void Update()
        {
            updateInputs();
            updateTouches();
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
        }

        #endregion

        #region Private functions

        private void updateDPI()
        {
            dpi = DisplayDevice == null ? 96 : DisplayDevice.DPI;
            dotsPerCentimeter = TouchManager.CM_TO_INCH*dpi;
#if TOUCHSCRIPT_DEBUG
            debugTouchSize = Vector2.one * dotsPerCentimeter;
#endif
        }

        private void updateLayers()
        {
            // filter empty layers
            layers = layers.FindAll(l => l != null);
        }

        private void createCameraLayer()
        {
            if (layers.Count == 0 && shouldCreateCameraLayer)
            {
                if (Camera.main != null)
                {
                    if (Application.isEditor)
                        Debug.Log("[TouchScript] No camera layer found, adding CameraLayer for the main camera. (this message is harmless)");
                    var layer = Camera.main.gameObject.AddComponent<CameraLayer>();
                    AddLayer(layer);
                }
            }
        }

        private void createTouchInput()
        {
            if (inputs.Count == 0 && shouldCreateStandardInput)
            {
                if (Application.isEditor)
                    Debug.Log("[TouchScript] No input source found, adding StandardInput. (this message is harmless)");
                GameObject obj = null;
                var objects = FindObjectsOfType<TouchManager>();
                if (objects.Length == 0)
                {
                    obj = GameObject.Find("TouchScript");
                    if (obj == null) obj = new GameObject("TouchScript");
                }
                else
                {
                    obj = objects[0].gameObject;
                }
                obj.AddComponent<StandardInput>();
            }
        }

        private void updateInputs()
        {
            var count = inputs.Count;
            for (var i = 0; i < count; i++) inputs[i].UpdateInput();
        }

        private void updateBegan(List<TouchPoint> points)
        {
            var count = points.Count;
            var list = touchListPool.Get();
            var layerCount = layers.Count;
            for (var i = 0; i < count; i++)
            {
                var touch = points[i];
                list.Add(touch);
                touches.Add(touch);
                idToTouch.Add(touch.Id, touch);

                for (var j = 0; j < layerCount; j++)
                {
                    var touchLayer = Layers[j];
                    if (touchLayer == null) continue;
                    if (touchLayer.INTERNAL_BeginTouch(touch)) break;
                }

#if TOUCHSCRIPT_DEBUG
                addDebugFigureForTouch(touch);
#endif
            }

            if (touchesBeganInvoker != null)
                touchesBeganInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(list));
            touchListPool.Release(list);
        }

        private void updateUpdated(List<int> points)
        {
            var updatedCount = points.Count;
            var list = touchListPool.Get();
            // Need to loop through all touches to reset those which did not move
            var count = touches.Count;
            for (var i = 0; i < count; i++)
            {
                touches[i].INTERNAL_ResetPosition();
            }
            for (var i = 0; i < updatedCount; i++)
            {
                var id = points[i];
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
#if TOUCHSCRIPT_DEBUG
                    Debug.LogWarning("TouchScript > Id [" + id + "] was in UPDATED list but no touch with such id found.");
#endif
                    continue;
                }
                list.Add(touch);
                if (touch.Layer != null) touch.Layer.INTERNAL_UpdateTouch(touch);

#if TOUCHSCRIPT_DEBUG
                addDebugFigureForTouch(touch);
#endif
            }

            if (touchesMovedInvoker != null)
                touchesMovedInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(list));
            touchListPool.Release(list);
        }

        private void updateEnded(List<int> points)
        {
            var endedCount = points.Count;
            var list = touchListPool.Get();
            for (var i = 0; i < endedCount; i++)
            {
                var id = points[i];
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
#if TOUCHSCRIPT_DEBUG
                    Debug.LogWarning("TouchScript > Id [" + id + "] was in ENDED list but no touch with such id found.");
#endif
                    continue;
                }
                idToTouch.Remove(id);
                touches.Remove(touch);
                list.Add(touch);
                if (touch.Layer != null) touch.Layer.INTERNAL_EndTouch(touch);

#if TOUCHSCRIPT_DEBUG
                removeDebugFigureForTouch(touch);
#endif
            }

            if (touchesEndedInvoker != null)
                touchesEndedInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(list));

            for (var i = 0; i < endedCount; i++) touchPointPool.Release(list[i] as TouchPoint);
            touchListPool.Release(list);
        }

        private void updateCancelled(List<int> points)
        {
            var cancelledCount = points.Count;
            var list = touchListPool.Get();
            for (var i = 0; i < cancelledCount; i++)
            {
                var id = points[i];
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
#if TOUCHSCRIPT_DEBUG
                    Debug.LogWarning("TouchScript > Id [" + id + "] was in CANCELLED list but no touch with such id found.");
#endif
                    continue;
                }
                idToTouch.Remove(id);
                touches.Remove(touch);
                list.Add(touch);
                if (touch.Layer != null) touch.Layer.INTERNAL_CancelTouch(touch);

#if TOUCHSCRIPT_DEBUG
                removeDebugFigureForTouch(touch);
#endif
            }

            if (touchesCancelledInvoker != null)
                touchesCancelledInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(list));

            for (var i = 0; i < cancelledCount; i++) touchPointPool.Release(list[i] as TouchPoint);
            touchListPool.Release(list);
        }

        private void updateManuallyCancelled(List<CancelledTouch> points)
        {
            var cancelledCount = points.Count;
            var list = touchListPool.Get();
            var redispatchList = touchListPool.Get();
            var releaseList = touchListPool.Get();
            for (var i = 0; i < cancelledCount; i++)
            {
                var data = points[i];
                var id = data.Id;
                TouchPoint touch;
                if (!idToTouch.TryGetValue(id, out touch))
                {
                    // might be dead already
                    continue;
                }

                if (data.Redispatch)
                {
                    redispatchList.Add(touch);
                }
                else
                {
                    idToTouch.Remove(id);
                    touches.Remove(touch);
                    releaseList.Add(touch);
#if TOUCHSCRIPT_DEBUG
                removeDebugFigureForTouch(touch);
#endif
                }

                list.Add(touch);
                if (touch.Layer != null) touch.Layer.INTERNAL_CancelTouch(touch);
            }

            if (touchesCancelledInvoker != null)
                touchesCancelledInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(list));

            touchListPool.Release(list);
            var count = releaseList.Count;
            for (var i = 0; i < count; i++) touchPointPool.Release(releaseList[i] as TouchPoint);
            touchListPool.Release(releaseList);

            count = redispatchList.Count;
            if (count > 0)
            {
                var layerCount = layers.Count;
                for (var i = 0; i < count; i++)
                {
                    var touch = redispatchList[i] as TouchPoint;
                    for (var j = 0; j < layerCount; j++)
                    {
                        var touchLayer = Layers[j];
                        if (touchLayer == null) continue;
                        if (touchLayer.INTERNAL_BeginTouch(touch)) break;
                    }
                }
                if (touchesBeganInvoker != null)
                    touchesBeganInvoker.InvokeHandleExceptions(this, TouchEventArgs.GetCachedEventArgs(redispatchList));
            }
            touchListPool.Release(redispatchList);
        }

        private void updateTouches()
        {
            if (frameStartedInvoker != null) frameStartedInvoker.InvokeHandleExceptions(this, EventArgs.Empty);

            // need to copy buffers here since they might get updated during execution
            if (touchesBegan.Count > 0)
            {
                var updateList = touchPointListPool.Get();
                lock (touchesBegan)
                {
                    updateList.AddRange(touchesBegan);
                    touchesBegan.Clear();
                }
                updateBegan(updateList);
                touchPointListPool.Release(updateList);
            }

            if (touchesUpdated.Count > 0)
            {
                var updateList = intListPool.Get();
                lock (touchesUpdated)
                {
                    updateList.AddRange(touchesUpdated);
                    touchesUpdated.Clear();
                }
                updateUpdated(updateList);
                intListPool.Release(updateList);
            }

            if (touchesEnded.Count > 0)
            {
                var updateList = intListPool.Get();
                lock (touchesEnded)
                {
                    updateList.AddRange(touchesEnded);
                    touchesEnded.Clear();
                }
                updateEnded(updateList);
                intListPool.Release(updateList);
            }

            if (touchesCancelled.Count > 0)
            {
                var updateList = intListPool.Get();
                lock (touchesCancelled)
                {
                    updateList.AddRange(touchesCancelled);
                    touchesCancelled.Clear();
                }
                updateCancelled(updateList);
                intListPool.Release(updateList);
            }

            if (touchesManuallyCancelled.Count > 0)
            {
                var updateList = cancelledListPool.Get();
                lock (touchesManuallyCancelled)
                {
                    updateList.AddRange(touchesManuallyCancelled);
                    touchesManuallyCancelled.Clear();
                }
                updateManuallyCancelled(updateList);
                cancelledListPool.Release(updateList);
            }

            if (frameFinishedInvoker != null) frameFinishedInvoker.InvokeHandleExceptions(this, EventArgs.Empty);
        }

#if TOUCHSCRIPT_DEBUG
        private Vector2 debugTouchSize;

        private void removeDebugFigureForTouch(ITouch touch)
        {
            GLDebug.RemoveFigure(TouchManager.DEBUG_GL_TOUCH + touch.Id);
        }

        private void addDebugFigureForTouch(ITouch touch)
        {
            GLDebug.DrawSquareScreenSpace(TouchManager.DEBUG_GL_TOUCH + touch.Id, touch.Position, 0, debugTouchSize, GLDebug.MULTIPLY, float.PositiveInfinity);
        }
#endif

        #endregion

        #region Structs

        private struct CancelledTouch
        {
            public int Id;
            public bool Redispatch;

            public CancelledTouch(int id, bool redispatch)
            {
                Id = id;
                Redispatch = redispatch;
            }

            public CancelledTouch(int id)
            {
                Id = id;
                Redispatch = false;
            }
        }

        #endregion
    }
}