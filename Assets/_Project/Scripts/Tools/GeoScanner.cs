using System;
using System.Collections;
using System.Collections.Generic;
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
    /// reveals the mineral and TiO2 reading on the scanner's own screen (spatial UI), mirrors it on the
    /// wrist display, plays a 3D beep, pulses the controller (haptics), and tells the MissionManager.
    ///
    /// PLAY-TEST FIXES: the readout is mirrored to the wrist display (the scanner screen alone was hard
    /// to read), the scanner raises <see cref="Released"/> so the <see cref="ToolHolster"/> can clip it
    /// back onto the player's belt, and it never collides with the player's own body capsule.
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
        [Tooltip("Extra displays that repeat the scanner readout (e.g. the wrist display).")]
        [SerializeField] private List<TMP_Text> mirrorTexts = new List<TMP_Text>();
        [Tooltip("Thin line shown for a moment when scanning (optional).")]
        [SerializeField] private LineRenderer beam;
        [SerializeField] private float beamSeconds = 0.25f;
        [SerializeField] private AudioSource sfxSource;
        [Tooltip("Neutral scan beep (Assets/_Project/Audio/SFX/scan_beep).")]
        [SerializeField] private AudioClip scanClip;
        [Tooltip("Positive 'match' chime (Assets/_Project/Audio/SFX/scan_match).")]
        [SerializeField] private AudioClip targetClip;
        [Range(0f, 1f)] [SerializeField] private float hapticAmplitude = 0.5f;
        [Tooltip("Hide the controller model while the scanner is held, so it doesn't block the screen.")]
        [SerializeField] private bool hideControllerModelWhileHeld = true;

        /// <summary>(rock, firstTimeScanned)</summary>
        public event Action<RockSample, bool> RockScanned;
        /// <summary>A hand (not a socket) picked the scanner up.</summary>
        public event Action PickedUp;
        /// <summary>A hand (not a socket) let go of the scanner.</summary>
        public event Action Released;

        public int ScanCount { get; private set; }
        public bool IsHeldByHand { get; private set; }

        private XRGrabInteractable grab;
        private Collider[] ownColliders;
        private float lastScanTime = -999f;
        private Coroutine beamRoutine;
        private readonly List<GameObject> hiddenControllerParts = new List<GameObject>();

        private void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            ownColliders = GetComponentsInChildren<Collider>();
            if (emitter == null) emitter = transform;
            if (beam != null) beam.enabled = false;
            ShowScreen("GEO-SCANNER\n<size=70%>Pick me up</size>", mirror: false);
        }

        private void Start()
        {
            // The scanner is carried right next to the body. Without this, a held or holstered scanner
            // would shove the player's CharacterController around.
            CharacterController body = FindAnyObjectByType<CharacterController>();
            if (body != null)
            {
                foreach (Collider c in ownColliders)
                {
                    if (c != null && !c.isTrigger) Physics.IgnoreCollision(c, body, true);
                }
            }
        }

        private void OnEnable()
        {
            grab.activated.AddListener(OnActivated);
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnLetGo);
        }

        private void OnDisable()
        {
            grab.activated.RemoveListener(OnActivated);
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnLetGo);
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            // Ignore being placed in a socket / holster; only react to a hand.
            if (args.interactorObject is XRSocketInteractor) return;
            IsHeldByHand = true;
            if (hideControllerModelWhileHeld) HideControllerModel(args.interactorObject.transform);
            ShowScreen("READY\n<size=70%>Aim at a rock\nPull trigger to scan</size>", mirror: false);
            PickedUp?.Invoke();
        }

        private void OnLetGo(SelectExitEventArgs args)
        {
            if (args.interactorObject is XRSocketInteractor) return;
            if (args.isCanceled && !isActiveAndEnabled) return;
            IsHeldByHand = false;
            RestoreControllerModel();
            Released?.Invoke();
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
                if (hit.collider is CharacterController) continue;           // skip the player's body
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

        /// <summary>Writes to the scanner screen (and, by default, to every mirror display).</summary>
        public void ShowScreen(string text, bool mirror = true)
        {
            if (screenText != null) screenText.text = text;
            if (!mirror) return;
            string compact = "SCAN: " + text.Replace("\n", "  |  ");
            foreach (TMP_Text t in mirrorTexts)
            {
                if (t != null) t.text = compact;
            }
        }

        // XRI Starter Assets rigs keep the model in a child named "... Controller Visual" next to the interactors.
        private void HideControllerModel(Transform interactor)
        {
            RestoreControllerModel();
            Transform controller = interactor != null ? interactor.parent : null;
            if (controller == null) return;
            foreach (Transform child in controller)
            {
                // the controller model and the template's tutorial callouts ("Grab", "Turn"...) next to it
                bool visual = child.name.Contains("Controller Visual") || child.name.Contains("Affordance Callouts");
                if (visual && child.gameObject.activeSelf)
                {
                    hiddenControllerParts.Add(child.gameObject);
                    child.gameObject.SetActive(false);
                }
            }
        }

        private void RestoreControllerModel()
        {
            foreach (GameObject go in hiddenControllerParts)
            {
                if (go != null) go.SetActive(true);
            }
            hiddenControllerParts.Clear();
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
