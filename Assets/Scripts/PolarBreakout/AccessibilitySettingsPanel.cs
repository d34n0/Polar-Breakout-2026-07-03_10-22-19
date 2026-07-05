using UnityEngine;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Binds the Accessibility category's Reduce Motion toggle and Colorblind Mode toggles to
    /// GameSettings.
    /// </summary>
    public class AccessibilitySettingsPanel : MonoBehaviour
    {
        public Toggle reduceMotionToggle;

        [Header("Colorblind Mode")]
        public Toggle colorblindNoneToggle;
        public Toggle colorblindProtanopiaToggle;
        public Toggle colorblindDeuteranopiaToggle;
        public Toggle colorblindTritanopiaToggle;

        private void OnEnable()
        {
            reduceMotionToggle.SetIsOnWithoutNotify(GameSettings.ReduceMotion);
            reduceMotionToggle.onValueChanged.AddListener(OnReduceMotionToggled);

            SetToggleForCurrentColorblindMode();
            colorblindNoneToggle.onValueChanged.AddListener(OnColorblindNoneToggled);
            colorblindProtanopiaToggle.onValueChanged.AddListener(OnColorblindProtanopiaToggled);
            colorblindDeuteranopiaToggle.onValueChanged.AddListener(OnColorblindDeuteranopiaToggled);
            colorblindTritanopiaToggle.onValueChanged.AddListener(OnColorblindTritanopiaToggled);
        }

        private void OnDisable()
        {
            reduceMotionToggle.onValueChanged.RemoveListener(OnReduceMotionToggled);
            colorblindNoneToggle.onValueChanged.RemoveListener(OnColorblindNoneToggled);
            colorblindProtanopiaToggle.onValueChanged.RemoveListener(OnColorblindProtanopiaToggled);
            colorblindDeuteranopiaToggle.onValueChanged.RemoveListener(OnColorblindDeuteranopiaToggled);
            colorblindTritanopiaToggle.onValueChanged.RemoveListener(OnColorblindTritanopiaToggled);
        }

        private void OnReduceMotionToggled(bool isOn)
        {
            GameSettings.ReduceMotion = isOn;
            GameSettings.ApplyAccessibility();
            GameSettings.Save();
        }

        private void OnColorblindNoneToggled(bool isOn) { if (isOn) SetColorblindMode(ColorblindMode.None); }
        private void OnColorblindProtanopiaToggled(bool isOn) { if (isOn) SetColorblindMode(ColorblindMode.Protanopia); }
        private void OnColorblindDeuteranopiaToggled(bool isOn) { if (isOn) SetColorblindMode(ColorblindMode.Deuteranopia); }
        private void OnColorblindTritanopiaToggled(bool isOn) { if (isOn) SetColorblindMode(ColorblindMode.Tritanopia); }

        private void SetColorblindMode(ColorblindMode mode)
        {
            GameSettings.ColorblindFilter = mode;
            GameSettings.Save();
        }

        private void SetToggleForCurrentColorblindMode()
        {
            var mode = GameSettings.ColorblindFilter;
            colorblindNoneToggle.SetIsOnWithoutNotify(mode == ColorblindMode.None);
            colorblindProtanopiaToggle.SetIsOnWithoutNotify(mode == ColorblindMode.Protanopia);
            colorblindDeuteranopiaToggle.SetIsOnWithoutNotify(mode == ColorblindMode.Deuteranopia);
            colorblindTritanopiaToggle.SetIsOnWithoutNotify(mode == ColorblindMode.Tritanopia);
        }
    }
}
