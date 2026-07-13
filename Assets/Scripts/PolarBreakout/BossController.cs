using System;
using System.Collections;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// The Boss stage's enemy - a hovering sprite with hit points, mirroring Brick's own
    /// health/Hit shape rather than BrickGridManager's bookkeeping (a boss is a standalone entity,
    /// not tracked by the brick grid). The actual hit collider is a PolygonCollider2D on the
    /// child "Body" GameObject (see Awake) - added there rather than on this root GameObject so
    /// Unity auto-shapes it from bodySprite's own physics outline instead of a generic circle, and
    /// so it scales correctly alongside the body's own footprint-sizing scale (see
    /// ApplyFootprintSize) without also distorting the sibling turret. Non-trigger, so the ball
    /// bounces off it via Unity's ordinary 2D physics exactly like it does off a brick - only the
    /// damage needs manual wiring (see BossBodyCollisionRelay/Hit). Spawned/configured by
    /// LevelManager when a Boss-objective level activates, and destroyed once OnDefeated fires (or
    /// the round otherwise ends).
    ///
    /// Movement is a randomized wander (see UpdateWander) rather than a fixed sine sweep - each
    /// leg picks a random speed/direction/duration within a bounded arc/radius band near the top
    /// of the arena, with turning rate-limited so it curves into each new heading instead of
    /// snapping, reading as considerably less predictable than a plain oscillation.
    /// </summary>
    public class BossController : MonoBehaviour
    {
        [Header("Visual")]
        public Sprite bodySprite;
        [Tooltip("Target on-screen diameter, expressed as this many hex flat-widths (sqrt(3) * " +
                 "settings.hexSize) side by side - e.g. 7 means the boss looks about as wide as 7 " +
                 "hexagons lined up edge to edge.")]
        public float footprintHexWidths = 7f;
        [Tooltip("The body sprite's resting tint (i.e. whenever it isn't mid hit-flash) - also " +
                 "used to tint explosionParticlesPrefab on defeat, so the explosion visibly " +
                 "matches the body's own color rather than always exploding white.")]
        public Color normalColor = Color.white;

        [Header("Health")]
        public float maxHealth = 5f;
        [Tooltip("How long the hit-flash lasts, seconds.")]
        public float flashDuration = 0.15f;
        [ColorUsage(true, true)]
        [Tooltip("Color the boss flashes to on every hit - push the HDR intensity above 1 (via " +
                 "the color picker) to make it bloom, same convention as BrickTypeSO.hitFlashColor.")]
        public Color hitFlashColor = new Color(2f, 2f, 2f, 1f);
        [Tooltip("Optional. When set, the renderer swaps to this material for the flash duration " +
                 "instead of just tinting the normal material - same swap/restore pattern as " +
                 "Brick's hitFlashMaterial. Leave unset to flash via hitFlashColor alone.")]
        public Material hitFlashMaterial;

        [Header("Defeat")]
        [Tooltip("Optional. Instantiated at the boss's position and Played the instant it's " +
                 "defeated. Leave unset for no explosion effect.")]
        public ParticleSystem explosionParticlesPrefab;
        [Tooltip("Time.timeScale the moment the boss is defeated, held for slowMoDuration - a " +
                 "dramatic beat for the killing blow. 1 disables the slow-mo entirely.")]
        public float slowMoTimeScale = 0.5f;
        [Tooltip("How long (unscaled seconds) the slow-mo lasts before Time.timeScale is restored " +
                 "to 1 and the boss GameObject is actually destroyed.")]
        public float slowMoDuration = 1f;

        [Header("Wander Movement")]
        [Tooltip("Center angle (degrees, 0 = +X axis) of the arc the boss wanders within - 90 is " +
                 "the top of the arena.")]
        public float hoverCenterAngleDegrees = 90f;
        [Tooltip("How far the wander arc extends to either side of hoverCenterAngleDegrees, degrees.")]
        public float hoverArcDegrees = 40f;
        [Tooltip("Closest the boss will wander toward settings.outerWallRadius, world units.")]
        public float hoverRadiusInsetMin = 1f;
        [Tooltip("Furthest inside settings.outerWallRadius the boss will wander, world units.")]
        public float hoverRadiusInsetMax = 3f;
        [Tooltip("Min/max seconds before picking a new random wander target, even if the current " +
                 "one hasn't been reached yet - keeps a slow leg from lasting too long.")]
        public float wanderTargetIntervalMin = 1.5f;
        public float wanderTargetIntervalMax = 4f;
        [Tooltip("Min/max speed (world units/second) randomly chosen for each new wander leg.")]
        public float wanderSpeedMin = 1.5f;
        public float wanderSpeedMax = 4f;
        [Tooltip("Max degrees/second the boss can turn - limits how sharply it redirects toward a " +
                 "new target, so it curves into each new heading instead of snapping onto it.")]
        public float wanderTurnRateDegrees = 150f;
        [Tooltip("Distance from the current wander target within which a new one is picked early.")]
        public float wanderArrivalThreshold = 0.5f;

        [Header("Idle Sound")]
        [Tooltip("Minimum seconds between plays of AudioManager.bossIdleSound while the boss is " +
                 "alive - paired with idleSoundIntervalMaxSeconds to pick a random wait each time " +
                 "(inclusive of both endpoints).")]
        public int idleSoundIntervalMinSeconds = 4;
        [Tooltip("Maximum seconds between plays of AudioManager.bossIdleSound - see " +
                 "idleSoundIntervalMinSeconds.")]
        public int idleSoundIntervalMaxSeconds = 10;

        [Header("References")]
        [Tooltip("Assigned by LevelManager right after Instantiate - needed for both the wander " +
                 "region/footprint sizing and handed down to the turret for ball-aiming.")]
        public PolarGridSettings settings;
        public BallManager ballManager;
        [Tooltip("Optional - assigned by LevelManager alongside the references above. Leave unset " +
                 "for a silent boss.")]
        public AudioManager audioManager;
        [Tooltip("Optional child turret - LevelManager copies LevelSO.bossFireInterval onto it " +
                 "and assigns its ballManager/audioManager references alongside this boss's own.")]
        public BossTurret turret;

        public float CurrentHealth { get; private set; }
        public bool IsDefeated { get; private set; }

        /// <summary>Fired once, the instant health reaches 0 - LevelManager subscribes this to
        /// its own BeginAdvanceToNextStage, the same way BrickGridManager.OnLevelCleared drives a
        /// Clear-type stage's advance. Fires immediately on the killing blow, before the slow-mo/
        /// explosion beat plays out and the GameObject is actually destroyed (see Hit).</summary>
        public event Action OnDefeated;

        private SpriteRenderer _renderer;
        private PolygonCollider2D _bodyCollider;
        private Material _normalMaterial;
        private float _flashTimeRemaining;

        private Vector2 _wanderTarget;
        private Vector2 _currentVelocity;
        private float _currentSpeed;
        private float _wanderTimer;
        private bool _wanderInitialized;
        private float _idleSoundTimer;

        private void Awake()
        {
            var rendererGO = new GameObject("Body");
            rendererGO.transform.SetParent(transform, worldPositionStays: false);
            _renderer = rendererGO.AddComponent<SpriteRenderer>();
            _renderer.sprite = bodySprite;
            _normalMaterial = _renderer.sharedMaterial;

            // Added after the sprite is assigned, and on the same GameObject as the
            // SpriteRenderer, so Unity auto-generates its shape from bodySprite's own physics
            // outline (set in the Sprite Editor, or its fallback tight-fit rectangle) instead of
            // needing a hand-built outline the way Brick does for its procedural hex mesh.
            _bodyCollider = rendererGO.AddComponent<PolygonCollider2D>();

            var relay = rendererGO.AddComponent<BossBodyCollisionRelay>();
            relay.owner = this;
        }

        /// <summary>Settings/maxHealth-dependent setup (health init, footprint sizing, initial
        /// wander position) is deferred to Start rather than Awake - LevelManager assigns
        /// `settings`/`maxHealth` right after Instantiate but Awake already ran synchronously
        /// during that same Instantiate call, so both would still be at their prefab-default
        /// values there. Start runs afterward, once LevelManager's own SpawnBoss call has finished
        /// assigning every reference.</summary>
        private void Start()
        {
            CurrentHealth = maxHealth;
            _renderer.color = normalColor;
            ApplyFootprintSize();
            InitializeWander();
            RollNewIdleSoundTimer();
        }

        /// <summary>Scales the body sprite so the boss's world-space diameter matches
        /// footprintHexWidths worth of hex flat-widths at the level's own hexSize. Re-derives
        /// from the sprite's native size rather than hardcoding a scale, so swapping bodySprite
        /// doesn't require re-tuning this by hand. The PolygonCollider2D lives on this same
        /// GameObject (see Awake), so it scales right along with the sprite automatically -
        /// no separate collider sizing needed, unlike the old CircleCollider2D approach.</summary>
        private void ApplyFootprintSize()
        {
            if (settings == null || bodySprite == null) return;

            float targetDiameter = footprintHexWidths * Mathf.Sqrt(3f) * settings.hexSize;
            float nativeDiameter = Mathf.Max(0.0001f, bodySprite.bounds.size.x);
            float scale = targetDiameter / nativeDiameter;

            _renderer.transform.localScale = new Vector3(scale, scale, 1f);
        }

        private void Update()
        {
            UpdateWander();
            UpdateFlash();
            UpdateIdleSound();
        }

        /// <summary>Plays AudioManager.bossIdleSound on a repeating, randomized timer while the
        /// boss is alive - each play rolls a fresh random wait (inclusive of both
        /// idleSoundIntervalMinSeconds and idleSoundIntervalMaxSeconds) before the next one.</summary>
        private void UpdateIdleSound()
        {
            _idleSoundTimer -= Time.deltaTime;
            if (_idleSoundTimer > 0f) return;

            audioManager?.PlayBossIdle();
            RollNewIdleSoundTimer();
        }

        private void RollNewIdleSoundTimer()
        {
            _idleSoundTimer = UnityEngine.Random.Range(idleSoundIntervalMinSeconds, idleSoundIntervalMaxSeconds + 1);
        }

        private void InitializeWander()
        {
            if (settings == null) return;

            PickNewWanderTarget();
            // Start already sitting at the freshly-rolled target, rather than wherever the prefab
            // happened to be placed, so the very first frame doesn't visibly snap.
            transform.position = _wanderTarget;
            _wanderInitialized = true;
        }

        private void UpdateWander()
        {
            if (settings == null || !_wanderInitialized) return;

            _wanderTimer -= Time.deltaTime;
            Vector2 currentPos = transform.position;
            bool reachedTarget = (currentPos - _wanderTarget).sqrMagnitude <= wanderArrivalThreshold * wanderArrivalThreshold;
            if (_wanderTimer <= 0f || reachedTarget)
                PickNewWanderTarget();

            Vector2 toTarget = _wanderTarget - currentPos;
            Vector2 desiredDir = toTarget.sqrMagnitude > 0.0001f
                ? toTarget.normalized
                : (_currentVelocity.sqrMagnitude > 0.0001f ? _currentVelocity.normalized : Vector2.up);

            float currentAngle = _currentVelocity.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(_currentVelocity.y, _currentVelocity.x) * Mathf.Rad2Deg
                : Mathf.Atan2(desiredDir.y, desiredDir.x) * Mathf.Rad2Deg;
            float desiredAngle = Mathf.Atan2(desiredDir.y, desiredDir.x) * Mathf.Rad2Deg;
            float newAngle = Mathf.MoveTowardsAngle(currentAngle, desiredAngle, wanderTurnRateDegrees * Time.deltaTime);

            Vector2 dir = new Vector2(Mathf.Cos(newAngle * Mathf.Deg2Rad), Mathf.Sin(newAngle * Mathf.Deg2Rad));
            _currentVelocity = dir * _currentSpeed;

            Vector2 newPos = currentPos + _currentVelocity * Time.deltaTime;
            transform.position = ClampToWanderRegion(newPos);
        }

        /// <summary>Rolls a fresh random target point within the allowed arc/radius band, a random
        /// speed for the leg heading there, and a random time budget before the next reroll.</summary>
        private void PickNewWanderTarget()
        {
            GetRadiusBand(out float minRadius, out float maxRadius);

            float angle = hoverCenterAngleDegrees + UnityEngine.Random.Range(-hoverArcDegrees, hoverArcDegrees);
            float radius = UnityEngine.Random.Range(minRadius, maxRadius);
            float rad = angle * Mathf.Deg2Rad;
            _wanderTarget = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

            _currentSpeed = UnityEngine.Random.Range(wanderSpeedMin, wanderSpeedMax);
            _wanderTimer = UnityEngine.Random.Range(wanderTargetIntervalMin, wanderTargetIntervalMax);
        }

        /// <summary>Clamps a candidate position back into the allowed wander arc/radius band -
        /// keeps the randomized movement from drifting outside the intended patch near the top of
        /// the arena, the same "clamp within an arc" idea as PolarGridSettings' own boundary math.</summary>
        private Vector2 ClampToWanderRegion(Vector2 pos)
        {
            GetRadiusBand(out float minRadius, out float maxRadius);

            float radius = Mathf.Clamp(pos.magnitude, minRadius, maxRadius);
            float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;
            float clampedAngle = hoverCenterAngleDegrees
                + Mathf.Clamp(Mathf.DeltaAngle(hoverCenterAngleDegrees, angle), -hoverArcDegrees, hoverArcDegrees);

            float rad = clampedAngle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }

        private void GetRadiusBand(out float minRadius, out float maxRadius)
        {
            minRadius = Mathf.Max(0f, settings.outerWallRadius - hoverRadiusInsetMax);
            maxRadius = Mathf.Max(minRadius, settings.outerWallRadius - hoverRadiusInsetMin);
        }

        private void UpdateFlash()
        {
            if (_flashTimeRemaining <= 0f) return;

            _flashTimeRemaining -= Time.deltaTime;
            bool flashing = _flashTimeRemaining > 0f;
            _renderer.color = flashing ? hitFlashColor : normalColor;
            if (hitFlashMaterial != null)
                _renderer.sharedMaterial = flashing ? hitFlashMaterial : _normalMaterial;
        }

        /// <summary>Called whenever the ball or a player projectile touches the boss - decrements
        /// health, flashes, and on the hit that brings it to 0 fires OnDefeated and starts the
        /// slow-mo/explosion death sequence (see PlayDefeatSequence). source is accepted (unused)
        /// for parity with Brick.Hit(GameObject) - future work may want it (e.g. per-source damage
        /// amounts).</summary>
        public void Hit(GameObject source)
        {
            if (IsDefeated) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - 1f);
            _flashTimeRemaining = flashDuration;
            _renderer.color = hitFlashColor;
            if (hitFlashMaterial != null) _renderer.sharedMaterial = hitFlashMaterial;
            audioManager?.PlayBossHit();

            if (turret != null && maxHealth > 0f)
                turret.SetHealthFraction(CurrentHealth / maxHealth);

            if (CurrentHealth <= 0f)
            {
                IsDefeated = true;
                _bodyCollider.enabled = false;
                OnDefeated?.Invoke();
                StartCoroutine(PlayDefeatSequence());
            }
        }

        /// <summary>Slows time for a dramatic beat on the killing blow, plays the explosion
        /// particle effect (if assigned, tinted to normalColor so it matches the body) at the
        /// boss's current position, and tells the turret to begin its own death sequence (see
        /// BossTurret.BeginDeathSequence) - it detaches from this GameObject and stops firing
        /// immediately, then lingers floating aimlessly for its own short random duration before
        /// flashing and exploding in turn, entirely independent of this coroutine's own timing.
        /// Restores normal speed and destroys the boss GameObject (everything except the
        /// now-detached turret) once slowMoDuration elapses. Runs on unscaled time so the slow-mo
        /// duration itself isn't stretched by the very timeScale change it's applying.</summary>
        private IEnumerator PlayDefeatSequence()
        {
            Time.timeScale = slowMoTimeScale;
            audioManager?.PlayBossDeath();

            if (explosionParticlesPrefab != null)
            {
                ParticleSystem explosion = Instantiate(explosionParticlesPrefab, transform.position, Quaternion.identity);
                var main = explosion.main;
                Color tint = normalColor;
                tint.a = 1f;
                main.startColor = tint;
                explosion.Play();
            }

            // Hide the body immediately - the explosion effect (if any) reads as the boss's death,
            // rather than the sprite just sitting there through the whole slow-mo beat.
            _renderer.enabled = false;

            if (turret != null) turret.BeginDeathSequence();

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, slowMoDuration));

            Time.timeScale = 1f;
            Destroy(gameObject);
        }
    }

    /// <summary>Forwards ball collisions from the Body child's own PolygonCollider2D up to the
    /// owning BossController.Hit - physics2D collision callbacks are only ever dispatched to
    /// components on the GameObject that actually owns the collider, not to a parent, so this
    /// small relay is what lets the hit collider live on Body (see BossController.Awake) while
    /// the health/defeat logic stays on the root BossController.</summary>
    public class BossBodyCollisionRelay : MonoBehaviour
    {
        public BossController owner;

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var ball = collision.collider.GetComponent<BallController>();
            if (ball != null && owner != null) owner.Hit(ball.gameObject);
        }
    }
}
