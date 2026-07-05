using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Purely a display layer - mirrors ScoreManager/LivesManager state onto TextMeshPro
    /// labels. No gameplay logic lives here, so a future animated HUD can replace this
    /// wholesale without touching ScoreManager/LivesManager.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public ScoreManager scoreManager;
        public LivesManager livesManager;

        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI livesText;

        private void OnEnable()
        {
            if (scoreManager != null) scoreManager.OnScoreChanged += HandleScoreChanged;
            if (livesManager != null) livesManager.OnLivesChanged += HandleLivesChanged;
        }

        private void OnDisable()
        {
            if (scoreManager != null) scoreManager.OnScoreChanged -= HandleScoreChanged;
            if (livesManager != null) livesManager.OnLivesChanged -= HandleLivesChanged;
        }

        private void Start()
        {
            // Show correct starting values immediately instead of waiting for the first change.
            if (scoreManager != null) HandleScoreChanged(scoreManager.CurrentScore);
            if (livesManager != null) HandleLivesChanged(livesManager.CurrentLives);
        }

        private void HandleScoreChanged(int score)
        {
            if (scoreText != null) scoreText.text = "Score: " + score;
        }

        private void HandleLivesChanged(int lives)
        {
            if (livesText != null) livesText.text = "Lives: " + lives;
        }
    }
}
