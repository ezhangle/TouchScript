﻿/*
 * @author Valentin Simonov / http://va.lent.in/
 */

using System;
using System.Collections.Generic;
using TouchScript.Devices.Display;
using TouchScript.Layers;
using TouchScript.Utils.Attributes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TouchScript
{
    /// <summary>
    /// A façade object to configure and hold parameters for an instance of <see cref="ITouchManager"/>. Contains constants used throughout the library.
    /// <seealso cref="ITouchManager"/>
    /// </summary>
    /// <remarks>
    /// <para>An instance of <see cref="TouchManager"/> may be added to a Unity scene to hold (i.e. serialize them to the scene) parameters needed to configure an instance of <see cref="ITouchManager"/> used in application. Which can be accessed via <see cref="TouchManager.Instance"/> static property.</para>
    /// <para>Though it's not required it is a convenient way to configure <b>TouchScript</b> for your scene. You can use different configuration options for different scenes.</para>
    /// </remarks>
    /// <example>
    /// This sample shows how to get Pointer Manager instance and subscribe to events.
    /// <code>
    /// TouchManager.Instance.PointersPressed += 
    ///     (sender, args) => { foreach (var pointer in args.Pointers) Debug.Log("Pressed: " + pointer.Id); }; 
    /// TouchManager.Instance.PointersReleased += 
    ///     (sender, args) => { foreach (var pointer in args.Pointers) Debug.Log("Released: " + pointer.Id); }; 
    /// </code>
    /// </example>
    [AddComponentMenu("TouchScript/Pointer Manager")]
    [HelpURL("http://touchscript.github.io/docs/html/T_TouchScript_TouchManager.htm")]
    public sealed class TouchManager : MonoBehaviour
    {
        #region Constants

#if TOUCHSCRIPT_DEBUG
        public const int DEBUG_GL_START = int.MinValue;
        public const int DEBUG_GL_TOUCH = DEBUG_GL_START;
#endif

        /// <summary>
        /// Values of a bit-mask representing which Unity messages an instance of <see cref="TouchManager"/> will dispatch.
        /// </summary>
        [Flags]
        public enum MessageType
        {
            /// <summary>
            /// Pointer frame started.
            /// </summary>
            FrameStarted = 1 << 0,

            /// <summary>
            /// Pointer frame finished.
            /// </summary>
            FrameFinished = 1 << 1,

            /// <summary>
            /// Some pointers were added during the frame.
            /// </summary>
            PointersAdded = 1 << 2,

            /// <summary>
            /// Some pointers have moved during the frame.
            /// </summary>
            PointersUpdated = 1 << 3,

            /// <summary>
            /// Some pointers have touched the surface during the frame.
            /// </summary>
            PointersPressed = 1 << 4,

            /// <summary>
            /// Some pointers were released during the frame.
            /// </summary>
            PointersReleased = 1 << 5,

            /// <summary>
            /// Some pointers were removed during the frame.
            /// </summary>
            PointersRemoved = 1 << 6,

            /// <summary>
            /// Some pointers were cancelled during the frame.
            /// </summary>
            PointersCancelled = 1 << 7
        }

        /// <summary>
        /// Names of dispatched Unity messages.
        /// </summary>
        public enum MessageName
        {
            /// <summary>
            /// Pointer frame started.
            /// </summary>
            OnPointerFrameStarted = MessageType.FrameStarted,

            /// <summary>
            /// Pointer frame finished.
            /// </summary>
            OnPointerFrameFinished = MessageType.FrameFinished,

            /// <summary>
            /// Some pointers were added during the frame.
            /// </summary>
            OnPointersAdded = MessageType.PointersAdded,

            /// <summary>
            /// Some pointers have updated during the frame.
            /// </summary>
            OnPointersUpdated = MessageType.PointersUpdated,

            /// <summary>
            /// Some pointers have touched the surface during the frame.
            /// </summary>
            OnPointersPressed = MessageType.PointersPressed,

            /// <summary>
            /// Some pointers were released during the frame.
            /// </summary>
            OnPointersReleased = MessageType.PointersReleased,

            /// <summary>
            /// Some pointers were removed during the frame.
            /// </summary>
            OnPointersRemoved = MessageType.PointersRemoved,

            /// <summary>
            /// Some pointers were cancelled during the frame.
            /// </summary>
            OnPointersCancelled = MessageType.PointersCancelled
        }

        /// <summary>
        /// Centimeter to inch ratio to be used in DPI calculations.
        /// </summary>
        public const float CM_TO_INCH = 0.393700787f;

        /// <summary>
        /// Inch to centimeter ratio to be used in DPI calculations.
        /// </summary>
        public const float INCH_TO_CM = 1 / CM_TO_INCH;

        /// <summary>
        /// The value used to represent an unknown state of a screen position. Use <see cref="TouchManager.IsInvalidPosition"/> to check if a point has unknown value.
        /// </summary>
        public static readonly Vector2 INVALID_POSITION = new Vector2(float.NaN, float.NaN);

        /// <summary>
        /// TouchScript version.
        /// </summary>
        public static readonly Version VERSION = new Version(8, 1);

        #endregion

        #region Public properties

        /// <summary>
        /// Gets the instance of <see cref="ITouchManager"/> implementation used in the application.
        /// </summary>
        /// <value>An instance of <see cref="ITouchManager"/> which is in charge of global pointer input control in the application.</value>
        public static ITouchManager Instance
        {
            get { return TouchManagerInstance.Instance; }
        }

        /// <summary>
        /// Gets or sets current display device.
        /// </summary>
        /// <value>Object which holds properties of current display device, like DPI and others.</value>
        /// <remarks>A shortcut for <see cref="ITouchManager.DisplayDevice"/> which is also serialized into scene.</remarks>
        public IDisplayDevice DisplayDevice
        {
            get
            {
                if (Instance == null) return displayDevice as IDisplayDevice;
                return Instance.DisplayDevice;
            }
            set
            {
                if (Instance == null)
                {
                    displayDevice = value as Object;
                    return;
                }
                Instance.DisplayDevice = value;
            }
        }

        /// <summary>
        /// Indicates if TouchScript should create a CameraLayer for you if no layers present in a scene.
        /// </summary>
        /// <value><c>true</c> if a CameraLayer should be created on startup; otherwise, <c>false</c>.</value>
        /// <remarks>This is usually a desired behavior but sometimes you would want to turn this off if you are using TouchScript only to get pointer input from some device.</remarks>
        public bool ShouldCreateCameraLayer
        {
            get { return shouldCreateCameraLayer; }
            set { shouldCreateCameraLayer = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether a <see cref="TouchScript.InputSources.StandardInput"/> should be created in scene if no inputs present.
        /// </summary>
        /// <value> <c>true</c> if StandardInput should be created; otherwise, <c>false</c>. </value>
        /// <remarks>This is usually a desired behavior but sometimes you would want to turn this off.</remarks>
        public bool ShouldCreateStandardInput
        {
            get { return shouldCreateStandardInput; }
            set { shouldCreateStandardInput = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether Unity messages are sent when <see cref="ITouchManager"/> dispatches events.
        /// </summary>
        /// <value><c>true</c> if Unity messages are used; otherwise, <c>false</c>.</value>
        /// <remarks>If Unity messages are used they are sent to an object set as a value of <see cref="SendMessageTarget"/> property or to TouchManager's GameObject if it's <c>null</c>.</remarks>
        public bool UseSendMessage
        {
            get { return useSendMessage; }
            set
            {
                if (value == useSendMessage) return;
                useSendMessage = value;
                updateSubscription();
            }
        }

        /// <summary>
        /// Gets or sets the bit-mask which indicates which events from an instance of <see cref="ITouchManager"/> are sent as Unity messages.
        /// </summary>
        /// <value>Bit-mask with corresponding bits for used events.</value>
        public MessageType SendMessageEvents
        {
            get { return sendMessageEvents; }
            set
            {
                if (sendMessageEvents == value) return;
                sendMessageEvents = value;
                updateSubscription();
            }
        }

        /// <summary>
        /// Gets or sets the SendMessage target GameObject.
        /// </summary>
        /// <value>Which GameObject to use to dispatch Unity messages. If <c>null</c>, TouchManager's GameObject is used.</value>
        public GameObject SendMessageTarget
        {
            get { return sendMessageTarget; }
            set
            {
                sendMessageTarget = value;
                if (value == null) sendMessageTarget = gameObject;
            }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Determines whether a Vector2 represents an invalid position, i.e. if it is equal to <see cref="INVALID_POSITION"/>.
        /// </summary>
        /// <param name="position">Screen position.</param>
        /// <returns><c>true</c> if position is invalid; otherwise, <c>false</c>.</returns>
        public static bool IsInvalidPosition(Vector2 position)
        {
            return position.Equals(INVALID_POSITION);
        }

        #endregion

        #region Private variables

        [SerializeField]
        private Object displayDevice;

        [SerializeField]
        [ToggleLeft]
        private bool shouldCreateCameraLayer = true;

        [SerializeField]
        [ToggleLeft]
        private bool shouldCreateStandardInput = true;

        [SerializeField]
        [ToggleLeft]
        private bool useSendMessage = false;

        [SerializeField]
        private MessageType sendMessageEvents = MessageType.PointersPressed | MessageType.PointersCancelled |
                                                MessageType.PointersReleased | MessageType.PointersUpdated;

        [SerializeField]
        private GameObject sendMessageTarget;

        [SerializeField]
        private List<TouchLayer> layers = new List<TouchLayer>();

        #endregion

        #region Unity

        private void Awake()
        {
            if (Instance == null) return;

            Instance.DisplayDevice = displayDevice as IDisplayDevice;
            Instance.ShouldCreateCameraLayer = ShouldCreateCameraLayer;
            Instance.ShouldCreateStandardInput = ShouldCreateStandardInput;
            for (var i = 0; i < layers.Count; i++)
            {
                Instance.AddLayer(layers[i], i);
            }
        }

        private void OnEnable()
        {
            updateSubscription();
        }

        private void OnDisable()
        {
            removeSubscriptions();
        }

        #endregion

        #region Private functions

        private void updateSubscription()
        {
            if (!Application.isPlaying) return;
            if (Instance == null) return;

            if (sendMessageTarget == null) sendMessageTarget = gameObject;

            removeSubscriptions();

            if (!useSendMessage) return;

            if ((SendMessageEvents & MessageType.FrameStarted) != 0) Instance.FrameStarted += frameStartedHandler;
            if ((SendMessageEvents & MessageType.FrameFinished) != 0) Instance.FrameFinished += frameFinishedHandler;
            if ((SendMessageEvents & MessageType.PointersAdded) != 0) Instance.PointersAdded += pointersAddedHandler;
            if ((SendMessageEvents & MessageType.PointersUpdated) != 0) Instance.PointersUpdated += PointersUpdatedHandler;
            if ((SendMessageEvents & MessageType.PointersPressed) != 0) Instance.PointersPressed += pointersPressedHandler;
            if ((SendMessageEvents & MessageType.PointersReleased) != 0) Instance.PointersReleased += pointersReleasedHandler;
            if ((SendMessageEvents & MessageType.PointersRemoved) != 0) Instance.PointersRemoved += pointersRemovedHandler;
            if ((SendMessageEvents & MessageType.PointersCancelled) != 0) Instance.PointersCancelled += pointersCancelledHandler;
        }

        private void removeSubscriptions()
        {
            if (!Application.isPlaying) return;
            if (Instance == null) return;

            Instance.FrameStarted -= frameStartedHandler;
            Instance.FrameFinished -= frameFinishedHandler;
            Instance.PointersAdded -= pointersAddedHandler;
            Instance.PointersUpdated -= PointersUpdatedHandler;
            Instance.PointersPressed -= pointersPressedHandler;
            Instance.PointersReleased -= pointersReleasedHandler;
            Instance.PointersRemoved -= pointersRemovedHandler;
            Instance.PointersCancelled -= pointersCancelledHandler;
        }

        private void pointersAddedHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersAdded.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void PointersUpdatedHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersUpdated.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void pointersPressedHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersPressed.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void pointersReleasedHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersReleased.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void pointersRemovedHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersRemoved.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void pointersCancelledHandler(object sender, PointerEventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointersCancelled.ToString(), e.Pointers,
                SendMessageOptions.DontRequireReceiver);
        }

        private void frameStartedHandler(object sender, EventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointerFrameStarted.ToString(),
                SendMessageOptions.DontRequireReceiver);
        }

        private void frameFinishedHandler(object sender, EventArgs e)
        {
            sendMessageTarget.SendMessage(MessageName.OnPointerFrameFinished.ToString(),
                SendMessageOptions.DontRequireReceiver);
        }

        #endregion
    }
}