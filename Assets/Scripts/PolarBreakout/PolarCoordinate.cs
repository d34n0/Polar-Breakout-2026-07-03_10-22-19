using System;

namespace PolarBreakout
{
    /// <summary>
    /// A position in the polar brick grid.
    /// ring 0 = innermost ring (closest to the paddle / death zone), increasing outward.
    /// segment = index around that ring, starting at angle 0 (+X axis) going counter-clockwise.
    /// </summary>
    [Serializable]
    public struct PolarCoordinate : IEquatable<PolarCoordinate>
    {
        public int ring;
        public int segment;

        public PolarCoordinate(int ring, int segment)
        {
            this.ring = ring;
            this.segment = segment;
        }

        public bool Equals(PolarCoordinate other) => ring == other.ring && segment == other.segment;
        public override bool Equals(object obj) => obj is PolarCoordinate other && Equals(other);
        public override int GetHashCode() => (ring * 397) ^ segment;
        public override string ToString() => $"(ring:{ring}, seg:{segment})";
    }
}
