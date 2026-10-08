using System;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Solar-storm countdown. Gives the mission its time pressure and its failure ending.
    ///
    /// The "storm" is a SOLAR PARTICLE EVENT (radiation from a solar flare / CME), not a dust storm - the Moon
    /// has no air or wind. <see cref="Intensity"/> ramps up over the last <c>stormBuildUp</c> seconds and drives
    /// <see cref="StormEffects"/> (dosimeter clicks, cosmic-ray flashes, levitating dust, visor tint) plus the
    /// radio-static loop on this component.
    /// </summary>
    public class StormTimer : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Total time the player has, in seconds. 300 = 5 minutes. Tune this after the walkthrough test.")]
        [SerializeField] private float missionDuration = 300f;

        [Header("Radio warnings (MissionComms line ids)")]
        [SerializeField] private MissionComms comms;

        [Header("Storm mood (optional)")]
        [Tooltip("The scene's sun (Directional Light). Tints toward the storm colour in the final stretch.")]
        [SerializeField] private Light sunLight;
        [SerializeField] private Color stormSunColor = new Color(1f, 0.62f, 0.45f);
        [Tooltip("Seconds remaining when the sun starts tinting / the rumble fades in.")]
        [SerializeField] private float moodStartsAt = 60f;

        [Tooltip("Looping radio static (2D, in the helmet). Solar radio bursts interfere with comms.")]
        [SerializeField] private AudioSource stormRumble;
        [SerializeField] private float rumbleMaxVolume = 0.35f;

        [Tooltip("Seconds before the storm arrives when the radiation effects start building (StormEffects).")]
        [SerializeField] private float stormBuildUp = 150f;

        /// <summary>Raised once when the countdown hits zero.</summary>
        public event Action Expired;

        public float Duration => missionDuration;
        public float Remaining { get; private set; }
        public bool Running { get; private set; }

        /// <summary>0 = calm ... 1 = storm has arrived. Eases in over the last <c>stormBuildUp</c> seconds.</summary>
        public float Intensity
        {
            get
            {
                if (!Running && Remaining >= missionDuration) return 0f; // not started yet
                float t = stormBuildUp > 0f ? Mathf.Clamp01(1f - Remaining / stormBuildUp) : 0f;
                return t * t * (3f - 2f * t); // smoothstep
            }
        }

        private Color sunStartColor = Color.white;
        private bool warned180, warned60, warned30;

        private void Awake()
        {
            Remaining = missionDuration;
            if (sunLight != null) sunStartColor = sunLight.color;
            if (stormRumble != null)
            {
                stormRumble.loop = true;
                stormRumble.volume = 0f;
            }
        }

        public void StartTimer()
        {
            Remaining = missionDuration;
            warned180 = missionDuration <= 180f; // skip warnings longer than the mission itself
            warned60 = missionDuration <= 60f;
            warned30 = missionDuration <= 30f;
            Running = true;
            if (stormRumble != null && stormRumble.clip != null && !stormRumble.isPlaying) stormRumble.Play();
        }

        public void StopTimer()
        {
            Running = false;
        }

        /// <summary>Testing / demo shortcut: jump the countdown so the storm effects can be shown quickly.</summary>
        public void SkipTo(float secondsRemaining)
        {
            Remaining = Mathf.Clamp(secondsRemaining, 0f, missionDuration);
        }

        [ContextMenu("Debug: skip to 0:45 remaining")]
        private void DebugSkipTo45() => SkipTo(45f);

        private void Update()
        {
            if (!Running) return;

            Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);

            if (!warned180 && Remaining <= 180f) { warned180 = true; Warn("storm_3min"); }
            if (!warned60 && Remaining <= 60f) { warned60 = true; Warn("storm_1min"); }
            if (!warned30 && Remaining <= 30f) { warned30 = true; Warn("storm_30s"); }

            UpdateMood();

            if (Remaining <= 0f)
            {
                Running = false;
                Expired?.Invoke();
            }
        }

        private void Warn(string id)
        {
            if (comms != null) comms.Say(id);
        }

        private void UpdateMood()
        {
            // 0 = calm, 1 = storm arriving
            float t = moodStartsAt > 0f ? Mathf.Clamp01(1f - Remaining / moodStartsAt) : 0f;
            if (sunLight != null) sunLight.color = Color.Lerp(sunStartColor, stormSunColor, t);
            if (stormRumble != null) stormRumble.volume = Intensity * rumbleMaxVolume;
        }

        /// <summary>Formats seconds as m:ss.</summary>
        public static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
