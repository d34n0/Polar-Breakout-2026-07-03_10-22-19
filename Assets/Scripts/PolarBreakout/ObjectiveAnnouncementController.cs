using System.Collections;
using TMPro;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Pops up a short message describing the current stage's objective (e.g. "Clear the
    /// Bricks!" or "Survive for 1:00!"), driven by LevelManager.OnObjectiveAnnounced - which
    /// fires right as a level activates, at the same moment the round-start dissolve-in begins,
    /// so the message appears while the player is dissolving in. Shows instantly, holds for
    /// displayDuration, then fades out over fadeOutDuration - purely a display layer, same spirit
    /// as HUDController/SurviveTimerController.
    /// </summary>
    public class ObjectiveAnnouncementController : MonoBehaviour
    {
        public LevelManager levelManager;
        public TextMeshProUGUI objectiveText;

        [Tooltip("How long the message stays fully visible before fading out, seconds.")]
        public float displayDuration = 2.5f;
        [Tooltip("How long the fade-out itself takes, seconds.")]
        public float fadeOutDuration = 0.5f;

        private Coroutine _routine;

        private void OnEnable()
        {
            if (levelManager != null) levelManager.OnObjectiveAnnounced += HandleObjectiveAnnounced;
        }

        private void OnDisable()
        {
            if (levelManager != null) levelManager.OnObjectiveAnnounced -= HandleObjectiveAnnounced;
        }

        private void HandleObjectiveAnnounced(StageObjectiveType type, float surviveDuration)
        {
            if (objectiveText == null) return;

            objectiveText.text = BuildMessage(type, surviveDuration);

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(ShowThenFade());
        }

        private static string BuildMessage(StageObjectiveType type, float surviveDuration)
        {
            switch (type)
            {
                case StageObjectiveType.Survive:
                    return "Survive for " + FormatTime(surviveDuration) + "!";
                case StageObjectiveType.Boss:
                    return "Defeat the Boss!";
                case StageObjectiveType.Clear:
                default:
                    return "Clear the Bricks!";
            }
        }

        // Unscaled throughout - matches HexWipeTransition/DissolveEffect's own reasoning, so a
        // pause (Time.timeScale = 0, e.g. a card offer) can never desync or strand this mid-fade.
        private IEnumerator ShowThenFade()
        {
            Color color = objectiveText.color;
            color.a = 1f;
            objectiveText.color = color;
            objectiveText.gameObject.SetActive(true);

            yield return new WaitForSecondsRealtime(displayDuration);

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                color.a = fadeOutDuration > 0f ? Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration) : 0f;
                objectiveText.color = color;
            }

            objectiveText.gameObject.SetActive(false);
            _routine = null;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return total / 60 + ":" + (total % 60).ToString("00");
        }
    }
}
