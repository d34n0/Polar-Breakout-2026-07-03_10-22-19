using System;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// A position in the hexagonal brick grid - pointy-top axial coordinates, radiating outward
    /// from a single central hexagon at (0,0). See PolarGridSettings.HexToWorld/WorldToHex for the
    /// axial &lt;-&gt; world conversion.
    /// </summary>
    [Serializable]
    public struct HexCoordinate : IEquatable<HexCoordinate>
    {
        public int q;
        public int r;

        public HexCoordinate(int q, int r)
        {
            this.q = q;
            this.r = r;
        }

        /// <summary>Third cube coordinate, derived so q+r+s always sums to 0.</summary>
        public int S => -q - r;

        /// <summary>Number of hex steps from the origin (0,0).</summary>
        public int DistanceFromOrigin() => (Mathf.Abs(q) + Mathf.Abs(r) + Mathf.Abs(S)) / 2;

        public static int Distance(HexCoordinate a, HexCoordinate b) =>
            (Mathf.Abs(a.q - b.q) + Mathf.Abs(a.r - b.r) + Mathf.Abs(a.S - b.S)) / 2;

        /// <summary>The 6 axial neighbor offsets for a pointy-top hex, in a fixed order used
        /// consistently by Neighbor() and by HexArenaBoundary's edge-to-corner mapping.</summary>
        public static readonly HexCoordinate[] Directions =
        {
            new HexCoordinate(+1, 0),
            new HexCoordinate(+1, -1),
            new HexCoordinate(0, -1),
            new HexCoordinate(-1, 0),
            new HexCoordinate(-1, +1),
            new HexCoordinate(0, +1),
        };

        public HexCoordinate Neighbor(int direction) =>
            new HexCoordinate(q + Directions[direction].q, r + Directions[direction].r);

        public bool Equals(HexCoordinate other) => q == other.q && r == other.r;
        public override bool Equals(object obj) => obj is HexCoordinate other && Equals(other);
        public override int GetHashCode() => (q * 397) ^ r;
        public override string ToString() => $"(q:{q}, r:{r})";
    }
}
