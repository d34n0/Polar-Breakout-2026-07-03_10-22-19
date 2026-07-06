using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// One offerable roguelite run-card - a rarity tier (for reroll/offer-weighting once that
    /// system exists) plus a set of stat effects applied via RunModifiers. Deliberately just
    /// data: acquiring a card means calling RunModifiers.AddCard(this), nothing here talks to
    /// gameplay systems directly.
    /// </summary>
    [CreateAssetMenu(fileName = "Card", menuName = "Polar Breakout/Card")]
    public class CardSO : ScriptableObject
    {
        public string displayName;
        [TextArea]
        public string description;
        public CardRarity rarity;
        public CardEffect[] effects;

        [Header("Art (optional)")]
        [Tooltip("Optional. Illustration shown in the card offer UI (see CardOfferSlot) - leave " +
                 "unset to show just the plain rarity-tinted background with no artwork.")]
        public Sprite artSprite;
        [Tooltip("Optional. Overrides the default sprite rendering material, so a card's art can " +
                 "use a custom shader/effect instead of plain sprite rendering. Only used when " +
                 "artSprite above is set.")]
        public Material artMaterialOverride;
    }
}
