using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Wires the Title Screen's Start/Options/Exit buttons. Start loads Main Game directly,
    /// Options shows the shared OptionsMenuPanel (the same prefab Main Game's pause menu uses),
    /// Exit quits the built application.
    /// </summary>
    public class TitleScreenController : MonoBehaviour
    {
        [Tooltip("The OptionsMenuPanel instance in this scene - shown/hidden, never destroyed.")]
        public GameObject optionsMenuPanel;
        [Tooltip("Selected on startup so gamepad/keyboard navigation has something to move away " +
                 "from - EventSystem has no selection by default, so stick/D-pad input does " +
                 "nothing until something is selected.")]
        public GameObject startButton;
        [Tooltip("Restored as the selected object when the Options panel closes.")]
        public GameObject optionsButton;

        [Tooltip("Start/Options/Exit - disabled while the Options panel is open so automatic UI " +
                 "navigation can't escape onto them from underneath it (they'd otherwise stay " +
                 "fully interactable behind the overlay), and re-enabled once it closes.")]
        public Button[] mainMenuButtons;

        private OptionsMenuController _optionsController;

        private void Awake()
        {
            GameSettings.Load();
            GameSettings.ApplyGraphics();
            if (optionsMenuPanel != null) optionsMenuPanel.SetActive(false);

            if (startButton != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(startButton);

            if (optionsMenuPanel != null)
            {
                _optionsController = optionsMenuPanel.GetComponent<OptionsMenuController>();
                if (_optionsController != null)
                {
                    _optionsController.OnOpened += DisableMainMenuButtons;
                    _optionsController.OnClosed += EnableMainMenuButtons;
                }
            }
        }

        private void OnDestroy()
        {
            if (_optionsController != null)
            {
                _optionsController.OnOpened -= DisableMainMenuButtons;
                _optionsController.OnClosed -= EnableMainMenuButtons;
            }
        }

        private void DisableMainMenuButtons()
        {
            if (mainMenuButtons == null) return;
            foreach (var button in mainMenuButtons) button.interactable = false;
        }

        private void EnableMainMenuButtons()
        {
            if (mainMenuButtons == null) return;
            foreach (var button in mainMenuButtons) button.interactable = true;
        }

        public void OnStartPressed()
        {
            SceneManager.LoadScene("Main Game");
        }

        public void OnOptionsPressed()
        {
            if (optionsMenuPanel == null) return;

            var optionsController = optionsMenuPanel.GetComponent<OptionsMenuController>();
            if (optionsController != null) optionsController.returnSelectionTarget = optionsButton;
            optionsMenuPanel.SetActive(true);
        }

        public void OnExitPressed()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
