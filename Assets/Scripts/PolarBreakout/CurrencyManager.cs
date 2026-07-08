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
        [Header("Shard Visual")]
        [Tooltip("Optional. Assign real artwork for ShardPickup instances (see ShardPickup." +
                 "BuildVisual) - spawned dynamically at runtime with no prefab of their own, so " +
                 "this is the one central place to configure their look, the same role TurretSkin " +
                 "plays for cannon barrels. Leave unset to fall back to the plain procedural " +
                 "diamond mesh tinted by ShardPickup.color.")]
        public Sprite shardSprite;
        [Tooltip("Optional. Overrides the default sprite material when shardSprite is set, for a " +
                 "custom shader/effect. Leave unset for standard sprite rendering.")]
        public Material shardSpriteMaterialOverride;

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
