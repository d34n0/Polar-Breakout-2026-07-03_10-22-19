using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Tracks the running score, awarding each destroyed brick's BrickTypeSO.EffectiveScoreValue,
    /// a fixed bonus per power-up capsule collected, and granting extra lives at configurable
    /// score thresholds. The visible score doesn't jump straight to the new total - each award is
    /// queued and ticked onto DisplayedScore a chunk at a time (see Update), giving the classic
    /// arcade "score counter rolling up" effect instead of an instant snap. LevelManager awards
    /// its own stage-clear bonus directly via AddScore rather than this class listening for it.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        public BrickGridManager brickGridManager;
        [Tooltip("Optional. Grants an extra life the first time score crosses each threshold here " +
                 "(checked in ascending order, each fires once). Leave empty for no score-based lives.")]
        public LivesManager livesManager;

        [Tooltip("Points awarded for collecting any power-up capsule.")]
        public int capsuleBonusScore = 1000;

        [Tooltip("Score thresholds that each grant one extra life the first time crossed - checked " +
                 "in ascending order, e.g. {20000, 70000}.")]
        public int[] extraLifeScoreThresholds = { 20000, 70000 };

        public int CurrentScore { get; private set; }

        /// <summary>What's actually shown on the HUD - lags behind CurrentScore, ticking up a
        /// chunk at a time each frame (see Update) rather than jumping straight to the new total.</summary>
        public int DisplayedScore { get; private set; }

        /// <summary>Fires the instant CurrentScore changes - anything needing the real,
        /// authoritative total right away should use this rather than waiting for the display
        /// to catch up.</summary>
        public event System.Action<int> OnScoreChanged;

        /// <summary>Fires every frame DisplayedScore actually moves - HUDController binds the
        /// visible score text to this instead of OnScoreChanged.</summary>
        public event System.Action<int> OnDisplayedScoreChanged;

        private readonly Queue<int> _pendingIncrements = new Queue<int>();
        private int _activeIncrementRemaining;
        private int _nextLifeThresholdIndex;

        private void OnEnable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed += HandleBrickDestroyed;
            PowerUpCapsule.OnAnyCapsuleCollected += HandleCapsuleCollected;
        }

        private void OnDisable()
        {
            if (brickGridManager != null) brickGridManager.OnBrickDestroyed -= HandleBrickDestroyed;
            PowerUpCapsule.OnAnyCapsuleCollected -= HandleCapsuleCollected;
        }

        private void Update()
        {
            if (_activeIncrementRemaining <= 0)
            {
                if (_pendingIncrements.Count == 0) return;
                _activeIncrementRemaining = _pendingIncrements.Dequeue();
            }

            // Ticks in big 100-point jumps while there's a lot of ground to cover, dropping to
            // 10-point steps as it closes in - a classic arcade score counter rolling up fast
            // then settling precisely onto the final total, rather than an instant snap.
            int step = Mathf.Min(_activeIncrementRemaining >= 100 ? 100 : 10, _activeIncrementRemaining);
            DisplayedScore += step;
            _activeIncrementRemaining -= step;
            OnDisplayedScoreChanged?.Invoke(DisplayedScore);
        }

        private void HandleBrickDestroyed(Brick brick)
        {
            AddScore(brick.BrickType.EffectiveScoreValue);
        }

        private void HandleCapsuleCollected()
        {
            AddScore(capsuleBonusScore);
        }

        public void AddScore(int amount)
        {
            if (amount == 0) return;

            CurrentScore += amount;
            if (amount > 0) _pendingIncrements.Enqueue(amount);
            OnScoreChanged?.Invoke(CurrentScore);

            CheckExtraLifeThresholds();
        }

        private void CheckExtraLifeThresholds()
        {
            if (livesManager == null || extraLifeScoreThresholds == null) return;

            while (_nextLifeThresholdIndex < extraLifeScoreThresholds.Length
                && CurrentScore >= extraLifeScoreThresholds[_nextLifeThresholdIndex])
            {
                livesManager.AddLife();
                _nextLifeThresholdIndex++;
            }
        }
    }
}
