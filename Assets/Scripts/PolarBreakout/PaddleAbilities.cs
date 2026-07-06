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
        [Tooltip("Angle (degrees) between adjacent bullets when a barrel fires more than one per " +
                 "shot (see ModifierType.ExtraBulletsPerBarrel) - extras fan out symmetrically " +
                 "around the barrel's own aim direction (averaged/centered on it) rather than " +
                 "all firing dead ahead.")]
        public float bulletSpreadDegrees = 10f;

        [Header("Laser Beam (Laser Cannon legendary)")]
        [Tooltip("Full width (world units) of the beam each turret fires while the Laser Cannon " +
                 "card is active, before any Split the Atom widening below.")]
        public float laserBeamBaseWidth = 0.3f;
        [Tooltip("Extra width (world units) added per stack of ExtraBulletsPerBarrel (i.e. per " +
                 "Split the Atom card also equipped) - the combined firepower widens the single " +
                 "beam instead of also fanning out separate bullets.")]
        public float laserBeamWidthPerExtraBullet = 0.25f;
        [Tooltip("How long each beam stays active (visible and damaging) per shot, seconds.")]
        public float laserBeamDuration = 0.15f;
        [Tooltip("Optional. Overrides the beam's default procedural material/color - the hook " +
                 "for a future \"laser skin\" to give it a custom shader/texture. Leave unset to " +
                 "use the plain default beam color.")]
        public Material laserBeamMaterial;

        [Header("Turret Skin")]
        [Tooltip("Selectable turret sprites - the currently chosen one (CosmeticsManager." +
                 "TurretSkinIndex) is applied to both barrels and re-applied live whenever the " +
                 "player changes turret skin. Turrets are purely cosmetic (no collider), so " +
                 "unlike the paddle itself they render as sprites rather than a procedural mesh.")]
        public TurretSkin[] availableTurretSkins;

        [Header("Autopilot")]
        public float autopilotDuration = 5f;

        [Header("Twin Paddle")]
        [Tooltip("Optional. A second PaddleController instance, pre-built in the scene (inactive " +
                 "by default) with its mirrorSource already pointed at this paddle - activated/" +
                 "deactivated automatically to match ModifierType.TwinPaddleEnabled (see " +
                 "RefreshTwinPaddle). Leave unset to omit the mechanic entirely.")]
        public PaddleController twinPaddle;

        [Header("Run Modifiers")]
        [Tooltip("Optional. When set, cannon ammo/bullet speed/turret spacing/autopilot duration " +
                 "are boosted by any Cards acquired this run. Leave unset to use all four exactly " +
                 "as configured, unaffected by the card system.")]
        public RunModifiers runModifiers;

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
        private GameObject _twinCannonLeft;
        private GameObject _twinCannonRight;
        private Coroutine _cannonRevealCoroutine;
        private InputAction _fireAction;

        /// <summary>turretSpacing plus any TurretSpacingBonus from acquired Cards - both the
        /// barrels' own built position and each shot's spawn/travel offset read this instead of
        /// the raw field, so a "Split the Atom" card widens the spread live.</summary>
        private float EffectiveTurretSpacing => turretSpacing
            + (runModifiers != null ? runModifiers.GetBonus(ModifierType.TurretSpacingBonus) : 0f);

        private void Awake()
        {
            _paddle = GetComponent<PaddleController>();
            _cannonLeft = BuildCannonVisual(_paddle, -EffectiveTurretSpacing / 2f, "Left");
            _cannonRight = BuildCannonVisual(_paddle, EffectiveTurretSpacing / 2f, "Right");

            // Built (hidden) regardless of whether the Quantum Mirror card is active yet, same as
            // the main barrels above - RefreshTwinPaddle/SetCannonVisualsActive toggle them, not
            // their construction.
            if (twinPaddle != null)
            {
                _twinCannonLeft = BuildCannonVisual(twinPaddle, -EffectiveTurretSpacing / 2f, "TwinLeft");
                _twinCannonRight = BuildCannonVisual(twinPaddle, EffectiveTurretSpacing / 2f, "TwinRight");
            }

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
            if (runModifiers != null)
            {
                runModifiers.OnModifiersChanged += RefreshTurretSpacing;
                runModifiers.OnModifiersChanged += RefreshTwinPaddle;
            }
            RefreshTwinPaddle();
        }

        private void OnFirePerformed(InputAction.CallbackContext context)
        {
            _firePressed = true;
        }

        private void OnDestroy()
        {
            if (_fireAction != null) _fireAction.performed -= OnFirePerformed;
            CosmeticsManager.OnTurretSkinChanged -= RefreshTurretSkin;
            if (runModifiers != null)
            {
                runModifiers.OnModifiersChanged -= RefreshTurretSpacing;
                runModifiers.OnModifiersChanged -= RefreshTwinPaddle;
            }
        }

        /// <summary>Repositions every already-built barrel (main and twin) to the current
        /// EffectiveTurretSpacing - called whenever a Card changes TurretSpacingBonus, since
        /// BuildCannonVisual only ever positions them once, at Awake.</summary>
        private void RefreshTurretSpacing()
        {
            float half = EffectiveTurretSpacing / 2f;
            RepositionCannon(_cannonLeft, -half);
            RepositionCannon(_cannonRight, half);
            RepositionCannon(_twinCannonLeft, -half);
            RepositionCannon(_twinCannonRight, half);
        }

        private static void RepositionCannon(GameObject cannon, float lateralOffset)
        {
            if (cannon == null) return;
            var pos = cannon.transform.localPosition;
            pos.y = lateralOffset;
            cannon.transform.localPosition = pos;
        }

        /// <summary>Activates/deactivates twinPaddle to match ModifierType.TwinPaddleEnabled -
        /// called once at Awake (so a Quantum Mirror card already acquired earlier this run - e.g.
        /// on a fresh stage's rebuilt paddle - takes effect immediately) and again every time a
        /// Card is added. Snaps the twin to its mirrored angle on every refresh (not just the
        /// activating one) rather than tracking a separate "just turned on" flag - a snap while
        /// already active and roughly in sync is an imperceptible no-op, so it isn't worth the
        /// extra state.</summary>
        private void RefreshTwinPaddle()
        {
            if (twinPaddle == null) return;

            bool twinPaddleEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.TwinPaddleEnabled) > 0f;
            twinPaddle.gameObject.SetActive(twinPaddleEnabled);
            if (twinPaddleEnabled)
            {
                twinPaddle.SnapToMirrorAngle();
                SyncTwinCannonVisuals();
            }
        }

        /// <summary>Matches the twin cannons' active/grown state to whatever the main cannons are
        /// currently showing - needed because the main cannons might already be revealed (ammo
        /// already in hand) by the time the Quantum Mirror card activates mid-run, in which case the
        /// twin should appear already-grown immediately rather than staying hidden until the next
        /// Cannon pickup.</summary>
        private void SyncTwinCannonVisuals()
        {
            if (_twinCannonLeft == null || _twinCannonRight == null) return;

            bool cannonsVisible = _cannonLeft.activeSelf;
            _twinCannonLeft.SetActive(cannonsVisible);
            _twinCannonRight.SetActive(cannonsVisible);
            if (cannonsVisible)
            {
                _twinCannonLeft.transform.localScale = _cannonLeft.transform.localScale;
                _twinCannonRight.transform.localScale = _cannonRight.transform.localScale;
            }
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
                // Laser Cannon spends the whole pickup on a single shot - a Cannon capsule
                // should only ever fire one beam, regardless of how much ammo bonus other
                // acquired cards granted, rather than letting it be re-fired repeatedly.
                bool laserEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.LaserBeamEnabled) > 0f;
                FireCannon();
                _cannonAmmo = laserEnabled ? 0 : _cannonAmmo - 1;
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
                    float autopilotBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.AutopilotDurationBonus) : 0f;
                    _autopilotTimeRemaining = autopilotDuration + autopilotBonus;
                    break;
                case PowerUpType.Cannon:
                    float ammoBonus = runModifiers != null ? runModifiers.GetBonus(ModifierType.CannonAmmoBonus) : 0f;
                    _cannonAmmo = cannonAmmoPerPickup + Mathf.RoundToInt(ammoBonus);
                    if (_cannonRevealCoroutine != null) StopCoroutine(_cannonRevealCoroutine);
                    _cannonRevealCoroutine = StartCoroutine(RevealCannons());
                    break;
            }
        }

        /// <summary>Called by BallManager the instant every ball has been lost - abilities
        /// don't survive losing the ball, and any bullets already in flight are destroyed
        /// immediately rather than being left to finish their flight. Also clears any
        /// still-falling power-up capsule (e.g. an Autopilot drop that hadn't been caught yet) -
        /// otherwise catching it right after respawning would silently re-grant the very ability
        /// that's supposed to have just been reset, reading as "it persisted through death."</summary>
        public void ResetAbilities()
        {
            _cannonAmmo = 0;
            _autopilotTimeRemaining = 0f;
            _paddle.AutopilotOverrideAngleDegrees = null;
            SetCannonVisualsActive(false);

            foreach (var bullet in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Destroy(bullet.gameObject);
            foreach (var beam in Object.FindObjectsByType<LaserBeam>(FindObjectsSortMode.None))
                Destroy(beam.gameObject);
            foreach (var capsule in Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None))
                Destroy(capsule.gameObject);
        }

        /// <summary>Instantly hides every barrel, main and twin alike (used when ammo runs out
        /// or the ability is reset on death) - the grow-out reveal only plays on collection, via
        /// RevealCannons.</summary>
        private void SetCannonVisualsActive(bool active)
        {
            if (_cannonRevealCoroutine != null)
            {
                StopCoroutine(_cannonRevealCoroutine);
                _cannonRevealCoroutine = null;
            }

            _cannonLeft.SetActive(active);
            _cannonRight.SetActive(active);
            if (_twinCannonLeft != null) _twinCannonLeft.SetActive(active);
            if (_twinCannonRight != null) _twinCannonRight.SetActive(active);
            if (!active)
            {
                // Reset to zero-grown so the next pickup's reveal starts from scratch instead of
                // popping to whatever scale was left over from an interrupted animation.
                _cannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                _cannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
                if (_twinCannonLeft != null) _twinCannonLeft.transform.localScale = new Vector3(0f, 1f, 1f);
                if (_twinCannonRight != null) _twinCannonRight.transform.localScale = new Vector3(0f, 1f, 1f);
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
            if (_twinCannonLeft != null) _twinCannonLeft.SetActive(true);
            if (_twinCannonRight != null) _twinCannonRight.SetActive(true);

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
            if (_twinCannonLeft != null) _twinCannonLeft.transform.localScale = new Vector3(t, 1f, 1f);
            if (_twinCannonRight != null) _twinCannonRight.transform.localScale = new Vector3(t, 1f, 1f);
        }

        /// <summary>Applies the currently selected TurretSkin's sprite (and optional material
        /// override) to every barrel, main and twin alike.</summary>
        private void RefreshTurretSkin()
        {
            var skin = GetCurrentTurretSkin();
            if (skin == null) return;

            ApplyTurretSkin(_cannonLeft, skin);
            ApplyTurretSkin(_cannonRight, skin);
            if (_twinCannonLeft != null) ApplyTurretSkin(_twinCannonLeft, skin);
            if (_twinCannonRight != null) ApplyTurretSkin(_twinCannonRight, skin);
        }

        /// <summary>The currently equipped TurretSkin asset (not just its index) - read by
        /// FireBarrel to look up that skin's own BulletSkin, so each turret look fires its own
        /// kind of bullet.</summary>
        private TurretSkin GetCurrentTurretSkin()
        {
            if (availableTurretSkins == null || availableTurretSkins.Length == 0) return null;

            int index = Mathf.Clamp(CosmeticsManager.GetTurretSkinIndex(), 0, availableTurretSkins.Length - 1);
            return availableTurretSkins[index];
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

        /// <summary>Fires both of the main paddle's cannon barrels at once - one bullet per
        /// barrel, both flying straight out parallel to each other (matching the barrels' own
        /// visual orientation), just spawned from each barrel's own sideways-offset position
        /// rather than converging from the paddle's center. Also fires the twin paddle's own pair
        /// (see Card_QuantumMirror) when it's active, from the same single press and the same
        /// shared ammo pool - Quantum Mirror doubles firepower, not shot cost.</summary>
        private void FireCannon()
        {
            float half = EffectiveTurretSpacing / 2f;
            FireBarrel(_paddle, -half);
            FireBarrel(_paddle, half);

            if (twinPaddle != null && twinPaddle.gameObject.activeSelf)
            {
                FireBarrel(twinPaddle, -half);
                FireBarrel(twinPaddle, half);
            }
        }

        /// <summary>Fires one barrel's shot from the given paddle (the main one, or the twin -
        /// see FireCannon). Normally a single bullet, but a ModifierType.ExtraBulletsPerBarrel
        /// card (Split the Atom) fans out extra bullets around the barrel's own aim direction
        /// without costing any extra ammo (a shot is still a shot). A ModifierType.LaserBeamEnabled
        /// card (Laser Cannon) replaces bullets entirely with one continuous beam per barrel
        /// instead - Split the Atom stacks widen that single beam rather than also fanning out
        /// separate bullets. A ModifierType.BulletRicochetBonus card (Ricochet Rounds) gives each
        /// fired bullet that many extra bounces off a brick instead of being destroyed on hit -
        /// ignored while the Laser Cannon is active, since there are no bullets to bounce.</summary>
        private void FireBarrel(PaddleController paddle, float lateralOffset)
        {
            float fireAngleDegrees = paddle.CurrentAngleDegrees;
            float spawnRadius = paddle.settings.paddleOrbitRadius + paddle.radialThickness / 2f + 0.3f;
            Vector2 spawnPos = paddle.transform.TransformPoint(new Vector3(spawnRadius, lateralOffset, 0f));

            bool laserEnabled = runModifiers != null && runModifiers.GetBonus(ModifierType.LaserBeamEnabled) > 0f;
            int extraBullets = runModifiers != null ? Mathf.RoundToInt(runModifiers.GetBonus(ModifierType.ExtraBulletsPerBarrel)) : 0;

            if (laserEnabled)
            {
                FireLaserBeam(paddle, spawnPos, fireAngleDegrees, extraBullets);
                return;
            }

            int ricochets = runModifiers != null ? Mathf.RoundToInt(runModifiers.GetBonus(ModifierType.BulletRicochetBonus)) : 0;

            // The equipped turret's own BulletSkin (if any) overrides the shared defaults below,
            // so each turret look can fire its own kind of bullet. The run-modifier multiplier
            // stacks on top of (rather than replacing) that per-skin flavor.
            var bulletSkin = GetCurrentTurretSkin()?.bulletSkin;
            Material material = bulletSkin != null && bulletSkin.materialOverride != null ? bulletSkin.materialOverride : bulletMaterial;
            Color? color = bulletSkin != null ? bulletSkin.color : (Color?)null;
            float bulletSpeedMultiplier = runModifiers != null ? runModifiers.GetMultiplier(ModifierType.BulletSpeedMultiplier) : 1f;
            float speed = bulletSpeed * bulletSpeedMultiplier * (bulletSkin != null ? bulletSkin.speedMultiplier : 1f);

            int bulletCount = Mathf.Max(1, 1 + extraBullets);
            for (int i = 0; i < bulletCount; i++)
            {
                float offsetDegrees = bulletCount > 1 ? (i - (bulletCount - 1) / 2f) * bulletSpreadDegrees : 0f;
                float bulletAngleDegrees = fireAngleDegrees + offsetDegrees;

                var bulletObject = new GameObject("Bullet");
                var bullet = bulletObject.AddComponent<Bullet>();
                bullet.Launch(spawnPos, bulletAngleDegrees, speed, paddle.settings, material, color, ricochets: ricochets);

                // Bullets shouldn't physically collide with any ball in play (primary or
                // multiball clones) - without this, the physics solver would bounce the bullet
                // off (or nudge) the ball on contact, which reads as an unintended bug rather
                // than a shot cleanly passing through toward the bricks it's actually meant to hit.
                var bulletCollider = bulletObject.GetComponent<Collider2D>();
                foreach (var ball in Object.FindObjectsByType<BallController>(FindObjectsSortMode.None))
                {
                    var ballCollider = ball.GetComponent<Collider2D>();
                    if (ballCollider != null) Physics2D.IgnoreCollision(bulletCollider, ballCollider, true);
                }
            }
        }

        /// <summary>Fires one continuous beam from a single barrel - the whole beam appears
        /// instantly spanning out to the arena's outer wall (see LaserBeam), rather than a bullet
        /// traveling there. Width grows with extraBullets (i.e. how many Split the Atom cards are
        /// also equipped), so a combined build fires one dramatically wider beam instead of
        /// multiple separate ones.</summary>
        private void FireLaserBeam(PaddleController paddle, Vector2 spawnPos, float fireAngleDegrees, int extraBullets)
        {
            float width = laserBeamBaseWidth + extraBullets * laserBeamWidthPerExtraBullet;
            float length = Mathf.Max(1f, paddle.settings.outerWallRadius - spawnPos.magnitude);

            var beamObject = new GameObject("LaserBeam");
            var beam = beamObject.AddComponent<LaserBeam>();
            beam.duration = laserBeamDuration;
            beam.Initialize(spawnPos, fireAngleDegrees, width, length, laserBeamMaterial);
        }

        /// <summary>Builds one cannon barrel as a sprite sticking straight outward from the given
        /// paddle's outer edge (the main paddle, or the twin - see FireCannon), offset sideways
        /// from center by lateralOffset (world units) - parented under that paddle (built in the
        /// same local, angle-0-centered space as its own mesh) so it rotates along with it for
        /// free via the parent transform. Both barrels share the same (identity) local rotation
        /// so they point straight out parallel to each other, rather than each fanning outward at
        /// its own angle. Purely cosmetic (no collider), so - unlike the paddle itself - it's a
        /// plain SpriteRenderer rather than a procedural mesh, letting each turret skin be actual
        /// hand-drawn artwork. A small negative local Z nudges it in front of the paddle's own
        /// MeshRenderer, since Unity doesn't otherwise guarantee draw order between a
        /// SpriteRenderer and a MeshRenderer at the same depth. Starts hidden; CollectPowerUp/
        /// ResetAbilities toggle it via SetCannonVisualsActive. The sprite itself is assigned by
        /// RefreshTurretSkin, not here.</summary>
        private static GameObject BuildCannonVisual(PaddleController paddle, float lateralOffset, string label)
        {
            var go = new GameObject($"CannonBarrel_{label}");
            go.transform.SetParent(paddle.transform, worldPositionStays: false);

            float radius = paddle.settings.paddleOrbitRadius + paddle.radialThickness / 2f;
            go.transform.localPosition = new Vector3(radius, lateralOffset, -0.01f);
            go.transform.localRotation = Quaternion.identity;

            go.AddComponent<SpriteRenderer>();

            go.transform.localScale = new Vector3(0f, 1f, 1f);
            go.SetActive(false);
            return go;
        }
    }
}
