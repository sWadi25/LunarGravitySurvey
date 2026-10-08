using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey
{
    public enum GrabStyle
    {
        Auto,          // headset: hold grip. Simulator / keyboard & mouse: click to pick up, click again to drop
        HoldToGrab,    // always hold the button (XRI default "State Change")
        ClickToToggle  // always click once to grab and once to release
    }

    /// <summary>
    /// Chooses how the grab button behaves.
    ///
    /// WHY: in a headset, squeezing the grip to hold something is natural. With a mouse (desktop fallback)
    /// or the XR Interaction Simulator, keeping the left button pressed while also aiming, walking and
    /// pulling the trigger is awkward, and play-testers kept dropping the scanner. Without a headset we
    /// switch every Near-Far Interactor to XRI's built-in "Toggle" select mode.
    ///
    /// It only changes XRI's own <c>selectActionTrigger</c> setting; the same named "Select" actions are
    /// still used, so nothing about the interaction code changes.
    /// </summary>
    public class GrabStyleController : MonoBehaviour
    {
        [SerializeField] private GrabStyle style = GrabStyle.Auto;
        [Tooltip("Wait for the headset / simulator to start before deciding.")]
        [SerializeField] private float detectionDelay = 1f;

        public bool ToggleActive { get; private set; }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(detectionDelay);
            bool toggle = style == GrabStyle.ClickToToggle || (style == GrabStyle.Auto && !HeadsetInUse());
            Apply(toggle);
        }

        public void Apply(bool toggle)
        {
            ToggleActive = toggle;
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null) return;

            var trigger = toggle
                ? XRBaseInputInteractor.InputTriggerType.Toggle
                : XRBaseInputInteractor.InputTriggerType.StateChange;
            foreach (NearFarInteractor nf in origin.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nf.selectActionTrigger = trigger;
            }
            Debug.Log(toggle
                ? "[GrabStyleController] No headset - grab is click-to-pick-up / click-to-drop."
                : "[GrabStyleController] Headset - hold the grip to hold objects.");
        }

        private static bool HeadsetInUse()
        {
            if (DesktopFallbackRig.IsActive) return false;
            if (SimulatorPresent()) return false;
            return XRSettings.isDeviceActive;
        }

        // The XR Interaction Simulator is spawned at runtime from a sample prefab, so look it up by type name
        // (no hard dependency on the sample assembly).
        private static bool SimulatorPresent()
        {
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "XRInteractionSimulator" || n == "XRDeviceSimulator") return true;
            }
            return false;
        }
    }
}
