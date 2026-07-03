using UnityEngine;
using UnityEngine.InputSystem;

namespace PolarBreakout
{
    /// <summary>
    /// Owns all paddle-ability state (Multiball, Autopilot, Cannon) granted by collected
    /// power-up capsules. Lives alongside PaddleController on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(PaddleController))]
    public class PaddleAbilities : MonoBehaviour
    {
        [Header("References")]
        public BallManager ballManager;

        [Header("Cannon")]
        [Tooltip("How many shots a Cannon pickup grants.")]
        public int cannonAmmoPerPickup = 5;
        public float bulletSpeed = 12f;

        [Header("Autopilot")]
        public float autopilotDuration = 5f;

        public int CannonAmmo => _cannonAmmo;
        public bool IsAutopilotActive => _autopilotTimeRemaining > 0f;

        private PaddleController _paddle;
        private int _cannonAmmo;
        private float _autopilotTimeRemaining;
        private bool _firePressed;

        private void Awake()
        {
            _paddle = GetComponent<PaddleController>();
        }

        private void Update()
        {
            // Same latch-in-Update/consume-in-FixedUpdate pattern BallController uses for the
            // same button, for the same reason (wasPressedThisFrame isn't reliably readable
            // from FixedUpdate directly).
            if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                _firePressed = true;
        }

        private void FixedUpdate()
        {
            UpdateAutopilot();

            bool firePressed = _firePressed;
            _firePressed = false;

            // A launches the ball while docked (handled entirely inside BallController) and
            // fires the cannon once launched - the two never actually compete for the same
            // press since BallController only acts on it while Docked.
            bool ballInPlay = ballManager != null && ballManager.primaryBall != null
                && ballManager.primaryBall.State == BallState.Launched;
            if (firePressed && _cannonAmmo > 0 && ballInPlay)
            {
                FireCannon();
                _cannonAmmo--;
            }
        }

        public void CollectPowerUp(PowerUpType type)
        {
            switch (type)
            {
                case PowerUpType.Multiball:
                    if (ballManager != null) ballManager.ActivateMultiball();
                    break;
                case PowerUpType.Autopilot:
                    _autopilotTimeRemaining = autopilotDuration;
                    break;
                case PowerUpType.Cannon:
                    _cannonAmmo = cannonAmmoPerPickup;
                    break;
            }
        }

        /// <summary>Called by BallManager once every ball has been lost - abilities don't
        /// survive losing the ball.</summary>
        public void ResetAbilities()
        {
            _cannonAmmo = 0;
            _autopilotTimeRemaining = 0f;
            _paddle.AutopilotOverrideAngleDegrees = null;
        }

        private void UpdateAutopilot()
        {
            if (_autopilotTimeRemaining <= 0f) return;

            _autopilotTimeRemaining -= Time.fixedDeltaTime;
            if (_autopilotTimeRemaining > 0f && ballManager != null)
                _paddle.AutopilotOverrideAngleDegrees = ballManager.GetNearestBallAngleDegrees(_paddle.CurrentAngleDegrees);
            else
                _paddle.AutopilotOverrideAngleDegrees = null;
        }

        private void FireCannon()
        {
            float spawnRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f + 0.3f;
            float rad = _paddle.CurrentAngleDegrees * Mathf.Deg2Rad;
            Vector2 spawnPos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * spawnRadius;

            var bulletObject = new GameObject("Bullet");
            var bullet = bulletObject.AddComponent<Bullet>();
            bullet.Launch(spawnPos, _paddle.CurrentAngleDegrees, bulletSpeed, _paddle.settings);
        }
    }
}
