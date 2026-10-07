using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Drives every world-space display: briefing board, status board above the container,
    /// wrist display, and the outcome board. All panels live in the 3D world (no screen overlay).
    /// </summary>
    public class MissionHUD : MonoBehaviour
    {
        [Header("Briefing board (start)")]
        [SerializeField] private GameObject briefingRoot;
        [Tooltip("Body text of the briefing. Any '{STORM}' in it is replaced with the storm duration (e.g. 5:00).")]
        [SerializeField] private TMP_Text briefingBody;

        [Header("Live status (status board + wrist)")]
        [Tooltip("Every text here shows the storm countdown.")]
        [SerializeField] private List<TMP_Text> timerTexts = new List<TMP_Text>();
        [Tooltip("Every text here shows 'samples secured'.")]
        [SerializeField] private List<TMP_Text> sampleTexts = new List<TMP_Text>();
        [SerializeField] private Color timerCalmColor = new Color(0.85f, 0.95f, 1f);
        [SerializeField] private Color timerUrgentColor = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private float urgentBelowSeconds = 60f;

        [Header("Outcome board (end)")]
        [SerializeField] private GameObject outcomeRoot;
        [SerializeField] private TMP_Text outcomeTitle;
        [SerializeField] private TMP_Text outcomeBody;
        [Tooltip("When the mission ends, the outcome board is placed this far in front of the player " +
                 "(then stays put in the world - it is NOT head-locked, which is more comfortable).")]
        [SerializeField] private float outcomeDistance = 1.6f;

        private StormTimer storm;
        private MissionManager mission;

        private void Awake()
        {
            if (outcomeRoot != null) outcomeRoot.SetActive(false);
        }

        private void Update()
        {
            if (storm == null || mission == null) return;
            bool live = mission.State == MissionState.InProgress;
            float remaining = mission.State == MissionState.Briefing ? storm.Duration : storm.Remaining;
            string label = "STORM ETA  " + StormTimer.Format(remaining);
            Color c = live && remaining <= urgentBelowSeconds ? timerUrgentColor : timerCalmColor;
            foreach (TMP_Text t in timerTexts)
            {
                if (t == null) continue;
                t.text = label;
                t.color = c;
            }
        }

        public void SetSamples(int secured, int required)
        {
            string label = $"SAMPLES  {secured} / {required}";
            foreach (TMP_Text t in sampleTexts)
            {
                if (t != null) t.text = label;
            }
        }

        public void OnStateChanged(MissionManager manager, StormTimer timer)
        {
            mission = manager;
            storm = timer;

            if (briefingRoot != null) briefingRoot.SetActive(manager.State == MissionState.Briefing);
            if (manager.State == MissionState.Briefing && briefingBody != null && timer != null)
            {
                briefingBody.text = briefingBody.text.Replace("{STORM}", StormTimer.Format(timer.Duration));
            }

            if (manager.State == MissionState.Success || manager.State == MissionState.Failed)
            {
                ShowOutcome(manager, timer);
            }
        }

        private void ShowOutcome(MissionManager manager, StormTimer timer)
        {
            if (outcomeRoot == null) return;
            PlaceInFrontOfPlayer(outcomeRoot.transform);
            outcomeRoot.SetActive(true);

            bool success = manager.State == MissionState.Success;
            if (outcomeTitle != null)
            {
                outcomeTitle.text = success ? "MISSION COMPLETE" : "MISSION ABORTED";
                outcomeTitle.color = success ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 0.45f, 0.35f);
            }

            if (outcomeBody != null)
            {
                int scans = 0;
                GeoScanner scanner = FindAnyObjectByType<GeoScanner>();
                if (scanner != null) scans = scanner.ScanCount;

                string rating = success ? Rating(manager, timer) : "-";
                outcomeBody.text =
                    (success
                        ? "All samples secured before the solar storm.\n\n"
                        : "The solar storm arrived before the samples were secured.\n\n") +
                    $"Samples secured:   {manager.SecuredCount} / {manager.RequiredCount}\n" +
                    $"Mission time:        {StormTimer.Format(manager.ElapsedSeconds)}\n" +
                    $"Time to spare:       {StormTimer.Format(timer != null ? timer.Remaining : 0f)}\n" +
                    $"Scans taken:          {scans}\n" +
                    $"Rejected samples:  {manager.RejectedAttempts}\n\n" +
                    $"Rating:  {rating}";
            }
        }

        private static string Rating(MissionManager m, StormTimer timer)
        {
            int stars = 1;
            if (m.RejectedAttempts == 0) stars++;
            if (timer != null && timer.Remaining >= timer.Duration * 0.3f) stars++;
            return new string('*', stars) + new string('-', 3 - stars) + "  (" + stars + "/3)";
        }

        private void PlaceInFrontOfPlayer(Transform panel)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            panel.position = cam.transform.position + forward * outcomeDistance + Vector3.down * 0.1f;
            panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
