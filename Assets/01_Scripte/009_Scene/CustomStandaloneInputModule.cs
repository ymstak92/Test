﻿using UnityEngine.Serialization;
using ConfigDataDefine;

namespace UnityEngine.EventSystems {
    [AddComponentMenu("Event/Custom Standalone Input Module")]
    public class CustomStandaloneInputModule : PointerInputModule {
        private float m_PrevActionTime;
        private Vector2 m_LastMoveVector;
        private int m_ConsecutiveMoveCount = 0;

        private Vector2 m_LastMousePosition;
        private Vector2 m_MousePosition;

        private GameObject m_CurrentFocusedGameObject;

        private RIDLEDemo inputActions;

        protected CustomStandaloneInputModule() {
        }

        [SerializeField]
        private bool m_EnableMouse = false;


        private string _HorizontalAxes = ConfigData.Horizontal.String;
        private string _VerticalAxes = ConfigData.Vertical.String;

        private string _SubmitButton = ConfigData.Submit.String;
        private string _CancelButton = ConfigData.Cancel.String;

        [SerializeField]
        private float m_InputActionsPerSecond = 10;

        [SerializeField]
        private float m_RepeatDelay = 0.5f;

        [SerializeField]
        [FormerlySerializedAs("m_AllowActivationOnMobileDevice")]
        private bool m_ForceModuleActive;

        private bool _isKeyMovement;
        public bool isKeyInvalid {
            get { return _isKeyMovement; }
            set { _isKeyMovement = value; }
        }

        public bool enableMouse {
            get { return m_EnableMouse; }
            set { m_EnableMouse = value; }
        }

        public bool forceModuleActive {
            get { return m_ForceModuleActive; }
            set { m_ForceModuleActive = value; }
        }

        public float inputActionsPerSecond {
            get { return m_InputActionsPerSecond; }
            set { m_InputActionsPerSecond = value; }
        }

        public float repeatDelay {
            get { return m_RepeatDelay; }
            set { m_RepeatDelay = value; }
        }

        /// <summary>
        /// Name of the horizontal axis for movement (if axis events are used).
        /// </summary>
        public string horizontalAxis {
            get { return /*m_HorizontalAxis*/_HorizontalAxes; }
            set { _HorizontalAxes = value; }
        }

        /// <summary>
        /// Name of the vertical axis for movement (if axis events are used).
        /// </summary>
        public string verticalAxis {
            get { return _VerticalAxes; }
            set { _VerticalAxes = value; }
        }

        public string submitButton {
            get { return _SubmitButton; }
            set { _SubmitButton = value; }
        }

        public string cancelButton {
            get { return _CancelButton; }
            set { _CancelButton = value; }
        }

        public override void UpdateModule() {
            m_LastMousePosition = m_MousePosition;
            m_MousePosition = input.mousePosition;
        }

        public override bool IsModuleSupported() {
            return m_ForceModuleActive || input.mousePresent || input.touchSupported;
        }

        public override bool ShouldActivateModule() {
            if (!base.ShouldActivateModule())
                return false;

            var shouldActivate = m_ForceModuleActive;
            shouldActivate |= ConfigManager.Instance.config.GetKeyDown(_SubmitButton);
            shouldActivate |= ConfigManager.Instance.config.GetKeyDown(_CancelButton);
            shouldActivate |= !Mathf.Approximately(ConfigManager.Instance.config.GetAxisRaw(_HorizontalAxes), 0.0f);
            shouldActivate |= !Mathf.Approximately(ConfigManager.Instance.config.GetAxisRaw(_VerticalAxes), 0.0f);
            if (m_EnableMouse) {
                shouldActivate |= (m_MousePosition - m_LastMousePosition).sqrMagnitude > 0.0f;
                shouldActivate |= input.GetMouseButtonDown(0);
            }

            if (m_EnableMouse && input.touchCount > 0)
                shouldActivate = true;

            return shouldActivate;
        }

        public override void ActivateModule() {
            base.ActivateModule();
            m_MousePosition = input.mousePosition;
            m_LastMousePosition = input.mousePosition;

            var toSelect = eventSystem.currentSelectedGameObject;
            if (toSelect == null)
                toSelect = eventSystem.firstSelectedGameObject;

            eventSystem.SetSelectedGameObject(toSelect, GetBaseEventData());
        }

        public override void DeactivateModule() {
            base.DeactivateModule();
            ClearSelection();
        }

        public override void Process() {
            bool usedEvent = _isKeyMovement;
            if (eventSystem.sendNavigationEvents) {
                if (!usedEvent)
                    usedEvent |= SendMoveEventToSelectedObject();
                if (!usedEvent)
                    SendSubmitEventToSelectedObject();
            }

            if (!m_EnableMouse) { return; }
            // touch needs to take precedence because of the mouse emulation layer
            if (!ProcessTouchEvents() && input.mousePresent)
                ProcessMouseEvent();
        }

        private bool ProcessTouchEvents() {
            for (int i = 0; i < input.touchCount; ++i) {
                Touch touch = input.GetTouch(i);

                if (touch.type == TouchType.Indirect)
                    continue;

                bool released;
                bool pressed;
                var pointer = GetTouchPointerEventData(touch, out pressed, out released);

                ProcessTouchPress(pointer, pressed, released);

                if (!released) {
                    ProcessMove(pointer);
                    ProcessDrag(pointer);
                } else
                    RemovePointerData(pointer);
            }
            return input.touchCount > 0;
        }

        protected void ProcessTouchPress(PointerEventData pointerEvent, bool pressed, bool released) {
            var currentOverGo = pointerEvent.pointerCurrentRaycast.gameObject;

            // PointerDown notification
            if (pressed) {
                pointerEvent.eligibleForClick = true;
                pointerEvent.delta = Vector2.zero;
                pointerEvent.dragging = false;
                pointerEvent.useDragThreshold = true;
                pointerEvent.pressPosition = pointerEvent.position;
                pointerEvent.pointerPressRaycast = pointerEvent.pointerCurrentRaycast;

                DeselectIfSelectionChanged(currentOverGo, pointerEvent);

                if (pointerEvent.pointerEnter != currentOverGo) {
                    // send a pointer enter to the touched element if it isn't the one to select...
                    HandlePointerExitAndEnter(pointerEvent, currentOverGo);
                    pointerEvent.pointerEnter = currentOverGo;
                }

                // search for the control that will receive the press
                // if we can't find a press handler set the press
                // handler to be what would receive a click.
                var newPressed = ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.pointerDownHandler);

                // didnt find a press handler... search for a click handler
                if (newPressed == null)
                    newPressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // Debug.Log("Pressed: " + newPressed);

                float time = Time.unscaledTime;

                if (newPressed == pointerEvent.lastPress) {
                    var diffTime = time - pointerEvent.clickTime;
                    if (diffTime < 0.3f)
                        ++pointerEvent.clickCount;
                    else
                        pointerEvent.clickCount = 1;

                    pointerEvent.clickTime = time;
                } else {
                    pointerEvent.clickCount = 1;
                }

                pointerEvent.pointerPress = newPressed;
                pointerEvent.rawPointerPress = currentOverGo;

                pointerEvent.clickTime = time;

                // Save the drag handler as well
                pointerEvent.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentOverGo);

                if (pointerEvent.pointerDrag != null)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.initializePotentialDrag);
            }

            // PointerUp notification
            if (released) {
                // Debug.Log("Executing pressup on: " + pointer.pointerPress);
                ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);

                // Debug.Log("KeyCode: " + pointer.eventData.keyCode);

                // see if we mouse up on the same element that we clicked on...
                var pointerUpHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // PointerClick and Drop events
                if (pointerEvent.pointerPress == pointerUpHandler && pointerEvent.eligibleForClick) {
                    ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerClickHandler);
                } else if (pointerEvent.pointerDrag != null && pointerEvent.dragging) {
                    ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.dropHandler);
                }

                pointerEvent.eligibleForClick = false;
                pointerEvent.pointerPress = null;
                pointerEvent.rawPointerPress = null;

                if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.endDragHandler);

                pointerEvent.dragging = false;
                pointerEvent.pointerDrag = null;

                if (pointerEvent.pointerDrag != null)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.endDragHandler);

                pointerEvent.pointerDrag = null;

                // send exit events as we need to simulate this on touch up on touch device
                ExecuteEvents.ExecuteHierarchy(pointerEvent.pointerEnter, pointerEvent, ExecuteEvents.pointerExitHandler);
                pointerEvent.pointerEnter = null;
            }
        }

        /// <summary>
        /// Process submit keys.
        /// </summary>
        protected bool SendSubmitEventToSelectedObject() {
            if (eventSystem.currentSelectedGameObject == null)
                return false;

            var data = GetBaseEventData();
            if (ConfigManager.Instance.config.GetKeyDown(_SubmitButton))
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, data, ExecuteEvents.submitHandler);

            if (ConfigManager.Instance.config.GetKeyDown(_CancelButton))
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, data, ExecuteEvents.cancelHandler);
            return data.used;
        }

        private Vector2 GetRawMoveVector() {
            Vector2 move = Vector2.zero;
            //move.x = input.GetAxisRaw(m_HorizontalAxis);
            move.x = ConfigManager.Instance.config.GetAxisRaw(_HorizontalAxes);
            move.x += inputActions.Player.Move.ReadValue<Vector2>().x;
            move.y = ConfigManager.Instance.config.GetAxisRaw(_VerticalAxes);
            move.y += inputActions.Player.Move.ReadValue<Vector2>().y;
            if (ConfigManager.Instance.config.GetKeyDown(_HorizontalAxes)) {
                if (move.x < 0)
                    move.x = -1f;
                if (move.x > 0)
                    move.x = 1f;
            }//if
            if (ConfigManager.Instance.config.GetKeyDown(_VerticalAxes)) {
                if (move.y < 0)
                    move.y = -1f;
                if (move.y > 0)
                    move.y = 1f;
            }//if
            return move;
        }//GetRawMoveVector

        /// <summary>
        /// Process keyboard events.
        /// </summary>
        protected bool SendMoveEventToSelectedObject() {
            float time = Time.unscaledTime;

            Vector2 movement = GetRawMoveVector();
            if (Mathf.Approximately(movement.x, 0f) && Mathf.Approximately(movement.y, 0f)) {
                m_ConsecutiveMoveCount = 0;
                return false;
            }

            // If user pressed key again, always allow event
            bool allow = ConfigManager.Instance.config.GetKeyDown(_HorizontalAxes) || ConfigManager.Instance.config.GetKeyDown(_VerticalAxes);
            bool similarDir = (Vector2.Dot(movement, m_LastMoveVector) > 0);
            if (!allow) {
                // Otherwise, user held down key or axis.
                // If direction didn't change at least 90 degrees, wait for delay before allowing consequtive event.
                if (similarDir && m_ConsecutiveMoveCount == 1)
                    allow = (time > m_PrevActionTime + m_RepeatDelay);
                // If direction changed at least 90 degree, or we already had the delay, repeat at repeat rate.
                else
                    allow = (time > m_PrevActionTime + 1f / m_InputActionsPerSecond);
            }
            if (!allow)
                return false;

            // Debug.Log(m_ProcessingEvent.rawType + " axis:" + m_AllowAxisEvents + " value:" + "(" + x + "," + y + ")");
            var axisEventData = GetAxisEventData(movement.x, movement.y, 0.6f);

            if (axisEventData.moveDir != MoveDirection.None) {
                ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, axisEventData, ExecuteEvents.moveHandler);
                if (!similarDir)
                    m_ConsecutiveMoveCount = 0;
                m_ConsecutiveMoveCount++;
                m_PrevActionTime = time;
                m_LastMoveVector = movement;
            } else {
                m_ConsecutiveMoveCount = 0;
            }

            return axisEventData.used;
        }

        protected void ProcessMouseEvent() {
            ProcessMouseEvent(0);
        }

        /// <summary>
        /// Process all mouse events.
        /// </summary>
        protected void ProcessMouseEvent(int id) {
            var mouseData = GetMousePointerEventData(id);
            var leftButtonData = mouseData.GetButtonState(PointerEventData.InputButton.Left).eventData;

            m_CurrentFocusedGameObject = leftButtonData.buttonData.pointerCurrentRaycast.gameObject;

            // Process the first mouse button fully
            ProcessMousePress(leftButtonData);
            ProcessMove(leftButtonData.buttonData);
            ProcessDrag(leftButtonData.buttonData);

            // Now process right / middle clicks
            ProcessMousePress(mouseData.GetButtonState(PointerEventData.InputButton.Right).eventData);
            ProcessDrag(mouseData.GetButtonState(PointerEventData.InputButton.Right).eventData.buttonData);
            ProcessMousePress(mouseData.GetButtonState(PointerEventData.InputButton.Middle).eventData);
            ProcessDrag(mouseData.GetButtonState(PointerEventData.InputButton.Middle).eventData.buttonData);

            if (!Mathf.Approximately(leftButtonData.buttonData.scrollDelta.sqrMagnitude, 0.0f)) {
                var scrollHandler = ExecuteEvents.GetEventHandler<IScrollHandler>(leftButtonData.buttonData.pointerCurrentRaycast.gameObject);
                ExecuteEvents.ExecuteHierarchy(scrollHandler, leftButtonData.buttonData, ExecuteEvents.scrollHandler);
            }
        }

        protected bool SendUpdateEventToSelectedObject() {
            if (eventSystem.currentSelectedGameObject == null)
                return false;

            var data = GetBaseEventData();
            ExecuteEvents.Execute(eventSystem.currentSelectedGameObject, data, ExecuteEvents.updateSelectedHandler);
            return data.used;
        }

        /// <summary>
        /// Process the current mouse press.
        /// </summary>
        protected void ProcessMousePress(MouseButtonEventData data) {
            var pointerEvent = data.buttonData;
            var currentOverGo = pointerEvent.pointerCurrentRaycast.gameObject;

            // PointerDown notification
            if (data.PressedThisFrame()) {
                pointerEvent.eligibleForClick = true;
                pointerEvent.delta = Vector2.zero;
                pointerEvent.dragging = false;
                pointerEvent.useDragThreshold = true;
                pointerEvent.pressPosition = pointerEvent.position;
                pointerEvent.pointerPressRaycast = pointerEvent.pointerCurrentRaycast;

                DeselectIfSelectionChanged(currentOverGo, pointerEvent);

                // search for the control that will receive the press
                // if we can't find a press handler set the press
                // handler to be what would receive a click.
                var newPressed = ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.pointerDownHandler);

                // didnt find a press handler... search for a click handler
                if (newPressed == null)
                    newPressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // Debug.Log("Pressed: " + newPressed);

                float time = Time.unscaledTime;

                if (newPressed == pointerEvent.lastPress) {
                    var diffTime = time - pointerEvent.clickTime;
                    if (diffTime < 0.3f)
                        ++pointerEvent.clickCount;
                    else
                        pointerEvent.clickCount = 1;

                    pointerEvent.clickTime = time;
                } else {
                    pointerEvent.clickCount = 1;
                }

                pointerEvent.pointerPress = newPressed;
                pointerEvent.rawPointerPress = currentOverGo;

                pointerEvent.clickTime = time;

                // Save the drag handler as well
                pointerEvent.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(currentOverGo);

                if (pointerEvent.pointerDrag != null)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.initializePotentialDrag);
            }

            // PointerUp notification
            if (data.ReleasedThisFrame()) {
                // Debug.Log("Executing pressup on: " + pointer.pointerPress);
                ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerUpHandler);

                // see if we mouse up on the same element that we clicked on...
                var pointerUpHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

                // PointerClick and Drop events
                if (pointerEvent.pointerPress == pointerUpHandler && pointerEvent.eligibleForClick) {
                    ExecuteEvents.Execute(pointerEvent.pointerPress, pointerEvent, ExecuteEvents.pointerClickHandler);
                } else if (pointerEvent.pointerDrag != null && pointerEvent.dragging) {
                    ExecuteEvents.ExecuteHierarchy(currentOverGo, pointerEvent, ExecuteEvents.dropHandler);
                }

                pointerEvent.eligibleForClick = false;
                pointerEvent.pointerPress = null;
                pointerEvent.rawPointerPress = null;

                if (pointerEvent.pointerDrag != null && pointerEvent.dragging)
                    ExecuteEvents.Execute(pointerEvent.pointerDrag, pointerEvent, ExecuteEvents.endDragHandler);

                pointerEvent.dragging = false;
                pointerEvent.pointerDrag = null;

                if (currentOverGo != pointerEvent.pointerEnter) {
                    HandlePointerExitAndEnter(pointerEvent, null);
                    HandlePointerExitAndEnter(pointerEvent, currentOverGo);
                }
            }
        }

        protected GameObject GetCurrentFocusedGameObject() {
            return m_CurrentFocusedGameObject;
        }

        private void Awake() {
            inputActions = new RIDLEDemo();
            inputActions.Enable();
        }

    }

}