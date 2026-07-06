using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Defines the shape of the circular arena: death zone, paddle orbit, the hexagonal brick
    /// grid, and the outer wall boundary that clips it. Also handles hex &lt;-&gt; world conversion.
    /// (Class name kept as "PolarGridSettings" even though the grid itself is hex-based now, so
    /// existing .asset references - PolarGridSettings.asset, LevelDesigner.asset - keep working.)
    /// </summary>
    [CreateAssetMenu(fileName = "PolarGridSettings", menuName = "PolarBreakout/Grid Settings")]
    public class PolarGridSettings : ScriptableObject
    {
        [Header("Center / Death Zone")]
        public float deathZoneRadius = 1f;

        [Header("Paddle")]
        public float paddleOrbitRadius = 1.5f;

        [Header("Hex Grid")]
        [Tooltip("Center-to-corner radius of each hexagon, world units - the one knob for hex size/density.")]
        public float hexSize = 0.45f;
        [Tooltip("Visual/collider shrink applied uniformly toward each hex's own center, world units. 0 = hexes touch edge-to-edge.")]
        public float hexGap = 0.04f;

        [Header("Outer Wall")]
        [Tooltip("Also doubles as the play-area boundary radius - a hex is part of the level only if its center falls within this radius.")]
        public float outerWallRadius = 8f;

        [Header("Paddle Mesh")]
        [Tooltip("Smaller = smoother curved edges on the paddle's arc mesh, more triangles.")]
        public float curveResolutionDegrees = 4f;

        /// <summary>World position for a given angle on the paddle's orbit circle.</summary>
        public Vector2 PaddlePositionAtAngle(float angleDegrees)
        {
            float rad = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * paddleOrbitRadius;
        }

        /// <summary>Pointy-top axial -&gt; world conversion (Red Blob Games standard formula).</summary>
        public Vector2 HexToWorld(HexCoordinate coord)
        {
            float x = hexSize * (Mathf.Sqrt(3f) * coord.q + Mathf.Sqrt(3f) / 2f * coord.r);
            float y = hexSize * (1.5f * coord.r);
            return new Vector2(x, y);
        }

        /// <summary>World -&gt; nearest pointy-top axial coordinate, via fractional axial conversion
        /// then cube-rounding. Used by the Scene-view painter's mouse-to-cell lookup.</summary>
        public HexCoordinate WorldToHex(Vector2 world)
        {
            float qf = (Mathf.Sqrt(3f) / 3f * world.x - 1f / 3f * world.y) / hexSize;
            float rf = (2f / 3f * world.y) / hexSize;
            return CubeRound(qf, rf);
        }

        /// <summary>Rounds fractional cube coordinates to the nearest hex, fixing up whichever
        /// component had the largest rounding error so q+r+s still sums to exactly 0.</summary>
        private static HexCoordinate CubeRound(float qf, float rf)
        {
            float sf = -qf - rf;
            int q = Mathf.RoundToInt(qf);
            int r = Mathf.RoundToInt(rf);
            int s = Mathf.RoundToInt(sf);

            float qDiff = Mathf.Abs(q - qf);
            float rDiff = Mathf.Abs(r - rf);
            float sDiff = Mathf.Abs(s - sf);

            if (qDiff > rDiff && qDiff > sDiff) q = -r - s;
            else if (rDiff > sDiff) r = -q - s;
            // else s had the largest error - it's derived (HexCoordinate.S), nothing to store.

            return new HexCoordinate(q, r);
        }

        /// <summary>A hex is part of the level if its center falls within the outer wall radius -
        /// a hex is either fully in or fully out, never partially clipped.</summary>
        public bool IsValidCoordinate(HexCoordinate coord) => HexToWorld(coord).magnitude <= outerWallRadius;

        /// <summary>True if this hex is part of the level but at least one of its 6 neighbors
        /// isn't - i.e. it touches the play-area boundary. Used both for the EdgeCollider2D
        /// boundary wall (HexArenaBoundary) and the "border hexes" fill pattern.</summary>
        public bool IsBoundaryHex(HexCoordinate coord)
        {
            if (!IsValidCoordinate(coord)) return false;
            for (int dir = 0; dir < 6; dir++)
                if (!IsValidCoordinate(coord.Neighbor(dir))) return true;
            return false;
        }

        /// <summary>Every valid hex coordinate in the level, in a stable deterministic order -
        /// the universal enumeration primitive used by the level-building helpers, the Random
        /// Level Designer, and the Scene-view painter.</summary>
        public IEnumerable<HexCoordinate> EnumerateValidCoordinates()
        {
            int maxQR = Mathf.CeilToInt(outerWallRadius / (hexSize * Mathf.Sqrt(3f) / 2f)) + 1;
            for (int q = -maxQR; q <= maxQR; q++)
            {
                for (int r = -maxQR; r <= maxQR; r++)
                {
                    var coord = new HexCoordinate(q, r);
                    if (IsValidCoordinate(coord)) yield return coord;
                }
            }
        }
    }
}
