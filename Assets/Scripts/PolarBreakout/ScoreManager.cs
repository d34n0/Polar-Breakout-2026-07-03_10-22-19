using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Tracks the running score, awarding each destroyed brick's BrickTypeSO.scoreValue.
    /// Listens to BrickGridManager.OnBrickDestroyed directly rather than a single "level
    /// cleared" signal, since the score needs to update incrementally as each brick dies.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        public BrickGridManager brickGridManager;

        public int CurrentScore { get; private set; }

        public event System.Action<int> OnScoreChanged;

        private void OnEnable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed += HandleBrickDestroyed;
        }

        private void OnDisable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed -= HandleBrickDestroyed;
        }

        private void HandleBrickDestroyed(Brick brick)
        {
            AddScore(brick.BrickType.scoreValue);
        }

        public void AddScore(int amount)
        {
            CurrentScore += amount;
            OnScoreChanged?.Invoke(CurrentScore);
        }
    }
}
