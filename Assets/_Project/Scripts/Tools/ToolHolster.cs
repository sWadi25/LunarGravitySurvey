using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LunarSurvey
{
    /// <summary>
    /// Belt holster for the Geo-Scanner, attached to the player.
    ///
    /// WHY: play-testers lost the scanner as soon as they let go of it (or grabbed a rock with the same
    /// hand), and on a flat monitor there is no way to find it again. Now, once the scanner has been taken
    /// from the tool stand, letting go of it clips it back onto the astronaut's belt (left hip). Grab it
    /// from there with either hand (near grab in VR, or point + click on desktop / in the Simulator).
    ///
    /// HOW: an ordinary XRSocketInteractor that follows the player's body (head position + yaw). It only
    /// accepts the scanner (IXRSelectFilter / IXRHoverFilter), so rocks carried past the hip are ignored.
    /// A release from the hand triggers a forced SelectEnter into this socket after a short delay.
    /// </summary>
    [RequireComponent(typeof(XRSocketInteractor))]
    public class ToolHolster : MonoBehaviour, IXRSelectFilter, IXRHoverFilter
    {
        [SerializeField] private GeoScanner tool;

        [Header("Where it sits (relative to the head, rotated with the body's yaw)")]
        [Tooltip("x = left/right, y = down from the eyes, z = forward. Default: left hip, slightly forward.")]
        [SerializeField] private Vector3 offsetFromHead = new Vector3(-0.24f, -0.62f, 0.2f);
        [Tooltip("Never lower than this above the floor (seated players).")]
        [SerializeField] private float minHeightAboveFloor = 0.45f;
        [Tooltip("How quickly the holster turns to follow the head (body yaw lags the head a little).")]
        [SerializeField] private float yawFollowSpeed = 6f;

        [Header("Behaviour")]
        [Tooltip("Seconds after the hand lets go before the scanner snaps back to the belt.")]
        [SerializeField] private float returnDelay = 0.35f;
        [Tooltip("If on, the holster only starts working after the scanner was picked up once " +
                 "(so it still starts on the tool stand, as the briefing says).")]
        [SerializeField] private bool armOnFirstPickup = true;

        public bool canProcess => isActiveAndEnabled;
        public bool IsHolding => socket != null && socket.hasSelection;

        /// <summary>Raised the first time the scanner is clipped to the belt.</summary>
        public event System.Action FirstHolstered;

        private XRSocketInteractor socket;
        private XRGrabInteractable toolGrab;
        private XROrigin origin;
        private Transform head;
        private bool armed;
        private bool firstHolsterDone;
        private float yaw;
        private Coroutine pendingReturn;

        private void Awake()
        {
            socket = GetComponent<XRSocketInteractor>();
            socket.showInteractableHoverMeshes = false;
            socket.recycleDelayTime = 0f;
            if (tool == null) tool = FindAnyObjectByType<GeoScanner>();
            if (tool != null) toolGrab = tool.GetComponent<XRGrabInteractable>();
            armed = !armOnFirstPickup;
            socket.socketActive = armed;
        }

        private void OnEnable()
        {
            socket.hoverFilters.Add(this);
            socket.selectFilters.Add(this);
            socket.selectEntered.AddListener(OnHolstered);
            if (tool != null)
            {
                tool.PickedUp += OnToolPickedUp;
                tool.Released += OnToolReleased;
            }
        }

        private void OnDisable()
        {
            socket.hoverFilters.Remove(this);
            socket.selectFilters.Remove(this);
            socket.selectEntered.RemoveListener(OnHolstered);
            if (tool != null)
            {
                tool.PickedUp -= OnToolPickedUp;
                tool.Released -= OnToolReleased;
            }
        }

        private void Start()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null) head = origin.Camera.transform;
            if (head != null) yaw = head.eulerAngles.y;
            Follow(true);
        }

        private void OnToolPickedUp()
        {
            if (pendingReturn != null) { StopCoroutine(pendingReturn); pendingReturn = null; }
            if (!armed)
            {
                armed = true;
                socket.socketActive = true;
            }
        }

        private void OnToolReleased()
        {
            if (!armed || toolGrab == null || !isActiveAndEnabled) return;
            if (pendingReturn != null) StopCoroutine(pendingReturn);
            pendingReturn = StartCoroutine(ReturnToBelt());
        }

        private IEnumerator ReturnToBelt()
        {
            yield return new WaitForSeconds(returnDelay);
            pendingReturn = null;
            if (toolGrab == null || toolGrab.isSelected) yield break; // grabbed again, or put somewhere else

            Follow(true);
            Transform attach = socket.attachTransform != null ? socket.attachTransform : transform;
            toolGrab.transform.position = attach.position;
            if (socket.interactionManager != null)
            {
                socket.interactionManager.SelectEnter((IXRSelectInteractor)socket, (IXRSelectInteractable)toolGrab);
            }
        }

        private void OnHolstered(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            if (tool != null) tool.ShowScreen("ON BELT\n<size=70%>Grab me from your left hip</size>", mirror: false);
            if (!firstHolsterDone)
            {
                firstHolsterDone = true;
                FirstHolstered?.Invoke();
                MissionComms comms = FindAnyObjectByType<MissionComms>();
                if (comms != null) comms.Say("scanner_holstered");
            }
        }

        private void LateUpdate()
        {
            Follow(false);
        }

        private void Follow(bool snap)
        {
            if (head == null) return;

            float headYaw = head.eulerAngles.y;
            yaw = snap ? headYaw : Mathf.LerpAngle(yaw, headYaw, 1f - Mathf.Exp(-yawFollowSpeed * Time.deltaTime));
            Quaternion body = Quaternion.Euler(0f, yaw, 0f);

            Vector3 pos = head.position + body * offsetFromHead;
            float floorY = origin != null ? origin.transform.position.y : pos.y - 1f;
            pos.y = Mathf.Max(pos.y, floorY + minHeightAboveFloor);

            transform.SetPositionAndRotation(pos, body);
        }

        // ---------- Filters: the holster only ever takes the scanner ----------
        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable) => Accepts(interactable.transform);
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => Accepts(interactable.transform);

        private bool Accepts(Transform t)
        {
            return armed && tool != null && t != null && t.GetComponentInParent<GeoScanner>() == tool;
        }
    }
}
