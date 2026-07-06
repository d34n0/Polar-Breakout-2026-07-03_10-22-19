using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        [Tooltip("How long the flip (rotating edge-on and back out around the Y axis, swapping to " +
                 "the revealed front face exactly at the edge-on midpoint) takes, seconds.")]
        public float flipDuration = 0.3f;

        [Header("Selected Card Feedback")]
        [Tooltip("How much larger the selected card's RectTransform scales, e.g. 1.08 = 8% bigger " +
                 "- the primary at-a-glance cue for which card is currently selected.")]
        public float selectedScale = 1.08f;
        [Tooltip("Max tilt in degrees on each axis while this card is the EventSystem's currently " +
                 "selected one - kept small so the front stays almost facing forward, just enough " +
                 "to catch the light on a holo/foil material. Set to 0 to disable.")]
        public float idleWobbleDegrees = 3f;
        [Tooltip("How fast the idle wobble drifts, in cycles per second - low values read as a " +
                 "slow ambient tilt rather than a shake.")]
        public float idleWobbleSpeed = 0.15f;
        [Tooltip("How long the wobble takes to ease in from a dead stop each time this card is " +
                 "newly selected, seconds - guarantees the card is perfectly unrotated the instant " +
                 "it's picked (scale-up alone reads as the selection cue) rather than snapping " +
                 "straight into a mid-wobble tilt.")]
        public float wobbleRampInDuration = 0.3f;
        [Tooltip("How fast scale/rotation ease toward their selected/deselected target, higher = snappier.")]
        public float selectionTransitionSpeed = 8f;

        private bool _hasArt;
        private bool _revealed;
        private bool _flipping;
        private bool _wasSelected;
        private float _selectedSinceTime;
        private float _wobbleSeedX, _wobbleSeedY, _wobbleSeedZ;

        private void Awake()
        {
            _wobbleSeedX = Random.Range(0f, 100f);
            _wobbleSeedY = Random.Range(0f, 100f);
            _wobbleSeedZ = Random.Range(0f, 100f);
        }

        private void Update()
        {
            if (_flipping || !_revealed) return;

            var rect = (RectTransform)transform;
            bool isSelected = EventSystem.current != null &&
                              EventSystem.current.currentSelectedGameObject == button.gameObject;

            if (isSelected && !_wasSelected) _selectedSinceTime = Time.unscaledTime;
            _wasSelected = isSelected;

            Vector3 targetScale = Vector3.one * (isSelected ? selectedScale : 1f);
            rect.localScale = Vector3.Lerp(rect.localScale, targetScale, Time.unscaledDeltaTime * selectionTransitionSpeed);

            if (isSelected && idleWobbleDegrees > 0f)
            {
                // rampT is 0 the instant selection starts, so x/y/z below all come out to
                // exactly 0 (unrotated) at that moment no matter where the Perlin curves are.
                float rampT = wobbleRampInDuration > 0f
                    ? Mathf.Clamp01((Time.unscaledTime - _selectedSinceTime) / wobbleRampInDuration)
                    : 1f;
                float amount = idleWobbleDegrees * rampT;
                float t = Time.unscaledTime * idleWobbleSpeed;
                float x = (Mathf.PerlinNoise(_wobbleSeedX, t) * 2f - 1f) * amount;
                float y = (Mathf.PerlinNoise(_wobbleSeedY, t) * 2f - 1f) * amount;
                float z = (Mathf.PerlinNoise(_wobbleSeedZ, t) * 2f - 1f) * amount;
                rect.localRotation = Quaternion.Euler(x, y, z);
            }
            else if (rect.localRotation != Quaternion.identity)
            {
                rect.localRotation = Quaternion.Slerp(rect.localRotation, Quaternion.identity, Time.unscaledDeltaTime * selectionTransitionSpeed);
            }
        }

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
            _revealed = revealed;
            if (cardBackRoot != null) cardBackRoot.SetActive(!revealed);
            rarityText.gameObject.SetActive(revealed);
            nameText.gameObject.SetActive(revealed);
            descriptionText.gameObject.SetActive(revealed);
            if (artImage != null) artImage.gameObject.SetActive(revealed && _hasArt);
        }

        /// <summary>Pops up from nothing while face-down, holds briefly, then flips over -
        /// rotating around the Y axis to edge-on and back out, swapping to the revealed front
        /// face exactly at the edge-on midpoint - so a holo/foil card material gets its own
        /// moment in the spotlight rather than just appearing instantly. Uses an actual rotation
        /// (rather than squashing localScale.x to 0) so shaders that react to view direction/tilt
        /// - like a holo card material - visibly catch the light during the flip, not just when
        /// the card is dragged around afterward. Runs entirely on unscaled time, since the whole
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

            _flipping = true;

            var rect = (RectTransform)transform;
            rect.localScale = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            SetRevealed(false);

            yield return ScaleOverRealtime(rect, Vector3.zero, Vector3.one, popInDuration);
            yield return new WaitForSecondsRealtime(faceDownHoldDuration);

            float half = Mathf.Max(0.0001f, flipDuration / 2f);
            yield return RotateYOverRealtime(rect, 0f, 90f, half);

            SetRevealed(true);
            // 90 and -90 are the same edge-on pose, but jumping to -90 here (rather than
            // continuing on to 180) means both faces read right-way-round instead of the
            // revealed front appearing mirrored.
            rect.localRotation = Quaternion.Euler(0f, -90f, 0f);

            yield return RotateYOverRealtime(rect, -90f, 0f, half);

            _flipping = false;
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

        private static IEnumerator RotateYOverRealtime(RectTransform rect, float fromY, float toY, float duration)
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.0001f, duration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float y = Mathf.Lerp(fromY, toY, Mathf.Clamp01(elapsed / duration));
                rect.localRotation = Quaternion.Euler(0f, y, 0f);
                yield return null;
            }
            rect.localRotation = Quaternion.Euler(0f, toY, 0f);
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
