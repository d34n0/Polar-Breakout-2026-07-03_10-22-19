using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Aggregates every Card acquired so far this run into queryable per-stat bonuses/
    /// multipliers, read by PaddleController/PaddleAbilities/BallController/ScoreManager. Kept
    /// entirely separate from the base ScriptableObject settings (PolarGridSettings, each
    /// component's own configured fields) so those stay pristine - a run's cards are just a
    /// temporary overlay, discarded via ResetRun() rather than mutating any shared asset.
    /// Deliberately NOT PlayerPrefs-backed like GameSettings/CosmeticsManager - a run's cards
    /// don't survive past that run (roguelite permadeath); only meta-progression (unlocks,
    /// cosmetics, high scores) persists between runs.
    /// </summary>
    public class RunModifiers : MonoBehaviour
    {
        private readonly List<CardSO> _acquiredCards = new List<CardSO>();

        public IReadOnlyList<CardSO> AcquiredCards => _acquiredCards;

        /// <summary>Fires after a card is added or the run resets - PaddleController/
        /// PaddleAbilities listen to this to rebuild whatever geometry their own stat bonuses
        /// affect (paddle width, turret spacing) instead of only picking up the change on the
        /// next Awake.</summary>
        public event System.Action OnModifiersChanged;

        public void AddCard(CardSO card)
        {
            if (card == null) return;
            _acquiredCards.Add(card);
            OnModifiersChanged?.Invoke();
        }

        /// <summary>Clears every acquired card - call at the start of a fresh run (including
        /// after Game Over), since cards are run-scoped, not persistent.</summary>
        public void ResetRun()
        {
            _acquiredCards.Clear();
            OnModifiersChanged?.Invoke();
        }

        /// <summary>Sum of every acquired card's raw value for an additive-convention type
        /// (degrees, counts, seconds, world units) - 0 if none acquired.</summary>
        public float GetBonus(ModifierType type)
        {
            float total = 0f;
            foreach (var card in _acquiredCards)
            {
                if (card.effects == null) continue;
                foreach (var effect in card.effects)
                    if (effect.type == type) total += effect.value;
            }
            return total;
        }

        /// <summary>1 + sum of every acquired card's fractional value for a multiplicative-
        /// convention type (e.g. two +15% cards give 1.3, not 1.15 * 1.15) - 1 (no change) if
        /// none acquired.</summary>
        public float GetMultiplier(ModifierType type) => 1f + GetBonus(type);
    }
}
