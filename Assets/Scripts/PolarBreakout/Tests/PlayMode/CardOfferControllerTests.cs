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
    /// Covers CardOfferController's gamepad/keyboard selection handling around the flip
    /// reveal sequence (see CardOfferSlot.PlayFlipReveal) - a real regression once shipped
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

        private CardOfferSlot BuildSlot(Transform parent, string label, bool withCardBack = false)
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

            // cardBackRoot deliberately left unset by default - PlayFlipReveal then just
            // reveals instantly with no animation (see its own no-op fallback), which is exactly
            // what a fast selection-focused test wants; the interactable-toggling bug this test
            // guards against doesn't depend on the animation actually playing out. Tests that
            // need the real flip timing (e.g. the reveal-overlap test) pass withCardBack.
            if (withCardBack)
            {
                var cardBackGO = new GameObject("CardBack", typeof(RectTransform));
                cardBackGO.transform.SetParent(slotGO.transform, false);
                slot.cardBackRoot = cardBackGO;
            }

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
            // cardBackRoot is left unset by BuildSlot here, so PlayFlipReveal itself no-ops
            // instantly - but PlayRevealSequence's halfway-overlap wait (see
            // CardOfferSlot.TotalRevealDuration) is still computed from these duration fields
            // regardless of whether an animation actually plays, so they're zeroed here to keep
            // this selection-focused test fast and deterministic rather than waiting on real
            // reveal timing it doesn't care about.
            foreach (var slot in controller.slots)
            {
                slot.faceDownHoldDuration = 0f;
                slot.flipDuration = 0f;
            }

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
            float waitStart = Time.unscaledTime;
            while (selected == null && Time.unscaledTime - waitStart < 2f)
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

        [UnityTest]
        public IEnumerator SelectingACard_PlaysOneShot360Spin_UnselectedCardEasesToIdentity()
        {
            var eventSystemGO = Track(new GameObject("TestEventSystem2"));
            eventSystemGO.AddComponent<EventSystem>();

            var canvasGO = Track(new GameObject("TestCanvas2"));
            canvasGO.AddComponent<Canvas>();

            var slot = BuildSlot(canvasGO.transform, "Spin");
            slot.selectionSpinDuration = 0.3f;
            slot.idleWobbleDegrees = 0f; // Isolate the spin from the selected-card wobble noise.

            var card = ScriptableObject.CreateInstance<CardSO>();
            card.displayName = "Test Card";
            card.description = "Test description.";
            card.rarity = CardRarity.Common;
            slot.Initialize(card, _ => { });
            slot.SetRevealed(true);

            // Simulate a leftover rotation from the reveal flip - an unselected card should ease
            // this back to facing forward rather than holding it or continuing to rotate.
            slot.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            float startTime = Time.unscaledTime;
            while (Time.unscaledTime - startTime < 1f) yield return null;
            Assert.Less(Quaternion.Angle(slot.transform.localRotation, Quaternion.identity), 5f,
                "An unselected card should ease back to facing forward, not keep rotating.");

            // Selecting it should play a one-shot 360-degree spin - partway through, it should be
            // clearly away from both 0 and 360 (i.e. actually mid-turn, not skipped straight past).
            EventSystem.current.SetSelectedGameObject(slot.button.gameObject);
            startTime = Time.unscaledTime;
            while (Time.unscaledTime - startTime < slot.selectionSpinDuration / 2f) yield return null;
            float midSpinY = slot.transform.localRotation.eulerAngles.y;
            Assert.Greater(Mathf.Min(midSpinY, 360f - midSpinY), 30f,
                "Partway through selecting a card, it should be visibly mid-spin, not still near 0/360 degrees.");

            // Once the spin finishes, it should settle back to facing forward (then the normal
            // selected wobble - disabled here via idleWobbleDegrees=0 - would take over).
            while (Time.unscaledTime - startTime < slot.selectionSpinDuration + 0.2f) yield return null;
            Assert.Less(Quaternion.Angle(slot.transform.localRotation, Quaternion.identity), 5f,
                "Once the selection spin finishes, the card should settle back to facing forward.");
        }

        [UnityTest]
        public IEnumerator PlayRevealSequence_StartsNextCardHalfwayThroughThePreviousOnesReveal()
        {
            var eventSystemGO = Track(new GameObject("TestEventSystem3"));
            eventSystemGO.AddComponent<EventSystem>();

            var canvasGO = Track(new GameObject("TestCanvas3"));
            canvasGO.AddComponent<Canvas>();

            var controllerGO = Track(new GameObject("TestCardOfferController3"));
            var controller = controllerGO.AddComponent<CardOfferController>();

            var panelGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelGO.transform.SetParent(canvasGO.transform, false);
            panelGO.SetActive(false);
            controller.panelRoot = panelGO;

            var slotA = BuildSlot(panelGO.transform, "A", withCardBack: true);
            var slotB = BuildSlot(panelGO.transform, "B", withCardBack: true);
            foreach (var slot in new[] { slotA, slotB })
            {
                slot.faceDownHoldDuration = 0f;
                slot.flipDuration = 0.3f;
            }
            controller.slots = new[] { slotA, slotB };

            var cards = new CardSO[2];
            for (int i = 0; i < 2; i++)
            {
                var card = ScriptableObject.CreateInstance<CardSO>();
                card.displayName = $"Test Card {i}";
                card.description = "Test description.";
                card.rarity = CardRarity.Common;
                cards[i] = card;
            }
            controller.allCards = cards;

            controller.StartCoroutine(controller.ShowOffer());

            // slotA's TotalRevealDuration is 0.3s (0 hold + 0.3 flip), so slotB should start its
            // own reveal at the 0.15s halfway mark - well before slotA's reveal fully finishes.
            // Sampling at 0.2s (0.05s into slotB's own rotate-to-edge-on phase) should catch
            // slotB visibly mid-rotation, proving the two reveals overlap rather than running
            // strictly one at a time (if sequential, slotB wouldn't start until slotA's reveal
            // fully finishes at 0.3s, and would still be sitting at identity rotation here).
            float startTime = Time.unscaledTime;
            while (Time.unscaledTime - startTime < 0.2f) yield return null;

            float slotBAngle = Quaternion.Angle(slotB.transform.localRotation, Quaternion.identity);
            Assert.Greater(slotBAngle, 10f,
                "slotB should already be visibly mid-rotation by 0.2s in - its reveal should have " +
                "started at slotA's halfway point (0.15s), not after slotA fully finishes (0.3s).");
            Assert.Less(slotBAngle, 89f,
                "slotB should still be mid-flip (not already past the edge-on midpoint) at 0.2s in.");

            // Let the whole sequence finish naturally so ShowOffer's coroutine doesn't linger -
            // bounded by real time rather than frame count, since the reveal itself takes real
            // seconds (frame count alone is an unreliable proxy for elapsed wall-clock time).
            GameObject selected = null;
            float waitStart = Time.unscaledTime;
            while (selected == null && Time.unscaledTime - waitStart < 2f)
            {
                yield return null;
                selected = EventSystem.current.currentSelectedGameObject;
            }
            Assert.IsNotNull(selected);
            controller.slots[0].button.onClick.Invoke();
            yield return null;
        }
    }
}
