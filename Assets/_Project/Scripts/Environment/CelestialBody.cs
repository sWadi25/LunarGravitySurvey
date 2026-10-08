using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Keeps a sky object (the Sun disc, the Earth) "at infinity": it is re-positioned around the camera
    /// every frame, so it never gets closer or shifts when the player walks or teleports - just like the
    /// real Sun and Earth, which are far beyond any parallax.
    ///
    /// Sun: <see cref="Mode.FollowLight"/> puts the disc exactly where the Directional Light comes from,
    /// so the visible Sun, the shadows and the lit side of the Earth always agree.
    /// </summary>
    public class CelestialBody : MonoBehaviour
    {
        public enum Mode
        {
            FollowLight,   // opposite the light's forward axis (the Sun)
            FixedDirection // a fixed world direction (the Earth)
        }

        [SerializeField] private Mode mode = Mode.FixedDirection;
        [SerializeField] private Light lightSource;
        [SerializeField] private Vector3 direction = new Vector3(0.35f, 0.45f, 1f);
        [Tooltip("Metres from the camera. Must be less than the camera's far clip plane.")]
        [SerializeField] private float distance = 450f;
        [Tooltip("Turn a flat quad to face the camera (used for the Sun disc).")]
        [SerializeField] private bool billboard;
        [Tooltip("Slow spin around the object's own up axis (degrees per second). 0 = off.")]
        [SerializeField] private float spinDegreesPerSecond;

        private Camera cam;

        public Vector3 Direction => mode == Mode.FollowLight && lightSource != null
            ? -lightSource.transform.forward
            : direction.normalized;

        private void LateUpdate()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            if (cam == null) return;

            Vector3 dir = Direction;
            float d = Mathf.Min(distance, cam.farClipPlane * 0.95f);
            transform.position = cam.transform.position + dir * d;

            if (billboard)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            else if (spinDegreesPerSecond != 0f)
            {
                transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
            }
        }
    }
}
