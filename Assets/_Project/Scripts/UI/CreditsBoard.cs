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
            target.text = creditsFile != null
                ? creditsFile.text
                : "CREDITS\n\n(Assign Assets/_Project/Data/Credits.txt to the CreditsBoard component.)";
        }
    }
}
