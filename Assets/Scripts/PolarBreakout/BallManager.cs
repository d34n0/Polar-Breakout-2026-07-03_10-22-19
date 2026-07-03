using System;
using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Orchestrates the Multiball ability. Owns the one "primary" ball (the pre-existing
    /// scene ball, wired in the Inspector) plus any runtime-spawned clone balls, and decides
    /// when a ball falling into the death zone actually costs the player their ball versus
    /// just removing one of several balls in play. Losing an individual ball while others
    /// remain does nothing further; only once every ball is gone does the primary get
    /// reactivated/redocked and abilities reset.
    /// </summary>
    public class BallManager : MonoBehaviour
    {
        public BallController primaryBall;

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
            {
                primaryBall.gameObject.SetActive(true);
                primaryBall.Redock();

                if (primaryBall.paddle != null)
                {
                    var abilities = primaryBall.paddle.GetComponent<PaddleAbilities>();
                    if (abilities != null) abilities.ResetAbilities();
                }

                OnAllBallsLost?.Invoke();
            }
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
