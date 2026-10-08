using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Hinged scanner display that tilts itself toward the player's eyes.
    ///
    /// WHY (play-test): the screen had a fixed angle, so whenever the hand was held high or pitched down
    /// (the XR Interaction Simulator's default controller pose is at eye height, nose 43 deg down) the screen
    /// faced the sky and was seen edge-on. The hinge now rotates around the scanner's left-right axis so the
    /// readout always faces the head, within mechanical limits.
    ///
    /// SETUP: on an empty "Screen Hinge" placed at the screen's bottom edge; the Screen canvas and its housing
    /// are children, standing upright (+Y) with the readable side facing -Z. The editor fix script builds this.
    /// </summary>
    public class ScannerScreenTilt : MonoBehaviour
    {
        [Tooltip("Lowest / highest hinge angle in degrees (0 = upright, 90 = lying flat facing up).")]
        [SerializeField] private float minAngle = -15f;
        [SerializeField] private float maxAngle = 95f;
        [Tooltip("Higher = snappier.")]
        [SerializeField] private float followSpeed = 12f;

        private Transform head;
        private float angle = 35f;

        private void LateUpdate()
        {
            if (head == null)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                head = cam.transform;
            }

            Transform body = transform.parent != null ? transform.parent : transform;
            Vector3 d = body.InverseTransformDirection(head.position - transform.position);
            // readable normal for hinge angle t is (0, sin t, -cos t) in the scanner's local space
            float target = Mathf.Atan2(d.y, -d.z) * Mathf.Rad2Deg;
            target = Mathf.Clamp(target, minAngle, maxAngle);
            angle = Mathf.LerpAngle(angle, target, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            transform.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
