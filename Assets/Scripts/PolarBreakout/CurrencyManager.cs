using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Tracks in-round "crystal shards" - dropped by bricks (see BrickTypeSO.shardDropChance /
    /// ShardPickup), spent on rerolling CardOfferController's offered cards. Same run-scoped
    /// lifetime as RunModifiers: NOT PlayerPrefs-backed, reset via ResetRun() at the start of a
    /// fresh run rather than persisting between runs like cosmetics/high scores do.
    /// </summary>
    public class CurrencyManager : MonoBehaviour
    {
        public int CurrentShards { get; private set; }

        public event System.Action<int> OnShardsChanged;

        public void AddShards(int amount)
        {
            if (amount <= 0) return;
            CurrentShards += amount;
            OnShardsChanged?.Invoke(CurrentShards);
        }

        /// <summary>Deducts amount and returns true if there were enough shards; leaves
        /// CurrentShards untouched and returns false otherwise.</summary>
        public bool TrySpend(int amount)
        {
            if (amount <= 0) return true;
            if (CurrentShards < amount) return false;

            CurrentShards -= amount;
            OnShardsChanged?.Invoke(CurrentShards);
            return true;
        }

        /// <summary>Zeroes the balance - call at the start of a fresh run (including after Game
        /// Over), since shards don't survive past the run they were earned in.</summary>
        public void ResetRun()
        {
            CurrentShards = 0;
            OnShardsChanged?.Invoke(CurrentShards);
        }
    }
}
