using System.Collections;
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

        [Tooltip("Optional. When set, the timer waits for this controller's \"Survive for " +
                 "X:XX!\" announcement to finish fading out before fading itself in, so the " +
                 "timer visually takes its place instead of overlapping it. Leave unset to show " +
                 "the timer immediately, same as before.")]
        public ObjectiveAnnouncementController objectiveAnnouncementController;
        [Tooltip("How long the timer takes to fade in once it's cleared to appear, seconds - " +
                 "only used when objectiveAnnouncementController is set.")]
        public float fadeInDuration = 0.5f;

        // Set by HandleSurviveStageChanged when a Survive stage starts but the timer is still
        // waiting on the announcement to finish - consumed (and cleared) the moment
        // HandleAnnouncementFinished actually starts the fade-in, so a stray/late OnFinished
        // from a since-superseded announcement can't pop the timer in unexpectedly.
        private bool _pendingShow;
        private Coroutine _fadeRoutine;

        private void OnEnable()
        {
            if (levelManager != null)
            {
                levelManager.OnSurviveStageChanged += HandleSurviveStageChanged;
                levelManager.OnSurviveTimeChanged += HandleSurviveTimeChanged;
            }
            if (objectiveAnnouncementController != null)
                objectiveAnnouncementController.OnFinished += HandleAnnouncementFinished;
        }

        private void OnDisable()
        {
            if (levelManager != null)
            {
                levelManager.OnSurviveStageChanged -= HandleSurviveStageChanged;
                levelManager.OnSurviveTimeChanged -= HandleSurviveTimeChanged;
            }
            if (objectiveAnnouncementController != null)
                objectiveAnnouncementController.OnFinished -= HandleAnnouncementFinished;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
        }

        private void HandleSurviveStageChanged(bool isSurvive, float duration)
        {
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }

            if (isSurvive) HandleSurviveTimeChanged(duration);

            if (!isSurvive || objectiveAnnouncementController == null)
            {
                // Nothing to wait on (leaving a Survive stage, or no announcement controller
                // wired) - show/hide instantly, same as the original behavior.
                _pendingShow = false;
                SetVisible(isSurvive);
                SetAlpha(isSurvive ? 1f : 0f);
                return;
            }

            // Stay hidden until the "Survive for X:XX!" announcement finishes fading out.
            _pendingShow = true;
            SetVisible(false);
            SetAlpha(0f);
        }

        private void HandleAnnouncementFinished(StageObjectiveType type)
        {
            if (!_pendingShow || type != StageObjectiveType.Survive) return;
            _pendingShow = false;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeIn());
        }

        // Unscaled, matching ObjectiveAnnouncementController's own fade - so a pause (e.g. a
        // card offer) can never desync or strand this mid-fade.
        private IEnumerator FadeIn()
        {
            SetVisible(true);

            float elapsed = 0f;
            while (elapsed < fadeInDuration)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(fadeInDuration > 0f ? Mathf.Lerp(0f, 1f, elapsed / fadeInDuration) : 1f);
            }

            SetAlpha(1f);
            _fadeRoutine = null;
        }

        private void SetVisible(bool visible)
        {
            if (timerRoot != null) timerRoot.SetActive(visible);
        }

        private void SetAlpha(float alpha)
        {
            if (timerText == null) return;
            Color color = timerText.color;
            color.a = alpha;
            timerText.color = color;
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
