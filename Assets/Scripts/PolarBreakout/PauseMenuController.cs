using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PolarBreakout
{
    /// <summary>
    /// In-match pause menu: the Pause action (gamepad Start / keyboard Escape) toggles a pause
    /// overlay and freezes gameplay via Time.timeScale, reusing the same OptionsMenuPanel prefab
    /// instance the Title Screen uses (UI input isn't affected by timeScale, so the Options
    /// panel stays fully usable while paused). Quit to Title always restores Time.timeScale to 1
    /// before loading the Title Screen scene - otherwise the title would load frozen.
    /// </summary>
    public class PauseMenuController : MonoBehaviour
    {
        [Header("Input")]
        public InputActionAsset actions;

        [Header("References")]
        public GameObject pauseOverlayRoot;
        [Tooltip("The OptionsMenuPanel instance's root GameObject - the same prefab used by the Title Screen.")]
        public GameObject optionsMenuPanelRoot;
        public Button resumeButton;
        public Button optionsButton;
        public Button quitToTitleButton;

        private InputAction _pauseAction;
        private bool _isPaused;

        private void Awake()
        {
            GameSettings.Load();
            GameSettings.ApplyGraphics();
            GameSettings.ApplyAccessibility();

            if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(false);
            if (optionsMenuPanelRoot != null) optionsMenuPanelRoot.SetActive(false);

            if (actions != null)
            {
                _pauseAction = actions.FindActionMap("Player").FindAction("Pause");
                _pauseAction.performed += OnPausePerformed;
                _pauseAction.Enable();
            }

            resumeButton.onClick.AddListener(Resume);
            optionsButton.onClick.AddListener(OpenOptions);
            quitToTitleButton.onClick.AddListener(QuitToTitle);
        }

        private void OnDestroy()
        {
            if (_pauseAction != null) _pauseAction.performed -= OnPausePerformed;
        }

        private void OnPausePerformed(InputAction.CallbackContext context)
        {
            if (_isPaused) Resume();
            else Pause();
        }

        private void Pause()
        {
            _isPaused = true;
            Time.timeScale = 0f;
            if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(true);

            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);

            // Move/Fire share physical buttons with UI navigation/Submit (e.g. gamepad South is
            // both Fire and the default UI Submit), and InputAction.performed callbacks fire
            // regardless of Time.timeScale. Without this, clicking Resume/Options with the same
            // button used for Fire leaves a pending launch/cannon-shot that fires the instant
            // gameplay resumes - Pause itself stays enabled so the pause toggle keeps working.
            SetGameplayActionsEnabled(false);
        }

        public void Resume()
        {
            _isPaused = false;
            Time.timeScale = 1f;
            if (optionsMenuPanelRoot != null) optionsMenuPanelRoot.SetActive(false);
            if (pauseOverlayRoot != null) pauseOverlayRoot.SetActive(false);

            SetGameplayActionsEnabled(true);
        }

        private void SetGameplayActionsEnabled(bool enabled)
        {
            if (actions == null) return;

            var playerMap = actions.FindActionMap("Player");
            var move = playerMap.FindAction("Move");
            var fire = playerMap.FindAction("Fire");

            if (enabled)
            {
                move.Enable();
                fire.Enable();
            }
            else
            {
                move.Disable();
                fire.Disable();
            }
        }

        private void OpenOptions()
        {
            if (optionsMenuPanelRoot == null) return;

            var optionsController = optionsMenuPanelRoot.GetComponent<OptionsMenuController>();
            if (optionsController != null) optionsController.returnSelectionTarget = optionsButton.gameObject;
            optionsMenuPanelRoot.SetActive(true);
        }

        private void QuitToTitle()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene("Title Screen");
        }
    }
}
