using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace PolarBreakout
{
    /// <summary>
    /// One clickable card tile within CardOfferController's 3-card layout - purely a view:
    /// Initialize() fills in the text/color for a given CardSO and wires the whole tile's Button
    /// to the callback. CardOfferController owns everything else (picking which 3 to show,
    /// applying the chosen card).
    /// </summary>
    public class CardOfferSlot : MonoBehaviour
    {
        public TextMeshProUGUI rarityText;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI descriptionText;
        [Tooltip("Optional. Shows CardSO.artSprite when set - hidden entirely for cards with no " +
                 "art yet, rather than showing an empty white box.")]
        public Image artImage;
        public Button button;

        public void Initialize(CardSO card, System.Action<CardSO> onChosen)
        {
            rarityText.text = card.rarity.ToString().ToUpperInvariant();
            rarityText.color = RarityColor(card.rarity);
            nameText.text = card.displayName;
            descriptionText.text = card.description;

            if (artImage != null)
            {
                bool hasArt = card.artSprite != null;
                artImage.gameObject.SetActive(hasArt);
                if (hasArt)
                {
                    artImage.sprite = card.artSprite;
                    artImage.material = card.artMaterialOverride;
                }
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onChosen(card));
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
