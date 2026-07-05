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

        /// <summary>Shows the offer, blocks until the player picks one, applies it, then hides
        /// the panel again - call via `yield return cardOfferController.ShowOffer();`.</summary>
        public IEnumerator ShowOffer()
        {
            _rerollsUsedThisOffer = 0;
            RefreshOfferCards();

            Time.timeScale = 0f;
            SetGameplayActionsEnabled(false);
            if (panelRoot != null) panelRoot.SetActive(true);

            if (rerollButton != null)
            {
                rerollButton.onClick.RemoveAllListeners();
                rerollButton.onClick.AddListener(OnRerollClicked);
            }
            UpdateRerollUI();

            _chosenCard = null;
            _waitingForChoice = true;
            while (_waitingForChoice) yield return null;

            if (panelRoot != null) panelRoot.SetActive(false);
            Time.timeScale = 1f;
            SetGameplayActionsEnabled(true);

            if (_chosenCard != null && runModifiers != null) runModifiers.AddCard(_chosenCard);
        }

        private void OnCardChosen(CardSO card)
        {
            _chosenCard = card;
            _waitingForChoice = false;
        }

        private void OnRerollClicked()
        {
            int cost = CurrentRerollCost;
            if (currencyManager == null || !currencyManager.TrySpend(cost)) return;

            _rerollsUsedThisOffer++;
            RefreshOfferCards();
            UpdateRerollUI();
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

            if (EventSystem.current != null && offered.Count > 0)
                EventSystem.current.SetSelectedGameObject(slots[0].button.gameObject);
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
        /// pick removes that card from the pool so the same offer never repeats a card.</summary>
        private List<CardSO> PickOffer()
        {
            var pool = new List<CardSO>(allCards);
            var picked = new List<CardSO>();
            int count = Mathf.Min(slots.Length, pool.Count);

            for (int i = 0; i < count; i++)
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

                picked.Add(chosen);
                pool.Remove(chosen);
            }

            return picked;
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
