using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers the new brick-type/power-up/paddle-ability systems: exploding chain reactions,
    /// power-up capsule pickup, Multiball's ball-lifecycle orchestration, Autopilot, and Cannon.
    /// </summary>
    public class AbilityTests
    {
        // Every test builds its own paddle/ball/manager GameObjects from scratch, and none of
        // them rely on anything surviving between test methods - so tearing down whatever a
        // test created (plus any stray Bullet/PowerUpCapsule it spawned indirectly) keeps
        // later tests in this fixture from tripping over an earlier test's leftover ball or
        // paddle (e.g. FindObjectsByType<BallController> picking up a previous test's ball).
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

            foreach (var bullet in Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None))
                Object.DestroyImmediate(bullet.gameObject);
            foreach (var capsule in Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None))
                Object.DestroyImmediate(capsule.gameObject);
            foreach (var beam in Object.FindObjectsByType<LaserBeam>(FindObjectsSortMode.None))
                Object.DestroyImmediate(beam.gameObject);
        }

        [UnityTest]
        public IEnumerator ExplodingBrick_ChainReaction_DestroysConnectedBricksOnceEach()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.firstRingRadius = 2f;
            settings.ringSpacing = 1f;
            settings.ringCount = 1;
            settings.segmentsPerRing = new[] { 16 };
            settings.brickRadialThickness = 0.5f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;
            settings.curveResolutionDegrees = 10f;

            var explodingType = ScriptableObject.CreateInstance<ExplodingBrickType>();
            explodingType.maxHealth = 1;
            explodingType.explosionRadius = 1f;

            var standardType = ScriptableObject.CreateInstance<StandardBrickType>();
            standardType.maxHealth = 1;

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            // A chain of 3 exploding bricks (0,1,2), a standard brick just within the last
            // explosion's radius (3), and a standard brick far enough away to survive (8).
            level.SetBrick(0, 0, explodingType);
            level.SetBrick(0, 1, explodingType);
            level.SetBrick(0, 2, explodingType);
            level.SetBrick(0, 3, standardType);
            level.SetBrick(0, 8, standardType);

            var brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            var managerGO = Track(new GameObject("ExplodeTestManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();
            manager.brickPrefab = brickPrefab;
            manager.BuildLevel(level);

            Assert.AreEqual(5, manager.RemainingDestructibleCount, "Precondition: 5 destructible bricks placed.");

            var triggerBrick = manager.GetBrickAt(new PolarCoordinate(0, 0));
            Assert.IsNotNull(triggerBrick, "Precondition: trigger brick should exist.");

            triggerBrick.Hit(null);

            // Each exploding brick now flashes for its own fuseDuration before detonating and
            // triggering the next link (see ExplodingBrickType.OnFlashComplete), so the 3-brick
            // chain resolves over several sequential fuse delays instead of in a single frame -
            // give it enough real time to fully cascade through before checking the end state.
            yield return new WaitForSeconds(explodingType.fuseDuration * 4f + 0.5f);

            Assert.AreEqual(1, manager.RemainingDestructibleCount,
                "The chain should destroy the 3 exploding bricks plus the adjacent standard brick exactly once each, leaving only the far-away control brick.");
            Assert.IsNotNull(manager.GetBrickAt(new PolarCoordinate(0, 8)), "The far-away control brick should have survived.");
        }

        [UnityTest]
        public IEnumerator BrickTypeSO_GuaranteedDropChance_SpawnsPowerUpCapsuleOnDestroy()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.firstRingRadius = 2f;
            settings.ringSpacing = 1f;
            settings.ringCount = 1;
            settings.segmentsPerRing = new[] { 8 };
            settings.brickRadialThickness = 0.7f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;
            settings.curveResolutionDegrees = 10f;

            var managerGO = Track(new GameObject("DropChanceManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();

            // PowerUpCapsule.Update() self-destructs immediately if it can't find a
            // PaddleController in the scene, so one needs to exist even though this test isn't
            // about catching it. Its default angle (0, no stick input) doesn't line up with the
            // brick's segment center (~22.5 degrees), so it won't accidentally catch it either.
            var paddleGO = Track(new GameObject("DropChancePaddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            brickType.powerUpDropChance = 1f;
            brickType.possiblePowerUps = new[] { PowerUpType.Cannon };

            var brickGO = Track(new GameObject("DropChanceBrick"));
            var brick = brickGO.AddComponent<Brick>();
            brick.Initialize(manager, settings, new PolarCoordinate(0, 0), brickType);

            brick.Hit(null);
            yield return new WaitForSeconds(brick.flashDuration + 0.2f);

            var capsules = Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None);
            Assert.AreEqual(1, capsules.Length, "A brick type with a guaranteed (100%) drop chance should spawn exactly one power-up capsule when destroyed.");
            Assert.AreEqual(PowerUpType.Cannon, capsules[0].Type, "The spawned capsule should carry the brick type's configured power-up type.");
        }

        [UnityTest]
        public IEnumerator BrickTypeSO_ZeroDropChance_NeverSpawnsPowerUpCapsule()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.firstRingRadius = 2f;
            settings.ringSpacing = 1f;
            settings.ringCount = 1;
            settings.segmentsPerRing = new[] { 8 };
            settings.brickRadialThickness = 0.7f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;
            settings.curveResolutionDegrees = 10f;

            var managerGO = Track(new GameObject("NoDropChanceManager"));
            var manager = managerGO.AddComponent<BrickGridManager>();

            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            // powerUpDropChance left at its default (0) - the common case for most brick tiers.

            var brickGO = Track(new GameObject("NoDropChanceBrick"));
            var brick = brickGO.AddComponent<Brick>();
            brick.Initialize(manager, settings, new PolarCoordinate(0, 0), brickType);

            brick.Hit(null);
            yield return new WaitForSeconds(brick.flashDuration + 0.2f);

            var capsules = Object.FindObjectsByType<PowerUpCapsule>(FindObjectsSortMode.None);
            Assert.AreEqual(0, capsules.Length, "A brick type with a 0 drop chance should never spawn a power-up capsule.");
        }

        [UnityTest]
        public IEnumerator PowerUpCapsule_CaughtByPaddle_GrantsAbility()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("Capsule_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var abilities = paddle.gameObject.AddComponent<PaddleAbilities>();

            // Paddle sits at angle 0 by default (no stick input) - spawn the capsule directly
            // on top of it so it's caught essentially immediately.
            var capsuleGO = Track(new GameObject("TestCapsule"));
            var capsule = capsuleGO.AddComponent<PowerUpCapsule>();
            capsule.Initialize(new Vector2(settings.paddleOrbitRadius, 0f), PowerUpType.Cannon);

            bool caught = false;
            for (int i = 0; i < 10 && !caught; i++)
            {
                yield return null;
                caught = capsuleGO == null;
            }

            Assert.IsTrue(caught, "A capsule spawned right on the paddle should be caught.");
            Assert.Greater(abilities.CannonAmmo, 0, "Catching a Cannon capsule should grant Cannon ammo.");
        }

        [UnityTest]
        public IEnumerator PowerUpCapsule_Uncollected_SelfDestructsAtDeathZone()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("Capsule_Paddle2"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);
            paddleGO.AddComponent<PaddleAbilities>();

            // Spawn far from the paddle's angle (0) so it's never caught, and just above the
            // death zone so the test doesn't need to wait for a long fall.
            var capsuleGO = Track(new GameObject("TestCapsule2"));
            var capsule = capsuleGO.AddComponent<PowerUpCapsule>();
            float rad = 180f * Mathf.Deg2Rad;
            capsule.Initialize(new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * 1.2f, PowerUpType.Multiball);

            // Real-time wait rather than a fixed frame count - the capsule falls via
            // Time.deltaTime in Update(), so how much it actually progresses per frame depends
            // on how fast the Editor happens to be ticking, not on frame count alone. Distance
            // to cover is only 0.2 units at fallSpeed 2/s (0.1s minimum); this leaves generous margin.
            yield return new WaitForSeconds(1f);

            Assert.IsTrue(capsuleGO == null, "An uncollected capsule should self-destruct once it reaches the death zone.");
        }

        [UnityTest]
        public IEnumerator Multiball_LosingIndividualBalls_DoesNotRedockUntilAllGone()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("MB_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("MB_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("MB_Manager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;
            ball.ballManager = ballManager;

            ball.LaunchAt(new Vector2(3f, 0f), new Vector2(8f, 0f));
            yield return new WaitForFixedUpdate();

            ballManager.ActivateMultiball();

            var allBalls = Object.FindObjectsByType<BallController>(FindObjectsSortMode.None);
            Assert.AreEqual(3, allBalls.Length, "Multiball should result in 3 balls total.");

            var clones = new List<BallController>();
            foreach (var b in allBalls)
                if (b != ball) clones.Add(b);
            Assert.AreEqual(2, clones.Count, "Precondition: exactly 2 clones spawned.");

            // Lose the first clone.
            clones[0].GetComponent<Rigidbody2D>().position = Vector2.zero;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsTrue(clones[0] == null, "A lost clone should be destroyed.");
            Assert.AreEqual(BallState.Launched, ball.State, "Losing one of several balls should not redock the primary yet.");

            // Lose the second clone.
            clones[1].GetComponent<Rigidbody2D>().position = Vector2.zero;
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsTrue(clones[1] == null, "The other lost clone should be destroyed too.");
            Assert.AreEqual(BallState.Launched, ball.State, "One ball is still in play - should not redock yet.");

            // Lose the primary - now every ball is gone, so it should redock. BallManager now
            // runs a respawn sequence (explosion + delay + dissolve-in) rather than redocking
            // instantly - this test has no explosion prefab or DissolveEffect wired, so the only
            // real delay left is respawnDelay itself; wait it out with margin before asserting.
            ball.GetComponent<Rigidbody2D>().position = Vector2.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForSeconds(ballManager.respawnDelay + 0.3f);
            Assert.AreEqual(BallState.Docked, ball.State, "Losing the last ball in play should redock the primary.");
        }

        [UnityTest]
        public IEnumerator Autopilot_OverridesPaddleAngleThenReverts()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("AP_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("AP_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("AP_Manager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;

            var abilities = paddle.gameObject.AddComponent<PaddleAbilities>();
            abilities.ballManager = ballManager;
            abilities.autopilotDuration = 0.3f;

            // Park the ball at 90 degrees, well away from the paddle's default angle (0), with
            // zero velocity so it just sits there as a stable target to track.
            ball.LaunchAt(new Vector2(0f, 3f), Vector2.zero);

            abilities.CollectPowerUp(PowerUpType.Autopilot);
            Assert.IsTrue(abilities.IsAutopilotActive, "Collecting Autopilot should activate it immediately.");

            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();

            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(paddle.CurrentAngleDegrees, 90f)), 5f,
                "While Autopilot is active, the paddle should track the ball's angle.");

            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsFalse(abilities.IsAutopilotActive, "Autopilot should expire after its configured duration.");
            Assert.IsNull(paddle.AutopilotOverrideAngleDegrees, "Expired autopilot should release control back to the stick.");
        }

        [UnityTest]
        public IEnumerator Cannon_FiresBulletsThatDestroyBricks_AndDepletesAmmo()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;
            // A single full-circle "ring" so the bullet hits it regardless of exact firing angle.
            settings.firstRingRadius = 3f;
            settings.ringSpacing = 1f;
            settings.ringCount = 1;
            settings.segmentsPerRing = new[] { 1 };
            settings.brickRadialThickness = 1f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;

            var paddleGO = Track(new GameObject("Cannon_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("Cannon_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("Cannon_BallManager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;

            var abilities = paddle.gameObject.AddComponent<PaddleAbilities>();
            abilities.ballManager = ballManager;
            abilities.cannonAmmoPerPickup = 2;
            abilities.bulletSpeed = 20f;

            var brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            var brickManagerGO = Track(new GameObject("Cannon_BrickManager"));
            var brickManager = brickManagerGO.AddComponent<BrickGridManager>();
            brickManager.brickPrefab = brickPrefab;

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            level.SetBrick(0, 0, brickType);
            brickManager.BuildLevel(level);

            var targetBrick = brickManager.GetBrickAt(new PolarCoordinate(0, 0));
            Assert.IsNotNull(targetBrick, "Precondition: target brick should exist in the bullet's path.");

            // Spawned well outside deathZoneRadius (1f), moving further outward - this test never
            // wires ball.ballManager (only ballManager.primaryBall), so BallController takes its
            // no-manager death-zone branch and would silently redock (undoing Launched) if this
            // ever drifted back inside the death zone, regardless of script execution order.
            ball.LaunchAt(new Vector2(2f, 0f), new Vector2(8f, 0f)); // ball in Launched state so firing is allowed
            abilities.CollectPowerUp(PowerUpType.Cannon);
            Assert.AreEqual(2, abilities.CannonAmmo);

            var gamepad = InputSystem.AddDevice<Gamepad>();

            SetButtonSouth(gamepad, true);
            yield return WaitUntilAmmoIs(abilities, 1);
            Assert.AreEqual(1, abilities.CannonAmmo, "Firing once should consume one shot.");

            bool brickDestroyed = false;
            for (int i = 0; i < 20 && !brickDestroyed; i++)
            {
                yield return new WaitForFixedUpdate();
                brickDestroyed = targetBrick == null;
            }
            Assert.IsTrue(brickDestroyed, "The fired bullet should reach and destroy the brick in its path.");

            SetButtonSouth(gamepad, false);
            yield return new WaitForFixedUpdate();
            SetButtonSouth(gamepad, true);
            yield return WaitUntilAmmoIs(abilities, 0);
            Assert.AreEqual(0, abilities.CannonAmmo, "The second shot should deplete the ammo.");

            SetButtonSouth(gamepad, false);
            yield return new WaitForFixedUpdate();
            SetButtonSouth(gamepad, true);
            // No ammo left, so there's nothing to "wait until" - just give firing a few frames'
            // worth of chances to (incorrectly) go negative before confirming it stayed at 0.
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, abilities.CannonAmmo, "Firing with no ammo left should not go negative.");

            InputSystem.RemoveDevice(gamepad);
        }

        [UnityTest]
        public IEnumerator LaserBeam_ConsumesAllAmmoInOneShot_AndDestroysEveryBrickInPath()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 10f;
            settings.curveResolutionDegrees = 15f;
            // Each ring is a single full-circle segment, like the Cannon test above, so the beam
            // hits every ring regardless of the paddle's exact firing angle.
            settings.firstRingRadius = 3f;
            settings.ringSpacing = 1f;
            settings.ringCount = 4;
            settings.segmentsPerRing = new[] { 1, 1, 1, 1 };
            settings.brickRadialThickness = 1f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;

            var paddleGO = Track(new GameObject("Laser_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("Laser_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("Laser_BallManager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;

            var runModsGO = Track(new GameObject("Laser_RunModifiers"));
            var runMods = runModsGO.AddComponent<RunModifiers>();
            var laserCard = AssetDatabase.LoadAssetAtPath<CardSO>("Assets/Cards/Card_LaserCannon.asset");
            Assert.IsNotNull(laserCard, "Precondition: Card_LaserCannon.asset should exist.");
            runMods.AddCard(laserCard);

            var abilities = paddle.gameObject.AddComponent<PaddleAbilities>();
            abilities.ballManager = ballManager;
            abilities.runModifiers = runMods;
            // Deliberately more than 1 so depleting straight to 0 on a single shot is a
            // meaningful assertion, not a coincidence of cannonAmmoPerPickup happening to be 1.
            abilities.cannonAmmoPerPickup = 3;

            var brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            var brickManagerGO = Track(new GameObject("Laser_BrickManager"));
            var brickManager = brickManagerGO.AddComponent<BrickGridManager>();
            brickManager.brickPrefab = brickPrefab;

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            for (int ring = 0; ring < 4; ring++)
                level.SetBrick(ring, 0, brickType);
            brickManager.BuildLevel(level);

            var brick0 = brickManager.GetBrickAt(new PolarCoordinate(0, 0));
            var brick1 = brickManager.GetBrickAt(new PolarCoordinate(1, 0));
            var brick2 = brickManager.GetBrickAt(new PolarCoordinate(2, 0));
            var brick3 = brickManager.GetBrickAt(new PolarCoordinate(3, 0));
            Assert.IsNotNull(brick0, "Precondition: ring 0 brick should exist.");
            Assert.IsNotNull(brick1, "Precondition: ring 1 brick should exist.");
            Assert.IsNotNull(brick2, "Precondition: ring 2 brick should exist.");
            Assert.IsNotNull(brick3, "Precondition: ring 3 brick should exist.");

            ball.LaunchAt(new Vector2(2f, 0f), new Vector2(8f, 0f));
            abilities.CollectPowerUp(PowerUpType.Cannon);
            Assert.AreEqual(3, abilities.CannonAmmo);

            var gamepad = InputSystem.AddDevice<Gamepad>();
            SetButtonSouth(gamepad, true);
            // A laser shot spends the whole pickup at once - prevents firing the beam more than
            // once per Cannon capsule regardless of how much ammo bonus other cards granted.
            yield return WaitUntilAmmoIs(abilities, 0);
            Assert.AreEqual(0, abilities.CannonAmmo, "Firing the beam should consume all remaining ammo in one shot, not just one.");

            // The beam should destroy every brick along its path in the same instant, not stop
            // at the first one the way a normal (non-piercing) bullet would.
            bool allDestroyed = false;
            for (int i = 0; i < 10 && !allDestroyed; i++)
            {
                yield return new WaitForFixedUpdate();
                allDestroyed = brick0 == null && brick1 == null && brick2 == null && brick3 == null;
            }
            Assert.IsTrue(allDestroyed, "The laser beam should destroy every brick in its path in one shot.");

            // With ammo already at 0, another press should have nothing left to fire.
            SetButtonSouth(gamepad, false);
            yield return new WaitForFixedUpdate();
            SetButtonSouth(gamepad, true);
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, abilities.CannonAmmo, "Firing again with no ammo left should not go negative.");

            InputSystem.RemoveDevice(gamepad);
        }

        [UnityTest]
        public IEnumerator Bullet_WithOneRicochet_DestroysTwoBricksThenSelfDestructs()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.outerWallRadius = 10f;
            settings.firstRingRadius = 3f;
            settings.ringSpacing = 1f;
            settings.ringCount = 2;
            // Each ring a single full-circle segment (like the Cannon/Laser tests above) - the
            // bullet is fired dead along the X axis, so the exact hit angle doesn't matter, only
            // that both rings cover it.
            settings.segmentsPerRing = new[] { 1, 1 };
            settings.brickRadialThickness = 0.5f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;
            settings.curveResolutionDegrees = 10f;

            var managerGO = Track(new GameObject("Ricochet_Manager"));
            var manager = managerGO.AddComponent<BrickGridManager>();

            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;

            var innerGO = Track(new GameObject("Ricochet_InnerBrick"));
            var innerBrick = innerGO.AddComponent<Brick>();
            innerBrick.Initialize(manager, settings, new PolarCoordinate(0, 0), brickType);

            var outerGO = Track(new GameObject("Ricochet_OuterBrick"));
            var outerBrick = outerGO.AddComponent<Brick>();
            outerBrick.Initialize(manager, settings, new PolarCoordinate(1, 0), brickType);

            var bulletGO = Track(new GameObject("Ricochet_Bullet"));
            var bullet = bulletGO.AddComponent<Bullet>();
            // Fired dead along the X axis (angle 0) from just inside the inner ring, with one
            // ricochet: should hit and destroy the inner brick, bounce straight back the way it
            // came (reflecting off the purely-radial normal at that point reverses a purely-
            // radial velocity), pass back through the inner ring's (already-destroyed, collider-
            // disabled) radius untouched, then go on to hit and destroy the outer brick behind
            // it, and finally be destroyed itself once that second hit uses up its one ricochet.
            bullet.Launch(new Vector2(2.5f, 0f), 0f, 20f, settings, ricochets: 1);

            bool bothDestroyed = false;
            for (int i = 0; i < 40 && !bothDestroyed; i++)
            {
                yield return new WaitForFixedUpdate();
                bothDestroyed = innerGO == null && outerGO == null;
            }
            Assert.IsTrue(bothDestroyed,
                "A bullet with one ricochet should destroy both the brick it first hits and the one behind its bounced-back path.");

            bool bulletGone = false;
            for (int i = 0; i < 10 && !bulletGone; i++)
            {
                yield return new WaitForFixedUpdate();
                bulletGone = bulletGO == null;
            }
            Assert.IsTrue(bulletGone, "The bullet should be destroyed once its one ricochet is used up, not keep bouncing forever.");
        }

        [UnityTest]
        public IEnumerator RicochetRoundsCard_MakesFiredBulletSurviveItsFirstBrickHit()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;
            settings.firstRingRadius = 3f;
            settings.ringSpacing = 1f;
            settings.ringCount = 1;
            settings.segmentsPerRing = new[] { 1 };
            settings.brickRadialThickness = 1f;
            settings.radialGap = 0.04f;
            settings.angularGapWorldUnits = 0.04f;

            var paddleGO = Track(new GameObject("RicochetCard_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("RicochetCard_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("RicochetCard_BallManager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;

            var runModsGO = Track(new GameObject("RicochetCard_RunModifiers"));
            var runMods = runModsGO.AddComponent<RunModifiers>();
            var ricochetCard = AssetDatabase.LoadAssetAtPath<CardSO>("Assets/Cards/Card_RicochetRounds.asset");
            Assert.IsNotNull(ricochetCard, "Precondition: Card_RicochetRounds.asset should exist.");
            runMods.AddCard(ricochetCard);

            var abilities = paddle.gameObject.AddComponent<PaddleAbilities>();
            abilities.ballManager = ballManager;
            abilities.runModifiers = runMods;
            abilities.cannonAmmoPerPickup = 1;
            abilities.bulletSpeed = 20f;

            var brickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            var brickManagerGO = Track(new GameObject("RicochetCard_BrickManager"));
            var brickManager = brickManagerGO.AddComponent<BrickGridManager>();
            brickManager.brickPrefab = brickPrefab;

            var level = ScriptableObject.CreateInstance<LevelSO>();
            level.gridSettings = settings;
            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;
            level.SetBrick(0, 0, brickType);
            brickManager.BuildLevel(level);

            var targetBrick = brickManager.GetBrickAt(new PolarCoordinate(0, 0));
            Assert.IsNotNull(targetBrick, "Precondition: target brick should exist in the bullet's path.");

            ball.LaunchAt(new Vector2(2f, 0f), new Vector2(8f, 0f));
            abilities.CollectPowerUp(PowerUpType.Cannon);
            Assert.AreEqual(1, abilities.CannonAmmo);

            var gamepad = InputSystem.AddDevice<Gamepad>();
            SetButtonSouth(gamepad, true);
            yield return WaitUntilAmmoIs(abilities, 0);

            bool brickDestroyed = false;
            for (int i = 0; i < 20 && !brickDestroyed; i++)
            {
                yield return new WaitForFixedUpdate();
                brickDestroyed = targetBrick == null;
            }
            Assert.IsTrue(brickDestroyed, "The fired bullet should reach and destroy the brick in its path.");

            // FireCannon() always fires both barrels, and this brick is a single full-circle
            // segment (like the Cannon test above), so both bullets hit it. With Ricochet Rounds
            // equipped, both should still be alive (bounced instead of being destroyed on that
            // first hit) - a plain bullet (see Cannon_FiresBulletsThatDestroyBricks_AndDepletesAmmo
            // above) would already be gone by this point.
            Assert.AreEqual(2, Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None).Length,
                "Ricochet Rounds should make both fired bullets survive their first brick hit by bouncing instead of being destroyed.");

            InputSystem.RemoveDevice(gamepad);
        }

        [UnityTest]
        public IEnumerator TwinPaddleCard_ActivatesMirroredSecondPaddle_ThatTracksAndCanBeDeactivated()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;

            var runModsGO = Track(new GameObject("Twin_RunModifiers"));
            var runMods = runModsGO.AddComponent<RunModifiers>();
            var twinCard = AssetDatabase.LoadAssetAtPath<CardSO>("Assets/Cards/Card_QuantumMirror.asset");
            Assert.IsNotNull(twinCard, "Precondition: Card_QuantumMirror.asset should exist.");
            runMods.AddCard(twinCard);

            var paddleGO = Track(new GameObject("Twin_MainPaddle"));
            paddleGO.SetActive(false);
            var mainPaddle = paddleGO.AddComponent<PaddleController>();
            mainPaddle.settings = settings;
            mainPaddle.runModifiers = runMods;

            var twinGO = Track(new GameObject("Twin_SecondPaddle"));
            twinGO.SetActive(false);
            var twinPaddle = twinGO.AddComponent<PaddleController>();
            twinPaddle.settings = settings;
            twinPaddle.runModifiers = runMods;
            twinPaddle.mirrorSource = mainPaddle;

            var abilities = paddleGO.AddComponent<PaddleAbilities>();
            abilities.runModifiers = runMods;
            abilities.twinPaddle = twinPaddle;

            // Activating triggers PaddleController.Awake (on both) then PaddleAbilities.Awake,
            // which calls RefreshTwinPaddle immediately - the card was already acquired above, so
            // the twin should come online right away rather than waiting for a later card pick.
            paddleGO.SetActive(true);

            Assert.IsTrue(twinGO.activeSelf, "The Quantum Mirror card should activate the second paddle immediately.");

            float expectedAngle = Mathf.Repeat(mainPaddle.CurrentAngleDegrees + 180f, 360f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(expectedAngle, twinPaddle.CurrentAngleDegrees)), 0.01f,
                "The twin paddle should start already snapped 180 degrees opposite the main paddle, not sweeping there over time.");

            // Drive the main paddle to a new angle (via the same override mechanism Autopilot
            // uses) and confirm the twin keeps tracking 180 degrees opposite as it turns.
            mainPaddle.AutopilotOverrideAngleDegrees = 90f;
            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(mainPaddle.CurrentAngleDegrees, 90f)), 1f,
                "Precondition: main paddle should have turned to 90 degrees.");

            expectedAngle = Mathf.Repeat(mainPaddle.CurrentAngleDegrees + 180f, 360f);
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(expectedAngle, twinPaddle.CurrentAngleDegrees)), 1f,
                "The twin paddle should keep mirroring the main paddle's angle live as it moves.");

            runMods.ResetRun();
            Assert.IsFalse(twinGO.activeSelf, "Resetting the run (losing all cards) should deactivate the twin paddle again.");
        }

        // The button-press latch (Update sets a flag, FixedUpdate consumes it) needs the Input
        // System to have actually processed the queued state event first, which isn't always
        // guaranteed within exactly one Update+FixedUpdate pair - looping a bounded number of
        // times until the expected ammo change lands (matching GamepadInputTests'
        // PressButtonSouthUntilLaunched pattern) is more robust than assuming a single cycle
        // suffices.
        private static IEnumerator WaitUntilAmmoIs(PaddleAbilities abilities, int expectedAmmo, int maxIterations = 10)
        {
            for (int i = 0; i < maxIterations && abilities.CannonAmmo != expectedAmmo; i++)
            {
                yield return null;
                yield return new WaitForFixedUpdate();
            }
        }

        private static void SetButtonSouth(Gamepad gamepad, bool pressed)
        {
            var state = new GamepadState().WithButton(GamepadButton.South, pressed);
            InputSystem.QueueStateEvent(gamepad, state);
        }
    }
}
