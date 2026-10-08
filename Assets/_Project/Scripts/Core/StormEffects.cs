using Unity.XR.CoreUtils;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// Makes the approaching SOLAR STORM felt, using effects that are physically defensible on the Moon.
    ///
    /// What the "storm" is: a solar particle event (SPE) - a burst of high-energy protons from a solar flare /
    /// coronal mass ejection. The Moon has no atmosphere and no wind, so there is no dust storm and no rumble.
    /// The real danger is RADIATION, which is why Houston orders the astronaut back to the lander.
    ///
    /// Effects, all scaled by <see cref="StormTimer.Intensity"/> (0 = calm, 1 = storm has arrived):
    ///  - Dosimeter clicks (Geiger counter in the suit) - rate rises from a slow background tick to a rattle.
    ///  - Radio static on the comms (solar radio bursts really do this) - the StormTimer's 2D static loop.
    ///  - Cosmic-ray "light flashes": brief white streaks in the eyes, as reported by Apollo crews.
    ///  - Levitating dust: fine regolith hopping in slow ballistic arcs near the ground. Electrostatic
    ///    charging of the surface by the solar wind is a real (if subtle) process - Surveyor and Apollo 17
    ///    observations of "horizon glow" are attributed to it - so this is exaggerated, not invented.
    ///  - Gold sun-visor tint at the edges of the view, plus a slightly brighter Sun and Sun glow.
    /// The effects fade back out if the mission succeeds before the storm hits.
    /// </summary>
    public class StormEffects : MonoBehaviour
    {
        [SerializeField] private StormTimer storm;
        [SerializeField] private MissionComms comms;

        [Header("Sun")]
        [SerializeField] private Light sunLight;
        [SerializeField] private float sunIntensityBoost = 0.25f;
        [Tooltip("The Sun disc quad (scaled up a little as the flare builds).")]
        [SerializeField] private Transform sunDisc;
        [SerializeField] private float sunGlowBoost = 0.6f;

        [Header("Particles")]
        [Tooltip("Ground-level dust emitter. It is kept centred on the player.")]
        [SerializeField] private ParticleSystem levitatingDust;
        [SerializeField] private float maxDustRate = 350f;
        [Tooltip("Tiny streaks in front of the eyes (child of the camera).")]
        [SerializeField] private ParticleSystem cosmicFlashes;
        [SerializeField] private float maxFlashRate = 4f;

        [Header("Visor")]
        [Tooltip("Camera-attached quad with a radial gradient texture.")]
        [SerializeField] private Renderer visorOverlay;
        [SerializeField] private Color visorColor = new Color(1f, 0.72f, 0.3f, 1f);
        [SerializeField] private float maxVisorAlpha = 0.18f;

        [Header("Dosimeter (2D, inside the suit)")]
        [SerializeField] private AudioSource dosimeterSource;
        [SerializeField] private AudioClip geigerClick;
        [Tooltip("Clicks per second when calm (natural background radiation).")]
        [SerializeField] private float backgroundClickRate = 0.3f;
        [Tooltip("Clicks per second when the storm arrives.")]
        [SerializeField] private float maxClickRate = 22f;
        [Range(0f, 1f)] [SerializeField] private float clickVolume = 0.35f;

        public float Intensity { get; private set; }

        private XROrigin origin;
        private Transform head;
        private float sunBaseIntensity = -1f;
        private Vector3 sunDiscBaseScale;
        private bool announced;
        private MaterialPropertyBlock mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Start()
        {
            origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && origin.Camera != null) head = origin.Camera.transform;
            if (storm == null) storm = FindAnyObjectByType<StormTimer>();
            if (comms == null) comms = FindAnyObjectByType<MissionComms>();
            if (sunLight != null) sunBaseIntensity = sunLight.intensity;
            if (sunDisc != null) sunDiscBaseScale = sunDisc.localScale;
            mpb = new MaterialPropertyBlock();
            Apply(0f);
        }

        private void Update()
        {
            float target = storm != null ? storm.Intensity : 0f;
            MissionManager m = MissionManager.Instance;
            if (m != null && m.State == MissionState.Success) target = 0f; // made it - calm down
            Intensity = Mathf.MoveTowards(Intensity, target, Time.deltaTime * 0.25f);

            if (!announced && target > 0.02f && m != null && m.State == MissionState.InProgress)
            {
                announced = true;
                if (comms != null) comms.Say("storm_radiation");
            }

            Apply(Intensity);
            TickDosimeter(Intensity);
        }

        private void LateUpdate()
        {
            // keep the dust around the player, on the ground
            if (levitatingDust != null && head != null && origin != null)
            {
                levitatingDust.transform.position = new Vector3(head.position.x, origin.transform.position.y + 0.02f, head.position.z);
            }
        }

        private void Apply(float t)
        {
            if (sunLight != null && sunBaseIntensity >= 0f) sunLight.intensity = sunBaseIntensity * (1f + sunIntensityBoost * t);
            if (sunDisc != null) sunDisc.localScale = sunDiscBaseScale * (1f + sunGlowBoost * t);

            SetRate(levitatingDust, maxDustRate * t * t);
            SetRate(cosmicFlashes, t > 0.15f ? maxFlashRate * t : 0f);

            if (visorOverlay != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock(); // also survives a script reload in Play mode
                Color c = visorColor;
                c.a = maxVisorAlpha * t;
                visorOverlay.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, c);
                visorOverlay.SetPropertyBlock(mpb);
                visorOverlay.enabled = t > 0.001f;
            }
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            ParticleSystem.EmissionModule e = ps.emission;
            e.rateOverTime = rate;
            if (rate > 0f && !ps.isPlaying) ps.Play();
        }

        private void TickDosimeter(float t)
        {
            if (dosimeterSource == null || geigerClick == null) return;
            MissionManager m = MissionManager.Instance;
            if (m != null && m.State == MissionState.Briefing) return;

            float rate = Mathf.Lerp(backgroundClickRate, maxClickRate, t * t);
            // Poisson process: random, uneven clicks like a real Geiger counter
            float p = rate * Time.deltaTime;
            int clicks = 0;
            while (p > 0f && clicks < 3)
            {
                if (Random.value < Mathf.Min(p, 1f)) clicks++;
                p -= 1f;
            }
            for (int i = 0; i < clicks; i++)
            {
                dosimeterSource.pitch = Random.Range(0.85f, 1.25f);
                dosimeterSource.PlayOneShot(geigerClick, clickVolume * Random.Range(0.7f, 1f));
            }
        }
    }
}
