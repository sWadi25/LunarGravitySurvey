using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey
{
    /// <summary>
    /// Gatekeeper for one sample-container socket. Uses the XR Interaction Toolkit's filter system
    /// (IXRHoverFilter + IXRSelectFilter) instead of custom collision code, so the socket simply
    /// refuses anything that is not a scanned, high-titanium basalt sample.
    ///
    /// SETUP: put this on the same GameObject as an XRSocketInteractor. It registers itself.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class SampleSocketFilter : MonoBehaviour, IXRHoverFilter, IXRSelectFilter
    {
        [Tooltip("If on, a target rock must be scanned before the container accepts it (teaches the scanner).")]
        [SerializeField] private bool requireScan = true;

        [Tooltip("Seconds between rejection messages, so Houston doesn't repeat itself every frame.")]
        [SerializeField] private float feedbackCooldown = 5f;

        [Header("Rejection feedback (optional)")]
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("PLACEHOLDER: short 'denied' buzz.")]
        [SerializeField] private AudioClip rejectClip;

        private XRSocketInteractor socket;
        private static float lastFeedbackTime = -999f; // shared by all sockets on purpose

        public bool canProcess => isActiveAndEnabled;

        private void OnEnable()
        {
            socket = GetComponent<XRSocketInteractor>();
            socket.hoverFilters.Add(this);
            socket.selectFilters.Add(this);
        }

        private void OnDisable()
        {
            if (socket == null) return;
            socket.hoverFilters.Remove(this);
            socket.selectFilters.Remove(this);
        }

        // Hover decides whether the socket even reacts (ghost preview + snapping), and is where we
        // give feedback, because it fires while the player is still holding the rock near the socket.
        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
        {
            // A rock already locked in this socket always stays (e.g. after the mission ends).
            if (interactable is IXRSelectInteractable held && socket.IsSelecting(held)) return true;

            RockSample rock = interactable.transform.GetComponentInParent<RockSample>();
            bool accepted = IsAcceptable(rock);
            if (!accepted && rock != null && IsHeldByPlayer(interactable)) GiveFeedback(rock);
            return accepted;
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            if (interactor.IsSelecting(interactable)) return true;
            return IsAcceptable(interactable.transform.GetComponentInParent<RockSample>());
        }

        private bool IsAcceptable(RockSample rock)
        {
            if (rock == null) return false; // e.g. the scanner can't be put in a sample slot
            MissionManager mission = MissionManager.Instance;
            if (mission != null && mission.State != MissionState.InProgress) return false;
            return rock.IsTarget && (!requireScan || rock.IsScanned);
        }

        private static bool IsHeldByPlayer(IXRHoverInteractable interactable)
        {
            // Only complain when the player is actively offering the rock, not when it is just lying nearby.
            return interactable is IXRSelectInteractable selectable && selectable.isSelected;
        }

        private void GiveFeedback(RockSample rock)
        {
            if (Time.time - lastFeedbackTime < feedbackCooldown) return;
            lastFeedbackTime = Time.time;

            if (sfxSource != null && rejectClip != null) sfxSource.PlayOneShot(rejectClip);
            if (MissionManager.Instance != null) MissionManager.Instance.ReportRejectedSample(rock);
        }
    }
}
