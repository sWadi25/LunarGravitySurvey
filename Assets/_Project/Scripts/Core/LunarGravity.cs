using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Sets the global physics gravity to lunar surface gravity (1.62 m/s², about 1/6 of Earth's).
    ///
    /// WHY GLOBAL: Physics.gravity drives every Rigidbody (rocks, scanner, anything dropped or thrown),
    /// and the XRI Gravity Provider also reads Physics.gravity for the player's own fall speed.
    /// One value therefore gives consistent "Moon weight" to the whole scene - a deliberate design
    /// choice for the presentation, not a shortcut.
    ///
    /// SETUP: the Lunar Survey > Build menu adds this to the "[Managers]" object. Nothing to do manually.
    /// </summary>
    [DefaultExecutionOrder(-1000)] // run before anything that spawns or simulates physics objects
    public class LunarGravity : MonoBehaviour
    {
        public const float MoonGravity = 1.62f;
        public const float EarthGravity = 9.81f;

        [Tooltip("Downward acceleration in m/s². 1.62 = Moon. Change only for testing.")]
        [SerializeField] private float gravity = MoonGravity;

        private void Awake()
        {
            Apply();
        }

        private void OnValidate()
        {
            // Lets you tweak the value live in Play Mode from the Inspector.
            if (Application.isPlaying) Apply();
        }

        private void Apply()
        {
            Physics.gravity = new Vector3(0f, -Mathf.Abs(gravity), 0f);
        }

        // NOTE: we deliberately do NOT restore Earth gravity in OnDestroy. When the mission restarts
        // (scene reload) the old scene's OnDestroy can run AFTER the new scene's Awake, which would
        // leave the new mission on Earth gravity. We only restore it when the app / Play Mode stops,
        // so the project setting is not left on Moon gravity inside the Editor.
        private void OnApplicationQuit()
        {
            Physics.gravity = new Vector3(0f, -EarthGravity, 0f);
        }
    }
}
