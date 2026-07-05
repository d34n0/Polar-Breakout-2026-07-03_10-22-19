using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Purely a display layer - mirrors ScoreManager/LivesManager/LevelManager state onto
    /// TextMeshPro labels. No gameplay logic lives here, so a future animated HUD can replace
    /// this wholesale without touching those managers. The score label follows ScoreManager's
    /// DisplayedScore (which ticks up over several frames) rather than CurrentScore, so the
    /// visible number rolls up instead of snapping instantly.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        public ScoreManager scoreManager;
        public LivesManager livesManager;
        public LevelManager levelManager;

        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI livesText;
        public TextMeshProUGUI stageText;

        private void OnEnable()
        {
            if (scoreManager != null) scoreManager.OnDisplayedScoreChanged += HandleScoreChanged;
            if (livesManager != null) livesManager.OnLivesChanged += HandleLivesChanged;
            if (levelManager != null) levelManager.OnStageChanged += HandleStageChanged;
        }

        private void OnDisable()
        {
            if (scoreManager != null) scoreManager.OnDisplayedScoreChanged -= HandleScoreChanged;
            if (livesManager != null) livesManager.OnLivesChanged -= HandleLivesChanged;
            if (levelManager != null) levelManager.OnStageChanged -= HandleStageChanged;
        }

        private void Start()
        {
            // Show correct starting values immediately instead of waiting for the first change.
            if (scoreManager != null) HandleScoreChanged(scoreManager.DisplayedScore);
            if (livesManager != null) HandleLivesChanged(livesManager.CurrentLives);
            if (levelManager != null) HandleStageChanged(levelManager.CurrentStage);
        }

        private void HandleScoreChanged(int score)
        {
            if (scoreText != null) scoreText.text = "Score: " + score;
        }

        private void HandleLivesChanged(int lives)
        {
            if (livesText != null) livesText.text = "Lives: " + lives;
        }

        private void HandleStageChanged(int stage)
        {
            if (stageText != null) stageText.text = "Stage: " + stage;
        }
    }
}
