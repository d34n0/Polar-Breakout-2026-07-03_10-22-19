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
    /// </summary>
    public class InputActionsTests
    {
        private PolarGridSettings _settings;
        private InputActionAsset _actions;
        private GameObject _paddleObject;
        private GameObject _ballObject;
        private PaddleController _paddle;
        private BallController _ball;
        private Keyboard _keyboard;
        // Move also has a raw <Gamepad>/leftStick binding alongside the keyboard composites. If a
        // real physical gamepad happens to be connected to the machine running these tests, its
        // background state events can win the Vector2 action's last-actuated-control arbitration
        // over the simulated keyboard press, making these tests fail for a reason that has nothing
        // to do with the keyboard bindings under test. Disabled for the test's duration and
        // restored in TearDown so real-hardware presence doesn't affect the result either way.
        private Gamepad _realGamepad;
        // Belt-and-suspenders alongside the per-frame re-queue below: also force focus-independent
        // input for the test's duration, since this headless/automated run never holds real Game
        // View OS focus and the per-frame re-queue alone still raced intermittently (observed
        // passing cleanly once, then failing twice more with the exact same code - a timing race,
        // not a logic bug). Restored in TearDown since this is a project-wide singleton setting.
        private InputSettings.EditorInputBehaviorInPlayMode _originalEditorInputBehavior;

        [SetUp]
        public void SetUp()
        {
            _realGamepad = Gamepad.current;
            if (_realGamepad != null) InputSystem.DisableDevice(_realGamepad);

            _originalEditorInputBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

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

        [TearDown]
        public void TearDown()
        {
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_ballObject != null) Object.Destroy(_ballObject);
            if (_paddleObject != null) Object.Destroy(_paddleObject);
            if (_settings != null) Object.Destroy(_settings);
            if (_realGamepad != null) InputSystem.EnableDevice(_realGamepad);
            InputSystem.settings.editorInputBehaviorInPlayMode = _originalEditorInputBehavior;
        }

        private void SetKey(Key key, bool pressed)
        {
            var state = pressed ? new KeyboardState(key) : new KeyboardState();
            InputSystem.QueueStateEvent(_keyboard, state);
        }

        [UnityTest]
        public IEnumerator WKey_PutsPaddleAtTop()
        {
            // Re-queued every frame rather than once at the start: the Editor's default
            // "keyboard/mouse state resets unless the Game View has OS focus" behavior
            // (InputSettings.editorInputBehaviorInPlayMode) clears simulated keyboard state after
            // a single frame in this headless/automated environment, which never holds real
            // focus. Re-affirming the press each frame keeps it alive regardless.
            for (int i = 0; i < 60; i++)
            {
                SetKey(Key.W, true);
                InputSystem.Update();
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
                InputSystem.Update();
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
                InputSystem.Update();
                yield return null;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(BallState.Launched, _ball.State,
                "Pressing Space should launch the docked ball via the Fire action's keyboard binding.");
        }
    }
}
