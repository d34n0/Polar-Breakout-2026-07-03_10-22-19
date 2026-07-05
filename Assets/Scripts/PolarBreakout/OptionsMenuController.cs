using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Tab-switching shell for the Options menu - shows exactly one of the category content
    /// panels at a time. Owns no settings logic itself; each category panel script
    /// (AudioSettingsPanel etc.) talks to GameSettings directly. Tab button clicks are wired to
    /// SelectCategory(int) as persistent listeners at construction time, not here.
    /// </summary>
    public class OptionsMenuController : MonoBehaviour
    {
        [Tooltip("One entry per tab, in the same order as the tab buttons - index 0 shown by default.")]
        public GameObject[] categoryPanels;
        [Tooltip("One entry per tab button, same order as categoryPanels - used to give gamepad/" +
                 "keyboard navigation something to select when the panel opens (EventSystem needs " +
                 "a currently-selected object to navigate away from; without one, stick/D-pad input " +
                 "does nothing).")]
        public GameObject[] tabButtons;
        [Tooltip("The root GameObject this whole panel lives on - Back deactivates this rather " +
                 "than destroying it, so the same instance can be reopened.")]
        public GameObject panelRoot;
        [Tooltip("Set by whichever caller opens this panel (Title Screen's Options button, the " +
                 "pause menu's Options button) - Back restores selection here so gamepad/keyboard " +
                 "navigation has something selected again once the panel closes.")]
        public GameObject returnSelectionTarget;

        // The UI Cancel action (Escape on keyboard, East/B button on gamepad) already exists on
        // the EventSystem's InputSystemUIInputModule - hooked directly here rather than added to
        // PolarBreakoutControls, since it's UI-scoped input, not gameplay input, and this way
        // works regardless of which scene's EventSystem is active.
        private InputAction _cancelAction;
        private int _currentCategoryIndex;

        /// <summary>Fired when this panel opens/closes (tied to panelRoot's own active state) -
        /// whichever menu is showing behind this one (Title Screen, pause menu) uses these to
        /// disable/re-enable its own buttons for the duration, since they'd otherwise still sit
        /// there fully interactable underneath the overlay and Unity's automatic UI navigation
        /// can "escape" onto them from Back (a Selectable with no on-screen occluder to stop it).</summary>
        public event System.Action OnOpened;
        public event System.Action OnClosed;

        private void OnEnable()
        {
            if (categoryPanels != null && categoryPanels.Length > 0) ShowCategory(0);

            if (tabButtons != null && tabButtons.Length > 0 && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(tabButtons[0]);

            var uiModule = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            _cancelAction = uiModule != null ? uiModule.cancel.action : null;
            if (_cancelAction != null) _cancelAction.performed += OnCancelPerformed;

            OnOpened?.Invoke();
        }

        private void OnDisable()
        {
            if (_cancelAction != null) _cancelAction.performed -= OnCancelPerformed;
            _cancelAction = null;

            OnClosed?.Invoke();
        }

        // Two-level back: from inside a category's own controls (sliders/toggles/buttons),
        // Cancel returns focus to that category's tab rather than closing the whole panel -
        // pressing Cancel again from the tab list is what actually closes it. Distinguished by
        // checking whether the currently selected object lives under the active category panel.
        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            bool insideActiveCategory = selected != null
                && categoryPanels != null
                && _currentCategoryIndex < categoryPanels.Length
                && selected.transform.IsChildOf(categoryPanels[_currentCategoryIndex].transform);

            if (insideActiveCategory && tabButtons != null && _currentCategoryIndex < tabButtons.Length)
            {
                EventSystem.current.SetSelectedGameObject(tabButtons[_currentCategoryIndex]);
                return;
            }

            OnBackPressed();
        }

        public void ShowCategory(int index)
        {
            _currentCategoryIndex = index;
            for (int i = 0; i < categoryPanels.Length; i++)
                categoryPanels[i].SetActive(i == index);
        }

        /// <summary>
        /// What the tab buttons actually call: switches to the category (see ShowCategory) and
        /// also moves selection into its first control, since choosing a tab should land you
        /// inside its settings ready to adjust them, not leave you sitting on the tab button.
        /// ShowCategory itself stays selection-agnostic because OnEnable also calls it for the
        /// default category, where the tab button - not a content control - should stay selected
        /// (see OnEnable).
        /// </summary>
        public void SelectCategory(int index)
        {
            ShowCategory(index);

            if (EventSystem.current == null || categoryPanels == null || index >= categoryPanels.Length) return;

            var firstControl = categoryPanels[index].GetComponentInChildren<Selectable>(true);
            if (firstControl != null) EventSystem.current.SetSelectedGameObject(firstControl.gameObject);
        }

        public void OnBackPressed()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(returnSelectionTarget);
            if (panelRoot != null) panelRoot.SetActive(false);
        }
    }
}
