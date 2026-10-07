using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace LunarSurvey
{
    /// <summary>
    /// Keyboard-and-mouse fallback for the exported Windows build when NO headset is connected
    /// (brief, Section 7: "add a keyboard-and-mouse control scheme as a fallback binding on the same actions").
    ///
    /// What it does, only when no XR device is active:
    ///  1. Adds keyboard/mouse bindings to the SAME named XRI actions the controllers use
    ///     (Move, Teleport Mode, Snap Turn, Jump, Select, Activate, UI Press), in a "Keyboard&amp;Mouse" group.
    ///     The interaction code never changes - it still only listens to those actions.
    ///  2. Stands in for head tracking: mouse look on the camera, fixed eye height.
    ///  3. Stands in for hand tracking: right hand floats in front of the camera and aims at the screen
    ///     centre; left hand is held low-left so the wrist display stays readable.
    ///
    /// In the Editor it stays OFF by default, because the XR Interaction Simulator already handles input.
    /// If a headset is connected to the exported build (e.g. Quest Link at the panel), it also stays OFF.
    ///
    /// DESKTOP CONTROLS
    ///   Mouse ............ look / aim          WASD ........ walk
    ///   Left mouse (hold)  grab / press UI     Right mouse . use held tool (scan)
    ///   T (hold, release)  teleport            Q / E ....... snap turn
    ///   Space ............ jump (Lunar mode)   Esc ......... free the cursor (click to recapture)
    /// </summary>
    public class DesktopFallbackRig : MonoBehaviour
    {
        private const string Group = "Keyboard&Mouse";

        [Header("Actions")]
        [Tooltip("The XRI Default Input Actions asset used by the XR Origin (Starter Assets).")]
        [SerializeField] private InputActionAsset xriActions;

        [Tooltip("Desktop-only action that drives the camera (stands in for head tracking).")]
        [SerializeField] private InputAction lookAction = new InputAction("Desktop Look", InputActionType.Value, "<Mouse>/delta");
        [SerializeField] private InputAction releaseCursorAction = new InputAction("Desktop Release Cursor", InputActionType.Button, "<Keyboard>/escape");
        [SerializeField] private InputAction captureCursorAction = new InputAction("Desktop Capture Cursor", InputActionType.Button, "<Mouse>/leftButton");

        [Header("When to activate")]
        [Tooltip("Test the fallback inside the Editor. FIRST untick 'Use XR Interaction Simulator in scenes' " +
                 "(Project Settings > XR Plug-in Management > XR Interaction Toolkit), or both will fight over input.")]
        [SerializeField] private bool activateInEditor = false;
        [Tooltip("Seconds to wait for a headset to start before falling back to the desktop.")]
        [SerializeField] private float detectionDelay = 0.75f;

        [Header("Feel")]
        [SerializeField] private float lookSensitivity = 0.1f;
        [SerializeField] private float maxPitch = 80f;
        [Tooltip("Standing eye height in metres.")]
        [SerializeField] private float eyeHeight = 1.65f;
        [SerializeField] private Vector3 rightHandOffset = new Vector3(0.18f, -0.2f, 0.45f);
        [Tooltip("Where the wrist display should appear, relative to the camera.")]
        [SerializeField] private Vector3 wristDisplayOffset = new Vector3(-0.2f, -0.17f, 0.45f);
        [SerializeField] private float aimDistance = 15f;

        [Header("Links (set by the scene builder)")]
        [SerializeField] private LocomotionModeController locomotion;
        [Tooltip("The wrist display canvas under the Left Controller.")]
        [SerializeField] private Transform wristDisplay;

        public static bool IsActive { get; private set; }

        private XROrigin origin;
        private Camera cam;
        private Transform leftHand, rightHand;
        private float pitch;
        private bool cursorLocked;
        private Quaternion wristRotInHand = Quaternion.identity;
        private Vector3 wristPosInHand;

        private IEnumerator Start()
        {
            IsActive = false;
#if UNITY_EDITOR
            if (!activateInEditor) yield break;
#endif
            yield return new WaitForSeconds(detectionDelay);

            if (XRSettings.isDeviceActive)
            {
                Debug.Log("[DesktopFallbackRig] Headset detected - keyboard/mouse fallback not needed.");
                yield break;
            }

            Activate();
        }

        private void OnDisable()
        {
            lookAction.Disable();
            releaseCursorAction.Disable();
            captureCursorAction.Disable();
            if (IsActive) SetCursorLocked(false);
            IsActive = false;
        }

        private void Activate()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogError("[DesktopFallbackRig] No XR Origin found.", this);
                return;
            }
            cam = origin.Camera;

            // Tracking drivers would otherwise reset the camera/hands every frame.
            foreach (TrackedPoseDriver driver in origin.GetComponentsInChildren<TrackedPoseDriver>(true))
            {
                driver.enabled = false;
            }

            // The modality manager hides controllers that are not tracked - with no headset, that is all of them.
            XRInputModalityManager modality = origin.GetComponentInChildren<XRInputModalityManager>(true);
            if (modality == null) modality = FindAnyObjectByType<XRInputModalityManager>();
            if (modality != null)
            {
                if (modality.leftController != null) leftHand = modality.leftController.transform;
                if (modality.rightController != null) rightHand = modality.rightController.transform;
                if (modality.leftHand != null) modality.leftHand.SetActive(false);
                if (modality.rightHand != null) modality.rightHand.SetActive(false);
                modality.enabled = false;
            }
            if (leftHand == null) leftHand = FindChild(origin.transform, "Left Controller");
            if (rightHand == null) rightHand = FindChild(origin.transform, "Right Controller");
            if (leftHand != null) leftHand.gameObject.SetActive(true);
            if (rightHand != null) rightHand.gameObject.SetActive(true);

            if (leftHand != null && wristDisplay != null)
            {
                wristRotInHand = Quaternion.Inverse(leftHand.rotation) * wristDisplay.rotation;
                wristPosInHand = Quaternion.Inverse(leftHand.rotation) * (wristDisplay.position - leftHand.position);
            }

            AddKeyboardMouseBindings();

            lookAction.Enable();
            releaseCursorAction.Enable();
            captureCursorAction.Enable();
            SetCursorLocked(true);

            if (locomotion == null) locomotion = FindAnyObjectByType<LocomotionModeController>();
            if (locomotion != null) locomotion.EnableDesktopOverride();

            IsActive = true;
            Debug.Log("[DesktopFallbackRig] No headset - keyboard & mouse fallback active.");
        }

        private void LateUpdate()
        {
            if (!IsActive || cam == null) return;

            if (releaseCursorAction.WasPressedThisFrame()) SetCursorLocked(false);
            else if (!cursorLocked && captureCursorAction.WasPressedThisFrame()) SetCursorLocked(true);

            // Head: yaw turns the whole rig (so WASD follows the view), pitch tilts only the camera.
            if (cursorLocked)
            {
                Vector2 delta = lookAction.ReadValue<Vector2>() * lookSensitivity;
                origin.transform.RotateAround(cam.transform.position, Vector3.up, delta.x);
                pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
            }

            GameObject floorOffset = origin.CameraFloorOffsetObject;
            float offsetY = floorOffset != null ? floorOffset.transform.localPosition.y : 0f;
            cam.transform.localPosition = new Vector3(0f, eyeHeight - offsetY, 0f);
            cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            Transform head = cam.transform;

            // Right hand: in front of the camera, pointing at whatever is under the screen centre.
            if (rightHand != null)
            {
                Vector3 aimPoint = head.position + head.forward * aimDistance;
                if (Physics.Raycast(head.position, head.forward, out RaycastHit hit, aimDistance, ~0, QueryTriggerInteraction.Ignore))
                {
                    aimPoint = hit.point;
                }
                rightHand.position = head.TransformPoint(rightHandOffset);
                Vector3 dir = aimPoint - rightHand.position;
                if (dir.sqrMagnitude > 0.0001f) rightHand.rotation = Quaternion.LookRotation(dir, head.up);
            }

            // Left hand: posed so the wrist display faces the camera at the lower left of the view.
            if (leftHand != null)
            {
                if (wristDisplay != null)
                {
                    leftHand.rotation = head.rotation * Quaternion.Inverse(wristRotInHand);
                    leftHand.position = head.TransformPoint(wristDisplayOffset) - leftHand.rotation * wristPosInHand;
                }
                else
                {
                    leftHand.rotation = head.rotation;
                    leftHand.position = head.TransformPoint(wristDisplayOffset);
                }
            }
        }

        // ---------------------------------------------------------------------------------------
        // Bindings: same XRI actions, extra keyboard/mouse paths, tagged with the Keyboard&Mouse group.
        // ---------------------------------------------------------------------------------------
        private void AddKeyboardMouseBindings()
        {
            if (xriActions == null)
            {
                Debug.LogError("[DesktopFallbackRig] Assign 'XRI Default Input Actions' to Xri Actions.", this);
                return;
            }

            AddComposite("XRI Left Locomotion/Move", null,
                ("up", "<Keyboard>/w"), ("down", "<Keyboard>/s"), ("left", "<Keyboard>/a"), ("right", "<Keyboard>/d"));
            AddComposite("XRI Right Locomotion/Teleport Mode", "Sector(directions=1)", ("up", "<Keyboard>/t"));
            AddComposite("XRI Right Locomotion/Snap Turn", null, ("left", "<Keyboard>/q"), ("right", "<Keyboard>/e"));
            AddButton("XRI Right Locomotion/Jump", "<Keyboard>/space");

            AddButton("XRI Right Interaction/Select", "<Mouse>/leftButton");
            AddButton("XRI Right Interaction/Select Value", "<Mouse>/leftButton");
            AddButton("XRI Right Interaction/Activate", "<Mouse>/rightButton");
            AddButton("XRI Right Interaction/Activate Value", "<Mouse>/rightButton");
            AddButton("XRI Right Interaction/UI Press", "<Mouse>/leftButton");
            AddButton("XRI Right Interaction/UI Press Value", "<Mouse>/leftButton");
        }

        private void AddButton(string actionPath, string controlPath)
        {
            InputAction action = xriActions.FindAction(actionPath, false);
            if (action == null) { Debug.LogWarning($"[DesktopFallbackRig] Action not found: {actionPath}", this); return; }
            WithMapDisabled(action, () => action.AddBinding(controlPath, groups: Group));
        }

        private void AddComposite(string actionPath, string interactions, params (string part, string path)[] parts)
        {
            InputAction action = xriActions.FindAction(actionPath, false);
            if (action == null) { Debug.LogWarning($"[DesktopFallbackRig] Action not found: {actionPath}", this); return; }
            WithMapDisabled(action, () =>
            {
                InputActionSetupExtensions.CompositeSyntax composite = action.AddCompositeBinding("2DVector", interactions);
                foreach ((string part, string path) p in parts)
                {
                    composite = composite.With(p.part, p.path, Group);
                }
            });
        }

        // Bindings can't safely change while a map is enabled. Remember exactly which actions were on
        // (ControllerInputActionManager switches individual actions on/off) and restore only those.
        private static void WithMapDisabled(InputAction action, System.Action change)
        {
            InputActionMap map = action.actionMap;
            if (map == null) { change(); return; }

            var wasEnabled = new List<InputAction>();
            foreach (InputAction a in map.actions)
            {
                if (a.enabled) wasEnabled.Add(a);
            }

            map.Disable();
            change();
            foreach (InputAction a in wasEnabled) a.Enable();
        }

        private void SetCursorLocked(bool locked)
        {
            cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }
    }
}
