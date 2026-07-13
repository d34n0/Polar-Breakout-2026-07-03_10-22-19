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

        [Tooltip("Optional. Passed down to a spawned Boss-type level's boss/turret (see " +
                 "SpawnBoss) for its hit/fire/death/idle sounds. Leave unset for a silent boss.")]
        public AudioManager audioManager;

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

        [Tooltip("Optional. When set, a full-screen hex wipe plays on both the old level's tear-" +
                 "down (after the round-end dissolve, before the card offer) and the new level's " +
                 "build-in (after the card offer, replacing the instant BuildLevel call). Leave " +
                 "unset to fall back to the plain instant BuildLevel path, e.g. for isolated tests.")]
        public HexWipeTransition hexWipeTransition;

        [Tooltip("Boss-type levels only. Parent transform a spawned LevelSO.bossPrefab instance " +
                 "is instantiated under - leave unset (falls back to this GameObject's own " +
                 "transform) if the scene has no dedicated arena root for it.")]
        public Transform bossSpawnParent;

        public int CurrentStage { get; private set; } = 1;
        public event Action<int> OnStageChanged;

        /// <summary>Fired whenever a level activates (initial build-in or any stage advance) -
        /// (isSurviveStage, duration). Drives SurviveTimerController's show/hide.</summary>
        public event Action<bool, float> OnSurviveStageChanged;
        /// <summary>Fired on activation and every tick while a Survive stage is running - the
        /// remaining seconds. Drives SurviveTimerController's countdown text.</summary>
        public event Action<float> OnSurviveTimeChanged;
        /// <summary>Fired once whenever any level activates (initial build-in or any stage
        /// advance), right as the round-start dissolve-in begins - (objectiveType, surviveDuration,
        /// surviveDuration is only meaningful for Survive). Drives the objective announcement popup.</summary>
        public event Action<StageObjectiveType, float> OnObjectiveAnnounced;

        private LevelSO _activeLevel;
        private Coroutine _surviveTimerRoutine;
        // Set by HandleLevelCleared when a Survive stage is fully cleared before its timer
        // expires - consumed once, right before the next card offer, to guarantee it a Rare+ card.
        private bool _fullClearBonusPending;
        // Guards against AdvanceToNextStage starting twice for the same stage - a Survive stage's
        // timer expiring and a stray OnLevelCleared could otherwise both try to trigger it.
        private bool _stageAdvancing;

        // The currently-spawned boss for a Boss-type stage, if any - tracked so it can be
        // unsubscribed/destroyed on cleanup (defeat, or the round otherwise ending early).
        private BossController _activeBoss;

        private void Awake()
        {
            // Guaranteed to run before BrickGridManager.Start() - Unity calls every object's
            // Awake before any object's Start runs, regardless of GameObject/component order -
            // so this flag is always set in time to suppress its own default instant auto-build.
            if (hexWipeTransition != null && brickGridManager != null)
                brickGridManager.skipAutoBuildOnStart = true;
        }

        private void OnEnable()
        {
            if (brickGridManager != null) brickGridManager.OnLevelCleared += HandleLevelCleared;
        }

        private void OnDisable()
        {
            if (brickGridManager != null) brickGridManager.OnLevelCleared -= HandleLevelCleared;
        }

        private void Start()
        {
            if (hexWipeTransition != null && brickGridManager != null && brickGridManager.level != null)
                StartCoroutine(PlayInitialBuildIn());
        }

        /// <summary>Plays the same hex-wipe build-in used between stages, but for the very first
        /// level at game start, so it doesn't just pop in instantly - skips the tear-down half
        /// (nothing exists yet to tear down) and the score/stage-advance/card-offer steps (this
        /// isn't a stage clear).</summary>
        private IEnumerator PlayInitialBuildIn()
        {
            LevelSO initialLevel = brickGridManager.level;
            HexArenaBoundary boundary = brickGridManager.GetComponent<HexArenaBoundary>();

            boundary?.Hide();
            if (ballManager != null) yield return ballManager.PlayRoundEndDissolveOut();

            yield return hexWipeTransition.PlayBuildIn(initialLevel);
            boundary?.BuildBoundary(initialLevel.gridSettings);

            ApplyClearThreshold(initialLevel);
            ActivateLevel(initialLevel);

            if (ballManager != null)
            {
                ballManager.ResetForNewRound();
                yield return ballManager.PlayRoundStartDissolveIn();
            }
        }

        private void HandleLevelCleared()
        {
            // A Survive stage normally advances on its own timer, not on a brick clear - but
            // destroying every brick before the timer expires is itself a win condition too, so it
            // banks the same full-clear bonus (see AdvanceToNextStage's guaranteed-rare-card call)
            // AND advances immediately, rather than making the player sit out the rest of the timer
            // with nothing left to do.
            if (_activeLevel != null && _activeLevel.objectiveType == StageObjectiveType.Survive)
                _fullClearBonusPending = true;

            BeginAdvanceToNextStage();
        }

        /// <summary>One-shot gate into AdvanceToNextStage - both a Clear stage's OnLevelCleared
        /// and a Survive stage's own timer expiry funnel through here, so they can't race and
        /// double-trigger the same advance, and so every trigger clears the screen the same way.</summary>
        private void BeginAdvanceToNextStage()
        {
            if (_stageAdvancing) return;
            _stageAdvancing = true;

            // Immediately, before the end-of-round delay/dissolve/card-offer sequence even starts
            // - otherwise a power-up capsule still falling near the paddle, or a stray bullet/
            // laser beam still in flight, when the round ends could be legitimately caught/keep
            // acting during that window and carry into the next stage, even though the player
            // didn't use/catch it during the round it came from. Applies to every way a round can
            // end - a Clear stage's last brick falling and a Survive stage's timer running out
            // alike - so the screen always clears the same way regardless of which one triggered it.
            if (ballManager != null) ballManager.ClearTransientPickupsAndAbilities();

            if (_surviveTimerRoutine != null)
            {
                StopCoroutine(_surviveTimerRoutine);
                _surviveTimerRoutine = null;
            }

            // Hide the Survive timer immediately at round end rather than leaving it on screen
            // (frozen at whatever value it last had) through the whole tear-down/card-offer/
            // build-in sequence - ActivateLevel will show it again on its own once the next level
            // activates, if that next level also turns out to be a Survive stage.
            OnSurviveStageChanged?.Invoke(false, 0f);

            StartCoroutine(AdvanceToNextStage());
        }

        /// <summary>Ticks remaining down on scaled Time.deltaTime (so it naturally pauses whenever
        /// Time.timeScale is 0, e.g. a card offer - which can't overlap a running Survive timer
        /// anyway, since offers only appear between stages) - and only while a ball is actually
        /// in flight (see BallManager.IsAnyBallInPlay): docked-awaiting-launch and the death/
        /// respawn sequence both freeze the countdown rather than let it bleed away while the
        /// player isn't actively playing. Fires OnSurviveTimeChanged each step it does tick, then
        /// advances the stage once it hits zero.</summary>
        private IEnumerator RunSurviveTimer(float duration)
        {
            float remaining = duration;
            OnSurviveTimeChanged?.Invoke(remaining);
            while (remaining > 0f)
            {
                yield return null;
                if (ballManager != null && !ballManager.IsAnyBallInPlay()) continue;

                remaining -= Time.deltaTime;
                OnSurviveTimeChanged?.Invoke(Mathf.Max(0f, remaining));
            }

            _surviveTimerRoutine = null;
            BeginAdvanceToNextStage();
        }

        /// <summary>Marks lvl as the currently-live level: resets the full-clear bonus flag,
        /// (re)starts or clears the Survive timer based on its objective type, spawns/tears down a
        /// Boss-type level's boss, and notifies SurviveTimerController via OnSurviveStageChanged.</summary>
        private void ActivateLevel(LevelSO lvl)
        {
            _activeLevel = lvl;
            _fullClearBonusPending = false;

            if (_surviveTimerRoutine != null)
            {
                StopCoroutine(_surviveTimerRoutine);
                _surviveTimerRoutine = null;
            }

            bool isSurvive = lvl != null && lvl.objectiveType == StageObjectiveType.Survive;
            if (isSurvive)
                _surviveTimerRoutine = StartCoroutine(RunSurviveTimer(lvl.surviveDuration));

            OnSurviveStageChanged?.Invoke(isSurvive, lvl != null ? lvl.surviveDuration : 0f);

            DestroyActiveBoss();
            if (lvl != null && lvl.objectiveType == StageObjectiveType.Boss)
                SpawnBoss(lvl);

            if (lvl != null)
                OnObjectiveAnnounced?.Invoke(lvl.objectiveType, lvl.surviveDuration);
        }

        /// <summary>Instantiates lvl.bossPrefab (under bossSpawnParent, or this GameObject's own
        /// transform if unset), wires its settings/ballManager references (and its turret's, plus
        /// the fire interval from lvl), and subscribes OnDefeated to advance the stage the same
        /// way a Clear-type level's OnLevelCleared does today.</summary>
        private void SpawnBoss(LevelSO lvl)
        {
            if (lvl.bossPrefab == null) return;

            Transform parent = bossSpawnParent != null ? bossSpawnParent : transform;
            _activeBoss = Instantiate(lvl.bossPrefab, parent);
            _activeBoss.settings = lvl.gridSettings;
            _activeBoss.ballManager = ballManager;
            _activeBoss.audioManager = audioManager;
            _activeBoss.maxHealth = lvl.bossMaxHealth;

            if (_activeBoss.turret != null)
            {
                _activeBoss.turret.settings = lvl.gridSettings;
                _activeBoss.turret.ballManager = ballManager;
                _activeBoss.turret.audioManager = audioManager;
                _activeBoss.turret.fireInterval = lvl.bossFireInterval;
                // Snapshots the just-assigned fireInterval (and this prefab's own bulletSpeed) as
                // the "full health" baseline SetHealthFraction scales from as the boss takes
                // damage - must happen after the line above, not before.
                _activeBoss.turret.CaptureBaseline();
            }

            _activeBoss.OnDefeated += HandleBossDefeated;
        }

        /// <summary>Unsubscribes and destroys the current boss instance, if any - called before
        /// spawning the next level's boss (in case it somehow wasn't cleared already) and as a
        /// safety net whenever a round ends (see PlayEndOfRoundSequence), so a boss can never
        /// linger into a level that no longer wants one. Leaves an already-defeated boss alone -
        /// it's mid its own slow-mo/explosion death sequence (see BossController.PlayDefeatSequence)
        /// and will destroy itself once that finishes; force-destroying it here would cut that
        /// sequence off mid-flight and leave Time.timeScale stuck at its slow-mo value forever.</summary>
        private void DestroyActiveBoss()
        {
            if (_activeBoss == null || _activeBoss.IsDefeated) return;

            _activeBoss.OnDefeated -= HandleBossDefeated;
            Destroy(_activeBoss.gameObject);
            _activeBoss = null;
        }

        private void HandleBossDefeated()
        {
            BeginAdvanceToNextStage();
        }

        /// <summary>Computes and applies the soft clear threshold for Clear-type levels (advance
        /// once destructible bricks drop to this many or fewer - see BrickGridManager.ClearThreshold),
        /// or clears it back to 0 for Survive-type levels, so their OnLevelCleared only fires on a
        /// literal full clear (which doubles as the full-clear bonus condition, see HandleLevelCleared).</summary>
        private void ApplyClearThreshold(LevelSO lvl)
        {
            if (brickGridManager == null) return;

            if (lvl != null && lvl.objectiveType == StageObjectiveType.Clear)
                brickGridManager.SetClearThreshold(ComputeClearThreshold(brickGridManager.InitialDestructibleCount));
            else
                brickGridManager.SetClearThreshold(0);
        }

        private static int ComputeClearThreshold(int initialCount) =>
            Mathf.Max(3, Mathf.CeilToInt(0.05f * initialCount));

        private IEnumerator AdvanceToNextStage()
        {
            // Waits a frame so the last brick's own destruction/collision handling finishes
            // first, rather than tearing down and rebuilding the whole grid mid-callback.
            yield return null;

            if (scoreManager != null) scoreManager.AddScore(levelClearBonusPerStage * CurrentStage);

            CurrentStage++;
            OnStageChanged?.Invoke(CurrentStage);

            yield return PlayEndOfRoundSequence();

            HexArenaBoundary boundary = brickGridManager != null ? brickGridManager.GetComponent<HexArenaBoundary>() : null;

            if (hexWipeTransition != null)
            {
                boundary?.Hide();
                yield return hexWipeTransition.PlayTearDown();
            }

            if (_fullClearBonusPending && cardOfferController != null)
                cardOfferController.GuaranteeRareOrBetterNextOffer();

            if (cardOfferController != null)
                yield return cardOfferController.ShowOffer();

            LevelSO nextLevel = GetLevelForStage(CurrentStage);
            if (brickGridManager != null && nextLevel != null)
            {
                if (hexWipeTransition != null)
                {
                    yield return hexWipeTransition.PlayBuildIn(nextLevel);
                    // Full rebuild (not just Show()) since the new level's grid settings - hex
                    // size, wall radius - can differ from the old one; BuildBoundary already ends
                    // by re-showing whichever shape is active via its own ApplyActiveBoundary call.
                    boundary?.BuildBoundary(nextLevel.gridSettings);
                }
                else
                {
                    brickGridManager.BuildLevel(nextLevel);
                }

                ApplyClearThreshold(nextLevel);
                ActivateLevel(nextLevel);
            }

            // The new stage is fully live from here on - safe to let a future OnLevelCleared/timer
            // expiry trigger another advance.
            _stageAdvancing = false;

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
            // Safety net: a boss round should normally already be gone by the time this runs (its
            // own Hit() destroys it on defeat), but this guarantees one can never linger into the
            // next round if some future path (e.g. a boss stage that also has a Survive timer)
            // ever ends the round another way.
            DestroyActiveBoss();

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
