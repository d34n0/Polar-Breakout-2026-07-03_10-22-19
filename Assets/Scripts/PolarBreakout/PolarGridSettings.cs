using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Defines the shape of the circular arena: death zone, paddle orbit,
    /// brick rings, and outer wall. Also handles polar -> world conversion.
    /// </summary>
    [CreateAssetMenu(fileName = "PolarGridSettings", menuName = "PolarBreakout/Grid Settings")]
    public class PolarGridSettings : ScriptableObject
    {
        [Header("Center / Death Zone")]
        public float deathZoneRadius = 1f;

        [Header("Paddle")]
        public float paddleOrbitRadius = 1.5f;

        [Header("Brick Rings")]
        [Tooltip("Radius of the innermost brick ring's center line.")]
        public float firstRingRadius = 2.5f;
        [Tooltip("Distance between consecutive ring center lines.")]
        public float ringSpacing = 0.8f;
        [Tooltip("Number of brick rings.")]
        public int ringCount = 5;
        [Tooltip("Segments (bricks) per ring. Index 0 = innermost ring. " +
                 "If shorter than ringCount, the last value repeats.")]
        public int[] segmentsPerRing = { 12, 16, 20, 24, 28 };

        [Header("Outer Wall")]
        public float outerWallRadius = 8f;

        [Header("Brick Shape")]
        [Tooltip("Radial thickness of each brick, world units. Keep <= ringSpacing so rings don't overlap.")]
        public float brickRadialThickness = 0.7f;
        [Tooltip("Visual gap between adjacent rings, world units. 0 = bricks touch.")]
        public float radialGap = 0.04f;
        [Tooltip("Visual gap between adjacent bricks in the same ring, world units of arc length. 0 = bricks touch.")]
        public float angularGapWorldUnits = 0.04f;
        [Tooltip("Smaller = smoother curved edges on each brick, more triangles.")]
        public float curveResolutionDegrees = 4f;

        public int SegmentsInRing(int ring)
        {
            if (segmentsPerRing == null || segmentsPerRing.Length == 0) return 16;
            ring = Mathf.Clamp(ring, 0, segmentsPerRing.Length - 1);
            return Mathf.Max(1, segmentsPerRing[ring]);
        }

        public float RingRadius(int ring) => firstRingRadius + ring * ringSpacing;

        public float SegmentAngleDegrees(int ring) => 360f / SegmentsInRing(ring);

        /// <summary>Angle, in degrees, at the center of the given segment. 0 = +X axis, CCW positive.</summary>
        public float SegmentCenterAngle(PolarCoordinate coord)
        {
            float segAngle = SegmentAngleDegrees(coord.ring);
            return (coord.segment + 0.5f) * segAngle;
        }

        public Vector2 PolarToWorld(PolarCoordinate coord)
        {
            float radius = RingRadius(coord.ring);
            float angleRad = SegmentCenterAngle(coord) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angleRad) * radius, Mathf.Sin(angleRad) * radius);
        }

        /// <summary>Rotation so a brick's local "up" faces outward, away from the center.</summary>
        public Quaternion PolarToWorldRotation(PolarCoordinate coord)
        {
            float angleDeg = SegmentCenterAngle(coord);
            return Quaternion.Euler(0, 0, angleDeg - 90f);
        }

        /// <summary>World position for a given angle on the paddle's orbit circle.</summary>
        public Vector2 PaddlePositionAtAngle(float angleDegrees)
        {
            float rad = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * paddleOrbitRadius;
        }

        /// <summary>Inner/outer radius of a brick's mesh at this coordinate, after applying radialGap.</summary>
        public void GetBrickRadialRange(PolarCoordinate coord, out float innerRadius, out float outerRadius)
        {
            float centerRadius = RingRadius(coord.ring);
            float halfThickness = brickRadialThickness / 2f;
            innerRadius = centerRadius - halfThickness + radialGap / 2f;
            outerRadius = centerRadius + halfThickness - radialGap / 2f;
        }

        /// <summary>Start/end angle (degrees) of a brick's mesh at this coordinate, after applying angularGapWorldUnits.</summary>
        public void GetBrickAngleRange(PolarCoordinate coord, out float startAngleDeg, out float endAngleDeg)
        {
            float segAngle = SegmentAngleDegrees(coord.ring);
            float rawStart = coord.segment * segAngle;
            float rawEnd = rawStart + segAngle;

            float radius = RingRadius(coord.ring);
            float angularGapDeg = radius > 0.001f ? (angularGapWorldUnits / radius) * Mathf.Rad2Deg : 0f;

            startAngleDeg = rawStart + angularGapDeg / 2f;
            endAngleDeg = rawEnd - angularGapDeg / 2f;
        }

        public bool IsValidCoordinate(PolarCoordinate coord)
        {
            if (coord.ring < 0 || coord.ring >= ringCount) return false;
            int segs = SegmentsInRing(coord.ring);
            return coord.segment >= 0 && coord.segment < segs;
        }
    }
}
