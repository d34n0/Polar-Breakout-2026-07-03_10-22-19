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
    /// tools - the distance-shell fillers, the pattern fillers, and the random level generator.
    /// These are plain data operations on placements, so no scene/Play Mode setup is needed beyond
    /// the ScriptableObject instances themselves.
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

        private PolarGridSettings BuildGrid(float hexSize, float outerWallRadius)
        {
            var grid = Create<PolarGridSettings>();
            grid.hexSize = hexSize;
            grid.hexGap = 0f;
            grid.outerWallRadius = outerWallRadius;
            return grid;
        }

        [UnityTest]
        public IEnumerator ClearByDistance_RemovesOnlyThatDistancesPlacements()
        {
            // hexSize=1, outerWallRadius=2 keeps exactly distance-0 (the center) and
            // distance-1 (its 6 neighbors, all at magnitude sqrt(3)) - distance-2's closest
            // hexes sit at magnitude 3, safely excluded.
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 2f);
            var brick = Create<StandardBrickType>();

            level.FillByDistance(0, brick);
            level.FillByDistance(1, brick);
            Assert.AreEqual(7, level.placements.Count);

            level.ClearByDistance(1);

            Assert.IsTrue(level.placements.All(p => new HexCoordinate(p.q, p.r).DistanceFromOrigin() != 1),
                "No placement should remain at the cleared distance.");
            Assert.AreEqual(1, level.placements.Count,
                "Only the 6 distance-1 bricks should have been removed, leaving the center.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillCheckerboard_AlternatesPerCell()
        {
            // hexSize=1, outerWallRadius=3.5 keeps distances 0, 1 and 2 (max magnitude at
            // distance 2 is sqrt(3)*2 ~= 3.46; distance 3's closest hexes sit at ~4.58).
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f);
            var brick = Create<StandardBrickType>();

            level.FillCheckerboard(brick);

            var filled = new HashSet<(int, int)>(level.placements.Select(p => (p.q, p.r)));
            foreach (var coord in level.gridSettings.EnumerateValidCoordinates())
            {
                bool shouldBeFilled = Mod2(coord.q + coord.r) == 0;
                Assert.AreEqual(shouldBeFilled, filled.Contains((coord.q, coord.r)),
                    $"Cell {coord} checkerboard state was wrong.");
            }
            yield return null;
        }

        private static int Mod2(int value) => ((value % 2) + 2) % 2;

        [UnityTest]
        public IEnumerator FillEveryNthDistance_FillsOnlyMatchingDistances()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f); // distances 0, 1, 2 present
            var brick = Create<StandardBrickType>();

            level.FillEveryNthDistance(brick, interval: 2, offset: 1);

            var filledDistances = new HashSet<int>(
                level.placements.Select(p => new HexCoordinate(p.q, p.r).DistanceFromOrigin()));
            CollectionAssert.AreEquivalent(new[] { 1 }, filledDistances);
            Assert.AreEqual(6, level.placements.Count, "All 6 distance-1 hexes should be filled.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillBorderHexes_FillsOnlyBoundaryHexes()
        {
            // With outerWallRadius=2, only distance-1 hexes touch the boundary (each has at
            // least one distance-2 neighbor, which falls outside the radius) - the center's
            // neighbors are all valid distance-1 hexes, so it's never a boundary hex.
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 2f);
            var brick = Create<StandardBrickType>();

            level.FillBorderHexes(brick);

            Assert.AreEqual(6, level.placements.Count);
            Assert.IsFalse(level.placements.Any(p => p.q == 0 && p.r == 0),
                "The center hex is never a boundary hex here.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator FillWithinDistance_FillsEveryValidHex()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 2f);
            var brick = Create<StandardBrickType>();

            level.FillWithinDistance(brick);

            int expected = level.gridSettings.EnumerateValidCoordinates().Count();
            Assert.AreEqual(expected, level.placements.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_FullChanceFillsEveryCell()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>(), Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 1f, seed: 1);

            int expected = level.gridSettings.EnumerateValidCoordinates().Count();
            Assert.AreEqual(expected, level.placements.Count,
                "Every cell in the grid should be filled at fillChance=1.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_ZeroChanceFillsNothing()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 0f, seed: 1);

            Assert.AreEqual(0, level.placements.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_SameSeedProducesSameLayout()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>(), Create<StandardBrickType>(), Create<StandardBrickType>() };

            level.GenerateRandomLevel(pool, fillChance: 0.5f, seed: 42);
            var first = level.placements.Select(p => (p.q, p.r, p.brickType)).ToList();

            level.GenerateRandomLevel(pool, fillChance: 0.5f, seed: 42);
            var second = level.placements.Select(p => (p.q, p.r, p.brickType)).ToList();

            CollectionAssert.AreEqual(first, second,
                "The same seed should reproduce an identical layout, including brick type choices.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateRandomLevel_ReplacesExistingPlacements()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 2f);
            var oldBrick = Create<StandardBrickType>();
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.FillByDistance(0, oldBrick);
            level.GenerateRandomLevel(pool, fillChance: 0f, seed: 1);

            Assert.AreEqual(0, level.placements.Count,
                "Generating a random level should clear whatever was there before, not add on top.");
            yield return null;
        }
    }
}
