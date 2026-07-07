using System;
using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Tracks stage progression and awards the level-clear bonus (levelClearBonusPerStage times
    /// the stage just cleared) once BrickGridManager.OnLevelCleared fires. Advances through the
    /// authored `levels` list in order (stage 1 = levels[0], stage 2 = levels[1], ...); once that
    /// list is exhausted, the last entry is regenerated instead of erroring out, so the game keeps
    /// going rather than stalling once every hand-built level has been played.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public BrickGridManager brickGridManager;
        public ScoreManager scoreManager;

        [Tooltip("Optional. When set, the paddle dissolves out at the end of a cleared round " +
                 "(before the card offer appears) and dissolves back in once the new level " +
                 "builds, exactly like a life-lost respawn. Every new round is also forced into a " +
                 "clean, deterministic state: any multiball clones are cleared and the primary " +
                 "ball is redocked to the paddle, regardless of whatever state it was already in " +
                 "(including discarding a stray launch request). Leave unset for isolated tests " +
                 "that don't need this.")]
        public BallManager ballManager;

        [Tooltip("Levels to advance through in order (stage 1 = levels[0], stage 2 = levels[1], " +
                 "etc.). Once exhausted, the last entry is regenerated instead of erroring.")]
        public LevelSO[] levels;

        [Tooltip("Awarded on level clear, multiplied by the stage just cleared (e.g. clearing " +
                 "stage 5 with the default 1000 awards 5000).")]
        public int levelClearBonusPerStage = 1000;

        [Tooltip("Optional. When set, a mandatory 3-card choice is shown (and gameplay frozen) " +
                 "between every stage, before the next level builds. Leave unset to advance " +
                 "immediately, unaffected by the card system.")]
        public CardOfferController cardOfferController;

        [Tooltip("Pause after a round completes, before the card offer appears - gives the level-" +
                 "clear moment room to breathe instead of the offer popping up instantly. Also " +
                 "the natural place to plug in a future end-of-round animation/effect (see " +
                 "PlayEndOfRoundSequence).")]
        public float endOfRoundDelay = 1f;

        public int CurrentStage { get; private set; } = 1;
        public event Action<int> OnStageChanged;

        private void OnEnable()
        {
            if (brickGridManager != null) brickGridManager.OnLevelCleared += HandleLevelCleared;
        }

        private void OnDisable()
        {
            if (brickGridManager != null) brickGridManager.OnLevelCleared -= HandleLevelCleared;
        }

        private void HandleLevelCleared()
        {
            // Immediately, before the end-of-round delay/dissolve/card-offer sequence even
            // starts - otherwise a power-up capsule still falling near the paddle when the last
            // brick dies could be legitimately caught during that window and carry its ability
            // into the next stage, even though the player never used/caught it during the actual
            // round that dropped it.
            if (ballManager != null) ballManager.ClearTransientPickupsAndAbilities();
            StartCoroutine(AdvanceToNextStage());
        }

        private IEnumerator AdvanceToNextStage()
        {
            // Waits a frame so the last brick's own destruction/collision handling finishes
            // first, rather than tearing down and rebuilding the whole grid mid-callback.
            yield return null;

            if (scoreManager != null) scoreManager.AddScore(levelClearBonusPerStage * CurrentStage);

            CurrentStage++;
            OnStageChanged?.Invoke(CurrentStage);

            yield return PlayEndOfRoundSequence();

            if (cardOfferController != null)
                yield return cardOfferController.ShowOffer();

            LevelSO nextLevel = GetLevelForStage(CurrentStage);
            if (brickGridManager != null && nextLevel != null)
                brickGridManager.BuildLevel(nextLevel);

            if (ballManager != null)
            {
                ballManager.ResetForNewRound();
                yield return ballManager.PlayRoundStartDissolveIn();
            }
        }

        /// <summary>Runs right after a round ends and before the card offer appears - a brief
        /// pause (see endOfRoundDelay) for the level-clear moment to breathe, then the paddle
        /// dissolves out exactly like it does when a life is lost (see
        /// BallManager.PlayRoundEndDissolveOut), so the card offer appears with the arena empty
        /// rather than the paddle just sitting there mid-choice.</summary>
        private IEnumerator PlayEndOfRoundSequence()
        {
            yield return new WaitForSeconds(endOfRoundDelay);
            if (ballManager != null) yield return ballManager.PlayRoundEndDissolveOut();
        }

        private LevelSO GetLevelForStage(int stage)
        {
            if (levels == null || levels.Length == 0)
                return brickGridManager != null ? brickGridManager.level : null;

            int index = stage - 1;
            return levels[Mathf.Clamp(index, 0, levels.Length - 1)];
        }
    }
}
