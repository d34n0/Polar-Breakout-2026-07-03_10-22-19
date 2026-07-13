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
        [Tooltip("Optional. A bigger screen-space ripple shockwave instantiated alongside " +
                 "explosionParticlesPrefab, tinted to normalColor - the boss-scale counterpart to " +
                 "BrickBreakEffects.ripplePrefab. Leave unset for no ripple.")]
        public RippleEffect explosionRipplePrefab;
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

        [Header("Phase 2 (Teleporting)")]
        [Tooltip("Number of hits landed before the boss enters phase 2 - gaining the ability to " +
                 "teleport anywhere in the arena (not just the top hover band), moving/turning " +
                 "faster and firing more often from then on.")]
        public int phase2HitThreshold = 2;
        [Tooltip("Multiplies wander speed and turn rate once phase 2 is active.")]
        public float phase2SpeedMultiplier = 1.6f;
        [Tooltip("Multiplies the turret's fire rate (i.e. divides fireInterval) once phase 2 is " +
                 "active.")]
        public float phase2FireRateMultiplier = 1.6f;
        [Tooltip("Min/max seconds between phase 2 teleports.")]
        public float phase2TeleportIntervalMin = 3f;
        public float phase2TeleportIntervalMax = 5f;
        [Tooltip("On the final hit needed to defeat the boss (CurrentHealth <= 1), every phase 2 " +
                 "value above - speed, fire rate, and teleport frequency - is multiplied by this " +
                 "again, on top of its own phase 2 value.")]
        public float finalHitBoostMultiplier = 2f;
        [Tooltip("How long a single teleport takes end to end - dissolve out, reposition, dissolve " +
                 "back in - seconds. The boss can't move or fire for the whole duration.")]
        public float teleportDuration = 1.5f;
        [Tooltip("Closest to the arena's dead center the boss will ever teleport or wander in " +
                 "phase 2, expressed in hex rings out from the center (same ring concept as the " +
                 "level's own concentric hex shells) rather than raw world units - converted via " +
                 "ring * hexSize * hex-apothem, i.e. the ring's own nearest edge, so \"ring 6\" " +
                 "really means \"never inside ring 6\", not just its center line.")]
        public float teleportMinRadiusRings = 6f;
        [Tooltip("The boss will never wander or teleport to a point closer than this to the " +
                 "player's paddle, world units.")]
        public float teleportMinDistanceFromPaddle = 6f;
        [Tooltip("Dissolve shader graph material used for the phase 2 teleport visual - e.g. " +
                 "Assets/Shaders/Dissolve.mat. A DissolveEffect component is added at runtime to " +
                 "drive it.")]
        public Material dissolveMaterial;

        [Header("Spawn")]
        [Tooltip("Plays a dissolve-in (fully invisible to fully visible) the moment this boss " +
                 "spawns, using the same dissolveMaterial as the phase 2 teleport - so a Boss-type " +
                 "level's boss fades into existence instead of just popping in already there. " +
                 "Leave off to appear instantly instead.")]
        public bool dissolveInOnSpawn = true;
        [Tooltip("How long the spawn dissolve-in takes, unscaled seconds - runs during " +
                 "LevelManager's paused level-start beat, so it plays out fully before the level " +
                 "actually unpauses regardless of this duration.")]
        public float spawnDissolveInDuration = 0.8f;

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

        private DissolveEffect _dissolveEffect;
        private bool _phase2Active;
        private bool _finalHitActive;
        private bool _isTeleporting;
        private float _speedMultiplier = 1f;

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

            // Added on the root rather than the Body child so it also picks up the turret's own
            // SpriteRenderer (a sibling child) via its GetComponentsInChildren scan - the whole
            // boss fades together on a phase 2 teleport, not just the body.
            _dissolveEffect = gameObject.AddComponent<DissolveEffect>();
            _dissolveEffect.dissolveMaterial = dissolveMaterial;

            // Hidden immediately, before the first frame ever renders, so the boss never pops in
            // fully visible even for a single frame before Start() kicks off the real dissolve-in
            // below - same reasoning as ScaleInOvershoot hiding in Awake and revealing in Play().
            if (dissolveInOnSpawn) _dissolveEffect.SnapToDissolved();
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
            _dissolveEffect.audioManager = audioManager;
            ApplyFootprintSize();
            InitializeWander();
            RollNewIdleSoundTimer();

            // Runs on unscaled time (see DissolveEffect), so it plays out fully during
            // LevelManager's paused level-start beat even though Time.timeScale is 0 for the
            // whole duration.
            if (dissolveInOnSpawn) _dissolveEffect.DissolveIn(spawnDissolveInDuration);
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
            // Movement freezes entirely for the duration of a phase 2 teleport (see
            // TeleportRoutine) - resumes automatically once _isTeleporting clears at its end.
            if (!_isTeleporting) UpdateWander();
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
            float newAngle = Mathf.MoveTowardsAngle(
                currentAngle, desiredAngle, wanderTurnRateDegrees * _speedMultiplier * Time.deltaTime);

            Vector2 dir = new Vector2(Mathf.Cos(newAngle * Mathf.Deg2Rad), Mathf.Sin(newAngle * Mathf.Deg2Rad));
            _currentVelocity = dir * _currentSpeed;

            Vector2 newPos = currentPos + _currentVelocity * Time.deltaTime;
            transform.position = ClampToWanderRegion(newPos);
        }

        /// <summary>Rolls a fresh random target point within the allowed arc/radius band (the full
        /// arena once phase 2 is active - see GetRadiusBand/PickPointAvoidingPaddle - otherwise the
        /// original top hover band), a random speed for the leg heading there (scaled by
        /// _speedMultiplier), and a random time budget before the next reroll.</summary>
        private void PickNewWanderTarget()
        {
            GetRadiusBand(out float minRadius, out float maxRadius);
            _wanderTarget = PickPointAvoidingPaddle(minRadius, maxRadius, restrictToHoverArc: !_phase2Active);

            _currentSpeed = UnityEngine.Random.Range(wanderSpeedMin, wanderSpeedMax) * _speedMultiplier;
            _wanderTimer = UnityEngine.Random.Range(wanderTargetIntervalMin, wanderTargetIntervalMax);
        }

        /// <summary>Clamps a candidate position back into the allowed wander radius band - keeps
        /// the randomized movement from drifting outside the intended range. Phase 1 additionally
        /// clamps the angle to the top hover arc; phase 2 leaves the angle free (its radius band
        /// already spans the whole arena - see GetRadiusBand), matching the wider roam PickNewWanderTarget
        /// picks targets from once phase 2 is active.</summary>
        private Vector2 ClampToWanderRegion(Vector2 pos)
        {
            GetRadiusBand(out float minRadius, out float maxRadius);
            float radius = Mathf.Clamp(pos.magnitude, minRadius, maxRadius);
            float angle = Mathf.Atan2(pos.y, pos.x) * Mathf.Rad2Deg;

            if (!_phase2Active)
                angle = hoverCenterAngleDegrees
                    + Mathf.Clamp(Mathf.DeltaAngle(hoverCenterAngleDegrees, angle), -hoverArcDegrees, hoverArcDegrees);

            float rad = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;
        }

        /// <summary>Phase 1 hovers within an inset band near the outer wall (its original "top of
        /// the arena" patch); phase 2 widens the inner bound out to teleportMinRadiusRings so the
        /// whole arena - not just the top - is in play, for both continuous wander and teleport
        /// destinations, while still never letting the boss drift inside that ring.</summary>
        private void GetRadiusBand(out float minRadius, out float maxRadius)
        {
            minRadius = _phase2Active
                ? Mathf.Max(0f, RingToWorldRadius(teleportMinRadiusRings))
                : Mathf.Max(0f, settings.outerWallRadius - hoverRadiusInsetMax);
            maxRadius = Mathf.Max(minRadius, settings.outerWallRadius - hoverRadiusInsetMin);
        }

        /// <summary>Converts a hex-ring count to the world-unit radius of that ring's own nearest
        /// edge (its apothem, not its corner) - same cos(30 deg) constant PolarGridSettings uses
        /// for its own hexagon-shaped outerWallRadius boundary - so "ring N" reliably keeps the
        /// boss out of every hex up to and including ring N, not just past its center line.</summary>
        private float RingToWorldRadius(float rings)
        {
            const float HexApothem = 0.8660254f; // cos(30 deg) == sqrt(3)/2
            return rings * settings.hexSize * HexApothem;
        }

        /// <summary>Rolls a random point in the given radius band - restricted to the top hover arc
        /// when restrictToHoverArc is true, otherwise any angle - retrying up to 20 times for one
        /// at least teleportMinDistanceFromPaddle away from the player's paddle. Falls back to the
        /// band's outer edge directly opposite the paddle if no attempt clears that distance (e.g.
        /// a very small arena), so the boss still never spawns on top of the player.</summary>
        private Vector2 PickPointAvoidingPaddle(float minRadius, float maxRadius, bool restrictToHoverArc)
        {
            TryGetPaddlePosition(out Vector2 paddlePos);
            float minDistanceSqr = teleportMinDistanceFromPaddle * teleportMinDistanceFromPaddle;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float angle = restrictToHoverArc
                    ? hoverCenterAngleDegrees + UnityEngine.Random.Range(-hoverArcDegrees, hoverArcDegrees)
                    : UnityEngine.Random.Range(0f, 360f);
                float radius = UnityEngine.Random.Range(minRadius, maxRadius);
                float rad = angle * Mathf.Deg2Rad;
                Vector2 candidate = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * radius;

                if ((candidate - paddlePos).sqrMagnitude >= minDistanceSqr)
                    return candidate;
            }

            Vector2 awayDir = paddlePos.sqrMagnitude > 0.0001f ? -paddlePos.normalized : Vector2.up;
            return awayDir * maxRadius;
        }

        /// <summary>The paddle only ever rotates around the arena center at a fixed orbit radius
        /// (see PaddleController's class doc) - same derivation BallManager uses for its own
        /// paddle-position burst effects.</summary>
        private bool TryGetPaddlePosition(out Vector2 position)
        {
            position = Vector2.zero;
            PaddleController paddle = ballManager != null && ballManager.primaryBall != null
                ? ballManager.primaryBall.paddle
                : null;
            if (paddle == null || paddle.settings == null) return false;

            float rad = paddle.CurrentAngleDegrees * Mathf.Deg2Rad;
            position = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * paddle.settings.paddleOrbitRadius;
            return true;
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

            UpdatePhaseState();

            if (CurrentHealth <= 0f)
            {
                IsDefeated = true;
                _bodyCollider.enabled = false;
                OnDefeated?.Invoke();
                StartCoroutine(PlayDefeatSequence());
            }
        }

        /// <summary>Recomputes the phase 2/final-hit state from CurrentHealth and applies it -
        /// called after every Hit. Phase 2 (teleporting) turns on permanently once
        /// phase2HitThreshold hits have landed; from then on, every phase 2 value (wander
        /// speed/turn rate, turret fire rate, teleport frequency) is scaled by
        /// finalHitBoostMultiplier again once the boss is down to its last point of health. Rerolls
        /// the current wander target immediately (unless a teleport is already in flight) so a
        /// speed change reads instantly instead of waiting out however much of the current leg is
        /// left.</summary>
        private void UpdatePhaseState()
        {
            int hitsTaken = Mathf.RoundToInt(maxHealth - CurrentHealth);
            bool shouldBePhase2 = !IsDefeated && hitsTaken >= phase2HitThreshold;
            _finalHitActive = !IsDefeated && CurrentHealth <= 1f;

            float speedMult = 1f;
            float fireRateMult = 1f;
            if (shouldBePhase2)
            {
                float finalMult = _finalHitActive ? finalHitBoostMultiplier : 1f;
                speedMult = phase2SpeedMultiplier * finalMult;
                fireRateMult = phase2FireRateMultiplier * finalMult;
            }
            _speedMultiplier = speedMult;
            if (turret != null) turret.SetPhaseFireRateMultiplier(fireRateMult);

            bool justEnteredPhase2 = shouldBePhase2 && !_phase2Active;
            _phase2Active = shouldBePhase2;

            if (justEnteredPhase2) StartCoroutine(Phase2TeleportLoop());
            if (_wanderInitialized && !_isTeleporting) PickNewWanderTarget();
        }

        /// <summary>Runs for the rest of the boss's life once phase 2 activates - waits a random
        /// interval (halved again on the final hit, same as every other phase 2 value - see
        /// UpdatePhaseState) then plays a teleport, repeating until the boss is defeated.</summary>
        private IEnumerator Phase2TeleportLoop()
        {
            while (!IsDefeated)
            {
                float intervalMult = _finalHitActive ? finalHitBoostMultiplier : 1f;
                float wait = UnityEngine.Random.Range(
                    phase2TeleportIntervalMin / intervalMult, phase2TeleportIntervalMax / intervalMult);

                float elapsed = 0f;
                while (elapsed < wait)
                {
                    if (IsDefeated) yield break;
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                if (IsDefeated) yield break;
                yield return TeleportRoutine();
            }
        }

        /// <summary>Dissolves the boss out, repositions it to a fresh random point anywhere in the
        /// arena (never within teleportMinDistanceFromPaddle of the player - see
        /// PickPointAvoidingPaddle), then dissolves it back in - teleportDuration seconds
        /// altogether, split evenly between the two halves. Movement (UpdateWander), firing, and
        /// the body's hit collider are all suspended for the full duration via _isTeleporting/
        /// turret.SetPaused, resuming only once the dissolve-in completes.</summary>
        private IEnumerator TeleportRoutine()
        {
            _isTeleporting = true;
            _bodyCollider.enabled = false;
            turret?.SetPaused(true);

            float halfDuration = teleportDuration * 0.5f;
            yield return _dissolveEffect.DissolveOut(halfDuration);

            GetRadiusBand(out float minRadius, out float maxRadius);
            Vector2 newPos = PickPointAvoidingPaddle(minRadius, maxRadius, restrictToHoverArc: false);
            transform.position = newPos;
            _wanderTarget = newPos;
            _currentVelocity = Vector2.zero;

            yield return _dissolveEffect.DissolveIn(teleportDuration - halfDuration);

            turret?.SetPaused(false);
            if (!IsDefeated) _bodyCollider.enabled = true;
            _isTeleporting = false;
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

            if (explosionRipplePrefab != null)
            {
                RippleEffect ripple = Instantiate(explosionRipplePrefab, transform.position, Quaternion.identity);
                Color tint = normalColor;
                tint.a = 1f;
                ripple.Play(tint);
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
