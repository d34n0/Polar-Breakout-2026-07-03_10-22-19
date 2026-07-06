using System;
using System.Collections.Generic;

using UnityEngine;

namespace PolarBreakout
{
    [Serializable]
    public struct BrickPlacement
    {
        public int q;
        public int r;
        public BrickTypeSO brickType;
    }

    /// <summary>
    /// A level is just data: which grid settings to use, and which brick type
    /// (if any) sits at each hex coordinate. Build these as assets in the Inspector,
    /// or generate them procedurally using the helper methods below.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "PolarBreakout/Level")]
    public class LevelSO : ScriptableObject
    {
        public PolarGridSettings gridSettings;
        public List<BrickPlacement> placements = new List<BrickPlacement>();

        public IEnumerable<(HexCoordinate coord, BrickTypeSO type)> GetPlacements()
        {
            foreach (var p in placements)
            {
                if (p.brickType == null) continue;
                yield return (new HexCoordinate(p.q, p.r), p.brickType);
            }
        }

        // --- Level-building helpers (usable from editor tooling or procedural generation) ---

        public void SetBrick(HexCoordinate coord, BrickTypeSO type)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].q == coord.q && placements[i].r == coord.r)
                {
                    var updated = placements[i];
                    updated.brickType = type;
                    placements[i] = updated;
                    return;
                }
            }
            placements.Add(new BrickPlacement { q = coord.q, r = coord.r, brickType = type });
        }

        public void ClearBrick(HexCoordinate coord)
        {
            placements.RemoveAll(p => p.q == coord.q && p.r == coord.r);
        }

        /// <summary>Fills every valid hex exactly `distance` steps from the center - a "distance
        /// shell", the hex-grid analogue of the old concentric ring.</summary>
        public void FillByDistance(int distance, BrickTypeSO type)
        {
            if (gridSettings == null) return;
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
                if (coord.DistanceFromOrigin() == distance) SetBrick(coord, type);
        }

        public void ClearByDistance(int distance)
        {
            placements.RemoveAll(p => new HexCoordinate(p.q, p.r).DistanceFromOrigin() == distance);
        }

        // --- Pattern helpers (usable from editor tooling or procedural generation) ---

        /// <summary>Fills alternating hexes in a checkerboard - (q+r) parity is a valid 2-coloring
        /// of any hex grid, since adjacent hexes always differ in q+r by exactly 1.</summary>
        public void FillCheckerboard(BrickTypeSO type)
        {
            if (gridSettings == null) return;
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
                if (Mod2(coord.q + coord.r) == 0) SetBrick(coord, type);
        }

        private static int Mod2(int value) => ((value % 2) + 2) % 2;

        /// <summary>Fills every Nth distance shell starting at offset, e.g. interval=2 for
        /// alternating shells.</summary>
        public void FillEveryNthDistance(BrickTypeSO type, int interval, int offset = 0)
        {
            if (gridSettings == null || interval <= 0) return;
            int maxDistance = 0;
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
                maxDistance = Mathf.Max(maxDistance, coord.DistanceFromOrigin());

            for (int distance = offset; distance <= maxDistance; distance += interval)
                FillByDistance(distance, type);
        }

        /// <summary>Fills only the hexes that touch the play-area boundary, leaving the interior
        /// empty - the hex-grid analogue of the old first/last ring border fill.</summary>
        public void FillBorderHexes(BrickTypeSO type)
        {
            if (gridSettings == null) return;
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
                if (gridSettings.IsBoundaryHex(coord)) SetBrick(coord, type);
        }

        /// <summary>Fills every valid hex in the level - used for the "Fill All" button.</summary>
        public void FillWithinDistance(BrickTypeSO type)
        {
            if (gridSettings == null) return;
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
                SetBrick(coord, type);
        }

        /// <summary>Replaces the whole level with a random layout sized to gridSettings - each
        /// cell independently has fillChance of getting a brick, picked uniformly from
        /// brickPool. Seeded so the same seed reproduces the same layout.</summary>
        public void GenerateRandomLevel(IList<BrickTypeSO> brickPool, float fillChance, int seed)
        {
            if (gridSettings == null || brickPool == null || brickPool.Count == 0) return;

            var rng = new System.Random(seed);
            placements.Clear();
            foreach (var coord in gridSettings.EnumerateValidCoordinates())
            {
                if (rng.NextDouble() > fillChance) continue;
                var type = brickPool[rng.Next(brickPool.Count)];
                SetBrick(coord, type);
            }
        }
    }
}
