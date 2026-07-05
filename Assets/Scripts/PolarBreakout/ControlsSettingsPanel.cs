using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

namespace PolarBreakout
{
    /// <summary>
    /// Binds the Controls category's rebind rows (Fire: gamepad + keyboard), Invert Axis toggle,
    /// controller layout readout, and Reset to Defaults to the shared PolarBreakoutControls
    /// asset. Move's WASD/Arrow composites and the Pause binding aren't exposed for rebinding in
    /// this first pass - only Fire is, since that's enough to prove out the full rebind/persist/
    /// reset mechanism; the same StartRebind pattern extends to the others later.
    /// </summary>
    public class ControlsSettingsPanel : MonoBehaviour
    {
        [Tooltip("The shared PolarBreakoutControls asset - same instance used by BallController/PaddleAbilities.")]
        public InputActionAsset actions;

        [Header("Fire Rebind Rows")]
        public Button fireGamepadRebindButton;
        public TextMeshProUGUI fireGamepadBindingText;
        public Button fireKeyboardRebindButton;
        public TextMeshProUGUI fireKeyboardBindingText;

        [Header("Other Controls")]
        public Toggle invertAxisToggle;
        public TextMeshProUGUI controllerLayoutText;
        public Button resetToDefaultsButton;

        private const string RebindOverridesKey = "Settings.InputRebindOverrides";
        private const string RebindPromptText = "Press any key/button...";

        private InputAction _fireAction;
        private InputActionRebindingExtensions.RebindingOperation _activeRebind;

        private void OnEnable()
        {
            LoadBindingOverrides();

            _fireAction = actions.FindActionMap("Player").FindAction("Fire");
            RefreshBindingLabels();
            RefreshControllerLayout();

            invertAxisToggle.SetIsOnWithoutNotify(GameSettings.InvertPaddleAxis);
            invertAxisToggle.onValueChanged.AddListener(OnInvertToggled);

            fireGamepadRebindButton.onClick.AddListener(OnFireGamepadRebindClicked);
            fireKeyboardRebindButton.onClick.AddListener(OnFireKeyboardRebindClicked);
            resetToDefaultsButton.onClick.AddListener(OnResetToDefaults);

            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            invertAxisToggle.onValueChanged.RemoveListener(OnInvertToggled);
            fireGamepadRebindButton.onClick.RemoveListener(OnFireGamepadRebindClicked);
            fireKeyboardRebindButton.onClick.RemoveListener(OnFireKeyboardRebindClicked);
            resetToDefaultsButton.onClick.RemoveListener(OnResetToDefaults);
            InputSystem.onDeviceChange -= OnDeviceChange;

            _activeRebind?.Cancel();
            _activeRebind?.Dispose();
            _activeRebind = null;
        }

        private void OnInvertToggled(bool isOn)
        {
            GameSettings.InvertPaddleAxis = isOn;
            GameSettings.Save();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            RefreshControllerLayout();
        }

        private void RefreshControllerLayout()
        {
            controllerLayoutText.text = Gamepad.current != null
                ? "Connected: " + Gamepad.current.displayName
                : "No controller connected";
        }

        private void RefreshBindingLabels()
        {
            fireGamepadBindingText.text = InputControlPath.ToHumanReadableString(
                _fireAction.bindings[0].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
            fireKeyboardBindingText.text = InputControlPath.ToHumanReadableString(
                _fireAction.bindings[1].effectivePath,
                InputControlPath.HumanReadableStringOptions.OmitDevice);
        }

        private void OnFireGamepadRebindClicked() => StartRebind(0, fireGamepadBindingText);
        private void OnFireKeyboardRebindClicked() => StartRebind(1, fireKeyboardBindingText);

        private void StartRebind(int bindingIndex, TextMeshProUGUI label)
        {
            if (_activeRebind != null) return;

            label.text = RebindPromptText;
            _fireAction.Disable();
            _activeRebind = _fireAction.PerformInteractiveRebinding(bindingIndex)
                .WithControlsExcluding("Mouse")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(_ => FinishRebind())
                .OnCancel(_ => FinishRebind())
                .Start();
        }

        private void FinishRebind()
        {
            _activeRebind.Dispose();
            _activeRebind = null;
            _fireAction.Enable();
            RefreshBindingLabels();
            SaveBindingOverrides();
        }

        private void SaveBindingOverrides()
        {
            string json = actions.SaveBindingOverridesAsJson();
            PlayerPrefs.SetString(RebindOverridesKey, json);
            PlayerPrefs.Save();
        }

        private void LoadBindingOverrides()
        {
            string json = PlayerPrefs.GetString(RebindOverridesKey, "");
            if (!string.IsNullOrEmpty(json)) actions.LoadBindingOverridesFromJson(json);
        }

        private void OnResetToDefaults()
        {
            actions.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(RebindOverridesKey);
            RefreshBindingLabels();
        }
    }
}
