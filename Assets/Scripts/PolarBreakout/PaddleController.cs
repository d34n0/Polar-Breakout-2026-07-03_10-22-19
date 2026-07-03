using UnityEngine;
using UnityEngine.InputSystem;

namespace PolarBreakout
{
    /// <summary>
    /// Paddle that orbits the arena center at a fixed radius. Builds its visual/collision
    /// shape once, centered at angle 0, then moves by rotating its transform each frame -
    /// the same "geometry baked in local space" trick bricks use, just kinematic instead
    /// of static. Requires the GameObject to sit at the arena center (0,0,0).
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(PolygonCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class PaddleController : MonoBehaviour
    {
        [Header("References")]
        public PolarGridSettings settings;

        [Header("Shape")]
        [Tooltip("How wide the paddle is, in degrees of arc.")]
        public float angularWidthDegrees = 24f;
        [Tooltip("Radial thickness of the paddle, world units.")]
        public float radialThickness = 0.35f;

        [Header("Movement")]
        [Tooltip("Below this stick magnitude, the paddle holds its last angle instead of snapping to 0.")]
        public float stickDeadzone = 0.1f;
        [Tooltip("How fast the paddle eases toward the stick's target angle, in degrees per second. Higher = snappier.")]
        public float turnSpeedDegreesPerSecond = 720f;

        public float CurrentAngleDegrees { get; private set; }

        /// <summary>
        /// When set, overrides the stick-derived target angle each FixedUpdate (used by the
        /// Autopilot ability). Set back to null to return control to the stick.
        /// </summary>
        public float? AutopilotOverrideAngleDegrees;

        private float _targetAngleDegrees;
        private Rigidbody2D _rb;

        private void Awake()
        {
            transform.localPosition = Vector3.zero;

            _rb = GetComponent<Rigidbody2D>();
            _rb.bodyType = RigidbodyType2D.Kinematic;
            _rb.gravityScale = 0f;

            BuildShape();
        }

        private void BuildShape()
        {
            float innerRadius = settings.paddleOrbitRadius - radialThickness / 2f;
            float outerRadius = settings.paddleOrbitRadius + radialThickness / 2f;
            float halfWidth = angularWidthDegrees / 2f;

            var meshFilter = GetComponent<MeshFilter>();
            meshFilter.mesh = PolarMeshUtility.BuildArcSegmentMesh(
                innerRadius, outerRadius, -halfWidth, halfWidth, settings.curveResolutionDegrees);

            var points = PolarMeshUtility.BuildArcOutlinePoints(
                innerRadius, outerRadius, -halfWidth, halfWidth, settings.curveResolutionDegrees);
            var collider = GetComponent<PolygonCollider2D>();
            collider.pathCount = 1;
            collider.SetPath(0, points);
        }

        private void FixedUpdate()
        {
            if (AutopilotOverrideAngleDegrees.HasValue)
            {
                _targetAngleDegrees = AutopilotOverrideAngleDegrees.Value;
            }
            else
            {
                Vector2 stick = Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;

                // Direct position mapping: the target angle always matches the stick's direction
                // (stick up -> paddle at top), not a steering rate. Below the deadzone, hold the
                // last target instead of snapping to 0 for a centered/released stick.
                if (stick.magnitude > stickDeadzone)
                    _targetAngleDegrees = Mathf.Repeat(Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg, 360f);
            }

            // Ease the visual angle toward that target at a capped speed instead of jumping
            // straight to it, so a sudden stick flick doesn't look like the paddle teleporting.
            float maxDelta = turnSpeedDegreesPerSecond * Time.fixedDeltaTime;
            CurrentAngleDegrees = Mathf.MoveTowardsAngle(CurrentAngleDegrees, _targetAngleDegrees, maxDelta);

            _rb.MoveRotation(CurrentAngleDegrees);
        }
    }
}
