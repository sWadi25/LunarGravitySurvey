using Unity.XR.CoreUtils;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Keeps the player standing ON the terrain and INSIDE the survey area.
    ///
    /// WHY (play-test): the XR Interaction Simulator's WASD keys move the simulated headset inside the
    /// tracking space - exactly like a person physically walking around their room. That kind of movement
    /// never goes through the CharacterController, so it walked through bumps/crater rims and right off
    /// the edge of the terrain. The same thing happens with a real headset in room-scale.
    ///
    /// WHAT IT DOES (every frame, after locomotion):
    ///  1. Play area: if the head is further than <see cref="radius"/> from the centre, the whole XR Origin
    ///     is slid back so the head sits on the boundary (works for physical walking, thumbstick and teleport).
    ///     The invisible wall ring built by the editor script blocks thrown rocks and the teleport arc too.
    ///  2. Solid props: a body-sized capsule under the head is tested against static colliders (lander,
    ///     sample container, tool stand...). If it overlaps one, the XR Origin is slid back out horizontally, so
    ///     walking in the room / with the Simulator's WASD can't pass through them. Thumbstick movement was
    ///     already blocked by the CharacterController; this covers the movement that bypasses it.
    ///  3. Terrain follow: if the ground under the HEAD is higher than the XR Origin's floor (walking up a
    ///     slope or into a crater rim), the origin is raised onto it. Walking down is left to XRI's Gravity
    ///     Provider, which already reads Moon gravity, so stepping down a slope feels slightly floaty.
    /// </summary>
    [DefaultExecutionOrder(10000)] // after XRI locomotion and the simulator have moved things this frame
    public class PlayerGrounding : MonoBehaviour
    {
        [Header("Terrain following")]
        [SerializeField] private bool followTerrain = true;
        [Tooltip("Only Terrain colliders count as ground (props like the container are handled by the CharacterController).")]
        [SerializeField] private bool terrainOnly = true;
        [Tooltip("Ignore ground that is more than this far above the feet (e.g. walking into the lander).")]
        [SerializeField] private float maxStepUp = 0.8f;
        [Tooltip("Metres per second the player is lifted when the ground rises. High = no sinking.")]
        [SerializeField] private float riseSpeed = 6f;

        [Header("Solid props (push the player out)")]
        [SerializeField] private bool blockProps = true;
        [Tooltip("Radius of the body capsule around the head, in metres. Small, so leaning over the container to place a sample still works.")]
        [SerializeField] private float bodyRadius = 0.15f;
        [Tooltip("Ignore anything lower than this above the feet (small rocks, kerbs).")]
        [SerializeField] private float stepHeight = 0.25f;

        [Header("Play area (survey zone)")]
        [SerializeField] private bool clampToPlayArea = true;
        [SerializeField] private Vector3 centre = new Vector3(-1.5f, 0f, 6f);
        [Tooltip("Metres from the centre. Keep it slightly inside the invisible wall ring.")]
        [SerializeField] private float radius = 31.5f;

        [Header("Warning (optional)")]
        [SerializeField] private MissionComms comms;
        [Tooltip("Houston warns when the player gets this close to the edge.")]
        [SerializeField] private float warnDistance = 3f;
        [SerializeField] private float warnCooldown = 25f;

        public Vector3 Centre => centre;
        public float Radius => radius;

        private XROrigin origin;
        private Transform head;
        private float nextWarnTime;
        private readonly RaycastHit[] hits = new RaycastHit[8];
        private readonly Collider[] overlaps = new Collider[16];
        private CapsuleCollider probe;

        private void Start()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null) head = origin.Camera.transform;
            if (comms == null) comms = FindAnyObjectByType<MissionComms>();

            // Query-only capsule used with Physics.ComputePenetration (trigger on Ignore Raycast, never moved).
            var go = new GameObject("Body Probe (PlayerGrounding)");
            go.layer = 2; // Ignore Raycast
            go.transform.SetParent(transform, false);
            probe = go.AddComponent<CapsuleCollider>();
            probe.isTrigger = true;
            probe.direction = 1;
        }

        private void LateUpdate()
        {
            if (origin == null || head == null) return;

            Transform rig = origin.transform;
            Vector3 o = rig.position;
            Vector3 h = head.position;
            bool changed = false;

            // 1. Play area
            if (clampToPlayArea)
            {
                Vector2 rel = new Vector2(h.x - centre.x, h.z - centre.z);
                float d = rel.magnitude;
                if (d > radius)
                {
                    Vector2 push = rel / d * (d - radius);
                    o.x -= push.x; o.z -= push.y;
                    h.x -= push.x; h.z -= push.y;
                    changed = true;
                }

                if (comms != null && d > radius - warnDistance && Time.time >= nextWarnTime)
                {
                    nextWarnTime = Time.time + warnCooldown;
                    comms.Say("boundary_warning");
                }
            }

            // 2. Solid props
            if (blockProps && PushOutOfProps(ref o, ref h)) changed = true;

            // 3. Terrain follow (only ever lifts; gravity handles going down)
            if (followTerrain && TryGetGroundHeight(new Vector3(h.x, o.y, h.z), out float groundY))
            {
                float rise = groundY - o.y;
                if (rise > 0.002f && rise <= maxStepUp)
                {
                    o.y = Mathf.MoveTowards(o.y, groundY, Mathf.Max(riseSpeed * Time.deltaTime, rise * 0.5f));
                    changed = true;
                }
            }

            if (changed)
            {
                rig.position = o;
                Physics.SyncTransforms();
            }
        }

        private bool PushOutOfProps(ref Vector3 o, ref Vector3 h)
        {
            if (probe == null) return false;
            float bottom = o.y + stepHeight;
            float top = Mathf.Max(h.y, bottom + 0.1f);
            // The probe used for ComputePenetration is very tall, so the shortest way out of a prop is always
            // sideways (never "down through the floor" when the player is deep inside the lander).
            probe.radius = bodyRadius;
            probe.height = 200f;
            probe.center = Vector3.zero;

            bool moved = false;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                Vector3 centre = new Vector3(h.x, (bottom + top) * 0.5f, h.z); // tall probe, centred on the body
                Vector3 p0 = new Vector3(h.x, bottom + bodyRadius, h.z);
                Vector3 p1 = new Vector3(h.x, top - bodyRadius > bottom + bodyRadius ? top - bodyRadius : bottom + bodyRadius, h.z);
                int count = Physics.OverlapCapsuleNonAlloc(p0, p1, bodyRadius, overlaps, ~0, QueryTriggerInteraction.Ignore);
                Vector3 push = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Collider c = overlaps[i];
                    if (c == null || c == probe || c.isTrigger) continue;
                    if (c.attachedRigidbody != null) continue;                 // rocks, scanner: can be pushed / picked up
                    if (c is CharacterController || c is TerrainCollider) continue;
                    if (origin != null && c.transform.IsChildOf(origin.transform)) continue;
                    if (Physics.ComputePenetration(probe, centre, Quaternion.identity,
                            c, c.transform.position, c.transform.rotation, out Vector3 dir, out float dist))
                    {
                        dir.y = 0f; // only ever slide sideways
                        if (dir.sqrMagnitude < 1e-4f) continue;
                        push += dir.normalized * dist;
                    }
                }
                if (push.sqrMagnitude < 1e-6f) break;
                o.x += push.x; o.z += push.z;
                h.x += push.x; h.z += push.z;
                moved = true;
            }
            return moved;
        }

        private bool TryGetGroundHeight(Vector3 feet, out float y)
        {
            y = 0f;
            Vector3 start = feet + Vector3.up * (maxStepUp + 0.5f);
            int count = Physics.RaycastNonAlloc(start, Vector3.down, hits, maxStepUp + 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || c.attachedRigidbody != null || c is CharacterController) continue;
                if (terrainOnly && !(c is TerrainCollider)) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            if (float.IsNegativeInfinity(best)) return false;
            y = best;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            const int seg = 64;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Gizmos.DrawLine(centre + Vector3.up * 0.1f + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius,
                                centre + Vector3.up * 0.1f + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius);
            }
        }
    }
}
