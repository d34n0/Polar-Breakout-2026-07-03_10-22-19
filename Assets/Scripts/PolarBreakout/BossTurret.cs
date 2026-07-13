using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Child component of the boss - rotates a turret sprite to continuously face whichever ball
    /// is currently the threat (see BallManager.TryGetNearestBallPosition) and fires a BossBullet
    /// at it every fireInterval seconds. Sprite-building mirrors PaddleAbilities.BuildCannonVisual
    /// (a plain runtime-built SpriteRenderer, no procedural mesh needed).
    ///
    /// The turret sprite's pivot sits at its center, but the barrel itself points along local -Y
    /// (down) at rest, with its muzzle tip at the sprite's bottom edge (center of X, minimum of Y)
    /// - not local +X the way the player's own cannon barrels are drawn. RotateToward accounts for
    /// that 90-degree offset between "local -Y" and the standard 0-degrees-is-+X convention used
    /// everywhere else in this project, and Fire spawns bullets from that muzzle point (computed
    /// via transform.TransformPoint) rather than the turret's own pivot/center.
    /// </summary>
    public class BossTurret : MonoBehaviour
    {
        [Header("Visual")]
        public Sprite turretSprite;
        [Tooltip("The turret sprite's resting tint - also used to tint explosionParticlesPrefab " +
                 "when the boss dies, so the explosion visibly matches the turret's own color.")]
        public Color normalColor = Color.white;

        [Header("Firing")]
        [Tooltip("Seconds between shots - overwritten by LevelManager from LevelSO.bossFireInterval " +
                 "when the boss spawns (see CaptureBaseline, called right after).")]
        public float fireInterval = 5f;
        public float bulletSpeed = 6f;
        public BossBullet bossBulletPrefab;

        [Header("Defeat")]
        [Tooltip("Optional. Instantiated at the turret's position and Played once its own death " +
                 "sequence finishes (see BeginDeathSequence/Explode) - tinted to normalColor. " +
                 "Leave unset for no explosion effect.")]
        public ParticleSystem explosionParticlesPrefab;
        [Tooltip("Min/max seconds the turret lingers - floating aimlessly, no longer aiming or " +
                 "firing - after the boss body explodes, before it flashes and explodes in turn.")]
        public float deathLingerDurationMin = 0.5f;
        public float deathLingerDurationMax = 1f;
        [Tooltip("How fast the turret aimlessly drifts during its post-boss-death linger, world " +
                 "units/second.")]
        public float deathDriftSpeed = 0.6f;
        [Tooltip("How often (seconds) the aimless drift's direction randomly changes.")]
        public float deathDriftChangeInterval = 0.3f;
        [Tooltip("Gentle degrees/second the turret spins while lingering - purely cosmetic, reads " +
                 "as tumbling/drifting rather than staying rigidly aimed.")]
        public float deathDriftSpinDegrees = 60f;
        [ColorUsage(true, true)]
        [Tooltip("Color the turret blinks to right before it explodes - same blink-warning " +
                 "convention as Brick's own destroying flash.")]
        public Color preExplodeFlashColor = new Color(2f, 2f, 2f, 1f);
        [Tooltip("How long the pre-explosion blink warning lasts, seconds.")]
        public float preExplodeFlashDuration = 0.3f;
        [Tooltip("Seconds per blink toggle during the pre-explosion flash.")]
        public float preExplodeFlashBlinkInterval = 0.06f;

        [Header("Aiming")]
        [Tooltip("Max degrees/second the turret rotates to track the ball - keeps the aim from " +
                 "snapping instantly onto a fast-moving ball.")]
        public float rotationSpeedDegrees = 240f;

        [Header("Difficulty Scaling")]
        [Tooltip("Multiplies bulletSpeed once the boss is at 0 health (interpolated by remaining " +
                 "health fraction - see SetHealthFraction), so it fires faster-moving shots as it " +
                 "takes damage. 1 = no speed-up.")]
        public float maxSpeedMultiplier = 2f;
        [Tooltip("Multiplies fireInterval once the boss is at 0 health (below 1 = shoots more " +
                 "often as it takes damage). 1 = no frequency increase.")]
        public float minFireIntervalMultiplier = 0.4f;

        [Tooltip("Assigned by LevelManager alongside the boss's own ballManager reference.")]
        public BallManager ballManager;
        [Tooltip("Assigned by LevelManager - needed so fired bullets know the arena's outer wall " +
                 "radius for their own despawn check.")]
        public PolarGridSettings settings;
        [Tooltip("Optional - assigned by LevelManager alongside the references above. Leave unset " +
                 "for a silent turret.")]
        public AudioManager audioManager;

        private SpriteRenderer _renderer;
        private float _fireTimer;
        private float _currentAngleDegrees;
        private float _baseBulletSpeed;
        private float _baseFireInterval;
        private bool _baselineCaptured;

        private bool _isDying;
        private Vector2 _driftDirection;
        private float _driftChangeTimer;

        private bool _paused;
        private float _healthFraction = 1f;
        private float _phaseFireRateMultiplier = 1f;

        private void Awake()
        {
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = turretSprite;
            _renderer.color = normalColor;
            // Draws in front of the boss body's own SpriteRenderer (a separate child at the same
            // depth) rather than leaving draw order to chance.
            _renderer.sortingOrder = 1;
        }

        /// <summary>Snapshots the current bulletSpeed/fireInterval as the "full health" baseline
        /// for SetHealthFraction to scale from - called by LevelManager right after it overwrites
        /// fireInterval from the level asset, so the baseline reflects that per-level tuning
        /// rather than whatever the prefab's own field default happened to be.</summary>
        public void CaptureBaseline()
        {
            _baseBulletSpeed = bulletSpeed;
            _baseFireInterval = fireInterval;
            _baselineCaptured = true;
        }

        /// <summary>Scales bulletSpeed up and fireInterval down as the boss's remaining health
        /// fraction drops toward 0 - called by BossController.Hit after every hit, so the boss
        /// gets faster and more aggressive the more damage it's taken.</summary>
        public void SetHealthFraction(float remainingFraction)
        {
            if (!_baselineCaptured) CaptureBaseline();
            _healthFraction = Mathf.Clamp01(remainingFraction);
            RecomputeStats();
        }

        /// <summary>Layers an extra fire-rate boost on top of SetHealthFraction's own scaling -
        /// called by BossController.UpdatePhaseState with its phase 2/final-hit multiplier
        /// (1 outside phase 2). Values above 1 shrink fireInterval further, i.e. fire more often.</summary>
        public void SetPhaseFireRateMultiplier(float multiplier)
        {
            if (!_baselineCaptured) CaptureBaseline();
            _phaseFireRateMultiplier = Mathf.Max(0.01f, multiplier);
            RecomputeStats();
        }

        private void RecomputeStats()
        {
            float t = 1f - _healthFraction;
            bulletSpeed = Mathf.Lerp(_baseBulletSpeed, _baseBulletSpeed * maxSpeedMultiplier, t);
            fireInterval = Mathf.Lerp(_baseFireInterval, _baseFireInterval * minFireIntervalMultiplier, t)
                / _phaseFireRateMultiplier;
        }

        /// <summary>Suspends (or resumes) normal aim/fire without touching _isDying - used by
        /// BossController to freeze the turret for the duration of a phase 2 teleport (see
        /// BossController.TeleportRoutine), resuming the instant the teleport completes.</summary>
        public void SetPaused(bool paused) => _paused = paused;

        private void Update()
        {
            // Once the boss dies, DeathSequence (started from BeginDeathSequence) drives the
            // turret's motion frame-by-frame itself (see its while loop) - normal aim/fire is
            // skipped entirely rather than fighting it for control of transform.rotation/position.
            // Same during a phase 2 teleport (_paused) - see BossController.TeleportRoutine.
            if (_isDying || _paused) return;

            if (ballManager != null && ballManager.TryGetNearestBallPosition(out Vector2 targetPosition))
            {
                RotateToward(targetPosition);
                TickFireTimer(targetPosition);
            }
        }

        private void RotateToward(Vector2 targetPosition)
        {
            Vector2 toTarget = targetPosition - (Vector2)transform.position;
            if (toTarget.sqrMagnitude < 0.0001f) return;

            float targetAngle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            _currentAngleDegrees = Mathf.MoveTowardsAngle(
                _currentAngleDegrees, targetAngle, rotationSpeedDegrees * Time.deltaTime);

            // The sprite's own barrel rests pointing along local -Y, not local +X, so achieving a
            // world-facing angle of _currentAngleDegrees needs an extra +90 degrees of Z rotation
            // on top of it (rotating local -Y by (_currentAngleDegrees + 90) lands exactly on the
            // world direction (cos, sin) of _currentAngleDegrees).
            transform.rotation = Quaternion.Euler(0f, 0f, _currentAngleDegrees + 90f);
        }

        /// <summary>World position of the barrel's muzzle tip - the sprite's bottom edge, centered
        /// on X, in its own local (unrotated, unscaled-beyond-transform) space - transformed by
        /// the turret's current position/rotation/scale so it tracks the barrel exactly as it
        /// rotates.</summary>
        private Vector2 GetMuzzleWorldPosition()
        {
            if (_renderer.sprite == null) return transform.position;

            float halfHeight = _renderer.sprite.bounds.extents.y;
            Vector2 localMuzzle = new Vector2(0f, -halfHeight);
            return transform.TransformPoint(localMuzzle);
        }

        private void TickFireTimer(Vector2 targetPosition)
        {
            _fireTimer += Time.deltaTime;
            if (_fireTimer < fireInterval) return;

            _fireTimer = 0f;
            Fire(targetPosition);
        }

        private void Fire(Vector2 targetPosition)
        {
            if (bossBulletPrefab == null) return;

            Vector2 muzzlePosition = GetMuzzleWorldPosition();
            BossBullet bullet = Instantiate(bossBulletPrefab, muzzlePosition, Quaternion.identity);
            bullet.Launch(muzzlePosition, targetPosition, bulletSpeed, settings);
            bullet.SetBallManager(ballManager);
            audioManager?.PlayBossFire();
        }

        /// <summary>Called by BossController.PlayDefeatSequence the instant the boss body
        /// explodes - stops normal aim/fire immediately (see Update's _isDying check) and detaches
        /// from the boss root (worldPositionStays so it doesn't visibly jump) so it survives
        /// independently of the root's own Destroy call, then starts DeathSequence: linger,
        /// floating aimlessly, for a random 0.5-1s (see deathLingerDurationMin/Max), then flash and
        /// explode in turn (see Explode) rather than either continuing to act or just vanishing
        /// alongside the boss with no effect of its own.</summary>
        public void BeginDeathSequence()
        {
            if (_isDying) return;
            _isDying = true;

            transform.SetParent(null, worldPositionStays: true);
            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            float lingerDuration = UnityEngine.Random.Range(deathLingerDurationMin, deathLingerDurationMax);
            float elapsed = 0f;
            while (elapsed < lingerDuration)
            {
                elapsed += Time.deltaTime;
                DriftAimlessly();
                yield return null;
            }

            yield return PlayPreExplodeFlash();
            Explode();
        }

        /// <summary>Nudges the turret in a slowly-changing random direction (re-rolled every
        /// deathDriftChangeInterval) plus a gentle constant spin - reads as tumbling/floating
        /// aimlessly rather than the sharp, purposeful aim-tracking it did while alive.</summary>
        private void DriftAimlessly()
        {
            _driftChangeTimer -= Time.deltaTime;
            if (_driftChangeTimer <= 0f)
            {
                float angle = UnityEngine.Random.Range(0f, 360f);
                _driftDirection = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                _driftChangeTimer = deathDriftChangeInterval;
            }

            transform.position = (Vector2)transform.position + _driftDirection * deathDriftSpeed * Time.deltaTime;
            transform.Rotate(0f, 0f, deathDriftSpinDegrees * Time.deltaTime);
        }

        /// <summary>Blinks the turret between preExplodeFlashColor and normalColor for
        /// preExplodeFlashDuration - the same blink-warning convention as Brick's own destroying
        /// flash - so the explosion below doesn't come out of nowhere.</summary>
        private IEnumerator PlayPreExplodeFlash()
        {
            float elapsed = 0f;
            bool showFlash = true;
            while (elapsed < preExplodeFlashDuration)
            {
                _renderer.color = showFlash ? preExplodeFlashColor : normalColor;
                showFlash = !showFlash;

                float step = Mathf.Min(preExplodeFlashBlinkInterval, preExplodeFlashDuration - elapsed);
                yield return new WaitForSeconds(step);
                elapsed += step;
            }
        }

        /// <summary>Plays the turret's own explosion (tinted to normalColor) at its current
        /// position and destroys its own (now-detached) GameObject - the tail end of
        /// DeathSequence.</summary>
        private void Explode()
        {
            if (explosionParticlesPrefab != null)
            {
                ParticleSystem explosion = Instantiate(explosionParticlesPrefab, transform.position, Quaternion.identity);
                var main = explosion.main;
                Color tint = normalColor;
                tint.a = 1f;
                main.startColor = tint;
                explosion.Play();
            }

            audioManager?.PlayBossTurretExplosion();

            _renderer.enabled = false;
            Destroy(gameObject);
        }
    }
}
