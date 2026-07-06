using System;
using System.Collections.Generic;

using UnityEngine;

namespace PolarBreakout
{
    [Serializable]
    public struct BrickPlacement
    {
        public int ring;
        public int segment;
        public BrickTypeSO brickType;
    }

    /// <summary>
    /// A level is just data: which grid settings to use, and which brick type
    /// (if any) sits at each polar coordinate. Build these as assets in the Inspector,
    /// or generate them procedurally using the helper methods below.
    /// </summary>
    [CreateAssetMenu(fileName = "Level", menuName = "PolarBreakout/Level")]
    public class LevelSO : ScriptableObject
    {
        public PolarGridSettings gridSettings;
        public List<BrickPlacement> placements = new List<BrickPlacement>();

        public IEnumerable<(PolarCoordinate coord, BrickTypeSO type)> GetPlacements()
        {
            foreach (var p in placements)
            {
                if (p.brickType == null) continue;
                yield return (new PolarCoordinate(p.ring, p.segment), p.brickType);
            }
        }

        // --- Level-building helpers (usable from editor tooling or procedural generation) ---

        public void FillRing(int ring, BrickTypeSO type)
        {
            if (gridSettings == null) return;
            int segs = gridSettings.SegmentsInRing(ring);
            for (int s = 0; s < segs; s++)
                SetBrick(ring, s, type);
        }

        public void SetBrick(int ring, int segment, BrickTypeSO type)
        {
            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].ring == ring && placements[i].segment == segment)
                {
                    var updated = placements[i];
                    updated.brickType = type;
                    placements[i] = updated;
                    return;
                }
            }
            placements.Add(new BrickPlacement { ring = ring, segment = segment, brickType = type });
        }

        public void ClearBrick(int ring, int segment)
        {
            placements.RemoveAll(p => p.ring == ring && p.segment == segment);
        }

        public void ClearRing(int ring)
        {
            placements.RemoveAll(p => p.ring == ring);
        }

        // --- Pattern helpers (usable from editor tooling or procedural generation) ---

        /// <summary>Fills alternating segments in a checkerboard, offsetting by one each ring so
        /// adjacent rings don't line up into stripes.</summary>
        public void FillCheckerboard(BrickTypeSO type)
        {
            if (gridSettings == null) return;
            for (int ring = 0; ring < gridSettings.ringCount; ring++)
            {
                int segs = gridSettings.SegmentsInRing(ring);
                for (int s = 0; s < segs; s++)
                {
                    if ((ring + s) % 2 == 0) SetBrick(ring, s, type);
                }
            }
        }

        /// <summary>Fills every Nth ring starting at offset, e.g. interval=2 for alternating rings.</summary>
        public void FillEveryNthRing(BrickTypeSO type, int interval, int offset = 0)
        {
            if (gridSettings == null || interval <= 0) return;
            for (int ring = offset; ring < gridSettings.ringCount; ring += interval)
                FillRing(ring, type);
        }

        /// <summary>Fills only the innermost and outermost rings, leaving the middle empty.</summary>
        public void FillBorderRings(BrickTypeSO type)
        {
            if (gridSettings == null || gridSettings.ringCount <= 0) return;
            FillRing(0, type);
            FillRing(gridSettings.ringCount - 1, type);
        }

        /// <summary>Replaces the whole level with a random layout sized to gridSettings - each
        /// cell independently has fillChance of getting a brick, picked uniformly from
        /// brickPool. Seeded so the same seed reproduces the same layout.</summary>
        public void GenerateRandomLevel(IList<BrickTypeSO> brickPool, float fillChance, int seed)
        {
            if (gridSettings == null || brickPool == null || brickPool.Count == 0) return;

            var rng = new System.Random(seed);
            placements.Clear();
            for (int ring = 0; ring < gridSettings.ringCount; ring++)
            {
                int segs = gridSettings.SegmentsInRing(ring);
                for (int s = 0; s < segs; s++)
                {
                    if (rng.NextDouble() > fillChance) continue;
                    var type = brickPool[rng.Next(brickPool.Count)];
                    SetBrick(ring, s, type);
                }
            }
        }
    }
}
