using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Simulates a virtual gamepad via the Input System's low-level device API so paddle/ball
    /// gamepad control can be verified without physical hardware attached.
    /// </summary>
    public class GamepadInputTests
    {
        private PolarGridSettings _settings;
        private GameObject _paddleObject;
        private GameObject _ballObject;
        private PaddleController _paddle;
        private BallController _ball;
        private Gamepad _gamepad;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            _settings.paddleOrbitRadius = 3f;
            _settings.outerWallRadius = 8f;
            _settings.deathZoneRadius = 1f;
            _settings.curveResolutionDegrees = 15f;

            // Objects start inactive so `settings` can be assigned before Awake() runs.
            _paddleObject = new GameObject("TestPaddle");
            _paddleObject.SetActive(false);
            _paddle = _paddleObject.AddComponent<PaddleController>();
            _paddle.settings = _settings;
            _paddleObject.SetActive(true);

            _ballObject = new GameObject("TestBall");
            _ballObject.SetActive(false);
            _ball = _ballObject.AddComponent<BallController>();
            _ball.settings = _settings;
            _ball.paddle = _paddle;
            _ballObject.SetActive(true);

            _gamepad = InputSystem.AddDevice<Gamepad>();
        }

        // Buttons are packed as bitfields, so pressing one requires queuing a full state
        // event (via GamepadState.WithButton) rather than a per-control delta event.
        private void SetButtonSouth(bool pressed)
        {
            var state = new GamepadState().WithButton(GamepadButton.South, pressed);
            InputSystem.QueueStateEvent(_gamepad, state);
        }

        // Brick.Initialize now takes a pre-built shared mesh/outline (BrickGridManager normally
        // builds these once per level) - tests that construct a Brick directly need to build the
        // same pair themselves.
        private static (Mesh mesh, Vector2[] outline) BuildHexGeometry(PolarGridSettings settings)
        {
            float hexRadius = Mathf.Max(0.01f, settings.hexSize - settings.hexGap);
            return (PolarMeshUtility.BuildHexMesh(hexRadius), PolarMeshUtility.BuildHexOutlinePoints(hexRadius));
        }

        // BallController latches wasPressedThisFrame in Update() and consumes it in the
        // following FixedUpdate, so a held press just needs a couple of frames to land.
        private IEnumerator PressButtonSouthUntilLaunched(int maxIterations = 10)
        {
            SetButtonSouth(true);
            for (int i = 0; i < maxIterations && _ball.State != BallState.Launched; i++)
            {
                yield return null;
                yield return new WaitForFixedUpdate();
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_gamepad != null) InputSystem.RemoveDevice(_gamepad);
            if (_ballObject != null) Object.Destroy(_ballObject);
            if (_paddleObject != null) Object.Destroy(_paddleObject);
            if (_settings != null) Object.Destroy(_settings);
        }

        [UnityTest]
        public IEnumerator LeftStick_Up_PutsPaddleAtTop()
        {
            InputSystem.QueueDeltaStateEvent(_gamepad.leftStick, new Vector2(0f, 1f));
            InputSystem.Update();

            yield return new WaitForFixedUpdate();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, _paddle.CurrentAngleDegrees)), 20f,
                "Paddle should ease toward the target angle over several steps, not teleport there in one.");

            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(90f, _paddle.CurrentAngleDegrees, 1f,
                "Pushing the left stick up should eventually settle the paddle at the top of the arena.");
        }

        [UnityTest]
        public IEnumerator LeftStick_Left_PutsPaddleOppositeRight()
        {
            InputSystem.QueueDeltaStateEvent(_gamepad.leftStick, new Vector2(-1f, 0f));
            InputSystem.Update();

            yield return new WaitForFixedUpdate();
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(0f, _paddle.CurrentAngleDegrees)), 20f,
                "Paddle should ease toward the target angle over several steps, not teleport there in one.");

            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(0f, Mathf.Abs(Mathf.DeltaAngle(180f, _paddle.CurrentAngleDegrees)), 1f,
                "Pushing the left stick left should eventually settle the paddle on the opposite side from right.");
        }

        [UnityTest]
        public IEnumerator LeftStick_Centered_HoldsLastAngle()
        {
            InputSystem.QueueDeltaStateEvent(_gamepad.leftStick, new Vector2(0f, 1f));
            InputSystem.Update();
            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            float angleAtTop = _paddle.CurrentAngleDegrees;

            InputSystem.QueueDeltaStateEvent(_gamepad.leftStick, Vector2.zero);
            InputSystem.Update();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(angleAtTop, _paddle.CurrentAngleDegrees, 0.01f,
                "Releasing the stick to center should hold the paddle's last angle, not snap to 0.");
        }

        [UnityTest]
        public IEnumerator ButtonSouth_LaunchesDockedBall()
        {
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BallState.Docked, _ball.State, "Ball should start docked on the paddle.");

            yield return PressButtonSouthUntilLaunched();

            Assert.AreEqual(BallState.Launched, _ball.State, "Pressing the gamepad South (A) button should launch the ball.");
        }

        [UnityTest]
        public IEnumerator ButtonSouth_DoesNothingWhileAlreadyLaunched()
        {
            yield return PressButtonSouthUntilLaunched();
            Assert.AreEqual(BallState.Launched, _ball.State, "Precondition: ball should have launched.");

            Vector2 velocityAfterLaunch = _ballObject.GetComponent<Rigidbody2D>().linearVelocity;

            // Release then press again; ball should stay launched and keep moving under its own speed.
            SetButtonSouth(false);
            yield return new WaitForFixedUpdate();

            SetButtonSouth(true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(BallState.Launched, _ball.State);
            Assert.Greater(velocityAfterLaunch.magnitude, 0f);
        }

        [UnityTest]
        public IEnumerator RadialHit_BouncesBallBackTowardCenter()
        {
            yield return PressButtonSouthUntilLaunched();
            Assert.AreEqual(BallState.Launched, _ball.State, "Precondition: ball should have launched.");

            // A wall placed further out along the ball's current radial line stands in for a
            // brick's inner/outer face - hitting it head-on while moving straight outward
            // should reverse the radial component, sending the ball back toward the center.
            var wallGO = new GameObject("TestRadialWall");
            wallGO.transform.position = new Vector3(6f, 0f, 0f);
            var wallCollider = wallGO.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.4f, 4f);

            var rb = _ballObject.GetComponent<Rigidbody2D>();
            rb.position = new Vector2(5.2f, 0f);
            rb.linearVelocity = new Vector2(5f, 0f);

            bool bouncedInward = false;
            for (int i = 0; i < 15 && !bouncedInward; i++)
            {
                yield return new WaitForFixedUpdate();
                bouncedInward = rb.linearVelocity.x < 0f;
            }

            Assert.IsTrue(bouncedInward,
                "Hitting a wall along the radial direction should reverse the ball's outward velocity, sending it back toward the center.");

            Object.Destroy(wallGO);
        }

        [UnityTest]
        public IEnumerator BallHittingRealBrick_BouncesAndDestroysIt()
        {
            _settings.hexSize = 1f;
            _settings.hexGap = 0f;

            var managerGO = new GameObject("TestBrickManager");
            var manager = managerGO.AddComponent<BrickGridManager>();

            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;

            // (3,0) with hexSize=1 sits at world distance ~5.2 from the center, matching the old
            // firstRingRadius=5 ring test's distance.
            var coord = new HexCoordinate(3, 0);
            var (hexMesh, hexOutline) = BuildHexGeometry(_settings);
            var brickGO = new GameObject("TestBrick");
            brickGO.transform.position = _settings.HexToWorld(coord);
            var brick = brickGO.AddComponent<Brick>();
            brick.Initialize(manager, _settings, coord, brickType, hexMesh, hexOutline);

            Vector2 dir = _settings.HexToWorld(coord).normalized;

            yield return PressButtonSouthUntilLaunched();
            var rb = _ballObject.GetComponent<Rigidbody2D>();
            rb.position = dir * 4f;
            rb.linearVelocity = dir * 5f;

            bool bouncedBack = false;
            for (int i = 0; i < 30 && !bouncedBack; i++)
            {
                yield return new WaitForFixedUpdate();
                bouncedBack = Vector2.Dot(rb.linearVelocity, dir) < 0f;
            }

            Assert.IsTrue(bouncedBack, "Ball moving straight into a brick should bounce back rather than pass through it.");

            // A destroyed brick shows a brief white hit-flash before its GameObject actually
            // goes away (see Brick.DestroyAfterFlash). WaitForSeconds is real-time based, so
            // this stays correct regardless of how fast the test runner ticks frames (unlike a
            // fixed frame-count loop, which can elapse far less real time than the flash needs).
            yield return new WaitForSeconds(brick.flashDuration + 0.2f);
            Assert.IsTrue(brickGO == null, "Hitting the brick should have destroyed it (1 max health).");
        }

        [UnityTest]
        public IEnumerator PaddleFastSweep_ImpartsSpinToBallOnContact()
        {
            // Position the ball dead-center of where the paddle's arc will be after its very
            // first (fastest) physics tick following a stick flick, so a real collision happens
            // while the paddle's angular velocity reading is still large.
            float firstTickAngle = _paddle.turnSpeedDegreesPerSecond * Time.fixedDeltaTime;
            Assert.Less(firstTickAngle, 90f, "Test assumption: the paddle shouldn't reach its target in a single tick.");

            float rad = firstTickAngle * Mathf.Deg2Rad;
            Vector2 ballPos = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * _settings.paddleOrbitRadius;
            _ball.LaunchAt(ballPos, Vector2.zero);

            InputSystem.QueueDeltaStateEvent(_gamepad.leftStick, new Vector2(0f, 1f));
            InputSystem.Update();

            bool spunUp = false;
            for (int i = 0; i < 5 && !spunUp; i++)
            {
                yield return new WaitForFixedUpdate();
                spunUp = Mathf.Abs(_ball.Spin) > 0.01f;
            }

            Assert.IsTrue(spunUp, "A paddle swept quickly through the ball's position should impart spin to it.");
        }

        [UnityTest]
        public IEnumerator Phasing_BallPassesThroughBrickWithoutBouncing_AndDestroysIt()
        {
            _settings.hexSize = 1f;
            _settings.hexGap = 0f;

            var managerGO = new GameObject("PhaseTestBrickManager");
            var manager = managerGO.AddComponent<BrickGridManager>();

            var brickType = ScriptableObject.CreateInstance<StandardBrickType>();
            brickType.maxHealth = 1;

            // (3,0) with hexSize=1 sits at world distance ~5.2 from the center, matching the old
            // firstRingRadius=5 ring test's distance.
            var coord = new HexCoordinate(3, 0);
            var (hexMesh, hexOutline) = BuildHexGeometry(_settings);
            var brickGO = new GameObject("PhaseTestBrick");
            brickGO.transform.position = _settings.HexToWorld(coord);
            var brick = brickGO.AddComponent<Brick>();
            brick.Initialize(manager, _settings, coord, brickType, hexMesh, hexOutline);

            Vector2 dir = _settings.HexToWorld(coord).normalized;

            // Force the ball into a strongly-phasing spin state directly - isolates this
            // behavior from the separate paddle-contact spin-transfer mechanism (see
            // PaddleFastSweep_ImpartsSpinToBallOnContact), which has its own test.
            var spinProperty = typeof(BallController).GetProperty("Spin", BindingFlags.Public | BindingFlags.Instance);
            spinProperty.SetValue(_ball, 1f);

            _ball.LaunchAt(dir * 4f, dir * 5f);

            var rb = _ballObject.GetComponent<Rigidbody2D>();
            Vector2 velocityBeforeStep = rb.linearVelocity;
            bool everReversed = false;
            for (int i = 0; i < 40 && brickGO != null; i++)
            {
                velocityBeforeStep = rb.linearVelocity;
                yield return new WaitForFixedUpdate();
                if (Vector2.Dot(velocityBeforeStep, rb.linearVelocity) < 0f) everReversed = true;
            }

            yield return new WaitForSeconds(brick.flashDuration + 0.2f);
            Assert.IsTrue(brickGO == null, "A phasing ball should still destroy the brick it touches.");
            Assert.IsFalse(everReversed,
                "A phasing ball should punch through the brick without its velocity reversing (no bounce).");

            Object.Destroy(managerGO);
        }

        [Test]
        public void EnforceMinimumRadialComponent_PurelyTangentialVelocity_GetsNudgedRadial()
        {
            // Regression test: a sequence of bounces can leave the ball moving almost purely
            // tangentially - the polar equivalent of classic Breakout's infamous "ball stuck
            // bouncing perfectly horizontally" bug. That settles into a stable orbit grinding
            // along one ring, and once that ring wears through, the ball shoots straight out
            // with nothing left in its path, looking like it "passed through" everything else.
            var rb = _ballObject.GetComponent<Rigidbody2D>();
            rb.position = new Vector2(5f, 0f); // radial = +X, tangential = +Y
            rb.linearVelocity = new Vector2(0f, 8f); // purely tangential

            var enforce = typeof(BallController).GetMethod("EnforceMinimumRadialComponent", BindingFlags.NonPublic | BindingFlags.Instance);
            enforce.Invoke(_ball, null);

            float radialComponentAfter = rb.linearVelocity.x;
            Assert.GreaterOrEqual(Mathf.Abs(radialComponentAfter), _ball.speed * 0.4f - 0.01f,
                "A velocity that's almost purely tangential should be nudged to keep a minimum radial component, so the ball can't get stuck orbiting a ring.");
        }

        [UnityTest]
        public IEnumerator DockToPaddle_ScaledBall_SitsFlushAgainstPaddleNoGap()
        {
            // Regression test: the ball's transform is scaled down in the actual scene
            // (e.g. 0.2), making its true world-space radius smaller than
            // CircleCollider2D.radius (which is in local space). Docking math that used the
            // raw collider radius overestimated the needed clearance, leaving the ball
            // floating with a visible gap in front of the paddle instead of resting on it.
            _ballObject.transform.localScale = new Vector3(0.2f, 0.2f, 1f);

            yield return new WaitForFixedUpdate();

            float worldBallRadius = _ball.GetComponent<CircleCollider2D>().radius * 0.2f;
            float paddleOuterRadius = _settings.paddleOrbitRadius + _paddle.radialThickness / 2f;
            float ballInnerEdgeDist = _ball.GetComponent<Rigidbody2D>().position.magnitude - worldBallRadius;

            Assert.AreEqual(paddleOuterRadius + _ball.dockOffset, ballInnerEdgeDist, 0.01f,
                "A scaled-down ball should still dock flush against the paddle's outer edge, not floating past it with a gap sized for its unscaled collider radius.");
        }

        [UnityTest]
        public IEnumerator RealLevel_BallDoesNotSlipPastBricksToOuterWall()
        {
            // Uses the actual configured level/settings assets and the real Brick prefab
            // spawning path, not a synthetic replica, to catch anything that only shows up
            // with the real asset (segment counts, gaps, brick health, etc).
            var realSettings = AssetDatabase.LoadAssetAtPath<PolarGridSettings>(
                "Assets/Custom Assets/PolarGridSettings.asset");
            var realLevel = AssetDatabase.LoadAssetAtPath<LevelSO>(
                "Assets/Custom Assets/Level 1.asset");
            var realBrickPrefab = AssetDatabase.LoadAssetAtPath<Brick>("Assets/Prefabs/Brick.prefab");
            Assert.IsNotNull(realSettings, "Real PolarGridSettings asset should load.");
            Assert.IsNotNull(realLevel, "Real Level 1 asset should load.");
            Assert.IsNotNull(realBrickPrefab, "Real Brick prefab should load.");

            var managerGO = new GameObject("RealManager");
            var manager = managerGO.AddComponent<BrickGridManager>();
            manager.brickPrefab = realBrickPrefab;
            manager.BuildLevel(realLevel);
            int initialBrickCount = manager.RemainingDestructibleCount;
            Assert.Greater(initialBrickCount, 0, "Precondition: the real level should have destructible bricks.");

            var paddleGO = new GameObject("RealPaddle");
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = realSettings;
            paddleGO.SetActive(true);

            var ballGO = new GameObject("RealBall");
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = realSettings;
            ball.paddle = paddle;
            ballGO.SetActive(true);
            var rb = ballGO.GetComponent<Rigidbody2D>();

            SetButtonSouth(true);
            bool launched = false;
            for (int i = 0; i < 10 && !launched; i++)
            {
                yield return null;
                yield return new WaitForFixedUpdate();
                launched = ball.State == BallState.Launched;
            }
            Assert.IsTrue(launched, "Real ball should launch.");

            bool everEscaped = false;
            bool everStuckOrbiting = false;
            float windowMinDist = float.MaxValue;
            float windowMaxDist = float.MinValue;
            int stepsInWindow = 0;
            const int windowSize = 100;

            for (int i = 0; i < 1500 && !everEscaped && !everStuckOrbiting; i++)
            {
                yield return new WaitForFixedUpdate();

                // While docked, DockToPaddle pins the ball to a fixed distance every step -
                // that's not an "orbit," so don't let it pollute the stuck-orbiting check.
                if (ball.State != BallState.Launched)
                {
                    windowMinDist = float.MaxValue;
                    windowMaxDist = float.MinValue;
                    stepsInWindow = 0;
                    continue;
                }

                float dist = rb.position.magnitude;
                windowMinDist = Mathf.Min(windowMinDist, dist);
                windowMaxDist = Mathf.Max(windowMaxDist, dist);
                stepsInWindow++;

                if (stepsInWindow >= windowSize)
                {
                    // The ball's distance from center barely moving over 100 physics steps
                    // means it's settled into a stable orbit (moving almost purely
                    // tangentially) instead of bouncing back through the arena.
                    if (windowMaxDist - windowMinDist < 0.1f)
                        everStuckOrbiting = true;

                    if (dist > realSettings.outerWallRadius - 0.6f && manager.RemainingDestructibleCount > initialBrickCount * 0.5f)
                        everEscaped = true;

                    windowMinDist = float.MaxValue;
                    windowMaxDist = float.MinValue;
                    stepsInWindow = 0;
                }
            }

            Assert.IsFalse(everStuckOrbiting,
                "Ball settled into a stable orbit (its distance from center barely changed over 100 physics steps) instead of continuing to bounce through the arena.");
            Assert.IsFalse(everEscaped,
                string.Format("Ball reached near the outer wall (dist~{0}) while {1}/{2} destructible bricks still existed - it slipped past without bouncing.",
                    rb.position.magnitude, manager.RemainingDestructibleCount, initialBrickCount));
        }
    }
}
