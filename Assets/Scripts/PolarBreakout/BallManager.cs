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

        [Header("Respawn")]
        [Tooltip("Played at the arena center (the death zone) the moment every ball is lost. " +
                 "Leave unset to skip the effect.")]
        public GameObject explosionEffectPrefab;
        [Tooltip("How long to wait after the explosion before the ball/paddle respawn - gives " +
                 "the explosion effect room to play before everything resets.")]
        public float respawnDelay = 1f;
        [Tooltip("How long the paddle takes to dissolve out immediately after losing the ball.")]
        public float paddleDissolveOutDuration = 0.3f;
        [Tooltip("How long the paddle and ball take to dissolve back into place once respawning.")]
        public float dissolveInDuration = 0.6f;

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
            var abilities = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<PaddleAbilities>() : null;
            if (abilities != null) abilities.ResetAbilities();

            if (explosionEffectPrefab != null)
                Instantiate(explosionEffectPrefab, Vector3.zero, Quaternion.identity);

            var paddleDissolve = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<DissolveEffect>() : null;
            if (paddleDissolve != null)
                yield return paddleDissolve.DissolveOut(paddleDissolveOutDuration);

            // Game over: that was the player's last life, so there's no respawn - the paddle/ball
            // stay dissolved out and GameOverController's Retry/Quit panel takes over from here.
            if (livesManager != null && livesManager.IsGameOver) yield break;

            // Realtime rather than WaitForSeconds: if the player pauses (Time.timeScale = 0)
            // during this window, the respawn shouldn't stall until they happen to unpause -
            // it should just keep counting down in the background.
            yield return new WaitForSecondsRealtime(respawnDelay);

            primaryBall.gameObject.SetActive(true);
            primaryBall.Redock();

            var ballDissolve = primaryBall.GetComponent<DissolveEffect>();
            if (paddleDissolve != null) paddleDissolve.DissolveIn(dissolveInDuration);
            if (ballDissolve != null) yield return ballDissolve.DissolveIn(dissolveInDuration);
        }

        /// <summary>Called at the start of every new round/stage (see
        /// LevelManager.AdvanceToNextStage) to guarantee a clean, deterministic start regardless
        /// of whatever state the ball(s) were left in: any multiball clones from the previous
        /// stage are destroyed and the primary ball is forced back to Docked. This also discards
        /// any stray launch request - e.g. Unity's Input System re-checks already-actuated
        /// controls the moment an action is re-enabled (see CardOfferController.ShowOffer), which
        /// can fire Performed immediately if Fire happens to still be held when the card offer
        /// closes, otherwise launching the ball before the player has actually pressed anything
        /// for the new round.</summary>
        public void ResetForNewRound()
        {
            foreach (var clone in _clones)
                if (clone != null) Destroy(clone.gameObject);
            _clones.Clear();

            primaryBall.gameObject.SetActive(true);
            primaryBall.Redock();
        }

        /// <summary>Angle (degrees) of whichever active, launched ball is angularly closest
        /// to <paramref name="referenceAngle"/> - used by the Autopilot ability to decide
        /// which ball to track when multiball is in play. Falls back to
        /// <paramref name="referenceAngle"/> itself (i.e. "hold position") if no ball is
        /// currently in flight.</summary>
        public float GetNearestBallAngleDegrees(float referenceAngle)
        {
            float bestAngle = referenceAngle;
            float bestDelta = float.MaxValue;

            void Consider(BallController ball)
            {
                if (ball == null || !ball.gameObject.activeSelf || ball.State != BallState.Launched) return;
                Vector2 pos = ball.GetComponent<Rigidbody2D>().position;
                float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
                float delta = Mathf.Abs(Mathf.DeltaAngle(referenceAngle, angle));
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestAngle = angle;
                }
            }

            Consider(primaryBall);
            foreach (var clone in _clones)
                Consider(clone);

            return bestAngle;
        }
    }
}
