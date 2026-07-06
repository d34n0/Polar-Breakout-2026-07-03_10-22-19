using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers BallManager.ResetForNewRound() (see LevelManager.AdvanceToNextStage) - the fix for
    /// the ball sometimes auto-launching at the start of a new stage. Unity's Input System
    /// performs an "initial state check" when an action is re-enabled: if the bound control is
    /// already actuated at that moment (e.g. the player is still holding Fire when
    /// CardOfferController re-enables gameplay input after the card offer closes), it fires
    /// Performed immediately - queuing a launch request before the player has made any fresh
    /// press for the new round.
    /// </summary>
    public class RoundStartTests
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

        [UnityTest]
        public IEnumerator ResetForNewRound_DocksBallEvenIfFireHeldWhenActionsReEnable()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("RoundStart_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            // Minimal Player/Fire action bound to gamepad South - enough to exercise the same
            // Input System Enable()-while-already-held behaviour CardOfferController's
            // SetGameplayActionsEnabled relies on, without needing the full card-offer UI.
            var actionsAsset = ScriptableObject.CreateInstance<InputActionAsset>();
            var playerMap = actionsAsset.AddActionMap("Player");
            var fireAction = playerMap.AddAction("Fire", InputActionType.Button, "<Gamepad>/buttonSouth");

            var ballGO = Track(new GameObject("RoundStart_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ball.actions = actionsAsset;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("RoundStart_BallManager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;
            ball.ballManager = ballManager;

            // Ball is still mid-flight when the stage clears (the common case - the last brick
            // is destroyed mid-bounce), not already sitting docked.
            ball.LaunchAt(new Vector2(2f, 0f), new Vector2(6f, 0f));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BallState.Launched, ball.State, "Precondition: ball should be in flight.");

            var gamepad = InputSystem.AddDevice<Gamepad>();
            SetButtonSouth(gamepad, true);
            yield return null; // Let the Input System process the queued state event.

            // Mirrors CardOfferController.SetGameplayActionsEnabled(false) then (true) around the
            // card offer, with Fire physically held throughout - the exact scenario that can
            // queue an immediate Performed the instant it's re-enabled.
            fireAction.Disable();
            fireAction.Enable();

            // The round-start reset must win regardless of whether that re-enable just queued a
            // launch request.
            ballManager.ResetForNewRound();

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(BallState.Docked, ball.State,
                "A new round should always start with the ball docked to the paddle, even if " +
                "Fire happened to be held down when gameplay input was re-enabled after the " +
                "card offer closed.");

            SetButtonSouth(gamepad, false);
            InputSystem.RemoveDevice(gamepad);
            Object.DestroyImmediate(actionsAsset);
        }

        [UnityTest]
        public IEnumerator ResetForNewRound_DestroysMultiballClones_LeavingOnlyThePrimaryDocked()
        {
            var settings = ScriptableObject.CreateInstance<PolarGridSettings>();
            settings.paddleOrbitRadius = 1.5f;
            settings.deathZoneRadius = 1f;
            settings.outerWallRadius = 8f;
            settings.curveResolutionDegrees = 15f;

            var paddleGO = Track(new GameObject("RoundStart_MB_Paddle"));
            paddleGO.SetActive(false);
            var paddle = paddleGO.AddComponent<PaddleController>();
            paddle.settings = settings;
            paddleGO.SetActive(true);

            var ballGO = Track(new GameObject("RoundStart_MB_Ball"));
            ballGO.SetActive(false);
            var ball = ballGO.AddComponent<BallController>();
            ball.settings = settings;
            ball.paddle = paddle;
            ballGO.SetActive(true);

            var managerGO = Track(new GameObject("RoundStart_MB_BallManager"));
            var ballManager = managerGO.AddComponent<BallManager>();
            ballManager.primaryBall = ball;
            ball.ballManager = ballManager;

            ball.LaunchAt(new Vector2(3f, 0f), new Vector2(8f, 0f));
            yield return new WaitForFixedUpdate();

            // Counted relative to a baseline (rather than an absolute value) since this fixture
            // shares a scene/domain with other test fixtures in the same run - a still-pending
            // Destroy() from an earlier test (deferred to end-of-frame, unlike DestroyImmediate)
            // could otherwise still be alive here and make an absolute count flaky.
            int baselineCount = Object.FindObjectsByType<BallController>(FindObjectsSortMode.None).Length;

            ballManager.ActivateMultiball();
            Assert.AreEqual(baselineCount + 2, Object.FindObjectsByType<BallController>(FindObjectsSortMode.None).Length,
                "Precondition: Multiball should add exactly 2 clone balls.");

            ballManager.ResetForNewRound();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(baselineCount, Object.FindObjectsByType<BallController>(FindObjectsSortMode.None).Length,
                "A new round should destroy every multiball clone from the previous stage, leaving " +
                "only the balls that existed before Multiball was activated.");
            Assert.IsTrue(ball != null && ball.State == BallState.Docked,
                "The primary ball should still exist and be docked for the new round.");
        }

        private static void SetButtonSouth(Gamepad gamepad, bool pressed)
        {
            var state = new GamepadState().WithButton(GamepadButton.South, pressed);
            InputSystem.QueueStateEvent(gamepad, state);
        }
    }
}
