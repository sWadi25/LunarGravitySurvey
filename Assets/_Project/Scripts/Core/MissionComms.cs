using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>One radio message from Mission Control ("Houston").</summary>
    [System.Serializable]
    public class CommsLine
    {
        [Tooltip("Key used by code, e.g. \"briefing\". Do not rename unless you also change the code that calls it.")]
        public string id;

        [TextArea(2, 4)]
        [Tooltip("Subtitle shown on the wrist display and status board. Keep it identical to the recorded audio.")]
        public string subtitle;

        [Tooltip("PLACEHOLDER: drag a recorded voice clip here (Assets/_Project/Audio/Comms). " +
                 "If empty, the subtitle is shown for 'Hold Seconds' instead, so the mission still works.")]
        public AudioClip clip;

        [Tooltip("How long the subtitle stays up when no clip is assigned.")]
        public float holdSeconds = 4f;
    }

    /// <summary>
    /// Mission Control radio. Plays queued voice lines with an optional radio beep, and mirrors every
    /// line as a subtitle on the world-space displays (wrist + status board).
    ///
    /// AUDIO DESIGN (for the presentation): the radio is heard *inside the astronaut's helmet*, so the
    /// voice source is 2D (head-locked) on purpose - that is how a real suit radio sounds. Every
    /// *environmental* sound (scanner, container, lander beacon, storm) is a 3D spatial source instead.
    /// </summary>
    public class MissionComms : MonoBehaviour
    {
        [Header("Audio")]
        [Tooltip("2D AudioSource used for voice lines (spatial blend = 0, in-helmet radio).")]
        [SerializeField] private AudioSource voiceSource;

        [Tooltip("PLACEHOLDER: short radio 'chirp' played before each message (Quindar tone). Optional.")]
        [SerializeField] private AudioClip radioBeep;

        [Header("Subtitles")]
        [Tooltip("Every text here shows the current message. Filled in by the scene builder.")]
        [SerializeField] private List<TMP_Text> subtitleTargets = new List<TMP_Text>();

        [Tooltip("Seconds the last subtitle stays visible after its audio ends.")]
        [SerializeField] private float subtitleLinger = 3f;

        [Header("Script")]
        [Tooltip("All Houston lines. Record matching audio and drag each clip into its 'Clip' slot.")]
        [SerializeField] private List<CommsLine> lines = DefaultLines();

        private readonly Queue<CommsLine> queue = new Queue<CommsLine>();
        private Coroutine player;

        public bool IsSpeaking => player != null;

        private void Awake()
        {
            if (voiceSource == null)
            {
                voiceSource = gameObject.AddComponent<AudioSource>();
            }
            voiceSource.playOnAwake = false;
            voiceSource.spatialBlend = 0f; // in-helmet radio, see class summary
            SetSubtitle(string.Empty);
        }

        /// <summary>Queue a line by id. Lines play one after another.</summary>
        public void Say(string id)
        {
            CommsLine line = Find(id);
            if (line == null) return;
            queue.Enqueue(line);
            if (player == null) player = StartCoroutine(PlayQueue());
        }

        /// <summary>Clear anything queued and play this line immediately (used for mission end).</summary>
        public void Interrupt(string id)
        {
            CommsLine line = Find(id);
            if (line == null) return;
            queue.Clear();
            if (player != null) StopCoroutine(player);
            voiceSource.Stop();
            queue.Enqueue(line);
            player = StartCoroutine(PlayQueue());
        }

        private CommsLine Find(string id)
        {
            foreach (CommsLine l in lines)
            {
                if (l != null && l.id == id) return l;
            }
            Debug.LogWarning($"[MissionComms] No comms line with id '{id}'. Add it to the Lines list.", this);
            return null;
        }

        private IEnumerator PlayQueue()
        {
            while (queue.Count > 0)
            {
                CommsLine line = queue.Dequeue();
                SetSubtitle("HOUSTON: " + line.subtitle);

                if (radioBeep != null)
                {
                    voiceSource.PlayOneShot(radioBeep);
                    yield return new WaitForSeconds(radioBeep.length);
                }

                if (line.clip != null)
                {
                    voiceSource.clip = line.clip;
                    voiceSource.Play();
                    yield return new WaitForSeconds(line.clip.length + 0.3f);
                }
                else
                {
                    yield return new WaitForSeconds(Mathf.Max(1f, line.holdSeconds));
                }
            }

            yield return new WaitForSeconds(subtitleLinger);
            SetSubtitle(string.Empty);
            player = null;
        }

        private void SetSubtitle(string text)
        {
            foreach (TMP_Text t in subtitleTargets)
            {
                if (t != null) t.text = text;
            }
        }

        /// <summary>Default script. Edit freely in the Inspector; keep the ids.</summary>
        private static List<CommsLine> DefaultLines()
        {
            return new List<CommsLine>
            {
                new CommsLine { id = "briefing", holdSeconds = 7f,
                    subtitle = "Survey One, Houston. Welcome to the surface. Read the briefing board in front of you, then press Begin Mission." },
                new CommsLine { id = "begin", holdSeconds = 7f,
                    subtitle = "Clock is running. A solar storm is inbound. Grab the scanner from the tool stand and find three high-titanium basalt samples." },
                new CommsLine { id = "scanner_hint", holdSeconds = 6f,
                    subtitle = "Point the scanner at a rock and pull the trigger. Dark rocks are basalt, but only the scanner can confirm titanium content." },
                new CommsLine { id = "target_found", holdSeconds = 5f,
                    subtitle = "That's a match. High-titanium basalt. Bring it back and lock it into the sample container." },
                new CommsLine { id = "decoy_found", holdSeconds = 4f,
                    subtitle = "Negative, that one is not on the manifest. Keep looking." },
                new CommsLine { id = "unscanned_reject", holdSeconds = 4f,
                    subtitle = "Container won't accept an unverified sample. Scan it first." },
                new CommsLine { id = "decoy_reject", holdSeconds = 4f,
                    subtitle = "That rock isn't on the manifest. We only have room for high-titanium basalt." },
                new CommsLine { id = "sample_secured", holdSeconds = 3f,
                    subtitle = "Sample secured. Good work." },
                new CommsLine { id = "storm_3min", holdSeconds = 4f,
                    subtitle = "Three minutes until the storm front arrives." },
                new CommsLine { id = "storm_1min", holdSeconds = 4f,
                    subtitle = "One minute, Survey One. Start heading back to the lander." },
                new CommsLine { id = "storm_30s", holdSeconds = 3f,
                    subtitle = "Thirty seconds! Secure what you have!" },
                new CommsLine { id = "mission_success", holdSeconds = 8f,
                    subtitle = "Container sealed. All three samples secured before the storm. Outstanding work, Survey One. Mission complete." },
                new CommsLine { id = "storm_hit", holdSeconds = 8f,
                    subtitle = "Storm front has arrived. Abort the survey and shelter in the lander. We'll try again on the next window." },
            };
        }
    }
}
