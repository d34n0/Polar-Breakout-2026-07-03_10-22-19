using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Read-only leaderboard shown in the Options menu's High Scores tab - refreshes from
    /// HighScoreManager every time this panel becomes active, so it always reflects the latest
    /// standings without needing an explicit change event. Also owns the Clear button, which
    /// wipes the table and immediately refreshes the display.
    /// </summary>
    public class HighScorePanel : MonoBehaviour
    {
        [Tooltip("One row per rank, in order (rank 1 first). Shows '---' for any rank not yet filled.")]
        public TextMeshProUGUI[] rowTexts;
        public Button clearButton;

        private void Awake()
        {
            if (clearButton != null) clearButton.onClick.AddListener(ClearAndRefresh);
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void ClearAndRefresh()
        {
            HighScoreManager.ClearAll();
            Refresh();
        }

        private void Refresh()
        {
            var entries = HighScoreManager.Load();
            for (int i = 0; i < rowTexts.Length; i++)
                rowTexts[i].text = i < entries.Count ? $"{i + 1}. {entries[i].Name} - {entries[i].Score}" : $"{i + 1}. ---";
        }
    }
}
