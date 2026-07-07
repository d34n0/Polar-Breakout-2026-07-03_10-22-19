using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers the gauntlet pacing restructure: BrickGridManager's soft clear threshold (advance
    /// before a literal 100% clear), BrickTypeSO's power-up drop-rate surge near the tail,
    /// LevelManager's Clear-vs-Survive stage objective branching and the guaranteed-rare-card
    /// bonus it threads into CardOfferController, and LevelSO's seed-and-grow clustering fill.
    /// </summary>
    public class StageObjectiveTests
    {
        private readonly List<GameObject> _spawnedGameObjects = new List<GameObject>();
        private readonly List<Object> _spawnedObjects = new List<Object>();

        private GameObject Track(GameObject go)
        {
            _spawnedGameObjects.Add(go);
            return go;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            _spawnedObjects.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            // Belt-and-suspenders: CardOfferController.ShowOffer sets Time.timeScale = 0f while
            // waiting for a choice and only restores it once one is made. Every test that drives
            // ShowOffer already resolves that wait before asserting (see the ordering comments at
            // each call site), but a stuck 0 here would silently hang every scaled-time wait
            // (WaitForSeconds, Time.deltaTime) in the rest of the run, so reset unconditionally.
            Time.timeScale = 1f;

            foreach (var go in _spawnedGameObjects)
                if (go != null) Object.DestroyImmediate(go);
            _spawnedGameObjects.Clear();

            foreach (var obj in _spawnedObjects)
                if (obj != null) Object.DestroyImmediate(obj);
            _spawnedObjects.Clear();

            foreach (var capsule in Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None))
                if (capsule != null) Object.DestroyImmediate(capsule.gameObject);
        }

        private PolarGridSettings BuildGrid(float hexSize, float outerWallRadius)
        {
            var grid = Create<PolarGridSettings>();
            grid.hexSize = hexSize;
            grid.hexGap = 0f;
            grid.outerWallRadius = outerWallRadius;
            return grid;
        }

        private BrickGridManager BuildBrickManager()
        {
            var managerGO = Track(new GameObject("StageObj_BrickManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();
            manager.brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            return manager;
        }

        private static IEnumerator WaitRealSeconds(float seconds)
        {
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < seconds) yield return null;
        }

        /// <summary>Waits for LevelManager.OnSurviveStageChanged to fire, the precise signal that
        /// ActivateLevel (and therefore the Survive timer) has actually started - RemainingDestructibleCount
        /// reaching its target is NOT an equivalent proxy, since a brick's own spawn happens at the
        /// midpoint of its cell's flash, well before HexWipeTransition's overall sweep (which
        /// ActivateLevel waits on) finishes fading every cell's outline.</summary>
        private static IEnumerator WaitForSurviveStageActivation(LevelManager lm, float timeoutSeconds = 3f)
        {
            bool activated = false;
            void Handler(bool isSurvive, float duration) => activated = true;
            lm.OnSurviveStageChanged += Handler;

            float start = Time.unscaledTime;
            while (!activated && Time.unscaledTime - start < timeoutSeconds) yield return null;

            lm.OnSurviveStageChanged -= Handler;
        }

        // --- Soft clear threshold (BrickGridManager) ---

        [UnityTest]
        public IEnumerator SoftClearThreshold_FiresOnce_WhenRemainingHitsThreshold_NotZero()
        {
            var settings = BuildGrid(1f, 5f);
            var manager = BuildBrickManager();
            var level = Create<LevelSO>();
            level.gridSettings = settings;
            var brickType = Create<StandardBrickType>();
            brickType.maxHealth = 1;

            var coords = settings.EnumerateValidCoordinates().Take(10).ToList();
            foreach (var c in coords) level.SetBrick(c, brickType);

            manager.BuildLevel(level);
            manager.SetClearThreshold(3);

            int clearedCount = 0;
            manager.OnLevelCleared += () => clearedCount++;

            // Destroy down to exactly 3 remaining (7 destroyed) - should fire once, at the
            // threshold, not require reaching literal zero.
            for (int i = 0; i < 7; i++)
                manager.GetBrickAt(coords[i]).Hit(null);

            Assert.AreEqual(1, clearedCount, "OnLevelCleared should fire once RemainingDestructibleCount hits the threshold.");
            Assert.AreEqual(3, manager.RemainingDestructibleCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SoftClearThreshold_OnlyFiresOnce_AsMoreBricksDestroyedPastIt()
        {
            var settings = BuildGrid(1f, 5f);
            var manager = BuildBrickManager();
            var level = Create<LevelSO>();
            level.gridSettings = settings;
            var brickType = Create<StandardBrickType>();
            brickType.maxHealth = 1;

            var coords = settings.EnumerateValidCoordinates().Take(10).ToList();
            foreach (var c in coords) level.SetBrick(c, brickType);

            manager.BuildLevel(level);
            manager.SetClearThreshold(3);

            int clearedCount = 0;
            manager.OnLevelCleared += () => clearedCount++;

            foreach (var c in coords)
                manager.GetBrickAt(c).Hit(null);

            Assert.AreEqual(1, clearedCount,
                "OnLevelCleared must only fire once, not on every destruction past the threshold.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SoftClearThreshold_ResetsOnNextBuildLevel()
        {
            var settings = BuildGrid(1f, 5f);
            var manager = BuildBrickManager();
            var brickType = Create<StandardBrickType>();
            brickType.maxHealth = 1;
            var coords = settings.EnumerateValidCoordinates().Take(5).ToList();

            var level1 = Create<LevelSO>();
            level1.gridSettings = settings;
            foreach (var c in coords) level1.SetBrick(c, brickType);

            manager.BuildLevel(level1);
            manager.SetClearThreshold(2);

            int clearedCount = 0;
            manager.OnLevelCleared += () => clearedCount++;

            for (int i = 0; i < 3; i++) manager.GetBrickAt(coords[i]).Hit(null); // down to 2 remaining
            Assert.AreEqual(1, clearedCount);

            var level2 = Create<LevelSO>();
            level2.gridSettings = settings;
            foreach (var c in coords) level2.SetBrick(c, brickType);
            manager.BuildLevel(level2);
            manager.SetClearThreshold(2);

            for (int i = 0; i < 3; i++) manager.GetBrickAt(coords[i]).Hit(null);
            Assert.AreEqual(2, clearedCount, "A fresh BuildLevel should reset the one-shot guard.");
            yield return null;
        }

        // --- Power-up drop-rate surge (BrickTypeSO) ---

        [UnityTest]
        public IEnumerator PowerUpDropChance_ScalesUpNearClearThreshold()
        {
            var settings = BuildGrid(1f, 6f);
            var manager = BuildBrickManager();
            var brickType = Create<StandardBrickType>();
            brickType.maxHealth = 1;
            brickType.powerUpDropChance = 0.15f;
            brickType.possiblePowerUps = new[] { PowerUpType.Cannon };
            brickType.scaleDropChanceNearClear = true;

            var level = Create<LevelSO>();
            level.gridSettings = settings;
            var coords = settings.EnumerateValidCoordinates().Take(20).ToList();
            foreach (var c in coords) level.SetBrick(c, brickType);

            manager.BuildLevel(level); // InitialDestructibleCount = 20
            manager.SetClearThreshold(0);

            // Full health: RemainingDestructibleCount is still 20/20 (100%) - probe brick 0
            // repeatedly without actually destroying it, since OnDestroyed alone (not Hit) is
            // enough to roll TryDropPowerUp without touching health/manager bookkeeping.
            var probe = manager.GetBrickAt(coords[0]);
            int fullHealthDrops = CountPowerUpDrops(probe, trials: 250);

            // Destroy every other brick, leaving only the untouched probe - RemainingDestructibleCount
            // becomes 1/20 (5%), just above the clear threshold of 0.
            for (int i = 1; i < coords.Count; i++)
                manager.GetBrickAt(coords[i]).Hit(null);

            int nearThresholdDrops = CountPowerUpDrops(probe, trials: 250);

            float fullHealthRate = fullHealthDrops / 250f;
            float nearThresholdRate = nearThresholdDrops / 250f;

            Assert.Greater(nearThresholdRate, fullHealthRate * 2f,
                $"Drop rate near the clear threshold ({nearThresholdRate:P0}) should be meaningfully " +
                $"higher than at full health ({fullHealthRate:P0}).");
            yield return null;
        }

        private static int CountPowerUpDrops(Brick probe, int trials)
        {
            int drops = 0;
            for (int i = 0; i < trials; i++)
            {
                probe.BrickType.OnDestroyed(probe);
                var spawned = Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None);
                if (spawned.Length > 0)
                {
                    drops++;
                    foreach (var capsule in spawned) Object.DestroyImmediate(capsule.gameObject);
                }
            }
            return drops;
        }

        // --- Stage objective system (LevelManager) ---

        private (LevelManager levelManager, BrickGridManager brickManager, LevelSO level) BuildSurviveStageRig(
            float surviveDuration, int brickCount)
        {
            var settings = BuildGrid(1f, 5f);
            var manager = BuildBrickManager();

            var level = Create<LevelSO>();
            level.gridSettings = settings;
            level.objectiveType = StageObjectiveType.Survive;
            level.surviveDuration = surviveDuration;
            var brickType = Create<StandardBrickType>();
            brickType.maxHealth = 1;
            var coords = settings.EnumerateValidCoordinates().Take(brickCount).ToList();
            foreach (var c in coords) level.SetBrick(c, brickType);
            manager.level = level;

            var camGO = Track(new GameObject("StageObj_Camera"));
            var cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.aspect = 1f;
            camGO.transform.position = new Vector3(0f, 0f, -10f);

            var wipeGO = Track(new GameObject("StageObj_Wipe"));
            var wipe = wipeGO.AddComponent<HexWipeTransition>();
            wipe.targetCamera = cam;
            wipe.brickGridManager = manager;
            wipe.perHexFlashDuration = 0.01f;
            wipe.totalSweepDuration = 0.03f;
            // The sweep only finishes (and ActivateLevel only runs) once every swept cell's
            // outline has fully faded too - not just once bricks are spawned. outlineFadeDuration
            // defaults to 0.6s, which would leave _activeLevel null (and the Survive timer
            // unstarted) for well over half a second after RemainingDestructibleCount already
            // reads correctly - long enough for a test to destroy bricks before the stage is
            // actually "live", wrongly falling through to the default Clear-stage path.
            wipe.outlineFadeDuration = 0.01f;

            // Built inactive, fully configured, then activated - so Awake() (which reads
            // brickGridManager/hexWipeTransition to set BrickGridManager.skipAutoBuildOnStart)
            // runs only once every field is already assigned, mirroring how the real scene wires
            // this up via Inspector references before anything goes active.
            var lmGO = new GameObject("StageObj_LevelManager");
            lmGO.SetActive(false);
            var lm = lmGO.AddComponent<LevelManager>();
            lm.brickGridManager = manager;
            lm.hexWipeTransition = wipe;
            lm.levels = new[] { level };
            lm.endOfRoundDelay = 0f;
            Track(lmGO);
            lmGO.SetActive(true);

            return (lm, manager, level);
        }

        [UnityTest]
        public IEnumerator LevelManager_SurviveStage_DoesNotAdvance_OnFullClearBeforeTimerExpires()
        {
            var (lm, manager, level) = BuildSurviveStageRig(surviveDuration: 5f, brickCount: 3);

            yield return WaitForSurviveStageActivation(lm);
            Assert.AreEqual(3, manager.RemainingDestructibleCount, "Precondition: all 3 bricks should have spawned.");

            int stageChanges = 0;
            lm.OnStageChanged += _ => stageChanges++;

            var coords = level.placements.Select(p => new HexCoordinate(p.q, p.r)).ToList();
            foreach (var c in coords) manager.GetBrickAt(c)?.Hit(null);

            yield return WaitRealSeconds(1f); // well within the 5s survive timer

            Assert.AreEqual(1, lm.CurrentStage, "A Survive stage should not advance just because bricks were fully cleared.");
            Assert.AreEqual(0, stageChanges);
        }

        [UnityTest]
        public IEnumerator LevelManager_SurviveStage_Advances_WhenTimerExpires()
        {
            var (lm, _, _) = BuildSurviveStageRig(surviveDuration: 0.15f, brickCount: 3);

            float start = Time.unscaledTime;
            while (lm.CurrentStage == 1 && Time.unscaledTime - start < 5f) yield return null;

            Assert.AreEqual(2, lm.CurrentStage,
                "The Survive stage should auto-advance once its timer expires, regardless of remaining bricks.");
        }

        [UnityTest]
        public IEnumerator LevelManager_SurviveStage_FullClearBeforeTimer_GrantsGuaranteedRareOnNextOffer()
        {
            var (lm, manager, level) = BuildSurviveStageRig(surviveDuration: 0.15f, brickCount: 3);

            var eventSystemGO = Track(new GameObject("StageObj_EventSystem"));
            eventSystemGO.AddComponent<EventSystem>();
            var canvasGO = Track(new GameObject("StageObj_Canvas"));
            canvasGO.AddComponent<Canvas>();

            var controllerGO = Track(new GameObject("StageObj_CardOfferController"));
            var controller = controllerGO.AddComponent<CardOfferController>();
            var panelGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.SetActive(false);
            controller.panelRoot = panelGO;
            controller.slots = new[] { BuildSlot(panelGO.transform, "A"), BuildSlot(panelGO.transform, "B"), BuildSlot(panelGO.transform, "C") };
            foreach (var slot in controller.slots) { slot.faceDownHoldDuration = 0f; slot.flipDuration = 0f; }

            // Heavily weighted toward Common, with exactly one Rare card, so an un-guaranteed
            // offer would essentially never include it - isolating the guarantee's own effect.
            controller.commonWeight = 1000f;
            controller.uncommonWeight = 0f;
            controller.rareWeight = 0.001f;
            controller.legendaryWeight = 0f;

            var cards = new List<CardSO>();
            for (int i = 0; i < 5; i++)
            {
                var c = Create<CardSO>();
                c.displayName = $"Common {i}";
                c.description = "d";
                c.rarity = CardRarity.Common;
                cards.Add(c);
            }
            var rareCard = Create<CardSO>();
            rareCard.displayName = "The Rare One";
            rareCard.description = "d";
            rareCard.rarity = CardRarity.Rare;
            cards.Add(rareCard);
            controller.allCards = cards.ToArray();

            lm.cardOfferController = controller;

            yield return WaitForSurviveStageActivation(lm);

            var coords = level.placements.Select(p => new HexCoordinate(p.q, p.r)).ToList();
            foreach (var c in coords) manager.GetBrickAt(c)?.Hit(null);

            float waitStart = Time.unscaledTime;
            while (string.IsNullOrEmpty(controller.slots[0].rarityText.text) && Time.unscaledTime - waitStart < 5f)
                yield return null;

            bool anyRare = controller.slots.Any(s => s.rarityText.text == "RARE");

            // Resolve ShowOffer's wait-for-choice loop (which restores Time.timeScale to 1)
            // BEFORE asserting - a failed assertion here must never leave Time.timeScale stuck at
            // 0, which would hang every later scaled-time wait in the rest of the test run.
            controller.slots[0].button.onClick.Invoke();
            yield return null;

            Assert.IsTrue(anyRare,
                "A full clear before the Survive timer expires should guarantee a Rare+ card in the next offer.");
        }

        // --- Guaranteed-rare bonus (CardOfferController), isolated from LevelManager ---

        private CardOfferSlot BuildSlot(Transform parent, string label)
        {
            var slotGO = Track(new GameObject($"Slot_{label}", typeof(RectTransform)));
            slotGO.transform.SetParent(parent, false);
            var slot = slotGO.AddComponent<CardOfferSlot>();

            var rarityGO = new GameObject("Rarity", typeof(RectTransform));
            rarityGO.transform.SetParent(slotGO.transform, false);
            slot.rarityText = rarityGO.AddComponent<TextMeshProUGUI>();

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(slotGO.transform, false);
            slot.nameText = nameGO.AddComponent<TextMeshProUGUI>();

            var descGO = new GameObject("Desc", typeof(RectTransform));
            descGO.transform.SetParent(slotGO.transform, false);
            slot.descriptionText = descGO.AddComponent<TextMeshProUGUI>();

            var artGO = new GameObject("Art", typeof(RectTransform));
            artGO.transform.SetParent(slotGO.transform, false);
            slot.artImage = artGO.AddComponent<Image>();

            slot.button = slotGO.AddComponent<Button>();
            return slot;
        }

        [UnityTest]
        public IEnumerator GuaranteeRareOrBetterNextOffer_ForcesRareCardIntoOffer()
        {
            var eventSystemGO = Track(new GameObject("StageObj_EventSystem2"));
            eventSystemGO.AddComponent<EventSystem>();
            var canvasGO = Track(new GameObject("StageObj_Canvas2"));
            canvasGO.AddComponent<Canvas>();

            var controllerGO = Track(new GameObject("StageObj_CardOfferController2"));
            var controller = controllerGO.AddComponent<CardOfferController>();
            var panelGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.SetActive(false);
            controller.panelRoot = panelGO;
            controller.slots = new[] { BuildSlot(panelGO.transform, "A"), BuildSlot(panelGO.transform, "B"), BuildSlot(panelGO.transform, "C") };
            foreach (var slot in controller.slots) { slot.faceDownHoldDuration = 0f; slot.flipDuration = 0f; }

            controller.commonWeight = 1000f;
            controller.uncommonWeight = 0f;
            controller.rareWeight = 0.001f;
            controller.legendaryWeight = 0f;

            var cards = new List<CardSO>();
            for (int i = 0; i < 5; i++)
            {
                var c = Create<CardSO>();
                c.displayName = $"Common {i}";
                c.description = "d";
                c.rarity = CardRarity.Common;
                cards.Add(c);
            }
            var rareCard = Create<CardSO>();
            rareCard.displayName = "The Rare One";
            rareCard.description = "d";
            rareCard.rarity = CardRarity.Rare;
            cards.Add(rareCard);
            controller.allCards = cards.ToArray();

            controller.GuaranteeRareOrBetterNextOffer();
            controller.StartCoroutine(controller.ShowOffer());

            bool anyRare = controller.slots.Any(s => s.rarityText.text == "RARE");

            // Resolve the wait-for-choice loop (restores Time.timeScale to 1) before asserting -
            // see the identical comment in LevelManager_SurviveStage_...GrantsGuaranteedRareOnNextOffer.
            controller.slots[0].button.onClick.Invoke();
            yield return null;

            Assert.IsTrue(anyRare,
                "The offer should include the Rare card despite near-zero rare weight, thanks to the guarantee.");
        }

        [UnityTest]
        public IEnumerator GuaranteeRareOrBetterNextOffer_FallsBackGracefully_WhenNoRareCardsExist()
        {
            var eventSystemGO = Track(new GameObject("StageObj_EventSystem3"));
            eventSystemGO.AddComponent<EventSystem>();
            var canvasGO = Track(new GameObject("StageObj_Canvas3"));
            canvasGO.AddComponent<Canvas>();

            var controllerGO = Track(new GameObject("StageObj_CardOfferController3"));
            var controller = controllerGO.AddComponent<CardOfferController>();
            var panelGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.SetActive(false);
            controller.panelRoot = panelGO;
            controller.slots = new[] { BuildSlot(panelGO.transform, "A"), BuildSlot(panelGO.transform, "B"), BuildSlot(panelGO.transform, "C") };
            foreach (var slot in controller.slots) { slot.faceDownHoldDuration = 0f; slot.flipDuration = 0f; }

            var cards = new List<CardSO>();
            for (int i = 0; i < 5; i++)
            {
                var c = Create<CardSO>();
                c.displayName = $"Common {i}";
                c.description = "d";
                c.rarity = CardRarity.Common;
                cards.Add(c);
            }
            controller.allCards = cards.ToArray();

            controller.GuaranteeRareOrBetterNextOffer();

            // If PickOffer/RefreshOfferCards throws, it does so before ShowOffer ever sets
            // Time.timeScale = 0f (see ShowOffer's own ordering), so this specific assertion is
            // safe to run before any cleanup. Everything after it is not, though - see below.
            Assert.DoesNotThrow(() => controller.StartCoroutine(controller.ShowOffer()),
                "Guaranteeing a rare card should fall back gracefully, not throw, when the pool has none.");

            bool allPopulated = controller.slots.All(s => !string.IsNullOrEmpty(s.rarityText.text));

            // Resolve the wait-for-choice loop (restores Time.timeScale to 1) before asserting -
            // see the identical comment in LevelManager_SurviveStage_...GrantsGuaranteedRareOnNextOffer.
            controller.slots[0].button.onClick.Invoke();
            yield return null;

            Assert.IsTrue(allPopulated,
                "Every slot should still be populated even though the guarantee couldn't be satisfied.");
        }

        // --- Seed-and-grow clustering fill (LevelSO) ---

        [UnityTest]
        public IEnumerator GenerateClusteredLevel_SameSeedProducesSameLayout()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 5f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>(), Create<StandardBrickType>() };

            level.GenerateClusteredLevel(pool, targetFillChance: 0.4f, seedCount: 3, seed: 42);
            var first = new HashSet<(int, int, BrickTypeSO)>(level.placements.Select(p => (p.q, p.r, p.brickType)));

            level.GenerateClusteredLevel(pool, targetFillChance: 0.4f, seedCount: 3, seed: 42);
            var second = new HashSet<(int, int, BrickTypeSO)>(level.placements.Select(p => (p.q, p.r, p.brickType)));

            CollectionAssert.AreEquivalent(first, second, "The same seed should reproduce an identical layout.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateClusteredLevel_RespectsTargetFillCount()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 5f); // generous grid, growth won't stall before the target
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            int totalCells = level.gridSettings.EnumerateValidCoordinates().Count();
            level.GenerateClusteredLevel(pool, targetFillChance: 0.3f, seedCount: 3, seed: 7);

            int expected = Mathf.RoundToInt(totalCells * 0.3f);
            Assert.AreEqual(expected, level.placements.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateClusteredLevel_ZeroFillChance_FillsNothing()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 3.5f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.GenerateClusteredLevel(pool, targetFillChance: 0f, seedCount: 3, seed: 1);

            Assert.AreEqual(0, level.placements.Count);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateClusteredLevel_ReplacesExistingPlacements()
        {
            var level = Create<LevelSO>();
            level.gridSettings = BuildGrid(1f, 2f);
            var oldBrick = Create<StandardBrickType>();
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            level.FillByDistance(0, oldBrick);
            level.GenerateClusteredLevel(pool, targetFillChance: 0f, seedCount: 1, seed: 1);

            Assert.AreEqual(0, level.placements.Count,
                "Generating a clustered level should clear whatever was there before, not add on top.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator GenerateClusteredLevel_ProducesFewerConnectedComponents_ThanUniformScatter()
        {
            var settings = BuildGrid(1f, 6f);
            var pool = new List<BrickTypeSO> { Create<StandardBrickType>() };

            var clustered = Create<LevelSO>();
            clustered.gridSettings = settings;
            clustered.GenerateClusteredLevel(pool, targetFillChance: 0.35f, seedCount: 3, seed: 5);

            var scattered = Create<LevelSO>();
            scattered.gridSettings = settings;
            scattered.GenerateRandomLevel(pool, fillChance: 0.35f, seed: 5);

            int clusteredComponents = CountConnectedComponents(clustered);
            int scatteredComponents = CountConnectedComponents(scattered);

            Assert.Less(clusteredComponents, scatteredComponents,
                "Clustered generation should produce noticeably fewer, larger islands than uniform scatter at the same fill fraction.");
            yield return null;
        }

        private static int CountConnectedComponents(LevelSO level)
        {
            var cells = new HashSet<HexCoordinate>(level.placements.Select(p => new HexCoordinate(p.q, p.r)));
            var visited = new HashSet<HexCoordinate>();
            int components = 0;

            foreach (var start in cells)
            {
                if (visited.Contains(start)) continue;
                components++;

                var stack = new Stack<HexCoordinate>();
                stack.Push(start);
                visited.Add(start);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    for (int dir = 0; dir < HexCoordinate.Directions.Length; dir++)
                    {
                        var neighbor = current.Neighbor(dir);
                        if (cells.Contains(neighbor) && !visited.Contains(neighbor))
                        {
                            visited.Add(neighbor);
                            stack.Push(neighbor);
                        }
                    }
                }
            }
            return components;
        }
    }
}
