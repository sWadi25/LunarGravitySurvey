using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace LunarSurvey
{
    public enum LocomotionMode
    {
        Comfort, // default: teleport + snap turn, no jumping
        Lunar    // opt-in: continuous "moonwalk" + low-gravity jump, tunneling vignette stays on
    }

    /// <summary>
    /// Switches between the comfort-first default and the opt-in lunar movement mode.
    ///
    /// It reuses the XR Origin's existing XRI components rather than replacing them:
    ///  - ControllerInputActionManager.smoothMotionEnabled (left hand): teleport vs continuous move
    ///  - JumpProvider: enabled only in Lunar mode; jump height is "floaty" because the Gravity
    ///    Provider reads Physics.gravity, which LunarGravity sets to 1.62 m/s²
    ///  - The template's TunnelingVignette is left ON in both modes as the comfort aid.
    /// </summary>
    public class LocomotionModeController : MonoBehaviour
    {
        [SerializeField] private LocomotionMode startMode = LocomotionMode.Comfort;

        [Header("XR Origin parts (found automatically if empty)")]
        [SerializeField] private ControllerInputActionManager leftHandManager;
        [SerializeField] private ControllerInputActionManager rightHandManager;
        [SerializeField] private JumpProvider jumpProvider;
        [SerializeField] private ContinuousMoveProvider moveProvider;

        [Header("Lunar mode tuning")]
        [Tooltip("Metres per second. Keep it slow - astronauts moved carefully, and slow speeds reduce sickness.")]
        [SerializeField] private float lunarMoveSpeed = 1.2f;
        [Tooltip("Jump strength. With Moon gravity the player rises and falls slowly.")]
        [SerializeField] private float lunarJumpHeight = 1.1f;

        [Header("UI (optional)")]
        [SerializeField] private TMP_Text modeLabel;

        public LocomotionMode Mode { get; private set; }

        // Set by DesktopFallbackRig: on a flat monitor continuous movement is always on.
        private bool desktopOverride;

        private void Awake()
        {
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[LocomotionModeController] No XR Origin in the scene.", this);
                return;
            }

            if (leftHandManager == null || rightHandManager == null)
            {
                foreach (ControllerInputActionManager m in origin.GetComponentsInChildren<ControllerInputActionManager>(true))
                {
                    if (m.name.Contains("Left")) leftHandManager = leftHandManager != null ? leftHandManager : m;
                    else if (m.name.Contains("Right")) rightHandManager = rightHandManager != null ? rightHandManager : m;
                }
            }

            if (jumpProvider == null) jumpProvider = origin.GetComponentInChildren<JumpProvider>(true);
            if (moveProvider == null) moveProvider = origin.GetComponentInChildren<ContinuousMoveProvider>(true);
        }

        private void Start()
        {
            Apply(startMode);
        }

        // ---------- UI button hooks ----------
        public void SetComfortMode() => Apply(LocomotionMode.Comfort);
        public void SetLunarMode() => Apply(LocomotionMode.Lunar);
        public void ToggleMode() => Apply(Mode == LocomotionMode.Comfort ? LocomotionMode.Lunar : LocomotionMode.Comfort);

        /// <summary>Called by DesktopFallbackRig when running on a monitor without a headset.</summary>
        public void EnableDesktopOverride()
        {
            desktopOverride = true;
            Apply(Mode);
        }

        public void Apply(LocomotionMode mode)
        {
            Mode = mode;
            bool lunar = mode == LocomotionMode.Lunar;

            if (leftHandManager != null) leftHandManager.smoothMotionEnabled = lunar || desktopOverride;
            if (rightHandManager != null)
            {
                rightHandManager.smoothMotionEnabled = false; // right stick: teleport + turning
                rightHandManager.smoothTurnEnabled = false;   // snap turn is the comfort default
            }

            if (moveProvider != null) moveProvider.moveSpeed = lunarMoveSpeed;

            if (jumpProvider != null)
            {
                jumpProvider.jumpHeight = lunarJumpHeight;
                if (lunar) jumpProvider.gameObject.SetActive(true); // the template may ship it inactive
                jumpProvider.enabled = lunar;
            }

            if (modeLabel != null)
            {
                modeLabel.text = lunar
                    ? "CURRENT: LUNAR\n<size=70%>Thumbstick move + jump. May cause motion sickness.</size>"
                    : "CURRENT: COMFORT\n<size=70%>Teleport + snap turn. Recommended.</size>";
            }
        }
    }
}
