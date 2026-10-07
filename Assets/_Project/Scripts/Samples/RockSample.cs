using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace LunarSurvey
{
    /// <summary>Rock types found at the landing site (simplified from real Apollo sample classes).</summary>
    public enum MineralType
    {
        HighTitaniumBasalt, // mission target: rich in ilmenite, which can be processed for oxygen
        LowTitaniumBasalt,  // looks almost identical to the target - only the scanner tells them apart
        Anorthosite,        // pale highland rock
        Breccia             // broken, re-welded fragments from impacts
    }

    /// <summary>
    /// A collectable rock. Holds its (hidden) mineral identity, which the GeoScanner reveals.
    /// The MissionManager assigns minerals at mission start, so targets change every playthrough.
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class RockSample : MonoBehaviour
    {
        [SerializeField] private string sampleId = "LS-00";
        [SerializeField] private MineralType mineral = MineralType.Anorthosite;
        [Tooltip("Titanium dioxide content in weight-percent. Set automatically from the mineral type.")]
        [SerializeField] private float titaniumPercent = 0.4f;

        [Header("Visuals")]
        [Tooltip("Renderers that change colour / glow. Filled automatically if left empty.")]
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Color targetGlowColor = new Color(0.2f, 0.85f, 1f);
        [SerializeField] private float targetGlowIntensity = 1.6f;

        public string SampleId => sampleId;
        public MineralType Mineral => mineral;
        public float TitaniumPercent => titaniumPercent;
        public bool IsTarget => mineral == MineralType.HighTitaniumBasalt;
        public bool IsScanned { get; private set; }

        private void Awake()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>();
            }
        }

        /// <summary>Called by MissionManager when it hands out minerals.</summary>
        public void Configure(string id, MineralType type)
        {
            sampleId = id;
            mineral = type;
            titaniumPercent = RandomTitanium(type);
            IsScanned = false;
            ApplyAppearance();
        }

        /// <summary>Marks the rock as scanned and returns the text for the scanner screen.</summary>
        public string Scan()
        {
            IsScanned = true;
            if (IsTarget) SetGlow(true);

            string verdict = IsTarget ? "<color=#4CFF7A>>> TARGET - COLLECT</color>" : "<color=#FF6B5A>>> NOT REQUIRED</color>";
            return $"SAMPLE {sampleId}\n{DisplayName(mineral)}\nTiO<sub>2</sub>: {titaniumPercent:0.0} wt%\n{verdict}";
        }

        public static string DisplayName(MineralType type)
        {
            switch (type)
            {
                case MineralType.HighTitaniumBasalt: return "MARE BASALT (HIGH-Ti)";
                case MineralType.LowTitaniumBasalt: return "MARE BASALT (LOW-Ti)";
                case MineralType.Anorthosite: return "ANORTHOSITE";
                case MineralType.Breccia: return "IMPACT BRECCIA";
                default: return type.ToString().ToUpperInvariant();
            }
        }

        // Ranges are simplified but in line with published Apollo sample values.
        private static float RandomTitanium(MineralType type)
        {
            switch (type)
            {
                case MineralType.HighTitaniumBasalt: return Random.Range(9f, 13f);
                case MineralType.LowTitaniumBasalt: return Random.Range(1.5f, 5f);
                case MineralType.Anorthosite: return Random.Range(0.1f, 0.6f);
                case MineralType.Breccia: return Random.Range(1f, 3f);
                default: return 0f;
            }
        }

        // Basalts are dark, anorthosite is pale, breccia is in between. High- and low-Ti basalt are
        // deliberately almost the same colour, so the player has to use the scanner.
        private static Color BaseColor(MineralType type)
        {
            switch (type)
            {
                case MineralType.HighTitaniumBasalt: return new Color(0.17f, 0.17f, 0.18f);
                case MineralType.LowTitaniumBasalt: return new Color(0.21f, 0.20f, 0.19f);
                case MineralType.Anorthosite: return new Color(0.66f, 0.66f, 0.64f);
                case MineralType.Breccia: return new Color(0.42f, 0.40f, 0.37f);
                default: return Color.grey;
            }
        }

        private void ApplyAppearance()
        {
            Color c = BaseColor(mineral);
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Material m = r.material; // per-rock material instance
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); // URP Lit
                else m.color = c;
            }
            SetGlow(false);
        }

        private void SetGlow(bool on)
        {
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Material m = r.material;
                if (!m.HasProperty("_EmissionColor")) continue;
                if (on)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", targetGlowColor * targetGlowIntensity);
                }
                else
                {
                    m.SetColor("_EmissionColor", Color.black);
                }
            }
        }
    }
}
