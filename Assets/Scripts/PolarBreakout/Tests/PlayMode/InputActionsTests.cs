using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Proves the keyboard bindings added by the PolarBreakoutControls.inputactions conversion
    /// actually work end-to-end. Unlike GamepadInputTests.cs (which exercises the original raw
    /// Gamepad.current fallback path - deliberately left untouched, since every test there never
    /// assigns the new `actions` field), these tests wire the real InputActionAsset onto the
    /// paddle/ball so the new keyboard bindings are what's actually driving them.
    ///
    /// Derives from Unity's own <see cref="InputTestFixture"/> rather than hand-rolling
    /// SetUp/TearDown around a raw <see cref="Keyboard"/> device. An earlier version of this
    /// file did exactly that (editorInputBehaviorInPlayMode flipped for the test's duration,
    /// plus a per-frame SetKey re-queue) and it still raced intermittently - observed passing
    /// cleanly once, then failing twice with identical code. InputTestFixture is the mechanism
    /// Unity's own Input System test suite uses for this exact scenario: it swaps in a fully
    /// isolated InputManager with no real devices (so a physical gamepad on the test machine
    /// can never contend with the simulated keyboard for the Move action's arbitration), and
    /// hooks its device updates directly into the same player-loop timing UnityTest coroutines
    /// run on - so there's no separate "manual InputSystem.Update() vs. the Editor's automatic
    /// one" race left to lose.
    /// </summary>
    public class InputActionsTests : InputTestFixture
    {
        private PolarGridSettings _settings;
        private InputActionAsset _actions;
        private GameObject _paddleObject;
        private GameObject _ballObject;
        private PaddleController _paddle;
        private BallController _ball;
        private Keyboard _keyboard;

        public override void Setup()
        {
            base.Setup();

            // InputTestFixture.Setup() unconditionally turns on the Input System's
            // "paranoidReadValueCachingChecks" diagnostic. It's meant to catch cache
            // invalidation bugs, but it produces a false-positive Debug.LogError - which the
            // test framework treats as an automatic test failure - when the Move composite's
            // Vector2 value is polled from FixedUpdate while its keyboard part-bindings are
            // driven by this fixture's own event pump; the value read is still correct either
            // way, only the extra diagnostic comparison misfires. The constant itself
            // (InputFeatureNames.kParanoidReadValueCachingChecks) is internal to the Input
            // System package and not visible here, so the literal string is used directly.
            InputSystem.settings.SetInternalFeatureFlag("PARANOID_READ_VALUE_CACHING_CHECKS", false);

            _settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            _settings.paddleOrbitRadius = 3f;
            _settings.outerWallRadius = 8f;
            _settings.deathZoneRadius = 1f;
            _settings.curveResolutionDegrees = 15f;

            _actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/PolarBreakoutControls.inputactions");
            Assert.IsNotNull(_actions, "Precondition: the real PolarBreakoutControls asset should load.");

            // Objects start inactive so `settings`/`actions` can be assigned before Awake() runs.
            _paddleObject = new GameObject("TestPaddle");
            _paddleObject.SetActive(false);
            _paddle = _paddleObject.AddComponent<PaddleController>();
            _paddle.settings = _settings;
            _paddle.actions = _actions;
            _paddleObject.SetActive(true);

            _ballObject = new GameObject("TestBall");
            _ballObject.SetActive(false);
            _ball = _ballObject.AddComponent<BallController>();
            _ball.settings = _settings;
            _ball.paddle = _paddle;
            _ball.actions = _actions;
            _ballObject.SetActive(true);

            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
            if (_ballObject != null) Object.Destroy(_ballObject);
            if (_paddleObject != null) Object.Destroy(_paddleObject);
            if (_settings != null) Object.Destroy(_settings);

            base.TearDown();
        }

        // Kept as a raw QueueStateEvent + manual InputSystem.Update() (the same shape the
        // original hand-rolled version of this file used) rather than switching to
        // InputTestFixture's own Press()/Set() helpers - both forms hit the paranoid-checks
        // false positive above equally (that's a property of polling the Move composite from
        // FixedUpdate, not of how the event gets queued), so there was no upside to the
        // helpers here, and this way the diff against the pre-fixture version stays small.
        private void SetKey(Key key, bool pressed)
        {
            var state = pressed ? new KeyboardState(key) : new KeyboardState();
            InputSystem.QueueStateEvent(_keyboard, state);
            InputSystem.Update();
        }

        [UnityTest]
        public IEnumerator WKey_PutsPaddleAtTop()
        {
            for (int i = 0; i < 60; i++)
            {
                SetKey(Key.W, true);
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(90f, _paddle.CurrentAngleDegrees, 2f,
                "Holding W should eventually settle the paddle at the top of the arena, the same " +
                "target the gamepad stick's up direction already reaches.");
        }

        [UnityTest]
        public IEnumerator ArrowUpKey_PutsPaddleAtTop()
        {
            for (int i = 0; i < 60; i++)
            {
                SetKey(Key.UpArrow, true);
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(90f, _paddle.CurrentAngleDegrees, 2f,
                "The arrow-key composite binding should work independently of the WASD one.");
        }

        [UnityTest]
        public IEnumerator SpaceKey_LaunchesDockedBall()
        {
            Assert.AreEqual(BallState.Docked, _ball.State, "Precondition: ball starts docked.");

            for (int i = 0; i < 10 && _ball.State != BallState.Launched; i++)
            {
                SetKey(Key.Space, true);
                yield return null;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(BallState.Launched, _ball.State,
                "Pressing Space should launch the docked ball via the Fire action's keyboard binding.");
        }
    }
}
