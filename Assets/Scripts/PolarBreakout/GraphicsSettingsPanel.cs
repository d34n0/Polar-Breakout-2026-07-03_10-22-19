using UnityEngine;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Binds the Graphics category's Display Mode toggles (Fullscreen/Windowed/Borderless
    /// Windowed) to GameSettings.
    /// </summary>
    public class GraphicsSettingsPanel : MonoBehaviour
    {
        public Toggle fullscreenToggle;
        public Toggle windowedToggle;
        public Toggle borderlessToggle;

        private void OnEnable()
        {
            SetToggleForCurrentMode();

            fullscreenToggle.onValueChanged.AddListener(OnFullscreenToggled);
            windowedToggle.onValueChanged.AddListener(OnWindowedToggled);
            borderlessToggle.onValueChanged.AddListener(OnBorderlessToggled);
        }

        private void OnDisable()
        {
            fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenToggled);
            windowedToggle.onValueChanged.RemoveListener(OnWindowedToggled);
            borderlessToggle.onValueChanged.RemoveListener(OnBorderlessToggled);
        }

        private void OnFullscreenToggled(bool isOn) { if (isOn) SetMode(DisplayModeOption.Fullscreen); }
        private void OnWindowedToggled(bool isOn) { if (isOn) SetMode(DisplayModeOption.Windowed); }
        private void OnBorderlessToggled(bool isOn) { if (isOn) SetMode(DisplayModeOption.BorderlessWindowed); }

        private void SetMode(DisplayModeOption mode)
        {
            GameSettings.DisplayMode = mode;
            GameSettings.ApplyGraphics();
            GameSettings.Save();
        }

        private void SetToggleForCurrentMode()
        {
            var mode = GameSettings.DisplayMode;
            fullscreenToggle.SetIsOnWithoutNotify(mode == DisplayModeOption.Fullscreen);
            windowedToggle.SetIsOnWithoutNotify(mode == DisplayModeOption.Windowed);
            borderlessToggle.SetIsOnWithoutNotify(mode == DisplayModeOption.BorderlessWindowed);
        }
    }
}
