using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace PolarBreakout
{
    /// <summary>
    /// Shows the Game Over panel once LivesManager.OnGameOver fires - after a short delay so it
    /// doesn't pop in the instant the last ball is lost, overlapping the paddle/ball's own
    /// dissolve-out. BallManager checks LivesManager.IsGameOver itself and skips respawning the
    /// ball/paddle once the game is over, so this only needs to own the panel and the two
    /// buttons' outcomes.
    ///
    /// Two display modes share the same Retry/Quit buttons: a plain "Score: X / High Score: Y"
    /// for a run that didn't make the table, or the full top-10 scoreboard (with the just-
    /// submitted entry flashing) once NameEntryController confirms a name for a qualifying run.
    /// </summary>
    public class GameOverController : MonoBehaviour
    {
        public LivesManager livesManager;
        public ScoreManager scoreManager;
        [Tooltip("Optional. When the run's score qualifies for the high score table, this is " +
                 "shown first and the Game Over panel itself waits for it to finish.")]
        public NameEntryController nameEntryController;
        public GameObject panelRoot;
        public Button retryButton;
        public Button quitButton;

        [Header("Simple score display (non-qualifying run)")]
        public GameObject simpleScoreGroup;
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI highScoreText;

        [Header("Scoreboard display (new high score)")]
        public GameObject scoreboardGroup;
        [Tooltip("One row per rank, in order (rank 1 first). Shows '---' for any rank not yet filled.")]
        public TextMeshProUGUI[] scoreboardRows;
        public float flashInterval = 0.4f;

        [Tooltip("How long to wait after game-over triggers before the panel actually appears.")]
        public float showDelay = 0.5f;

        private Coroutine _flashCoroutine;

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            retryButton.onClick.AddListener(Retry);
            quitButton.onClick.AddListener(QuitToTitle);
        }

        private void OnEnable()
        {
            if (livesManager != null) livesManager.OnGameOver += HandleGameOver;
        }

        private void OnDisable()
        {
            if (livesManager != null) livesManager.OnGameOver -= HandleGameOver;
            StopFlashing();
        }

        private void HandleGameOver()
        {
            StartCoroutine(ShowPanelAfterDelay());
        }

        private IEnumerator ShowPanelAfterDelay()
        {
            // Realtime rather than WaitForSeconds: nothing about showing this panel should be
            // affected if timeScale is ever non-1 at this moment.
            yield return new WaitForSecondsRealtime(showDelay);

            int finalScore = scoreManager != null ? scoreManager.CurrentScore : 0;

            if (nameEntryController != null && HighScoreManager.QualifiesForTable(finalScore))
            {
                nameEntryController.Open(name =>
                {
                    int rank = HighScoreManager.SubmitEntry(name, finalScore);
                    ShowScoreboard(rank);
                });
            }
            else
            {
                ShowSimpleScore(finalScore);
            }
        }

        private void ShowSimpleScore(int finalScore)
        {
            int highScore = HighScoreManager.GetTopScore();
            if (scoreText != null) scoreText.text = "Score: " + finalScore;
            if (highScoreText != null) highScoreText.text = "High Score: " + highScore;

            if (simpleScoreGroup != null) simpleScoreGroup.SetActive(true);
            if (scoreboardGroup != null) scoreboardGroup.SetActive(false);

            OpenPanel();
        }

        private void ShowScoreboard(int highlightRank)
        {
            var entries = HighScoreManager.Load();
            for (int i = 0; i < scoreboardRows.Length; i++)
            {
                scoreboardRows[i].text = i < entries.Count ? $"{i + 1}. {entries[i].Name} - {entries[i].Score}" : $"{i + 1}. ---";
                scoreboardRows[i].color = Color.white;
            }

            if (simpleScoreGroup != null) simpleScoreGroup.SetActive(false);
            if (scoreboardGroup != null) scoreboardGroup.SetActive(true);

            OpenPanel();

            if (highlightRank >= 0 && highlightRank < scoreboardRows.Length)
                _flashCoroutine = StartCoroutine(FlashRow(scoreboardRows[highlightRank]));
        }

        private void OpenPanel()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            StartCoroutine(SelectRetryNextFrame());
        }

        // Deferred by one frame: when this follows straight on from NameEntryController's END
        // confirmation, selecting Retry in the SAME frame that Submit (A/Space) was just pressed
        // causes that identical press to also be redelivered to Retry as its own Submit event
        // (Buttons invoke onClick on Submit, same as a click) - instantly firing Retry and
        // reloading the game right after confirming a name. Waiting a frame lets that press fully
        // expire first.
        private IEnumerator SelectRetryNextFrame()
        {
            yield return null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(retryButton.gameObject);
        }

        private IEnumerator FlashRow(TextMeshProUGUI row)
        {
            while (true)
            {
                row.color = Color.yellow;
                yield return new WaitForSecondsRealtime(flashInterval);
                row.color = Color.white;
                yield return new WaitForSecondsRealtime(flashInterval);
            }
        }

        private void StopFlashing()
        {
            if (_flashCoroutine == null) return;
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }

        private void Retry()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Main Game");
        }

        private void QuitToTitle()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Title Screen");
        }
    }
}
