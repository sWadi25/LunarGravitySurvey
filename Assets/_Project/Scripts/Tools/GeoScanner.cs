using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey
{
    /// <summary>
    /// Handheld geology scanner (the project's advanced feature).
    ///
    /// INPUT: there is no hard-coded button here. The scanner fires on the XR Grab Interactable's
    /// "Activated" event, which XRI raises from the named "Activate" input action (trigger on a
    /// controller, right mouse button in the desktop fallback). That is exactly how the brief asks
    /// handling to be built.
    ///
    /// HOW IT WORKS: on activate it casts a ray from the emitter tip. If it hits a RockSample it
    /// reveals the mineral and TiO2 reading on the scanner's own screen (spatial UI), plays a 3D beep,
    /// pulses the controller (haptics), and tells the MissionManager.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class GeoScanner : MonoBehaviour
    {
        [Header("Scanning")]
        [Tooltip("Tip of the scanner. Its blue (forward / Z) axis is the scan direction.")]
        [SerializeField] private Transform emitter;
        [SerializeField] private float range = 2.5f;
        [SerializeField] private LayerMask scanMask = ~0;
        [Tooltip("Minimum time between scans, so holding the trigger doesn't spam.")]
        [SerializeField] private float cooldown = 0.6f;

        [Header("Feedback")]
        [Tooltip("World-space text on the scanner's screen.")]
        [SerializeField] private TMP_Text screenText;
        [Tooltip("Thin line shown for a moment when scanning (optional).")]
        [SerializeField] private LineRenderer beam;
        [SerializeField] private float beamSeconds = 0.25f;
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("PLACEHOLDER: neutral scan beep.")]
        [SerializeField] private AudioClip scanClip;
        [Tooltip("PLACEHOLDER: positive 'match' chime.")]
        [SerializeField] private AudioClip targetClip;
        [Range(0f, 1f)] [SerializeField] private float hapticAmplitude = 0.5f;

        /// <summary>(rock, firstTimeScanned)</summary>
        public event Action<RockSample, bool> RockScanned;
        public event Action PickedUp;

        public int ScanCount { get; private set; }

        private XRGrabInteractable grab;
        private Collider[] ownColliders;
        private float lastScanTime = -999f;
        private Coroutine beamRoutine;

        private void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            ownColliders = GetComponentsInChildren<Collider>();
            if (emitter == null) emitter = transform;
            if (beam != null) beam.enabled = false;
            ShowScreen("GEO-SCANNER\n<size=70%>Pick me up</size>");
        }

        private void OnEnable()
        {
            grab.activated.AddListener(OnActivated);
            grab.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            grab.activated.RemoveListener(OnActivated);
            grab.selectEntered.RemoveListener(OnGrabbed);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // Ignore being placed in a socket; only react to a hand.
            if (args.interactorObject is XRSocketInteractor) return;
            ShowScreen("READY\n<size=70%>Aim at a rock\nPull trigger to scan</size>");
            PickedUp?.Invoke();
        }

        private void OnActivated(ActivateEventArgs args)
        {
            if (Time.time - lastScanTime < cooldown) return;
            lastScanTime = Time.time;

            Haptic(args.interactorObject, hapticAmplitude, 0.08f);
            Scan();
        }

        /// <summary>Performs one scan. Public so it can be tested from a UI button or the Inspector.</summary>
        public void Scan()
        {
            ScanCount++;
            Ray ray = new Ray(emitter.position, emitter.forward);
            Vector3 end = ray.origin + ray.direction * range;
            RockSample rock = null;

            RaycastHit[] hits = Physics.RaycastAll(ray, range, scanMask, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (Array.IndexOf(ownColliders, hit.collider) >= 0) continue; // skip the scanner itself
                end = hit.point;
                rock = hit.collider.GetComponentInParent<RockSample>();
                break; // first thing we hit blocks the beam
            }

            ShowBeam(end);

            if (rock == null)
            {
                ShowScreen("NO SAMPLE\n<size=70%>Move within " + range.ToString("0.#") + " m</size>");
                Play(scanClip);
                return;
            }

            bool firstTime = !rock.IsScanned;
            ShowScreen(rock.Scan());
            Play(rock.IsTarget ? targetClip : scanClip);
            RockScanned?.Invoke(rock, firstTime);
        }

        private void ShowScreen(string text)
        {
            if (screenText != null) screenText.text = text;
        }

        private void Play(AudioClip clip)
        {
            if (sfxSource != null && clip != null) sfxSource.PlayOneShot(clip);
        }

        private static void Haptic(IXRInteractor interactor, float amplitude, float duration)
        {
            if (interactor is XRBaseInputInteractor input) input.SendHapticImpulse(amplitude, duration);
        }

        private void ShowBeam(Vector3 end)
        {
            if (beam == null) return;
            if (beamRoutine != null) StopCoroutine(beamRoutine);
            beamRoutine = StartCoroutine(BeamFlash(end));
        }

        private IEnumerator BeamFlash(Vector3 end)
        {
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.SetPosition(0, emitter.position);
            beam.SetPosition(1, end);
            beam.enabled = true;
            yield return new WaitForSeconds(beamSeconds);
            beam.enabled = false;
            beamRoutine = null;
        }
    }
}
