using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

namespace LunarSurvey
{
    /// <summary>
    /// Apollo-style "bunny hop" lope for thumbstick walking in Lunar mode.
    ///
    /// Apollo astronauts rarely walked heel-to-toe on the Moon: in 1/6 g with a stiff suit, the efficient gait
    /// was a series of small hops. This component reproduces that by moving the Camera Offset along a real
    /// ballistic arc while the player is moving with the thumbstick:
    ///     airtime  T = 2 * sqrt(2h / g)     (h = hop height, g = |Physics.gravity| = 1.62 m/s²)
    /// e.g. h = 6 cm gives ~0.54 s in the air per hop, so the rhythm itself is physically "Moon-like".
    /// A soft suit-borne thud plays on every landing.
    ///
    /// COMFORT: vertical camera motion can cause motion sickness, so it is
    ///  - only active in the opt-in Lunar mode (and the desktop fallback), never in Comfort/teleport mode,
    ///  - only for artificial locomotion (physically walking in a headset is never bobbed),
    ///  - small by default and adjustable with <see cref="intensity"/> (0 = off).
    /// </summary>
    public class LunarGait : MonoBehaviour
    {
        [SerializeField] private LocomotionModeController locomotion;

        [Header("Hop")]
        [Range(0f, 1f)]
        [Tooltip("0 = off. 1 = full hop height. Lower it if anyone feels unwell.")]
        [SerializeField] private float intensity = 1f;
        [Tooltip("Peak height of each hop in metres.")]
        [SerializeField] private float hopHeight = 0.06f;
        [Tooltip("Time on the ground between hops in seconds.")]
        [SerializeField] private float groundContactTime = 0.12f;
        [Tooltip("Below this speed (m/s) the player is treated as standing still.")]
        [SerializeField] private float minSpeed = 0.25f;

        [Header("Footsteps (2D, heard through the suit)")]
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private AudioClip[] footstepClips = new AudioClip[0];
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.55f;

        public bool IsHopping { get; private set; }

        private XROrigin origin;
        private Transform cameraOffset;
        private GravityProvider gravity;
        private JumpProvider jump;
        private Vector3 lastOriginPos;
        private float cycleTime;     // seconds into the current hop cycle
        private float appliedBob;    // what we added to the Camera Offset last frame
        private float bob;

        private void Start()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin == null) { enabled = false; return; }
            cameraOffset = origin.CameraFloorOffsetObject != null ? origin.CameraFloorOffsetObject.transform : null;
            gravity = origin.GetComponentInChildren<GravityProvider>(true);
            jump = origin.GetComponentInChildren<JumpProvider>(true);
            if (locomotion == null) locomotion = FindAnyObjectByType<LocomotionModeController>();
            lastOriginPos = origin.transform.position;
        }

        private void LateUpdate()
        {
            if (cameraOffset == null) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 p = origin.transform.position;
            Vector3 delta = p - lastOriginPos;
            lastOriginPos = p;
            delta.y = 0f;
            float speed = delta.magnitude / dt;
            if (speed > 8f) speed = 0f; // a teleport, not walking

            bool smoothMove = locomotion == null || locomotion.SmoothMoveActive;
            bool grounded = gravity == null || gravity.isGrounded;
            bool jumping = jump != null && jump.isJumping;
            bool hop = intensity > 0f && smoothMove && grounded && !jumping && speed > minSpeed;

            float g = Mathf.Max(0.1f, -Physics.gravity.y);
            float h = hopHeight * intensity;
            float airTime = 2f * Mathf.Sqrt(2f * Mathf.Max(h, 0.001f) / g);
            float cycle = airTime + groundContactTime;

            if (hop)
            {
                float before = cycleTime;
                cycleTime += dt;
                if (before < airTime && cycleTime >= airTime) Footstep(); // landing
                if (cycleTime >= cycle) cycleTime -= cycle;

                float v0 = Mathf.Sqrt(2f * g * h);
                bob = cycleTime < airTime ? Mathf.Max(0f, v0 * cycleTime - 0.5f * g * cycleTime * cycleTime) : 0f;
            }
            else
            {
                // finish the current hop smoothly, then stand still
                bob = Mathf.MoveTowards(bob, 0f, 0.4f * dt);
                if (bob <= 0f) cycleTime = 0f;
            }
            IsHopping = hop;

            // Apply as an offset on top of whatever XRI / the desktop rig set this frame.
            Vector3 lp = cameraOffset.localPosition;
            float baseY = lp.y - appliedBob;
            lp.y = baseY + bob;
            cameraOffset.localPosition = lp;
            appliedBob = bob;
        }

        private void Footstep()
        {
            if (footstepSource == null || footstepClips == null || footstepClips.Length == 0) return;
            AudioClip clip = footstepClips[Random.Range(0, footstepClips.Length)];
            if (clip == null) return;
            footstepSource.pitch = Random.Range(0.9f, 1.08f);
            footstepSource.PlayOneShot(clip, footstepVolume);
        }

        private void OnDisable()
        {
            if (cameraOffset == null) return;
            Vector3 lp = cameraOffset.localPosition;
            lp.y -= appliedBob;
            cameraOffset.localPosition = lp;
            appliedBob = 0f;
            bob = 0f;
        }
    }
}
