using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PolarBreakout
{
    /// <summary>
    /// One clickable card tile within CardOfferController's 3-card layout - purely a view:
    /// Initialize() fills in the text/color for a given CardSO and wires the whole tile's Button
    /// to the callback. CardOfferController owns everything else (picking which 3 to show,
    /// applying the chosen card, and staggering each slot's PlayPopAndFlipReveal).
    /// </summary>
    public class CardOfferSlot : MonoBehaviour
    {
        public TextMeshProUGUI rarityText;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI descriptionText;
        [Tooltip("Optional. Shows CardSO.artSprite and/or artMaterialOverride when either is set " +
                 "- hidden entirely for cards with neither yet, rather than showing an empty " +
                 "white box. A material alone is enough (e.g. a self-contained shader like " +
                 "HoloCard that bakes its own textures rather than reading the Image's sprite).")]
        public Image artImage;
        public Button button;

        [Header("Layout (matches CardTemplateBase)")]
        [Tooltip("Optional. Background behind rarityText/nameText, sized/positioned to match " +
                 "CardTemplateBase's Title Box sprite - purely a visual frame, no logic reads it.")]
        public Image titleBoxImage;
        [Tooltip("Optional. Background behind artImage, sized/positioned to match " +
                 "CardTemplateBase's Icon Circle sprite - artImage's rect already lines up with " +
                 "this circle, so a future per-card icon sprite will sit correctly inside it.")]
        public Image iconCircleImage;
        [Tooltip("Optional. Background behind descriptionText, sized/positioned to match " +
                 "CardTemplateBase's Description Box sprite - purely a visual frame, no logic reads it.")]
        public Image descriptionBoxImage;

        [Header("Reveal Animation")]
        [Tooltip("Optional. The face-down card-back visual, covering the rarity/name/description/" +
                 "art - shown until PlayPopAndFlipReveal flips it away. Leave unset to skip the " +
                 "pop/flip animation entirely and just show the front immediately (matches the " +
                 "original, pre-reveal-animation behavior).")]
        public GameObject cardBackRoot;
        [Tooltip("How long the initial pop-in (scaling up from nothing while still face-down) takes, seconds.")]
        public float popInDuration = 0.18f;
        [Tooltip("How long the card sits face-down at full size before flipping, seconds - gives " +
                 "the pop a moment to read before the flip starts.")]
        public float faceDownHoldDuration = 0.15f;
        [Tooltip("How long the flip (scaling edge-on and back out, swapping to the revealed front " +
                 "face exactly at the edge-on midpoint) takes, seconds.")]
        public float flipDuration = 0.3f;

        private bool _hasArt;

        public void Initialize(CardSO card, System.Action<CardSO> onChosen)
        {
            rarityText.text = card.rarity.ToString().ToUpperInvariant();
            rarityText.color = RarityColor(card.rarity);
            nameText.text = card.displayName;
            descriptionText.text = card.description;

            _hasArt = card.artSprite != null || card.artMaterialOverride != null;
            if (artImage != null)
            {
                artImage.sprite = card.artSprite;
                artImage.material = card.artMaterialOverride;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onChosen(card));

            SetRevealed(false);
        }

        /// <summary>Instantly shows either the face-down back or the fully-revealed front, no
        /// animation - used to reset state before a reveal plays (see PlayPopAndFlipReveal) and
        /// as the immediate fallback when cardBackRoot isn't wired.</summary>
        public void SetRevealed(bool revealed)
        {
            if (cardBackRoot != null) cardBackRoot.SetActive(!revealed);
            rarityText.gameObject.SetActive(revealed);
            nameText.gameObject.SetActive(revealed);
            descriptionText.gameObject.SetActive(revealed);
            if (artImage != null) artImage.gameObject.SetActive(revealed && _hasArt);
        }

        /// <summary>Pops up from nothing while face-down, holds briefly, then flips over -
        /// scaling to edge-on and back out, swapping to the revealed front face exactly at the
        /// edge-on midpoint - so a holo/foil card material gets its own moment in the spotlight
        /// rather than just appearing instantly. Runs entirely on unscaled time, since the whole
        /// card offer plays with Time.timeScale at 0 (see CardOfferController.ShowOffer). No-op
        /// (just reveals instantly) if cardBackRoot isn't wired, so slots built before this
        /// feature existed keep working unchanged.</summary>
        public IEnumerator PlayPopAndFlipReveal()
        {
            if (cardBackRoot == null)
            {
                SetRevealed(true);
                yield break;
            }

            var rect = (RectTransform)transform;
            Vector3 fullScale = Vector3.one;
            rect.localScale = Vector3.zero;
            SetRevealed(false);

            yield return ScaleOverRealtime(rect, Vector3.zero, fullScale, popInDuration);
            yield return new WaitForSecondsRealtime(faceDownHoldDuration);

            float half = Mathf.Max(0.0001f, flipDuration / 2f);
            Vector3 edgeOn = new Vector3(0f, fullScale.y, fullScale.z);
            yield return ScaleOverRealtime(rect, fullScale, edgeOn, half);

            SetRevealed(true);

            yield return ScaleOverRealtime(rect, edgeOn, fullScale, half);
        }

        private static IEnumerator ScaleOverRealtime(RectTransform rect, Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.0001f, duration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                rect.localScale = Vector3.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            rect.localScale = to;
        }

        /// <summary>Grey/blue/purple/gold - deliberately avoids green entirely, since the
        /// selected/highlighted button state (see CardOfferController) already uses a bright
        /// green glow; an Uncommon-green rarity color would wash out illegibly whenever that
        /// card happened to be the currently-selected one.</summary>
        public static Color RarityColor(CardRarity rarity)
        {
            switch (rarity)
            {
                case CardRarity.Common: return new Color(0.75f, 0.75f, 0.75f);
                case CardRarity.Uncommon: return new Color(0.35f, 0.65f, 1f);
                case CardRarity.Rare: return new Color(0.7f, 0.45f, 1f);
                case CardRarity.Legendary: return new Color(1f, 0.7f, 0.15f);
                default: return Color.white;
            }
        }
    }
}
