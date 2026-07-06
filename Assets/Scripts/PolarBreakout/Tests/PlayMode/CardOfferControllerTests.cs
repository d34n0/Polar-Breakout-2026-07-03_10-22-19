using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using UnityEngine.TestTools;

namespace PolarBreakout.Tests
{
    /// <summary>
    /// Covers CardOfferController's gamepad/keyboard selection handling around the pop/flip
    /// reveal sequence (see CardOfferSlot.PlayPopAndFlipReveal) - a real regression once shipped
    /// here: Selectable.interactable deselects a Selectable outright the instant it's set false
    /// while selected, and nothing restored that selection once PlayRevealSequence set
    /// interactable back to true, leaving gamepad/keyboard navigation with nothing selected to
    /// navigate from.
    /// </summary>
    public class CardOfferControllerTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private GameObject Track(GameObject go)
        {
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private CardOfferSlot BuildSlot(Transform parent, string label)
        {
            var slotGO = Track(new GameObject($"Slot_{label}", typeof(RectTransform)));
            slotGO.transform.SetParent(parent, false);
            var slot = slotGO.AddComponent<CardOfferSlot>();

            var rarityGO = new GameObject("Rarity", typeof(RectTransform));
            rarityGO.transform.SetParent(slotGO.transform, false);
            slot.rarityText = rarityGO.AddComponent<TextMeshProUGUI>();

            var nameGO = new GameObject("Name", typeof(RectTransform));
            nameGO.transform.SetParent(slotGO.transform, false);
            slot.nameText = nameGO.AddComponent<TextMeshProUGUI>();

            var descGO = new GameObject("Desc", typeof(RectTransform));
            descGO.transform.SetParent(slotGO.transform, false);
            slot.descriptionText = descGO.AddComponent<TextMeshProUGUI>();

            var artGO = new GameObject("Art", typeof(RectTransform));
            artGO.transform.SetParent(slotGO.transform, false);
            slot.artImage = artGO.AddComponent<Image>();

            // cardBackRoot deliberately left unset - PlayPopAndFlipReveal then just reveals
            // instantly with no animation (see its own no-op fallback), which is exactly what a
            // fast selection-focused test wants; the interactable-toggling bug this test guards
            // against doesn't depend on the animation actually playing out.
            slot.button = slotGO.AddComponent<Button>();
            return slot;
        }

        [UnityTest]
        public IEnumerator ShowOffer_SelectsFirstCard_AfterRevealSequenceCompletes()
        {
            var eventSystemGO = Track(new GameObject("TestEventSystem"));
            eventSystemGO.AddComponent<EventSystem>();

            var canvasGO = Track(new GameObject("TestCanvas"));
            canvasGO.AddComponent<Canvas>();

            var controllerGO = Track(new GameObject("TestCardOfferController"));
            var controller = controllerGO.AddComponent<CardOfferController>();
            controller.revealStaggerDelay = 0f;

            var panelGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.SetActive(false);
            controller.panelRoot = panelGO;

            controller.slots = new[]
            {
                BuildSlot(panelGO.transform, "A"),
                BuildSlot(panelGO.transform, "B"),
                BuildSlot(panelGO.transform, "C"),
            };

            var cards = new CardSO[3];
            for (int i = 0; i < 3; i++)
            {
                var card = ScriptableObject.CreateInstance<CardSO>();
                card.displayName = $"Test Card {i}";
                card.description = "Test description.";
                card.rarity = CardRarity.Common;
                cards[i] = card;
            }
            controller.allCards = cards;

            controller.StartCoroutine(controller.ShowOffer());

            GameObject selected = null;
            for (int i = 0; i < 60 && selected == null; i++)
            {
                yield return null;
                selected = EventSystem.current.currentSelectedGameObject;
            }

            Assert.IsNotNull(selected, "A card should be selected once the reveal sequence completes.");
            Assert.AreEqual(controller.slots[0].button.gameObject, selected,
                "The first active card should be the default gamepad/keyboard selection.");
            Assert.IsTrue(controller.slots[0].button.interactable,
                "Cards should be interactable again once the reveal completes.");
            Assert.IsTrue(controller.slots[1].button.interactable);
            Assert.IsTrue(controller.slots[2].button.interactable);

            // Let ShowOffer's own wait-for-choice loop resolve naturally, so its coroutine
            // doesn't linger past this test on a torn-down controller.
            controller.slots[0].button.onClick.Invoke();
            yield return null;
        }
    }
}
