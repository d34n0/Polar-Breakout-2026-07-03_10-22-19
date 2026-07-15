using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

namespace PolarBreakout
{
    /// <summary>
    /// Shows a mandatory 3-card choice between stages (see LevelManager.AdvanceToNextStage) -
    /// picks 3 unique cards weighted by rarity, freezes gameplay while the player decides (no
    /// Cancel/skip - a run always picks one), then applies the chosen card via
    /// RunModifiers.AddCard and hands control back. Optionally lets the player spend shards (see
    /// CurrencyManager) to reroll the offered 3 before picking - the cost escalates with each
    /// reroll used within the same offer, resetting back to baseRerollCost on the next one.
    /// </summary>
    public class CardOfferController : MonoBehaviour
    {
        [Header("References")]
        public RunModifiers runModifiers;
        [Tooltip("The full pool this run can draw from - duplicates within a single 3-card " +
                 "offer are never shown, but the same card can reappear across different offers " +
                 "(and stacks with itself if picked more than once).")]
        public CardSO[] allCards;
        public GameObject panelRoot;
        public CardOfferSlot[] slots;

        [Header("Rarity Weights")]
        [Tooltip("Relative chance each rarity is offered - higher = more common. Doesn't need to sum to 100.")]
        public float commonWeight = 60f;
        public float uncommonWeight = 27f;
        public float rareWeight = 10f;
        public float legendaryWeight = 3f;

        [Header("Reroll")]
        [Tooltip("Optional. When set, the Reroll button spends shards to draw a fresh 3-card " +
                 "offer. Leave unset (or rerollButton unset) to omit rerolling entirely.")]
        public CurrencyManager currencyManager;
        public Button rerollButton;
        public TextMeshProUGUI rerollCostText;
        public TextMeshProUGUI shardsText;
        [Tooltip("Cost of the first reroll in a given offer.")]
        public int baseRerollCost = 1;
        [Tooltip("Added to the cost each additional reroll within the same offer - resets to " +
                 "baseRerollCost on the next offer.")]
        public int rerollCostIncrement = 1;

        [Header("Input")]
        [Tooltip("Optional. Move/Fire are disabled while this panel is up, same as the pause " +
                 "menu - leave unset if not needed.")]
        public InputActionAsset actions;

        private CardSO _chosenCard;
        private bool _waitingForChoice;
        private int _rerollsUsedThisOffer;
        // Unity's InputSystemUIInputModule has its own Submit action, entirely separate from the
        // "Fire" action above - but they're normally bound to the same physical controls (gamepad
        // South, Space). SetGameplayActionsEnabled(false) only disables "Fire" itself, so Submit
        // stays fully live throughout the offer. Since the first card slot is auto-selected the
        // instant the reveal finishes (see PlayRevealSequence), a player who's still physically
        // holding Fire from firing the cannon moments before the level cleared would otherwise
        // have that same held press register as "Submit" on the just-selected card, silently
        // picking it without ever consciously choosing anything. This flag suppresses exactly
        // that: any card choice arriving before Fire's bound control has been freshly released.
        private bool _suppressAutoConfirmUntilFireReleased;

        // Set by LevelManager when a Survive stage is fully cleared before its timer expires -
        // consumed (and reset) the moment the next offer is picked, guaranteeing at least one
        // Rare-or-better card in that offer regardless of the normal weighted roll.
        private bool _guaranteeRareOrBetterNextOffer;

        /// <summary>Called by LevelManager when a Survive stage is fully cleared before its timer
        /// expires - the next ShowOffer() call guarantees at least one Rare-or-better card in one
        /// of its slots, instead of the normal fully-weighted-random roll. One-shot: consumed and
        /// reset the moment that next offer is picked, regardless of what the player chooses.</summary>
        public void GuaranteeRareOrBetterNextOffer() => _guaranteeRareOrBetterNextOffer = true;

        /// <summary>Shows the offer, blocks until the player picks one, applies it, then hides
        /// the panel again - call via `yield return cardOfferController.ShowOffer();`.</summary>
        public IEnumerator ShowOffer()
        {
            _rerollsUsedThisOffer = 0;

            Time.timeScale = 0f;
            SetGameplayActionsEnabled(false);
            // Activate the panel before touching any slot - slots are its children, so
            // activating/Initializing them first (while panelRoot is still inactive) leaves them
            // inactive-in-hierarchy too, meaning Unity never calls Awake() on a slot that hasn't
            // been active before. Initialize() relies on Awake() having already cached
            // _rootImage, so on a slot's very first-ever offer that left it silently null,
            // skipping the legendary holo material with no error.
            if (panelRoot != null) panelRoot.SetActive(true);

            RefreshOfferCards();

            yield return PlayRevealSequence();

            if (rerollButton != null)
            {
                rerollButton.onClick.RemoveAllListeners();
                rerollButton.onClick.AddListener(() => StartCoroutine(RerollSequence()));
            }
            UpdateRerollUI();

            _chosenCard = null;
            _waitingForChoice = true;
            while (_waitingForChoice)
            {
                if (_suppressAutoConfirmUntilFireReleased && !IsFirePhysicallyHeld())
                    _suppressAutoConfirmUntilFireReleased = false;
                yield return null;
            }

            if (panelRoot != null) panelRoot.SetActive(false);
            Time.timeScale = 1f;
            SetGameplayActionsEnabled(true);

            if (_chosenCard != null && runModifiers != null) runModifiers.AddCard(_chosenCard);
        }

        private void OnCardChosen(CardSO card)
        {
            // See _suppressAutoConfirmUntilFireReleased - ignore a choice that arrives from a
            // Fire press already held over from before the offer's cards became selectable,
            // rather than a fresh, deliberate press/click.
            if (_suppressAutoConfirmUntilFireReleased) return;

            _chosenCard = card;
            _waitingForChoice = false;
        }

        /// <summary>True if whatever physical control(s) "Fire" is bound to are currently held
        /// down, read directly off the resolved controls so this works regardless of which device
        /// (gamepad, keyboard) is in use - independent of whether the Fire action itself is
        /// currently enabled, since a disabled action's bound controls still report real hardware
        /// state.</summary>
        private bool IsFirePhysicallyHeld()
        {
            if (actions == null) return false;
            var fire = actions.FindActionMap("Player")?.FindAction("Fire");
            if (fire == null) return false;

            foreach (var control in fire.controls)
                if (control is UnityEngine.InputSystem.Controls.ButtonControl button && button.isPressed)
                    return true;
            return false;
        }

        private IEnumerator RerollSequence()
        {
            int cost = CurrentRerollCost;
            if (currencyManager == null || !currencyManager.TrySpend(cost)) yield break;

            _rerollsUsedThisOffer++;
            RefreshOfferCards();
            UpdateRerollUI();
            yield return PlayRevealSequence();
            // Re-asserted after the reveal restores every button's interactable flag to true -
            // otherwise Reroll would stay clickable regardless of currency once the flip finishes.
            UpdateRerollUI();
        }

        /// <summary>Each active slot sits face-down at normal scale, then flips over to reveal
        /// its card (see CardOfferSlot.PlayFlipReveal) - each next card starts its own reveal once
        /// the previous one is halfway through its own total reveal duration (see
        /// CardOfferSlot.TotalRevealDuration), rather than waiting for it to fully finish, so
        /// reveals overlap and the whole sequence completes faster while still reading as a
        /// left-to-right cascade. Every button (cards and Reroll alike) stays non-interactable for
        /// the whole sequence, so a trigger-happy click can't pick a card before its face is even
        /// showing, or reroll again mid-flip and overlap two reveals at once.</summary>
        private IEnumerator PlayRevealSequence()
        {
            SetOfferInteractable(false);

            var running = new List<Coroutine>();
            CardOfferSlot previousSlot = null;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].gameObject.activeSelf) continue;

                if (previousSlot != null)
                    yield return new WaitForSecondsRealtime(previousSlot.TotalRevealDuration / 2f);

                running.Add(StartCoroutine(slots[i].PlayFlipReveal()));
                previousSlot = slots[i];
            }

            foreach (var routine in running)
                yield return routine;

            SetOfferInteractable(true);

            // Arm the guard the instant cards become selectable/submittable - if Fire is already
            // held right now (e.g. the player was still firing the cannon when the last brick
            // died), the auto-selection below would otherwise let that same held press register
            // as an immediate Submit on the first card. See _suppressAutoConfirmUntilFireReleased.
            _suppressAutoConfirmUntilFireReleased = IsFirePhysicallyHeld();

            // Selecting the first active card here, after the reveal, rather than relying on
            // RefreshOfferCards' own selection call from before it - Selectable.interactable
            // deselects a Selectable outright the instant it's set false while selected (see
            // SetOfferInteractable(false) above), and nothing restores that selection
            // automatically once interactable again, leaving gamepad/keyboard navigation with
            // nothing selected to navigate from.
            if (EventSystem.current != null)
            {
                foreach (var slot in slots)
                {
                    if (!slot.gameObject.activeSelf) continue;
                    // This is the game's own default selection, not a player pick - suppress the
                    // one-shot 360 spin (see CardOfferSlot.SuppressNextSelectionSpin) so it only
                    // plays once the player actually selects a (different) card afterward.
                    slot.SuppressNextSelectionSpin();
                    EventSystem.current.SetSelectedGameObject(slot.button.gameObject);
                    break;
                }
            }
        }

        private void SetOfferInteractable(bool interactable)
        {
            foreach (var slot in slots)
                if (slot.gameObject.activeSelf) slot.button.interactable = interactable;
            if (rerollButton != null) rerollButton.interactable = interactable;
        }

        private int CurrentRerollCost => baseRerollCost + rerollCostIncrement * _rerollsUsedThisOffer;

        /// <summary>Picks a fresh 3 cards and (re-)populates the slots - used both for the
        /// initial offer and every reroll after.</summary>
        private void RefreshOfferCards()
        {
            var offered = PickOffer();
            for (int i = 0; i < slots.Length; i++)
            {
                bool hasCard = i < offered.Count;
                slots[i].gameObject.SetActive(hasCard);
                if (hasCard) slots[i].Initialize(offered[i], OnCardChosen);
            }

            // Not selected here - PlayRevealSequence selects the first active card itself once
            // the reveal finishes and buttons are interactable again (see its own comment).
        }

        private void UpdateRerollUI()
        {
            int shards = currencyManager != null ? currencyManager.CurrentShards : 0;
            int cost = CurrentRerollCost;

            if (shardsText != null) shardsText.text = "Shards: " + shards;
            if (rerollCostText != null) rerollCostText.text = $"Reroll ({cost})";
            if (rerollButton != null) rerollButton.interactable = currencyManager != null && shards >= cost;
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

        /// <summary>Picks slots.Length unique cards from allCards, weighted by rarity - each
        /// pick removes that card from the pool so the same offer never repeats a card. If
        /// _guaranteeRareOrBetterNextOffer is set, the last slot is forced to a Rare-or-better
        /// card unless an earlier slot already organically rolled one - see
        /// GuaranteeRareOrBetterNextOffer.</summary>
        private List<CardSO> PickOffer()
        {
            var pool = new List<CardSO>(allCards);
            var picked = new List<CardSO>();
            int count = Mathf.Min(slots.Length, pool.Count);

            bool guarantee = _guaranteeRareOrBetterNextOffer;
            _guaranteeRareOrBetterNextOffer = false;

            for (int i = 0; i < count; i++)
            {
                bool forceRareThisSlot = guarantee && i == count - 1 &&
                    !picked.Exists(c => c.rarity >= CardRarity.Rare);

                CardSO chosen;
                if (forceRareThisSlot)
                {
                    var rareOrBetterPool = pool.FindAll(c => c.rarity >= CardRarity.Rare);
                    chosen = rareOrBetterPool.Count > 0
                        ? rareOrBetterPool[Random.Range(0, rareOrBetterPool.Count)]
                        : WeightedPick(pool); // No Rare+ card available at all - fall back rather than error.
                }
                else
                {
                    chosen = WeightedPick(pool);
                }

                picked.Add(chosen);
                pool.Remove(chosen);
            }

            return picked;
        }

        private CardSO WeightedPick(List<CardSO> pool)
        {
            float totalWeight = 0f;
            foreach (var c in pool) totalWeight += WeightFor(c.rarity);

            float roll = Random.value * totalWeight;
            float cumulative = 0f;
            CardSO chosen = pool[pool.Count - 1];
            foreach (var c in pool)
            {
                cumulative += WeightFor(c.rarity);
                if (roll <= cumulative) { chosen = c; break; }
            }
            return chosen;
        }

        private float WeightFor(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Common: return commonWeight;
                case CardRarity.Uncommon: return uncommonWeight;
                case CardRarity.Rare: return rareWeight;
                case CardRarity.Legendary: return legendaryWeight;
                default: return 0f;
            }
        }
    }
}
