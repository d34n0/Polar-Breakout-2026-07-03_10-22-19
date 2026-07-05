using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PolarBreakout
{
    public enum BallState { Docked, Launched }

    /// <summary>
    /// Ball movement uses a real Rigidbody2D, and separation/reflection off the paddle and
    /// bricks is left entirely to the physics engine's own elastic collision response
    /// (PhysicsMaterial2D, bounciness 1) rather than a manual "reflect + teleport off the
    /// surface" script. With ten densely packed brick rings, any hand-picked nudge distance
    /// either wasn't enough to clear the current brick (grinding along the same ring) or
    /// overshot into the next one (a fresh pileup) - the native solver already handles
    /// multi-contact/corner geometry robustly, so fighting it was the actual bug. Speed is
    /// renormalized every step regardless (see MaintainConstantSpeed), so partial energy
    /// loss from imperfect material combining doesn't matter - only the resulting direction
    /// does, and a small follow-up nudges that direction to avoid a rare degenerate case
    /// (see EnforceMinimumRadialComponent).
    ///
    /// The outer wall and center death zone are NOT physics colliders - they're simple
    /// radius checks against the arena center each physics step, since "contain an
    /// object inside a circle" isn't a native 2D collider shape.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class BallController : MonoBehaviour
    {
        [Header("References")]
        public PolarGridSettings settings;
        public PaddleController paddle;
        [Tooltip("Optional. When set, losing this ball is routed through the manager (for " +
                 "Multiball) instead of redocking immediately. Leave unset for single-ball play.")]
        public BallManager ballManager;

        [Header("Movement")]
        public float speed = 8f;
        [Tooltip("How far outward from the paddle's outer edge the ball sits while docked.")]
        public float dockOffset = 0.25f;

        [Header("Speed Ramp")]
        [Tooltip("Units/sec that speed gains per second spent in flight, so long rallies get progressively harder.")]
        public float speedRampPerSecond = 0.4f;
        [Tooltip("Upper bound speed ramps toward - keeps the ball from eventually becoming unplayable.")]
        public float maxSpeed = 20f;

        [Header("Spin")]
        [Tooltip("How much of the paddle's angular velocity (deg/sec) at the moment of contact " +
                 "converts into ball spin. A paddle swept quickly past the ball imparts more.")]
        public float spinTransferFactor = -0.012f;
        [Tooltip("Spin magnitude is capped to this.")]
        public float maxSpin = 1.5f;
        [Tooltip("Spin decays toward zero at this rate per second.")]
        public float spinDecayPerSecond = 0.6f;
        [Tooltip("How strongly spin bends the ball's travel direction, in degrees/second per unit of spin.")]
        public float spinCurveStrength = 140f;
        [Tooltip("Minimum |spin| for the ball to phase through bricks (destroying them without " +
                 "bouncing) instead of bouncing off them normally.")]
        public float phaseSpinThreshold = 0.25f;

        [Header("Trail Crossfade")]
        [Tooltip("How long (seconds) it takes to cross-fade between trails - covers docked<->" +
                 "flying and normal<->spin transitions alike.")]
        public float trailCrossfadeDuration = 0.2f;

        [Header("Input")]
        [Tooltip("Optional. When set, launching reads from this asset's Player/Fire action " +
                 "(gamepad + keyboard) instead of polling Gamepad.current directly. Leave unset " +
                 "- as every existing isolated test does - to fall back to the original raw " +
                 "Gamepad.current read, so nothing about existing test behavior changes.")]
        public InputActionAsset actions;

        [Header("Run Modifiers")]
        [Tooltip("Optional. When set, speed/speedRampPerSecond/phaseSpinThreshold are adjusted " +
                 "by any Cards acquired this run. Leave unset to use all three exactly as " +
                 "configured, unaffected by the card system - every existing isolated test does " +
                 "this.")]
        public RunModifiers runModifiers;

        /// <summary>Signed current spin - sign is curve direction, magnitude decays over time
        /// (see spinDecayPerSecond) until it drops below phaseSpinThreshold.</summary>
        public float Spin { get; private set; }

        /// <summary>True while |Spin| is above the effective phase threshold - while true, the
        /// ball destroys bricks it touches without bouncing off them (see OnCollisionEnter2D). A
        /// PhaseThresholdReduction card can push the effective threshold down to 0, making this
        /// permanently true regardless of Spin (the "always phasing" Legendary card).</summary>
        public bool IsPhasing => Mathf.Abs(Spin) >= EffectivePhaseThreshold;

        private float EffectivePhaseThreshold => Mathf.Max(0f, phaseSpinThreshold
            - (runModifiers != null ? runModifiers.GetBonus(ModifierType.PhaseThresholdReduction) : 0f));

        private float EffectiveSpeedMultiplier => runModifiers != null ? runModifiers.GetMultiplier(ModifierType.BallSpeedMultiplier) : 1f;

        private float EffectiveRampMultiplier => runModifiers != null ? runModifiers.GetMultiplier(ModifierType.BallSpeedRampMultiplier) : 1f;

        public BallState State { get; private set; } = BallState.Docked;

        /// <summary>Raised when the ball falls into the center death zone.</summary>
        public event System.Action OnBallLost;

        private Rigidbody2D _rb;
        private CircleCollider2D _collider;
        private bool _launchRequested;
        private bool _bounceOccurred;
        private bool _reportedLost;
        private float _initialSpeed;
        private Camera _arenaCamera;
        private Vector2 _lastStableVelocity;
        // Normal (non-spin) trail lives on a separate "TrailNormal" child so its look can be
        // authored/tweaked independently in the Editor; the spin trail is whatever TrailRenderer
        // is directly on the ball itself (e.g. a custom energy-glow shader), swapped in only
        // while phasing - see UpdateTrailEmission.
        private TrailRenderer _trail;
        private TrailRenderer _spinTrail;
        private TrailMode _currentTrailMode = TrailMode.None;
        private Coroutine _trailCrossfadeCoroutine;
        private InputAction _fireAction;

        private enum TrailMode { None, Normal, Spin }

        // CircleCollider2D.radius is in local space; the ball's transform is scaled down
        // (e.g. 0.5 with a 0.2 scale is really only 0.1 in world space), so any positioning
        // math needs this, not the raw collider radius, to avoid leaving a visible gap.
        private float WorldRadius => _collider.radius * transform.lossyScale.x;

        private void Awake()
        {
            // _initialSpeed is the pure configured base, captured once and never touched again -
            // ResetToDocked recomputes the effective reset speed from it fresh every time, so a
            // card acquired mid-run is picked up on the next life without double-applying.
            _initialSpeed = speed;
            speed = _initialSpeed * EffectiveSpeedMultiplier;
            _arenaCamera = Camera.main;

            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _trail = transform.Find("TrailNormal")?.GetComponent<TrailRenderer>();
            _spinTrail = GetComponent<TrailRenderer>();

            // Force both off immediately rather than waiting for the first FixedUpdate's
            // UpdateTrailEmission() to catch up - TrailRenderer defaults to emitting=true in the
            // Editor and starts recording points the instant the object exists, so without this
            // the ball's very first jump from its raw placed position to the paddle's dock spot
            // (before DockToPaddle() has run even once) gets drawn as a trail streak.
            if (_trail != null) _trail.emitting = false;
            if (_spinTrail != null) _spinTrail.emitting = false;

            if (actions != null)
            {
                _fireAction = actions.FindActionMap("Player").FindAction("Fire");
                _fireAction.performed += OnFirePerformed;
                _fireAction.Enable();
            }

            _collider = GetComponent<CircleCollider2D>();
            _collider.sharedMaterial = new PhysicsMaterial2D("BallBounce") { bounciness = 1f, friction = 0f };

            // While docked the ball is just teleported to follow the paddle every FixedUpdate -
            // it shouldn't receive collision-response forces from anything yet. Kinematic bodies
            // never get pushed by the solver (only Dynamic ones do), so a fast-turning paddle
            // clipping the ball for a frame before DockToPaddle() repositions it can no longer
            // inject a depenetration impulse that looked like the ball trying to squirm away.
            // Switches back to Dynamic in Launch()/LaunchAt() so real bouncing physics applies
            // once in play. (Deliberately not using Rigidbody2D.simulated for this - toggling it
            // off and back on can leave the body's internal state stale enough to produce a
            // spurious velocity/position snap the instant simulation resumes.)
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        private void Update()
        {
            // Fallback path only - when an actions asset is assigned, OnFirePerformed (an
            // edge-triggered callback registered once in Awake) handles this instead, and is
            // strictly more robust than polling wasPressedThisFrame's single-Update validity
            // window. This raw poll only runs for objects with no actions assigned, e.g. every
            // existing isolated test, so their behavior is unchanged.
            if (_fireAction == null && Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
                _launchRequested = true;
        }

        private void OnFirePerformed(InputAction.CallbackContext context)
        {
            _launchRequested = true;
        }

        private void OnDestroy()
        {
            if (_fireAction != null) _fireAction.performed -= OnFirePerformed;
        }

        private void FixedUpdate()
        {
            if (State == BallState.Docked)
            {
                DockToPaddle();

                if (_launchRequested)
                    Launch();


                _launchRequested = false;
                _bounceOccurred = false;
                UpdateTrailEmission();
                return;
            }

            _launchRequested = false;

            // Checked here rather than inside OnCollisionEnter2D so the native collision
            // response (this step's physics solve) has already fully resolved the bounce and
            // separated the ball before we look at - and possibly nudge - the result. (Phased
            // brick collisions are the one exception - see OnCollisionEnter2D - since that
            // cancellation needs to land before this same tick ends, not next tick.)
            if (_bounceOccurred)
            {
                _bounceOccurred = false;
                EnforceMinimumRadialComponent();
            }

            if (Spin != 0f)
            {
                float curveDegrees = Spin * spinCurveStrength * Time.fixedDeltaTime;
                _rb.linearVelocity = RotateVector(_rb.linearVelocity, curveDegrees);
                Spin = Mathf.MoveTowards(Spin, 0f, spinDecayPerSecond * Time.fixedDeltaTime);
            }

            speed = Mathf.Min(maxSpeed, speed + speedRampPerSecond * EffectiveRampMultiplier * Time.fixedDeltaTime);

            MaintainConstantSpeed();
            HandleOuterWallAndDeathZone();
            UpdateTrailEmission();

            _lastStableVelocity = _rb.linearVelocity;
        }

        /// <summary>Decides which trail (none/normal/spin) should be showing right now and, on
        /// change, kicks off a cross-fade between whatever was showing before and the new one -
        /// see CrossfadeTrails.</summary>
        private void UpdateTrailEmission()
        {
            bool launched = State == BallState.Launched;
            TrailMode desired = !launched ? TrailMode.None : (IsPhasing ? TrailMode.Spin : TrailMode.Normal);
            if (desired == _currentTrailMode) return;

            TrailMode previous = _currentTrailMode;
            _currentTrailMode = desired;

            if (_trailCrossfadeCoroutine != null) StopCoroutine(_trailCrossfadeCoroutine);
            _trailCrossfadeCoroutine = StartCoroutine(CrossfadeTrails(previous, desired));
        }

        private TrailRenderer GetTrail(TrailMode mode)
        {
            switch (mode)
            {
                case TrailMode.Normal: return _trail;
                case TrailMode.Spin: return _spinTrail;
                default: return null;
            }
        }

        /// <summary>Ramps the outgoing trail's widthMultiplier down to 0 while ramping the
        /// incoming trail's up to 1, so the switch reads as a smooth cross-fade instead of an
        /// instant cut - both trails briefly overlap and blend mid-transition. Driven via
        /// widthMultiplier (a plain TrailRenderer property) rather than a shader-specific alpha,
        /// since it works regardless of what material/shader either trail uses - important here
        /// since TrailNormal and the ball's own trail can be completely different custom shaders.
        /// Once the outgoing trail has fully faded, it's set back to non-emitting and Clear()ed
        /// (same reasoning as before: emitting=false alone leaves its existing tail lingering).</summary>
        private IEnumerator CrossfadeTrails(TrailMode from, TrailMode to)
        {
            var fadeOutTrail = GetTrail(from);
            var fadeInTrail = GetTrail(to);

            // Captured rather than assumed to be 1/0 - if this fade interrupts an in-flight one
            // (e.g. spin flickers on and off faster than trailCrossfadeDuration), starting from
            // wherever the width actually is right now avoids a visible snap before re-fading.
            float fadeOutStartWidth = fadeOutTrail != null ? fadeOutTrail.widthMultiplier : 0f;
            float fadeInStartWidth = fadeInTrail != null ? fadeInTrail.widthMultiplier : 0f;

            if (fadeInTrail != null) fadeInTrail.emitting = true;

            float elapsed = 0f;
            float duration = Mathf.Max(0.0001f, trailCrossfadeDuration);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (fadeOutTrail != null) fadeOutTrail.widthMultiplier = Mathf.Lerp(fadeOutStartWidth, 0f, t);
                if (fadeInTrail != null) fadeInTrail.widthMultiplier = Mathf.Lerp(fadeInStartWidth, 1f, t);
                yield return null;
            }

            if (fadeOutTrail != null)
            {
                fadeOutTrail.emitting = false;
                fadeOutTrail.widthMultiplier = 1f;
                fadeOutTrail.Clear();
            }
            if (fadeInTrail != null) fadeInTrail.widthMultiplier = 1f;

            _trailCrossfadeCoroutine = null;
        }

        private static Vector2 RotateVector(Vector2 v, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }

        private void DockToPaddle()
        {
            float dockRadius = settings.paddleOrbitRadius + paddle.radialThickness / 2f + WorldRadius + dockOffset;
            float rad = paddle.CurrentAngleDegrees * Mathf.Deg2Rad;
            _rb.position = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dockRadius;
            _rb.linearVelocity = Vector2.zero;
        }

        private void Launch()
        {
            _rb.bodyType = RigidbodyType2D.Dynamic;
            float rad = paddle.CurrentAngleDegrees * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            _rb.linearVelocity = direction * speed;
            _lastStableVelocity = _rb.linearVelocity;
            State = BallState.Launched;
        }

        /// <summary>Used by BallManager to bring a freshly spawned multiball clone straight
        /// into flight, bypassing the normal dock-then-launch flow (and its own private
        /// State setter, which the manager can't reach directly).</summary>
        public void LaunchAt(Vector2 position, Vector2 velocity)
        {
            _rb.bodyType = RigidbodyType2D.Dynamic;
            _rb.position = position;
            _rb.linearVelocity = velocity;
            _lastStableVelocity = velocity;
            State = BallState.Launched;
        }

        private void MaintainConstantSpeed()
        {
            if (_rb.linearVelocity.sqrMagnitude < 0.0001f) return;
            _rb.linearVelocity = _rb.linearVelocity.normalized * speed;
        }

        private void HandleOuterWallAndDeathZone()
        {
            // Guards against a ball that's already been reported lost to the manager (e.g. a
            // multiball clone) but whose Destroy() hasn't taken effect yet this frame - without
            // this it could keep bouncing off the outer wall or re-report itself lost.
            if (_reportedLost) return;

            Vector2 pos = _rb.position;
            float dist = pos.magnitude;
            float ballRadius = WorldRadius;

            if (_arenaCamera != null && _arenaCamera.orthographic)
                BounceOffScreenEdges(ballRadius);
            else
                BounceOffCircularWall(pos, ballRadius);

            if (dist < settings.deathZoneRadius)
            {
                if (ballManager != null)
                {
                    _reportedLost = true;
                    ballManager.NotifyBallLost(this);
                }
                else
                {
                    ResetToDocked();
                }

                OnBallLost?.Invoke();
            }
        }

        /// <summary>Reflects the ball off the actual visible viewport (the camera's orthographic
        /// bounds), so the outer boundary always matches the screen edges regardless of
        /// resolution/aspect ratio, rather than a fixed radius that only lined up with one
        /// specific aspect ratio.</summary>
        private void BounceOffScreenEdges(float ballRadius)
        {
            float halfHeight = _arenaCamera.orthographicSize;
            float halfWidth = halfHeight * _arenaCamera.aspect;

            float rightLimit = halfWidth - ballRadius;
            float leftLimit = -rightLimit;
            float topLimit = halfHeight - ballRadius;
            float bottomLimit = -topLimit;

            Vector2 pos = _rb.position;
            Vector2 vel = _rb.linearVelocity;
            bool bounced = false;

            if (pos.x > rightLimit && vel.x > 0f) { vel.x = -vel.x; bounced = true; }
            else if (pos.x < leftLimit && vel.x < 0f) { vel.x = -vel.x; bounced = true; }

            if (pos.y > topLimit && vel.y > 0f) { vel.y = -vel.y; bounced = true; }
            else if (pos.y < bottomLimit && vel.y < 0f) { vel.y = -vel.y; bounced = true; }

            if (!bounced) return;

            pos.x = Mathf.Clamp(pos.x, leftLimit, rightLimit);
            pos.y = Mathf.Clamp(pos.y, bottomLimit, topLimit);
            _rb.position = pos;
            _rb.linearVelocity = vel;
        }

        /// <summary>Fallback used when there's no real camera to derive screen edges from (e.g.
        /// isolated unit tests that build a ball in a bare scene) - keeps the original
        /// radius-based containment so those tests stay meaningful without a camera.</summary>
        private void BounceOffCircularWall(Vector2 pos, float ballRadius)
        {
            float outerLimit = settings.outerWallRadius - ballRadius;
            float dist = pos.magnitude;
            if (dist <= outerLimit) return;

            Vector2 normal = pos.normalized;
            float outwardComponent = Vector2.Dot(_rb.linearVelocity, normal);
            if (outwardComponent > 0f)
                _rb.linearVelocity -= 2f * outwardComponent * normal;

            _rb.position = normal * outerLimit;
        }

        private void ResetToDocked()
        {
            State = BallState.Docked;
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _reportedLost = false;
            speed = _initialSpeed * EffectiveSpeedMultiplier;
            Spin = 0f;

            // Discards any launch request that arrived while this ball's GameObject was
            // deactivated (BallManager.RespawnSequence deactivates it for the explosion/dissolve/
            // delay window before redocking). OnFirePerformed's event subscription stays live the
            // whole time regardless of GameObject.activeSelf, but FixedUpdate - the only place
            // that normally resets this flag - doesn't run while inactive, so an eager Fire press
            // during that window would otherwise sit pending and fire the instant the ball
            // reappears, launching it without an actual press after respawn.
            _launchRequested = false;
        }

        /// <summary>Public entry point for BallManager to bring the primary ball back into
        /// play once every ball has been lost.</summary>
        public void Redock() => ResetToDocked();

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var hitPaddle = collision.collider.GetComponent<PaddleController>();
            if (hitPaddle != null && State == BallState.Launched)
            {
                // A paddle swept quickly past the ball at the moment of contact imparts spin -
                // overwritten rather than accumulated, since each paddle strike is a fresh
                // deliberate shot that should determine its own curve, not stack with whatever
                // spin happened to be left over from before. Gated to Launched only - while
                // still Docked, the ball is just riding the paddle (kinematic, being carried by
                // DockToPaddle), and a fast pre-launch swing shouldn't already be winding up spin
                // before the player has actually released the ball.
                Spin = Mathf.Clamp(hitPaddle.AngularVelocityDegreesPerSecond * spinTransferFactor, -maxSpin, maxSpin);
            }

            if (IsPhasing && collision.collider.GetComponent<Brick>() != null)
            {
                // Let the brick's own OnCollisionEnter2D still destroy/damage it as normal
                // (Brick.cs reacts to any BallController it touches, regardless of spin state).
                // Cancel the bounce the physics solver just computed for this contact right here
                // - restoring the direction the ball was heading in just before the collision -
                // rather than deferring to next FixedUpdate, so there's no single-tick flicker
                // of reversed velocity before the correction lands.
                _rb.linearVelocity = _lastStableVelocity.normalized * speed;
                return;
            }

            // Just a flag - the native PhysicsMaterial2D bounce has already computed and
            // applied a correctly separated reflection by the time this fires. The actual
            // check/adjustment happens next FixedUpdate; see the comment there.
            _bounceOccurred = true;
        }

        private void EnforceMinimumRadialComponent()
        {
            Vector2 velocity = _rb.linearVelocity;
            if (velocity.sqrMagnitude < 0.0001f) return;

            // Guard against the classic Breakout degenerate case: a sequence of bounces can
            // leave the ball moving almost purely tangentially (the polar equivalent of a ball
            // stuck bouncing perfectly horizontally forever). Left alone, it settles into a
            // stable orbit grinding along a single ring instead of bouncing back through the
            // arena - and once that ring is worn through, it shoots straight out with nothing
            // left to bounce off, looking exactly like it "passed through" everything. This
            // only ever adjusts direction, never position - the native collision response
            // already separated the ball correctly, so there's no overlap left to fight.
            Vector2 radialDir = _rb.position.normalized;
            float radialComponent = Vector2.Dot(velocity, radialDir);
            float minRadialComponent = speed * 0.4f;
            if (Mathf.Abs(radialComponent) < minRadialComponent)
            {
                Vector2 tangentDir = new Vector2(-radialDir.y, radialDir.x);
                float tangentComponent = Vector2.Dot(velocity, tangentDir);
                float wantedRadial = minRadialComponent * (radialComponent >= 0f ? 1f : -1f);
                float wantedTangentMagnitude = Mathf.Sqrt(Mathf.Max(0f, speed * speed - wantedRadial * wantedRadial));
                float wantedTangent = wantedTangentMagnitude * (tangentComponent >= 0f ? 1f : -1f);
                _rb.linearVelocity = radialDir * wantedRadial + tangentDir * wantedTangent;
            }
        }
    }
}
