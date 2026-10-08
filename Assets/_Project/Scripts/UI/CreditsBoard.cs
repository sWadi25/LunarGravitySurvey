using TMPro;
using UnityEngine;

namespace LunarSurvey
{
    /// <summary>
    /// In-experience credits board (the brief requires credits inside the build AND in the slides).
    /// Reads Assets/_Project/Data/Credits.txt so there is one list to maintain.
    ///
    /// TO UPDATE: edit Credits.txt (not this script). Keep it identical to CREDITS.md in the repo root.
    /// </summary>
    public class CreditsBoard : MonoBehaviour
    {
        [SerializeField] private TextAsset creditsFile;
        [SerializeField] private TMP_Text target;

        private void Awake()
        {
            if (target == null) target = GetComponentInChildren<TMP_Text>();
            if (target == null) return;
            // Shrink to fit if the list grows, instead of spilling outside the board.
            float max = target.fontSize;
            target.enableAutoSizing = true;
            target.fontSizeMax = max;
            target.fontSizeMin = Mathf.Min(10f, max);
            target.overflowMode = TextOverflowModes.Truncate;
            target.text = creditsFile != null
                ? creditsFile.text
                : "CREDITS\n\n(Assign Assets/_Project/Data/Credits.txt to the CreditsBoard component.)";
        }
    }
}
