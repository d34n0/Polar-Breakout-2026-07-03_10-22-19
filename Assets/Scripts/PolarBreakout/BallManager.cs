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

        public void ActivateMultiball()
        {
            // Only meaningful once there's a ball actually in play to split.
            if (primaryBall.State != BallState.Launched) return;

            SpawnClone(-20f);
            SpawnClone(20f);
        }

        private void SpawnClone(float angleOffsetDegrees)
        {
            var primaryRb = primaryBall.GetComponent<Rigidbody2D>();
            Vector2 baseVelocity = primaryRb.linearVelocity;
            float baseAngleDegrees = Mathf.Atan2(baseVelocity.y, baseVelocity.x) * Mathf.Rad2Deg;
            float newAngleRad = (baseAngleDegrees + angleOffsetDegrees) * Mathf.Deg2Rad;
            Vector2 newVelocity = new Vector2(Mathf.Cos(newAngleRad), Mathf.Sin(newAngleRad)) * baseVelocity.magnitude;

            var cloneObject = Instantiate(primaryBall.gameObject, primaryBall.transform.parent);
            var clone = cloneObject.GetComponent<BallController>();
            clone.LaunchAt(primaryRb.position, newVelocity);

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

            if (explosionEffectPrefab != null)
                Instantiate(explosionEffectPrefab, Vector3.zero, Quaternion.identity);

            var paddleDissolve = primaryBall.paddle != null ? primaryBall.paddle.GetComponent<DissolveEffect>() : null;
            if (paddleDissolve != null)
                yield return paddleDissolve.DissolveOut(paddleDissolveOutDuration);

            yield return new WaitForSeconds(respawnDelay);

            primaryBall.gameObject.SetActive(true);
            primaryBall.Redock();

            if (primaryBall.paddle != null)
            {
                var abilities = primaryBall.paddle.GetComponent<PaddleAbilities>();
                if (abilities != null) abilities.ResetAbilities();
            }

            var ballDissolve = primaryBall.GetComponent<DissolveEffect>();
            if (paddleDissolve != null) paddleDissolve.DissolveIn(dissolveInDuration);
            if (ballDissolve != null) yield return ballDissolve.DissolveIn(dissolveInDuration);
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
