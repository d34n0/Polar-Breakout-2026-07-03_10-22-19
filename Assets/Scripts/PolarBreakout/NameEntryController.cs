using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace PolarBreakout
{
    /// <summary>
    /// Classic arcade-style 3-letter high score name entry: each of the 3 slots cycles A-Z with
    /// the stick, confirmed one at a time with Submit (A/Space) - once all 3 are locked in, the
    /// cursor lands on a 4th "END" option that finishes entry. Cancel (B/Escape) steps back one
    /// slot, undoing its confirmed letter. Reuses the same UI Navigate/Submit/Cancel actions
    /// already wired to the EventSystem's InputSystemUIInputModule (the same actions
    /// OptionsMenuController hooks for its own Cancel handling), rather than adding new gameplay
    /// bindings, since this is UI-scoped input driven by raw actions rather than Selectable
    /// components - there's nothing here for the EventSystem to "select".
    /// </summary>
    public class NameEntryController : MonoBehaviour
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private static readonly Color ActiveColor = Color.yellow;
        private static readonly Color InactiveColor = Color.white;

        public GameObject panelRoot;
        [Tooltip("Exactly 3 - one TMP text per letter slot.")]
        public TextMeshProUGUI[] letterSlots;
        public TextMeshProUGUI endText;

        private readonly char[] _letters = { 'A', 'A', 'A' };
        private int _cursor;
        private bool _isOpen;
        private Action<string> _onNameConfirmed;

        private InputAction _navigateAction;
        private InputAction _submitAction;
        private InputAction _cancelAction;
        private bool _actionsWired;

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_navigateAction != null) _navigateAction.performed -= OnNavigatePerformed;
            if (_submitAction != null) _submitAction.performed -= OnSubmitPerformed;
            if (_cancelAction != null) _cancelAction.performed -= OnCancelPerformed;
        }

        // Deferred to first Open() call rather than Awake(): this GameObject is active from
        // scene load, so there's no guarantee EventSystem.current is already set that early
        // (Unity runs every object's Awake before any OnEnable, so EventSystem's own
        // current-assignment isn't guaranteed to have happened yet). By the time a game actually
        // ends and Open() is called, the EventSystem has long since initialized.
        private void EnsureActionsWired()
        {
            if (_actionsWired) return;

            var uiModule = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            if (uiModule == null) return;

            _navigateAction = uiModule.move.action;
            _submitAction = uiModule.submit.action;
            _cancelAction = uiModule.cancel.action;

            if (_navigateAction != null) _navigateAction.performed += OnNavigatePerformed;
            if (_submitAction != null) _submitAction.performed += OnSubmitPerformed;
            if (_cancelAction != null) _cancelAction.performed += OnCancelPerformed;

            _actionsWired = true;
        }

        /// <summary>Opens the panel and resets to "AAA" with the cursor on the first slot.
        /// onConfirmed is invoked with the finished 3-letter name once the player selects END.</summary>
        public void Open(Action<string> onConfirmed)
        {
            EnsureActionsWired();

            // Name entry reads raw actions directly rather than using Selectable-based
            // navigation, so there's nothing here for the EventSystem to select - but whatever
            // was selected before this opened (e.g. a leftover pause-menu button) would otherwise
            // keep sitting there and could react to the same Submit/Cancel presses driving letter
            // entry. Clearing it removes that risk for the whole time this panel is open.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            _onNameConfirmed = onConfirmed;
            _letters[0] = _letters[1] = _letters[2] = 'A';
            _cursor = 0;
            _isOpen = true;
            if (panelRoot != null) panelRoot.SetActive(true);
            RefreshDisplay();
        }

        private void OnNavigatePerformed(InputAction.CallbackContext context)
        {
            if (!_isOpen || _cursor >= 3) return;

            Vector2 dir = context.ReadValue<Vector2>();
            if (dir.y > 0.5f) CycleLetter(1);
            else if (dir.y < -0.5f) CycleLetter(-1);
        }

        private void CycleLetter(int delta)
        {
            int index = Alphabet.IndexOf(_letters[_cursor]);
            index = (index + delta + Alphabet.Length) % Alphabet.Length;
            _letters[_cursor] = Alphabet[index];
            RefreshDisplay();
        }

        private void OnSubmitPerformed(InputAction.CallbackContext context)
        {
            if (!_isOpen) return;

            if (_cursor < 3)
            {
                _cursor++;
                RefreshDisplay();
            }
            else
            {
                Confirm();
            }
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            if (!_isOpen || _cursor <= 0) return;
            _cursor--;
            RefreshDisplay();
        }

        private void Confirm()
        {
            _isOpen = false;
            string name = new string(_letters);
            if (panelRoot != null) panelRoot.SetActive(false);
            _onNameConfirmed?.Invoke(name);
        }

        private void RefreshDisplay()
        {
            for (int i = 0; i < letterSlots.Length; i++)
            {
                letterSlots[i].text = _letters[i].ToString();
                letterSlots[i].color = i == _cursor ? ActiveColor : InactiveColor;
            }
            if (endText != null) endText.color = _cursor == 3 ? ActiveColor : InactiveColor;
        }
    }
}
