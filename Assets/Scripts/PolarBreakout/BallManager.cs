using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Orchestrates the Multiball ability. Owns the one "primary" ball (the pre-existing
    /// scene ball, wired in the Inspector) plus any runtime-spawned clone balls, and decides
    /// when a ball falling into the death zone actually costs the player their ball versus
    /// just removing one of several balls in play. Losing an individual ball while others
    /// remain does nothing further; only once every ball is gone does a respawn sequence run:
    /// an explosion effect plays at the death zone, then - after a short delay - the paddle and
    /// ball dissolve back into place together (see DissolveEffect) as the primary ball redocks.
    /// </summary>
    public class BallManager : MonoBehaviour
    {
        public BallController primaryBall;

        [Tooltip("Optional. When set, RespawnSequence checks IsGameOver right after the losing-" +
                 "the-ball dissolve plays and skips the actual respawn if the game is over - the " +
                 "paddle/ball stay gone and GameOverController's Retry/Quit panel takes over " +
                 "instead. Leave unset to always respawn (e.g. in isolated tests).")]
        public LivesManager livesManager;

        [Tooltip("Optional. The death zone's own portal reveal (see DeathZoneVisual/" +
                 "ScaleInOvershoot) - when set, it plays its scale-in-with-overshoot animation " +
                 "once at the start of a new round (see PlayRoundStartDissolveIn), instead of " +
                 "auto-playing on Start (which would fire before the build-in/card-offer sequence " +
                 "even finishes). Deliberately NOT replayed on an ordinary death respawn (see " +
                 "RespawnSequence) - the death zone itself never went anywhere, only the ball did, " +
                 "so it should stay visible rather than re-popping in every life lost. Leave unset " +
                 "for no portal reveal.")]
        public ScaleInOvershoot deathZonePortal;

        [Tooltip("Optional. Plays AudioManager.deathSound the moment every ball is lost, " +
                 "alongside explosionEffectPrefab. Leave unset for a silent death.")]
        public AudioManager audioManager;

        [Header("Respawn")]
        [Tooltip("Played at the arena center (the death zone) the moment every ball is lost. " +
                 "Leave unset to skip the effect.")]
        public GameObject explosionEffectPrefab;
        [Tooltip("How long to wait after the explosion before the ball/paddle respawn - gives " +
                 "the explosion effect room to play before everything resets.")]
        public float respawnDelay = 1f;
        [Tooltip("How long the paddle takes to dissolve out - immediately after losing the ball, " +
                 "or at the end of a cleared round (see PlayRoundEndDissolveOut).")]
        public float paddleDissolveOutDuration = 0.3f;
        [Tooltip("How long the paddle and ball take to dissolve back into place - on respawn " +
                 "after a death, or at the start of a new round (see PlayRoundStartDissolveIn).")]
        public float dissolveInDuration = 0.6f;
        [Tooltip("How long deathZonePortal gets to start opening before the paddle dissolve-in " +
                 "begins (see PlayRoundStartDissolveIn) - a short head start so the portal is " +
                 "already visibly popping open by the time the paddle appears, rather than both " +
                 "starting on the exact same frame.")]
        public float deathZonePortalLeadTime = 0.2f;

        private readonly List<BallController> _clones = new List<BallController>();

        /// <summary>Fired once every ball (primary + all clones) has been lost - the actual
        /// "player loses their ball" moment, distinct from BallController.OnBallLost which
        /// fires for each individual ball instance leaving play.</summary>
        public event Action OnAllBallsLost;

        /// <summary>Splits the currently active, launched ball furthest from the arena center
        /// into two extra clones fanned off its current velocity - deliberately NOT always the
        /// primary ball, since the primary specifically can be the one already lost while clones
        /// from an earlier Multiball remain in play (its BallState field stays stuck reporting
        /// Launched even once deactivated, since it's only ever updated by Redock()). Picking
        /// whichever active ball is actually furthest out means a second Multiball capsule keeps
        /// working no matter which of the balls in play has been lost so far, and lets multiple
        /// Multiball pickups stack instead of only ever working once.</summary>
        public void ActivateMultiball()
        {
            BallController source = GetFurthestActiveBall();
            if (source == null) return;

            SpawnCloneFrom(source, -20f);
            SpawnCloneFrom(source, 20f);
        }

        private BallController GetFurthestActiveBall()
        {
            BallController furthest = null;
            float furthestSqrDistance = -1f;

            void Consider(BallController ball)
            {
                if (ball == null || !ball.gameObject.activeSelf || ball.State != BallState.Launched) return;
                float sqrDistance = ball.GetComponent<Rigidbody2D>().position.sqrMagnitude;
                if (sqrDistance > furthestSqrDistance)
                {
                    furthestSqrDistance = sqrDistance;
                    furthest = ball;
                }
            }

            Consider(primaryBall);
            foreach (var clone in _clones)
                Consider(clone);

            return furthest;
        }

        private void SpawnCloneFrom(BallController source, float angleOffsetDegrees)
        {
            var sourceRb = source.GetComponent<Rigidbody2D>();
            Vector2 baseVelocity = sourceRb.linearVelocity;
            float baseAngleDegrees = Mathf.Atan2(baseVelocity.y, baseVelocity.x) * Mathf.Rad2Deg;
            float newAngleRad = (baseAngleDegrees + angleOffsetDegrees) * Mathf.Deg2Rad;
            Vector2 newVelocity = new Vector2(Mathf.Cos(newAngleRad), Mathf.Sin(newAngleRad)) * baseVelocity.magnitude;

            // Always parented alongside the primary (not source.transform.parent) so clones stay
            // siblings of one another regardless of which ball - primary or an existing clone -
            // was chosen as this split's source.
            var cloneObject = Instantiate(source.gameObject, primaryBall.transform.parent);
            var clone = cloneObject.GetComponent<BallController>();
            clone.LaunchAt(sourceRb.position, newVelocity);

            _clones.Add(clone);
        }

        public void NotifyBallLost(BallController ball)
        {
            if (ball == primaryBall)
            {
                primaryBall.gameObject.SetActive(false);
            }
            else
            {
                _clones.Remove(ball);
                Destroy(ball.gameObject);
            }

            if (!primaryBall.gameObject.activeSelf && _clones.Count == 0)
                StartCoroutine(RespawnSequence());
        }

        private IEnumerator RespawnSequence()
        {
            // Fired immediately rather than after the sequence below, so the lives/score HUD
            // reacts the instant the ball is actually lost instead of lagging behind the effect.
            OnAllBallsLost?.Invoke();

            // Also immediate rather than deferred to the end of the sequence - abilities (and any
            // bullets already in flight) shouldn't survive a death for the ~1s+ the rest of this
            // coroutine takes to play out, or the player can keep firing the Cannon after losing.
            // ResetAbilities also aborts any power-up focus cinematic mid-flight (see
            // PaddleAbilities.AbortFocusSequence), so it must run before the freeze below - the
            // abort restores Time.timeScale to 1 as part of its cleanup.
            var abilities = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<PaddleAbilities>() : null;
            if (abilities != null) abilities.ResetAbilities();

            // Death freeze: the whole scene halts for the death beat - only things explicitly on
            // unscaled time keep moving, i.e. the death zone's black hole particles (see
            // ScaleInOvershoot.Awake), the explosion below, and the paddle dissolve (DissolveEffect
            // is already unscaled). Everything in this coroutine up to the unfreeze runs on
            // realtime waits, so the sequence itself is immune to its own freeze.
            Time.timeScale = 0f;

            if (explosionEffectPrefab != null)
            {
                var explosion = Instantiate(explosionEffectPrefab, Vector3.zero, Quaternion.identity);
                // The explosion IS the death feedback, so it plays through the freeze.
                foreach (var ps in explosion.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.useUnscaledTime = true;
                }
            }
            audioManager?.PlayDeath();

            var paddleDissolve = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<DissolveEffect>() : null;
            var paddleOutRoutine = paddleDissolve != null ? paddleDissolve.DissolveOut(paddleDissolveOutDuration) : null;
            if (paddleOutRoutine != null) yield return paddleOutRoutine;

            // Fully deactivate the paddle once it's done fading out - not just visually
            // invisible, since a MonoBehaviour left active still runs Update/FixedUpdate and
            // responds to Move input the instant it's re-enabled, well before the respawn dissolve-
            // in is supposed to bring it back. Mirrors how primaryBall itself is already handled.
            if (primaryBall.paddle != null) primaryBall.paddle.gameObject.SetActive(false);

            // Game over: that was the player's last life, so there's no respawn - the paddle/ball
            // stay dissolved out and GameOverController's Retry/Quit panel takes over from here.
            // Time stays frozen deliberately (the run is over; nothing should keep simulating
            // behind the panel) - GameOverController's Retry/Quit both restore timeScale to 1.
            if (livesManager != null && livesManager.IsGameOver) yield break;

            // Realtime rather than WaitForSeconds: the freeze above (and a player pause) must not
            // stall the respawn countdown - it keeps counting down in the background regardless.
            yield return new WaitForSecondsRealtime(respawnDelay);

            // Unfreeze before reactivating the ball/paddle: DockToPaddle only runs in FixedUpdate,
            // which never ticks at timeScale 0 - left frozen, the ball would visibly dissolve in
            // at the death zone's center and then teleport to the paddle on the first live frame.
            Time.timeScale = 1f;

            primaryBall.gameObject.SetActive(true);
            primaryBall.Redock();
            if (primaryBall.paddle != null) primaryBall.paddle.gameObject.SetActive(true);

            var ballDissolve = primaryBall.GetComponent<DissolveEffect>();
            if (paddleDissolve != null) paddleDissolve.DissolveIn(dissolveInDuration);
            if (ballDissolve != null) yield return ballDissolve.DissolveIn(dissolveInDuration);
        }

        /// <summary>Called at the start of every new round/stage (see
        /// LevelManager.AdvanceToNextStage) to guarantee a clean, deterministic start regardless
        /// of whatever state was left over from the stage just cleared: the primary ball is
        /// forced back to Docked, and any leftovers (multiball clones, Bullets, LaserBeams,
        /// uncollected PowerUpCapsules, ability state - see ClearTransientPickupsAndAbilities) are
        /// swept again as a safety net, since the real cleanup already happened immediately at
        /// BeginAdvanceToNextStage, well before this point. This also discards any stray launch
        /// request - e.g. Unity's Input System re-checks already-actuated controls the moment an
        /// action is re-enabled (see CardOfferController.ShowOffer), which can fire Performed
        /// immediately if Fire happens to still be held when the card offer closes, otherwise
        /// launching the ball before the player has actually pressed anything for the new round.</summary>
        public void ResetForNewRound()
        {
            primaryBall.gameObject.SetActive(true);
            primaryBall.Redock();
            // Reactivated here alongside the ball, right before PlayRoundStartDissolveIn plays -
            // see PlayRoundEndDissolveOut for why the paddle is fully deactivated (not just
            // dissolved) for the whole tear-down/card-offer/build-in window.
            if (primaryBall.paddle != null) primaryBall.paddle.gameObject.SetActive(true);

            ClearTransientPickupsAndAbilities();
        }

        /// <summary>Destroys any multiball clones, every leftover Bullet, LaserBeam, and
        /// uncollected PowerUpCapsule, and resets the paddle's Cannon/Autopilot ability state if
        /// present. Called immediately when a round clears/a Survive timer expires (see
        /// LevelManager.BeginAdvanceToNextStage) - before the end-of-round delay/dissolve/card-
        /// offer/build-in sequence even starts - so none of it visibly lingers through that whole
        /// window (which can take several real seconds, e.g. while the player is choosing a card)
        /// only to vanish once the next level has already appeared. This also means a power-up
        /// capsule still falling near the paddle at that exact moment can't be legitimately caught
        /// during the transition and carry its ability into the next level. Also called again from
        /// ResetForNewRound itself as a safety net.</summary>
        public void ClearTransientPickupsAndAbilities()
        {
            var abilities = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<PaddleAbilities>() : null;
            if (abilities != null) abilities.ResetAbilities();

            foreach (var clone in _clones)
                if (clone != null) Destroy(clone.gameObject);
            _clones.Clear();

            foreach (var bullet in FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Destroy(bullet.gameObject);
            foreach (var beam in FindObjectsByType<LaserBeam>(FindObjectsSortMode.None))
                Destroy(beam.gameObject);
            foreach (var capsule in FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None))
                Destroy(capsule.gameObject);
        }

        /// <summary>Called by LevelManager right after a round is cleared, before the card offer
        /// appears - dissolves the paddle out exactly like it does when a life is lost (see
        /// RespawnSequence), and hides the ball too (which RespawnSequence doesn't need to do
        /// itself, since by that point the ball has already been lost/deactivated). Unlike a
        /// real death, there's no explosion effect or respawnDelay here - the round just clears
        /// and the paddle steps off-screen for the card choice. The death zone's own portal
        /// reveal (see deathZonePortal) has no "close" animation, so it's left alone here and
        /// only re-played on the way back in - see PlayRoundStartDissolveIn.
        ///
        /// The paddle is fully deactivated once it's done fading out, not just left invisible -
        /// otherwise, since its GameObject stays active, it keeps responding to Move input (re-
        /// enabled the instant the card offer closes - see CardOfferController.ShowOffer) and
        /// effectively "occupies the scene" for the entire tear-down/card-offer/build-in window,
        /// well before ResetForNewRound/PlayRoundStartDissolveIn are supposed to bring it back.</summary>
        public IEnumerator PlayRoundEndDissolveOut()
        {
            primaryBall.gameObject.SetActive(false);

            var paddleDissolve = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<DissolveEffect>() : null;
            var paddleOutRoutine = paddleDissolve != null ? paddleDissolve.DissolveOut(paddleDissolveOutDuration) : null;
            if (paddleOutRoutine != null) yield return paddleOutRoutine;

            if (primaryBall.paddle != null) primaryBall.paddle.gameObject.SetActive(false);
        }

        /// <summary>Called by LevelManager once the new level has been built and ResetForNewRound
        /// has already reactivated/redocked the ball - dissolves the paddle and ball back into
        /// place together exactly like a respawn (see RespawnSequence), so the new round visually
        /// arrives rather than just popping in. Also plays the death zone's own portal reveal (see
        /// deathZonePortal) a short deathZonePortalLeadTime beforehand, rather than having it
        /// auto-play on Start (which would fire immediately at scene load, well before the paddle/
        /// ball are actually ready to be seen) or exactly alongside the paddle (which reads as two
        /// things happening at once rather than the portal opening first).</summary>
        public IEnumerator PlayRoundStartDissolveIn()
        {
            var paddleDissolve = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<DissolveEffect>() : null;
            var ballDissolve = primaryBall.GetComponent<DissolveEffect>();

            deathZonePortal?.Play();
            if (deathZonePortal != null) yield return new WaitForSecondsRealtime(deathZonePortalLeadTime);

            if (paddleDissolve != null) paddleDissolve.DissolveIn(dissolveInDuration);
            if (ballDissolve != null) yield return ballDissolve.DissolveIn(dissolveInDuration);
        }

        /// <summary>True if any ball (the primary or a multiball clone) is currently active and
        /// launched - i.e. genuinely in flight, as opposed to docked awaiting launch or lost and
        /// mid-respawn. A clone is always launched the instant it's spawned (see SpawnCloneFrom),
        /// so checking active+Launched uniformly covers both. Used by LevelManager's Survive-stage
        /// timer to pause while the player isn't actively playing.</summary>
        public bool IsAnyBallInPlay()
        {
            if (primaryBall != null && primaryBall.gameObject.activeSelf && primaryBall.State == BallState.Launched)
                return true;

            foreach (var clone in _clones)
                if (clone != null && clone.gameObject.activeSelf && clone.State == BallState.Launched)
                    return true;

            return false;
        }

        /// <summary>Angle (degrees) of whichever active, launched ball currently sits closest to
        /// the arena center - used by the Autopilot ability to decide which ball to track when
        /// multiball is in play. Prioritizing proximity to the center (rather than whichever ball
        /// needs the least paddle movement) means Autopilot always guards the ball most at risk
        /// of reaching the death zone. Falls back to <paramref name="referenceAngle"/> itself
        /// (i.e. "hold position") if no ball is currently in flight.</summary>
        public float GetNearestBallAngleDegrees(float referenceAngle)
        {
            float bestAngle = referenceAngle;
            float bestSqrDistance = float.MaxValue;

            void Consider(BallController ball)
            {
                if (ball == null || !ball.gameObject.activeSelf || ball.State != BallState.Launched) return;
                Vector2 pos = ball.GetComponent<Rigidbody2D>().position;
                float sqrDistance = pos.sqrMagnitude;
                if (sqrDistance < bestSqrDistance)
                {
                    bestSqrDistance = sqrDistance;
                    bestAngle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
                }
            }

            Consider(primaryBall);
            foreach (var clone in _clones)
                Consider(clone);

            return bestAngle;
        }
    }
}
