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
    }
}
