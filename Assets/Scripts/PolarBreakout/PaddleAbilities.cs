using System.Collections;
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
        [Tooltip("How many shots (button presses) a Cannon pickup grants - each shot fires both " +
                 "barrels at once, so total bullets fired is double this.")]
        public int cannonAmmoPerPickup = 5;
        public float bulletSpeed = 12f;
        [Tooltip("How far apart (world units) the two cannon barrels sit, offset sideways from " +
                 "the paddle's center - each barrel's bullet also spawns from that same offset " +
                 "position. Both barrels point straight out, parallel to each other and to the " +
                 "paddle's own facing direction, rather than fanning outward at an angle.")]
        public float turretSpacing = 0.5f;
        [Tooltip("How long the grow-out-of-the-paddle reveal animation takes when a Cannon " +
                 "capsule is collected, seconds.")]
        public float cannonRevealDuration = 0.25f;
        [Tooltip("Optional. Overrides a fired bullet's default procedural material. Leave unset " +
                 "to use the shared default.")]
        public Material bulletMaterial;

        [Header("Turret Skin")]
        [Tooltip("Selectable turret sprites - the currently chosen one (CosmeticsManager." +
                 "TurretSkinIndex) is applied to both barrels and re-applied live whenever the " +
                 "player changes turret skin. Turrets are purely cosmetic (no collider), so " +
                 "unlike the paddle itself they render as sprites rather than a procedural mesh.")]
        public TurretSkin[] availableTurretSkins;

        [Header("Autopilot")]
        public float autopilotDuration = 5f;

        [Header("Power-Up Capsule Materials")]
        [Tooltip("Optional per-type material overrides for power-up capsules, read by " +
                 "PowerUpCapsule at spawn time - leave any of these unset to use the shared " +
                 "default procedural material (with that type's default color) instead.")]
        public Material multiballCapsuleMaterial;
        public Material autopilotCapsuleMaterial;
        public Material cannonCapsuleMaterial;

        public int CannonAmmo => _cannonAmmo;
        public bool IsAutopilotActive => _autopilotTimeRemaining > 0f;

        private PaddleController _paddle;
        private int _cannonAmmo;
        private float _autopilotTimeRemaining;
        private bool _firePressed;
        private GameObject _cannonLeft;
        private GameObject _cannonRight;
        private Coroutine _cannonRevealCoroutine;
        private InputAction _fireAction;

        private void Awake()
        {
            _paddle = GetComponent<PaddleController>();
            _cannonLeft = BuildCannonVisual(-turretSpacing / 2f, "Left");
            _cannonRight = BuildCannonVisual(turretSpacing / 2f, "Right");

            // Reuses the same actions asset PaddleController already holds (same GameObject) -
            // leave unset, as every existing isolated test does, to fall back to the original
            // raw Gamepad.current poll below, unchanged.
            if (_paddle.actions != null)
            {
                _fireAction = _paddle.actions.FindActionMap("Player").FindAction("Fire");
                _fireAction.performed += OnFirePerformed;
                _fireAction.Enable();
            }

            RefreshTurretSkin();
            CosmeticsManager.OnTurretSkinChanged += RefreshTurretSkin;
        }

        private void OnFirePerformed(InputAction.CallbackContext context)
        {
            _firePressed = true;
        }

        private void OnDestroy()
        {
            if (_fireAction != null) _fireAction.performed -= OnFirePerformed;
            CosmeticsManager.OnTurretSkinChanged -= RefreshTurretSkin;
        }

        private void Update()
        {
            // Fallback path only - see OnFirePerformed/OnDestroy above for the actions-driven path.
            if (_fireAction == null && Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
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
                if (_cannonAmmo <= 0) SetCannonVisualsActive(false);
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
                    if (_cannonRevealCoroutine != null) StopCoroutine(_cannonRevealCoroutine);
                    _cannonRevealCoroutine = StartCoroutine(RevealCannons());
                    break;
            }
        }

        /// <summary>Called by BallManager the instant every ball has been lost - abilities
        /// don't survive losing the ball, and any bullets already in flight are destroyed
        /// immediately rather than being left to finish their flight.</summary>
        public void ResetAbilities()
        {
            _cannonAmmo = 0;
            _autopilotTimeRemaining = 0f;
            _paddle.AutopilotOverrideAngleDegrees = null;
            SetCannonVisualsActive(false);

            foreach (var bullet in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Destroy(bullet.gameObject);
        }

        /// <summary>Instantly hides both barrels (used when ammo runs out or the ability is
        /// reset on death) - the grow-out reveal only plays on collection, via RevealCannons.</summary>
        private void SetCannonVisualsActive(bool active)
        {
            if (_cannonRevealCoroutine != null)
            {
                StopCoroutine(_cannonRevealCoroutine);
                _cannonRevealCoroutine = null;
            }

            _cannonLeft.SetActive(active);
            _cannonRight.SetActive(active);
            if (!active)
            {
                // Reset to zero-grown so the next pickup's reveal starts from scratch instead of
                // popping to whatever scale was left over from an interrupted animation.
                _cannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                _cannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
            }
        }

        /// <summary>Animates both barrels scaling up from nothing along local +X (the direction
        /// each sprite should extend away from the paddle's surface, per its own pivot), so they
        /// visibly grow out of the paddle rather than just popping into existence.</summary>
        private IEnumerator RevealCannons()
        {
            // Re-synced on every reveal (not just once at Awake) so the barrels always match
            // whatever turret skin is currently selected - handy while iterating, since a freshly
            // caught Cannon capsule always picks up the latest choice.
            RefreshTurretSkin();
            _cannonLeft.SetActive(true);
            _cannonRight.SetActive(true);

            float elapsed = 0f;
            float duration = Mathf.Max(0.0001f, cannonRevealDuration);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetCannonGrowth(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            SetCannonGrowth(1f);
            _cannonRevealCoroutine = null;
        }

        private void SetCannonGrowth(float t)
        {
            _cannonLeft.transform.localScale = new Vector3(t, 1f, 1f);
            _cannonRight.transform.localScale = new Vector3(t, 1f, 1f);
        }

        /// <summary>Applies the currently selected TurretSkin's sprite (and optional material
        /// override) to both barrels.</summary>
        private void RefreshTurretSkin()
        {
            if (availableTurretSkins == null || availableTurretSkins.Length == 0) return;

            int index = Mathf.Clamp(CosmeticsManager.GetTurretSkinIndex(), 0, availableTurretSkins.Length - 1);
            var skin = availableTurretSkins[index];
            if (skin == null) return;

            ApplyTurretSkin(_cannonLeft, skin);
            ApplyTurretSkin(_cannonRight, skin);
        }

        private static void ApplyTurretSkin(GameObject cannon, TurretSkin skin)
        {
            var spriteRenderer = cannon.GetComponent<SpriteRenderer>();
            if (skin.sprite != null) spriteRenderer.sprite = skin.sprite;
            if (skin.materialOverride != null) spriteRenderer.sharedMaterial = skin.materialOverride;
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

        /// <summary>Fires both cannon barrels at once - one bullet per barrel, both flying
        /// straight out parallel to each other (matching the barrels' own visual orientation),
        /// just spawned from each barrel's own sideways-offset position rather than converging
        /// from the paddle's center.</summary>
        private void FireCannon()
        {
            FireBarrel(-turretSpacing / 2f);
            FireBarrel(turretSpacing / 2f);
        }

        private void FireBarrel(float lateralOffset)
        {
            float fireAngleDegrees = _paddle.CurrentAngleDegrees;
            float spawnRadius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f + 0.3f;
            Vector2 spawnPos = transform.TransformPoint(new Vector3(spawnRadius, lateralOffset, 0f));

            var bulletObject = new GameObject("Bullet");
            var bullet = bulletObject.AddComponent<Bullet>();
            bullet.Launch(spawnPos, fireAngleDegrees, bulletSpeed, _paddle.settings, bulletMaterial);

            // Bullets shouldn't physically collide with any ball in play (primary or multiball
            // clones) - without this, the physics solver would bounce the bullet off (or nudge)
            // the ball on contact, which reads as an unintended bug rather than a shot cleanly
            // passing through toward the bricks it's actually meant to hit.
            var bulletCollider = bulletObject.GetComponent<Collider2D>();
            foreach (var ball in Object.FindObjectsByType<BallController>(FindObjectsSortMode.None))
            {
                var ballCollider = ball.GetComponent<Collider2D>();
                if (ballCollider != null) Physics2D.IgnoreCollision(bulletCollider, ballCollider, true);
            }
        }

        /// <summary>Builds one cannon barrel as a sprite sticking straight outward from the
        /// paddle's outer edge, offset sideways from center by lateralOffset (world units) -
        /// parented under the paddle (built in the same local, angle-0-centered space as the
        /// paddle's own mesh) so it rotates along with the paddle for free via the parent
        /// transform. Both barrels share the same (identity) local rotation so they point
        /// straight out parallel to each other, rather than each fanning outward at its own
        /// angle. Purely cosmetic (no collider), so - unlike the paddle itself - it's a plain
        /// SpriteRenderer rather than a procedural mesh, letting each turret skin be actual
        /// hand-drawn artwork. A small negative local Z nudges it in front of the paddle's own
        /// MeshRenderer, since Unity doesn't otherwise guarantee draw order between a
        /// SpriteRenderer and a MeshRenderer at the same depth. Starts hidden; CollectPowerUp/
        /// ResetAbilities toggle it via SetCannonVisualsActive. The sprite itself is assigned by
        /// RefreshTurretSkin, not here.</summary>
        private GameObject BuildCannonVisual(float lateralOffset, string label)
        {
            var go = new GameObject($"CannonBarrel_{label}");
            go.transform.SetParent(transform, worldPositionStays: false);

            float radius = _paddle.settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            go.transform.localPosition = new Vector3(radius, lateralOffset, -0.01f);
            go.transform.localRotation = Quaternion.identity;

            go.AddComponent<SpriteRenderer>();

            go.transform.localScale = new Vector3(0f, 1f, 1f);
            go.SetActive(false);
            return go;
        }
    }
}
