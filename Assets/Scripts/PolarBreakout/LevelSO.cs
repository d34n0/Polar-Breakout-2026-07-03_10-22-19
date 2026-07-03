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
    }
}
