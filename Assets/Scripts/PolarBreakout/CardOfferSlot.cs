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
    /// applying the chosen card, and staggering each slot's PlayFlipReveal).
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
                 "art - shown until PlayFlipReveal flips it away. Leave unset to skip the flip " +
                 "animation entirely and just show the front immediately (matches the original, " +
                 "pre-reveal-animation behavior).")]
        public GameObject cardBackRoot;
        [Tooltip("How long the card sits face-down at normal scale before flipping, seconds - " +
                 "gives the face-down card a moment to read before the flip starts.")]
        public float faceDownHoldDuration = 0.15f;
        [Tooltip("How long the flip (rotating edge-on and back out around the Y axis, swapping to " +
                 "the revealed front face exactly at the edge-on midpoint) takes, seconds.")]
        public float flipDuration = 0.3f;

        /// <summary>Total real time PlayFlipReveal takes from start to finish (face-down hold +
        /// flip) - read by CardOfferController to know when the NEXT card's own reveal should
        /// start (halfway through this one), rather than waiting for this one to fully finish
        /// first.</summary>
        public float TotalRevealDuration => faceDownHoldDuration + flipDuration;

        [Header("Selected Card Feedback")]
        [Tooltip("How much larger the selected card's RectTransform scales, e.g. 1.08 = 8% bigger " +
                 "- the primary at-a-glance cue for which card is currently selected.")]
        public float selectedScale = 1.08f;
        [Tooltip("Max tilt in degrees on the X axis while this card is the EventSystem's currently " +
                 "selected one - kept small so the front stays almost facing forward, just enough " +
                 "to catch the light on a holo/foil material. Also scales the Y/Z wobble before " +
                 "those get clamped to +/-8 degrees (see Update). Set to 0 to disable.")]
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
        [Tooltip("How long the one-shot 360-degree Y spin takes the instant this card becomes " +
                 "selected, seconds - eased in and out (via Mathf.SmoothStep) so it accelerates " +
                 "into the turn and settles cleanly facing forward, rather than snapping straight " +
                 "into the enlarged/wobble look. The scale-up to selectedScale happens at the same " +
                 "time, not after. Set to 0 to skip the spin and go straight to the normal " +
                 "selected behaviour.")]
        public float selectionSpinDuration = 0.4f;

        [Header("Holographic Shader Tilt")]
        [Tooltip("Multiplies the simulated tilt fed to the Holographic material's " +
                 "_Simulated_Tilt property (a UV offset), driven every frame from this card's own " +
                 "current rotation - the main camera is orthographic, so the shader's real " +
                 "ViewDirection input never actually changes on its own, and the holo effect would " +
                 "otherwise never be visible. Tune to taste; 0 disables it (material stays at a " +
                 "flat 0,0 offset).")]
        public float simulatedTiltStrength = 0.25f;

        private static readonly int SimulatedTiltId = Shader.PropertyToID("_Simulated_Tilt");

        private bool _hasArt;
        private bool _revealed;
        private bool _flipping;
        private bool _wasSelected;
        private float _selectedSinceTime;
        private bool _suppressNextSelectionSpin;
        private bool _spinSuppressedForCurrentSelection;
        private bool _spinShowingBack;
        private float _wobbleSeedX, _wobbleSeedY, _wobbleSeedZ;
        private Material _rootMaterialInstance;

        private void Awake()
        {
            _wobbleSeedX = Random.Range(0f, 100f);
            _wobbleSeedY = Random.Range(0f, 100f);
            _wobbleSeedZ = Random.Range(0f, 100f);

            // Instanced (not shared) so each card's own rotation drives its own tilt - setting
            // the property straight on the shared material asset would make every card using it
            // show whichever card last wrote to it.
            var rootImage = GetComponent<Image>();
            if (rootImage != null && rootImage.material != null && rootImage.material.HasProperty(SimulatedTiltId))
            {
                _rootMaterialInstance = new Material(rootImage.material);
                rootImage.material = _rootMaterialInstance;
            }
        }

        /// <summary>Feeds the card's current rotation to the Holographic material as a simulated
        /// view-angle offset (see simulatedTiltStrength) - sin/cos of the euler angles rather than
        /// the raw degrees, so the value is continuous (no jump when Quaternion.eulerAngles wraps
        /// from 359 back to 0, or during the one-shot 360 spin) and naturally settles to (0,0)
        /// when the card faces forward. No-op if this card's material doesn't expose the
        /// property (see Awake).</summary>
        private void UpdateSimulatedTilt(Quaternion rotation)
        {
            if (_rootMaterialInstance == null) return;

            Vector3 euler = rotation.eulerAngles;
            float tiltX = Mathf.Sin(euler.x * Mathf.Deg2Rad) * simulatedTiltStrength;
            float tiltY = Mathf.Sin(euler.y * Mathf.Deg2Rad) * simulatedTiltStrength;
            _rootMaterialInstance.SetVector(SimulatedTiltId, new Vector4(tiltX, tiltY, 0f, 0f));
        }

        /// <summary>Call right before the very first, automatic post-reveal selection (see
        /// CardOfferController.PlayRevealSequence) - the one-shot 360 spin should only play when
        /// the player actually picks a new card, not for the default selection the game itself
        /// applies the instant the reveal finishes.</summary>
        public void SuppressNextSelectionSpin() => _suppressNextSelectionSpin = true;

        private void Update()
        {
            if (_flipping || !_revealed) return;

            var rect = (RectTransform)transform;
            bool isSelected = EventSystem.current != null &&
                              EventSystem.current.currentSelectedGameObject == button.gameObject;

            if (isSelected && !_wasSelected)
            {
                _selectedSinceTime = Time.unscaledTime;
                _spinSuppressedForCurrentSelection = _suppressNextSelectionSpin;
                _suppressNextSelectionSpin = false;
            }
            _wasSelected = isSelected;

            Vector3 targetScale = Vector3.one * (isSelected ? selectedScale : 1f);
            rect.localScale = Vector3.Lerp(rect.localScale, targetScale, Time.unscaledDeltaTime * selectionTransitionSpeed);

            if (isSelected)
            {
                float timeSinceSelected = Time.unscaledTime - _selectedSinceTime;
                float spinDuration = _spinSuppressedForCurrentSelection ? 0f : selectionSpinDuration;

                if (timeSinceSelected < spinDuration)
                {
                    // One-shot 360-degree Y spin the instant this card becomes selected, at the
                    // same time as the scale-up above - eased in and out (SmoothStep) so it
                    // accelerates into the turn and settles cleanly facing forward, rather than
                    // snapping straight into the wobble below. Skipped entirely for the game's own
                    // default post-reveal selection (see SuppressNextSelectionSpin) - only a
                    // genuine player pick should spin.
                    float spinT = Mathf.Clamp01(timeSinceSelected / Mathf.Max(0.0001f, spinDuration));
                    float angle = Mathf.SmoothStep(0f, 360f, spinT);
                    rect.localRotation = Quaternion.Euler(0f, angle, 0f);

                    // The UI shader doesn't cull backfaces, so without this the front content
                    // (text/art/title/description/icon boxes) would still render - mirrored -
                    // once the card rotates past edge-on. Swap to the card-back visual for the
                    // half of the spin that's actually facing away from the camera, same as the
                    // reveal flip does at its own edge-on midpoint (see PlayFlipReveal).
                    bool showBack = angle > 90f && angle < 270f;
                    if (showBack != _spinShowingBack)
                    {
                        SetCardBackVisible(showBack);
                        _spinShowingBack = showBack;
                    }
                }
                else
                {
                    if (_spinShowingBack)
                    {
                        SetCardBackVisible(false);
                        _spinShowingBack = false;
                    }

                    if (idleWobbleDegrees > 0f)
                    {
                        // Ramp starts once the spin above finishes (not from the moment selection
                        // began), so the wobble eases in cleanly right where the spin left off -
                        // facing forward - instead of racing ahead mid-spin. If the spin was
                        // suppressed, spinDuration is 0 so the ramp starts immediately instead.
                        float rampT = wobbleRampInDuration > 0f
                            ? Mathf.Clamp01((timeSinceSelected - spinDuration) / wobbleRampInDuration)
                            : 1f;
                        float amount = idleWobbleDegrees * rampT;
                        float t = Time.unscaledTime * idleWobbleSpeed;
                        float x = (Mathf.PerlinNoise(_wobbleSeedX, t) * 2f - 1f) * amount;
                        // Y (turning left/right) and Z (in-plane roll) are capped at +/-8 degrees
                        // regardless of idleWobbleDegrees - large values there read as the card
                        // spinning or tipping over rather than a subtle idle wobble.
                        float y = Mathf.Clamp((Mathf.PerlinNoise(_wobbleSeedY, t) * 2f - 1f) * amount, -8f, 8f);
                        float z = Mathf.Clamp((Mathf.PerlinNoise(_wobbleSeedZ, t) * 2f - 1f) * amount, -8f, 8f);
                        rect.localRotation = Quaternion.Euler(x, y, z);
                    }
                    else if (rect.localRotation != Quaternion.identity)
                    {
                        rect.localRotation = Quaternion.Slerp(rect.localRotation, Quaternion.identity, Time.unscaledDeltaTime * selectionTransitionSpeed);
                    }
                }
            }
            else
            {
                if (_spinShowingBack)
                {
                    SetCardBackVisible(false);
                    _spinShowingBack = false;
                }

                if (rect.localRotation != Quaternion.identity)
                    rect.localRotation = Quaternion.Slerp(rect.localRotation, Quaternion.identity, Time.unscaledDeltaTime * selectionTransitionSpeed);
            }

            UpdateSimulatedTilt(rect.localRotation);
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
        /// animation - used to reset state before a reveal plays (see PlayFlipReveal) and
        /// as the immediate fallback when cardBackRoot isn't wired.</summary>
        public void SetRevealed(bool revealed)
        {
            _revealed = revealed;
            SetCardBackVisible(!revealed);
        }

        /// <summary>Shows either the card back (cardBackRoot) or all the front content - text,
        /// art, and the title/icon/description box backgrounds - never both at once. Used both by
        /// SetRevealed (the face-down/revealed states either side of a flip) and by the one-shot
        /// selection spin (see Update), which needs to swap to the back mid-spin for the half of
        /// the rotation that's actually facing away from the camera.</summary>
        private void SetCardBackVisible(bool showBack)
        {
            if (cardBackRoot != null) cardBackRoot.SetActive(showBack);

            bool showFront = !showBack;
            rarityText.gameObject.SetActive(showFront);
            nameText.gameObject.SetActive(showFront);
            descriptionText.gameObject.SetActive(showFront);
            if (artImage != null) artImage.gameObject.SetActive(showFront && _hasArt);
            if (titleBoxImage != null) titleBoxImage.gameObject.SetActive(showFront);
            if (iconCircleImage != null) iconCircleImage.gameObject.SetActive(showFront);
            if (descriptionBoxImage != null) descriptionBoxImage.gameObject.SetActive(showFront);
        }

        /// <summary>Holds briefly at normal scale while face-down, then flips over - rotating
        /// around the Y axis to edge-on and back out, swapping to the revealed front face exactly
        /// at the edge-on midpoint - so a holo/foil card material gets its own moment in the
        /// spotlight rather than just appearing instantly. Uses an actual rotation (rather than
        /// squashing localScale.x to 0) so shaders that react to view direction/tilt - like a holo
        /// card material - visibly catch the light during the flip, not just when the card is
        /// dragged around afterward. Runs entirely on unscaled time, since the whole card offer
        /// plays with Time.timeScale at 0 (see CardOfferController.ShowOffer). No-op (just reveals
        /// instantly) if cardBackRoot isn't wired, so slots built before this feature existed keep
        /// working unchanged.</summary>
        public IEnumerator PlayFlipReveal()
        {
            if (cardBackRoot == null)
            {
                SetRevealed(true);
                yield break;
            }

            _flipping = true;

            var rect = (RectTransform)transform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            SetRevealed(false);

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

        private IEnumerator RotateYOverRealtime(RectTransform rect, float fromY, float toY, float duration)
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.0001f, duration);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float y = Mathf.Lerp(fromY, toY, Mathf.Clamp01(elapsed / duration));
                rect.localRotation = Quaternion.Euler(0f, y, 0f);
                UpdateSimulatedTilt(rect.localRotation);
                yield return null;
            }
            rect.localRotation = Quaternion.Euler(0f, toY, 0f);
            UpdateSimulatedTilt(rect.localRotation);
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
