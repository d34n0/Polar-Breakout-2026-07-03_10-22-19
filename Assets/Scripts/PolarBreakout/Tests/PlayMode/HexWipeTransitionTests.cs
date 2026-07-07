using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers HexWipeTransition's build-in/tear-down sweeps (see LevelManager.AdvanceToNextStage)
    /// and PolarGridSettings.EnumerateCoordinatesInRect, the full-viewport hex enumeration the
    /// sweep uses to tile the whole camera - not just the circular play area.
    /// </summary>
    public class HexWipeTransitionTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private PolarGridSettings BuildGrid(float hexSize, float outerWallRadius)
        {
            var grid = ScriptableObject.CreateInstance<PolarGridSettings>();
            grid.hexSize = hexSize;
            grid.hexGap = 0f;
            grid.outerWallRadius = outerWallRadius;
            return grid;
        }

        private HexWipeTransition BuildWipe(BrickGridManager manager)
        {
            // Mirrors LevelManager.Awake() - PlayBuildIn assigns manager.level itself partway
            // through (without ever calling the monolithic BuildLevel, which is what normally
            // sets _hasBuilt), so without this a stray BrickGridManager.Start() firing mid-sweep
            // could redundantly auto-build the whole level in one instant frame underneath the
            // sweep's own progressive spawning.
            manager.skipAutoBuildOnStart = true;

            var camGO = Track(new GameObject("WipeTest_Camera"));
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 1f; // Square viewport: world rect is exactly (-5,-5) to (5,5).
            camGO.transform.position = new Vector3(0f, 0f, -10f);

            var wipeGO = Track(new GameObject("WipeTest_Wipe"));
            var wipe = wipeGO.AddComponent<HexWipeTransition>();
            wipe.targetCamera = cam;
            wipe.brickGridManager = manager;
            wipe.perHexFlashDuration = 0.02f;
            wipe.totalSweepDuration = 0.08f;
            return wipe;
        }

        [UnityTest]
        public IEnumerator PlayBuildIn_EventuallyProducesExactlyLevelPlacements()
        {
            var settings = BuildGrid(1f, 20f); // Large outerWallRadius so nothing here gets clipped.

            var managerGO = Track(new GameObject("WipeTest_BrickManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();
            manager.brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;

            // (0,0) -> x=0, (1,0) -> x=1.73 - both placed. (2,0) -> x=3.46 - deliberately left
            // empty, well within the camera's +-5 rect, to prove the "no brick" branch also works.
            level.SetBrick(new HexCoordinate(0, 0), brickType);
            level.SetBrick(new HexCoordinate(1, 0), brickType);

            var wipe = BuildWipe(manager);

            yield return wipe.PlayBuildIn(level);

            Assert.IsNotNull(manager.GetBrickAt(new HexCoordinate(0, 0)), "Placed cell (0,0) should have a brick after build-in.");
            Assert.IsNotNull(manager.GetBrickAt(new HexCoordinate(1, 0)), "Placed cell (1,0) should have a brick after build-in.");
            Assert.IsNull(manager.GetBrickAt(new HexCoordinate(2, 0)), "Unplaced cell (2,0) should stay empty after build-in.");
        }

        [UnityTest]
        public IEnumerator PlayTearDown_RemovesAllBricksWithoutFiringEvents()
        {
            var settings = BuildGrid(1f, 20f);

            var managerGO = Track(new GameObject("WipeTest_TearDown_BrickManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();
            manager.brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            level.SetBrick(new HexCoordinate(0, 0), brickType);
            level.SetBrick(new HexCoordinate(1, 0), brickType);
            manager.BuildLevel(level);

            Assert.AreEqual(2, manager.RemainingDestructibleCount, "Precondition: 2 bricks built normally.");

            bool brickDestroyedFired = false;
            bool levelClearedFired = false;
            manager.OnBrickDestroyed += _ => brickDestroyedFired = true;
            manager.OnLevelCleared += () => levelClearedFired = true;

            var wipe = BuildWipe(manager);
            yield return wipe.PlayTearDown();

            Assert.AreEqual(0, manager.GetActiveBrickCoordinates().Count, "Every brick should be gone after tear-down.");
            Assert.IsFalse(brickDestroyedFired, "Tear-down is a presentation-only clear - it must not fire OnBrickDestroyed.");
            Assert.IsFalse(levelClearedFired, "Tear-down must not fire OnLevelCleared - that's a player-driven event.");
        }

        [Test]
        public void EnumerateCoordinatesInRect_CoversHexesOutsideCircularArena()
        {
            var settings = BuildGrid(1f, 2f); // Small circular arena...
            var rectCoords = new HashSet<HexCoordinate>(
                settings.EnumerateCoordinatesInRect(new Vector2(-20f, -20f), new Vector2(20f, 20f))); // ...much bigger rect.

            bool foundOutsideArena = false;
            foreach (var coord in rectCoords)
            {
                if (!settings.IsValidCoordinate(coord))
                {
                    foundOutsideArena = true;
                    break;
                }
            }

            Assert.IsTrue(foundOutsideArena,
                "The rect enumeration should include hexes outside the circular play area - unlike " +
                "EnumerateValidCoordinates, it must not be filtered by outerWallRadius.");

            ScriptableObject.DestroyImmediate(settings);
        }

        [Test]
        public void EnumerateCoordinatesInRect_CoversEveryCornerOfRect()
        {
            var settings = BuildGrid(0.5f, 100f); // Large outerWallRadius so validity never filters anything out here.
            var min = new Vector2(-5f, -5f);
            var max = new Vector2(5f, 5f);

            var coords = settings.EnumerateCoordinatesInRect(min, max).ToList();
            var worldPositions = coords.Select(settings.HexToWorld).ToList();

            bool NearAnyPosition(Vector2 corner) => worldPositions.Any(p => Vector2.Distance(p, corner) < 1f);

            Assert.IsTrue(NearAnyPosition(new Vector2(min.x, max.y)), "Should have hexes near the top-left corner.");
            Assert.IsTrue(NearAnyPosition(new Vector2(max.x, max.y)), "Should have hexes near the top-right corner.");
            Assert.IsTrue(NearAnyPosition(new Vector2(min.x, min.y)), "Should have hexes near the bottom-left corner.");
            Assert.IsTrue(NearAnyPosition(new Vector2(max.x, min.y)), "Should have hexes near the bottom-right corner.");

            ScriptableObject.DestroyImmediate(settings);
        }
    }
}
