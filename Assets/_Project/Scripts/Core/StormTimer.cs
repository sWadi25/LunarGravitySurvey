using System;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Solar-storm countdown. Gives the mission its time pressure and its failure ending.
    /// Also drives the "storm approaching" mood: the sun tints and a rumble fades in near the end.
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

        [Tooltip("PLACEHOLDER: looping low rumble (2D ambient bed). Assign a clip to its AudioSource.")]
        [SerializeField] private AudioSource stormRumble;
        [SerializeField] private float rumbleMaxVolume = 0.6f;

        /// <summary>Raised once when the countdown hits zero.</summary>
        public event Action Expired;

        public float Duration => missionDuration;
        public float Remaining { get; private set; }
        public bool Running { get; private set; }

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
            if (stormRumble != null) stormRumble.volume = t * rumbleMaxVolume;
        }

        /// <summary>Formats seconds as m:ss.</summary>
        public static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
