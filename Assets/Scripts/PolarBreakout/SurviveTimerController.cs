using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Shows/hides and updates a countdown label for Survive-type stages, driven entirely by
    /// LevelManager's OnSurviveStageChanged (show/hide + reset) and OnSurviveTimeChanged (tick)
    /// events. Purely a display layer, same spirit as HUDController, but kept as its own script
    /// since a Survive countdown is stage-scoped/transient rather than an always-present stat.
    /// </summary>
    public class SurviveTimerController : MonoBehaviour
    {
        public LevelManager levelManager;
        public GameObject timerRoot;
        public TextMeshProUGUI timerText;

        private void OnEnable()
        {
            if (levelManager != null)
            {
                levelManager.OnSurviveStageChanged += HandleSurviveStageChanged;
                levelManager.OnSurviveTimeChanged += HandleSurviveTimeChanged;
            }
        }

        private void OnDisable()
        {
            if (levelManager != null)
            {
                levelManager.OnSurviveStageChanged -= HandleSurviveStageChanged;
                levelManager.OnSurviveTimeChanged -= HandleSurviveTimeChanged;
            }
        }

        private void HandleSurviveStageChanged(bool isSurvive, float duration)
        {
            if (timerRoot != null) timerRoot.SetActive(isSurvive);
            if (isSurvive) HandleSurviveTimeChanged(duration);
        }

        private void HandleSurviveTimeChanged(float remaining)
        {
            if (timerText != null) timerText.text = FormatTime(remaining);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
