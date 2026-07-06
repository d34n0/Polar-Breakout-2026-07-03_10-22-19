using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers LevelSO's level-building helpers used by LevelSOEditor's testing/pattern/random
    /// tools - ClearRing, the pattern fillers, and the random level generator. These are plain
    /// data operations on placements, so no scene/Play Mode setup is needed beyond the
    /// ScriptableObject instances themselves.
    /// </summary>
    public class LevelSOTests
    {
        private readonly List<Object> _created = new List<Object>();

        private T Create<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            _created.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        private PolarGridSettings BuildGrid(int ringCount, params int[] segmentsPerRing)
        {
            var grid = Create<PolarGridSettings>();
            grid.ringCount = ringCount;
            grid.segmentsPerRing = segmentsPerRing;
            return grid;
        }

        [UnityTest]
        public IEnumerator ClearRing_RemovesOnlyThatRingsPlacements()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(3, 4, 4, 4);
            var brick = Create<StandardBrickType>();

            for (int r = 0; r < 3; r++) level.FillRing(r, brick);
            Assert.AreEqual(12, level.placements.Count);

            level.ClearRing(1);

            Assert.IsTrue(level.placements.All(p => p.ring != 1),
                "No placement should remain on the cleared ring.");
            Assert.AreEqual(8, level.placements.Count,
                "Only ring 1's 4 bricks should have been removed.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillCheckerboard_AlternatesPerCellAndOffsetsPerRing()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(2, 4, 4);
            var brick = Create<StandardBrickType>();

            level.FillCheckerboard(brick);

            var filled = new HashSet<(int, int)>(level.placements.Select(p => (p.ring, p.segment)));
            for (int ring = 0; ring < 2; ring++)
            {
                for (int seg = 0; seg < 4; seg++)
                {
                    bool shouldBeFilled = (ring + seg) % 2 == 0;
                    Assert.AreEqual(shouldBeFilled, filled.Contains((ring, seg)),
                        $"Cell (ring={ring}, seg={seg}) checkerboard state was wrong.");
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillEveryNthRing_FillsOnlyMatchingRings()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(5, 4, 4, 4, 4, 4);
            var brick = Create<StandardBrickType>();

            level.FillEveryNthRing(brick, interval: 2, offset: 1);

            var filledRings = new HashSet<int>(level.placements.Select(p => p.ring));
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, filledRings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillBorderRings_FillsOnlyFirstAndLastRing()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(4, 4, 4, 4, 4);
            var brick = Create<StandardBrickType>();

            level.FillBorderRings(brick);

            var filledRings = new HashSet<int>(level.placements.Select(p => p.ring));
            CollectionAssert.AreEquivalent(new[] { 0, 3 }, filledRings);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_FullChanceFillsEveryCell()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(3, 4, 5, 6);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>(), Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 1f, seed: 1);

            Assert.AreEqual(4 + 5 + 6, level.placements.Count,
                "Every cell in the grid should be filled at fillChance=1.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_ZeroChanceFillsNothing()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(3, 4, 5, 6);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 0f, seed: 1);

            Assert.AreEqual(0, level.placements.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_SameSeedProducesSameLayout()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(4, 6, 8, 10, 12);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>(), Create<StandardBrickType>(), Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 0.5f, seed: 42);
            var first = level.placements.Select(p => (p.ring, p.segment, p.brickType)).ToList();

            level.GenerateRandomLevel(pool, fillChance: 0.5f, seed: 42);
            var second = level.placements.Select(p => (p.ring, p.segment, p.brickType)).ToList();

            CollectionAssert.AreEqual(first, second,
                "The same seed should reproduce an identical layout, including brick type choices.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_ReplacesExistingPlacements()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(2, 4, 4);
            var oldBrick = Create<StandardBrickType>();
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.FillRing(0, oldBrick);
            level.GenerateRandomLevel(pool, fillChance: 0f, seed: 1);

            Assert.AreEqual(0, level.placements.Count,
                "Generating a random level should clear whatever was there before, not add on top.");
            yield return null;
        }
    }
}
