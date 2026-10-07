using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LunarSurvey
{
    public enum MissionState
    {
        Briefing,   // start: player reads the briefing board, presses Begin
        InProgress, // middle: storm clock running, scan + collect
        Success,    // end A: all samples secured, lid closes
        Failed      // end B: storm arrived first
    }

    /// <summary>
    /// The single source of truth for the mission flow (beginning -> middle -> end).
    /// Other components raise events; this class decides what they mean.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }

        [Header("Systems (assigned by the scene builder)")]
        [SerializeField] private MissionComms comms;
        [SerializeField] private StormTimer storm;
        [SerializeField] private SampleContainer container;
        [SerializeField] private GeoScanner scanner;
        [SerializeField] private MissionHUD hud;

        [Header("Samples")]
        [Tooltip("Parent object of all rocks. Every RockSample under it takes part in the mission.")]
        [SerializeField] private Transform rocksRoot;
        [Tooltip("How many high-titanium samples to place. Should equal the container's socket count.")]
        [SerializeField] private int targetCount = 3;
        [Tooltip("If on, which rocks are targets is shuffled every playthrough (no memorising). " +
                 "If off, the first N rocks under Rocks Root are always the targets (handy for testing).")]
        [SerializeField] private bool randomiseTargets = true;

        public MissionState State { get; private set; } = MissionState.Briefing;
        public float ElapsedSeconds { get; private set; }
        public int RejectedAttempts { get; private set; }
        public int SecuredCount => container != null ? container.SecuredCount : 0;
        public int RequiredCount => container != null ? Mathf.Min(targetCount, container.Capacity) : targetCount;

        public event Action<MissionState> StateChanged;

        private readonly List<RockSample> rocks = new List<RockSample>();
        private bool scannerHintGiven;

        // Decoy rotation: low-Ti basalt appears most often because it looks like the target.
        private static readonly MineralType[] DecoyCycle =
        {
            MineralType.LowTitaniumBasalt, MineralType.Anorthosite, MineralType.LowTitaniumBasalt,
            MineralType.Breccia, MineralType.LowTitaniumBasalt, MineralType.Anorthosite, MineralType.Breccia
        };

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (storm != null) storm.Expired += OnStormExpired;
            if (container != null) container.SecuredCountChanged += OnSecuredCountChanged;
            if (scanner != null)
            {
                scanner.RockScanned += OnRockScanned;
                scanner.PickedUp += OnScannerPickedUp;
            }
        }

        private void OnDisable()
        {
            if (storm != null) storm.Expired -= OnStormExpired;
            if (container != null) container.SecuredCountChanged -= OnSecuredCountChanged;
            if (scanner != null)
            {
                scanner.RockScanned -= OnRockScanned;
                scanner.PickedUp -= OnScannerPickedUp;
            }
        }

        private void Start()
        {
            AssignMinerals();
            SetState(MissionState.Briefing);
            if (comms != null) comms.Say("briefing");
        }

        private void Update()
        {
            if (State == MissionState.InProgress) ElapsedSeconds += Time.deltaTime;
        }

        // ---------- Called from UI buttons (wired by the scene builder) ----------

        /// <summary>"BEGIN MISSION" button on the briefing board.</summary>
        public void BeginMission()
        {
            if (State != MissionState.Briefing) return;
            ElapsedSeconds = 0f;
            SetState(MissionState.InProgress);
            if (storm != null) storm.StartTimer();
            if (comms != null) comms.Say("begin");
        }

        /// <summary>"RESTART" button on the outcome board.</summary>
        public void RestartMission()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>"QUIT" button on the outcome board (does nothing inside the Editor).</summary>
        public void QuitApplication()
        {
            Application.Quit();
        }

        // ---------- Called by other mission components ----------

        public void ReportRejectedSample(RockSample rock)
        {
            if (State != MissionState.InProgress || rock == null) return;
            RejectedAttempts++;
            if (comms != null) comms.Say(rock.IsTarget ? "unscanned_reject" : "decoy_reject");
        }

        private void OnScannerPickedUp()
        {
            if (scannerHintGiven || State != MissionState.InProgress) return;
            scannerHintGiven = true;
            if (comms != null) comms.Say("scanner_hint");
        }

        private void OnRockScanned(RockSample rock, bool firstTime)
        {
            if (State != MissionState.InProgress || !firstTime) return;
            if (comms != null) comms.Say(rock.IsTarget ? "target_found" : "decoy_found");
        }

        private void OnSecuredCountChanged(int secured, int capacity)
        {
            if (hud != null) hud.SetSamples(secured, RequiredCount);
            if (State != MissionState.InProgress) return;

            if (secured >= RequiredCount)
            {
                Complete();
            }
            else if (comms != null)
            {
                comms.Say("sample_secured");
            }
        }

        private void OnStormExpired()
        {
            if (State != MissionState.InProgress) return;
            SetState(MissionState.Failed);
            if (comms != null) comms.Interrupt("storm_hit");
        }

        private void Complete()
        {
            if (storm != null) storm.StopTimer();
            if (container != null) container.CloseLid();
            SetState(MissionState.Success);
            if (comms != null) comms.Interrupt("mission_success");
        }

        private void SetState(MissionState next)
        {
            State = next;
            if (hud != null) hud.OnStateChanged(this, storm);
            StateChanged?.Invoke(next);
        }

        // ---------- Setup ----------

        private void AssignMinerals()
        {
            rocks.Clear();
            if (rocksRoot != null) rocks.AddRange(rocksRoot.GetComponentsInChildren<RockSample>());
            if (rocks.Count == 0)
            {
                Debug.LogWarning("[MissionManager] No RockSample found under 'Rocks Root'. Run Lunar Survey > Build menu or assign it.", this);
                return;
            }

            int targets = Mathf.Min(RequiredCount, rocks.Count);
            if (rocks.Count <= targets)
            {
                Debug.LogWarning("[MissionManager] Add more rocks than targets, otherwise there is nothing to scan for.", this);
            }

            // Sample IDs follow the rocks' fixed scene order, so an ID never gives away a target.
            var ids = new Dictionary<RockSample, string>();
            for (int i = 0; i < rocks.Count; i++) ids[rocks[i]] = $"LS-{i + 1:00}";

            if (randomiseTargets)
            {
                // Fisher-Yates shuffle so the targets are different every run.
                for (int i = rocks.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (rocks[i], rocks[j]) = (rocks[j], rocks[i]);
                }
            }

            int decoyIndex = 0;
            for (int i = 0; i < rocks.Count; i++)
            {
                MineralType type = i < targets ? MineralType.HighTitaniumBasalt : DecoyCycle[decoyIndex++ % DecoyCycle.Length];
                rocks[i].Configure(ids[rocks[i]], type);
            }

            if (hud != null) hud.SetSamples(0, RequiredCount);
        }
    }
}
