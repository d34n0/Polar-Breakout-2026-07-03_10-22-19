using UnityEngine;
using UnityEngine.Audio;

namespace PolarBreakout
{
    /// <summary>
    /// Base class for all brick types. To add a new brick type, subclass this
    /// and override OnHit / OnDestroyed - no changes to existing code needed.
    /// </summary>
    public abstract class BrickTypeSO : ScriptableObject
    {
        [Header("Identity")]
        public string brickId = "brick_default";
        public string displayName = "Brick";

        [Header("Stats")]
        public int maxHealth = 1;
        public int scoreValue = 10;
        [Tooltip("Multiplies scoreValue to get the actual points awarded - lets a multi-hit " +
                 "tier's base points and its multiplier be tuned independently rather than " +
                 "hand-computing one flat total (e.g. scoreValue=10, scoreMultiplier=3 for a " +
                 "3-hit brick awards 30). Leave at 1 for a plain flat scoreValue.")]
        public float scoreMultiplier = 1f;
        public bool isIndestructible = false;

        /// <summary>Actual points awarded on destruction - scoreValue * scoreMultiplier, rounded.</summary>
        public int EffectiveScoreValue => Mathf.RoundToInt(scoreValue * scoreMultiplier);

        [Header("Visuals")]
        public Color color = Color.white;
        [Tooltip("Optional. Overrides the brick prefab's default material for bricks of this " +
                 "type, so different types can use entirely different shaders (e.g. a glowing " +
                 "shader for exploding bricks) rather than just a color tint. Leave unset to use " +
                 "whatever material is already on Brick.prefab.")]
        public Material materialOverride;
        [Tooltip("Color the brick flashes to on every hit (both a surviving hit and the final " +
                 "destroying hit - see Brick.flashDuration/blinkIntervalSeconds for timing). " +
                 "Applied via MaterialPropertyBlock on top of whatever material is showing at the " +
                 "time (materialOverride/hitFlashMaterial/the shared brick material), so it works " +
                 "with any shader that reads _Color or _BaseColor. Push the HDR intensity slider " +
                 "in the color picker above 1 to make the flash bloom (see the scene's " +
                 "Volume > Bloom override) instead of just looking like a flat color swap.")]
        [ColorUsage(true, true)]
        public Color hitFlashColor = new Color(2f, 2f, 2f, 1f);
        [Tooltip("Optional. When set, the brick's renderer actually swaps to this material for " +
                 "the duration of the hit flash (see Brick.flashDuration), instead of just " +
                 "tinting the brick's normal material via hitFlashColor above - use this for a " +
                 "genuinely different shader while flashing (e.g. a dedicated glow/emissive " +
                 "shader) rather than a brighter color on the same shader. hitFlashColor is still " +
                 "applied via MaterialPropertyBlock on top of this material each flash, so a glow " +
                 "shader that reads _Color/_BaseColor can still have its intensity tuned per " +
                 "brick type from a single shared material asset. Leave unset to flash the " +
                 "brick's normal material with just a color swap, as before.")]
        public Material hitFlashMaterial;

        [Header("Audio")]
        [Tooltip("Played on a hit that the brick survives (health above 0 afterward). Accepts a " +
                 "plain AudioClip or an Audio Random Container asset (Window > Audio > Audio " +
                 "Random Container) for per-hit pitch/volume/clip-choice variation. Leave unset " +
                 "for a silent hit.")]
        public AudioResource hitSound;
        [Tooltip("Played on the hit that destroys the brick, instead of hitSound. Same " +
                 "AudioClip-or-Audio-Random-Container flexibility as hitSound. Leave unset for a " +
                 "silent destruction.")]
        public AudioResource destroyedSound;

        [Header("Power-Up Drop")]
        [Tooltip("Chance (0-1) that destroying a brick of this type drops a power-up capsule. " +
                 "0 = never drops - this is how most tiers should stay unless deliberately " +
                 "configured as a power-up source.")]
        [Range(0f, 1f)]
        public float powerUpDropChance = 0f;
        [Tooltip("Which power-up(s) this brick type can drop when the chance above succeeds - " +
                 "one is picked at random from this list each time. Irrelevant if " +
                 "powerUpDropChance is 0. Leave empty to never drop even with a nonzero chance.")]
        public PowerUpType[] possiblePowerUps = new PowerUpType[0];
        [Tooltip("When enabled, powerUpDropChance scales up as the stage's remaining " +
                 "destructible bricks dwindle toward its clear threshold - roughly 2x at 25% " +
                 "bricks remaining, roughly 3x near the clear threshold itself. Leave off for " +
                 "brick types that should always drop at a flat rate.")]
        public bool scaleDropChanceNearClear = true;

        [Header("Shard Drop")]
        [Tooltip("Chance (0-1) that destroying a brick of this type drops crystal shards - the " +
                 "in-round currency spent on rerolling CardOfferController's offered cards. " +
                 "Independent of powerUpDropChance above - a brick can drop both, either, or " +
                 "neither on the same destruction. 0 = never drops.")]
        [Range(0f, 1f)]
        public float shardDropChance = 0f;
        [Tooltip("Shard amount is picked at random (inclusive) between these two each time the " +
                 "chance above succeeds.")]
        public int shardDropAmountMin = 1;
        public int shardDropAmountMax = 3;

        /// <summary>
        /// Called whenever the ball hits a brick of this type.
        /// Return true if the brick should be destroyed as a result of this hit.
        /// </summary>
        public virtual bool OnHit(Brick brick, GameObject ball)
        {
            if (isIndestructible) return false;

            brick.CurrentHealth -= 1;
            return brick.CurrentHealth <= 0;
        }

        /// <summary>
        /// Called once, right when the brick is destroyed. Base implementation rolls
        /// powerUpDropChance and spawns a capsule on success - any brick tier can become a
        /// power-up source just by setting that chance above 0, rather than needing a dedicated
        /// power-up-only brick type. Override (calling base.OnDestroyed to keep the roll, or not,
        /// to replace it) for explosions, chain reactions into neighboring bricks, etc.
        /// </summary>
        public virtual void OnDestroyed(Brick brick)
        {
            TryDropPowerUp(brick);
            TryDropShards(brick);
        }

        /// <summary>Rolls powerUpDropChance and, on success, spawns a PowerUpCapsule at the
        /// brick's position. Exposed as protected so subclasses that override OnDestroyed for
        /// other behavior (explosions, etc.) can still opt into the same roll.</summary>
        protected void TryDropPowerUp(Brick brick)
        {
            if (powerUpDropChance <= 0f) return;
            if (possiblePowerUps == null || possiblePowerUps.Length == 0) return;

            float effectiveChance = powerUpDropChance;
            if (scaleDropChanceNearClear && brick.Manager != null)
                effectiveChance *= ComputeDropScale(brick.Manager);

            if (Random.value > Mathf.Clamp01(effectiveChance)) return;

            PowerUpType chosen = possiblePowerUps[Random.Range(0, possiblePowerUps.Length)];
            var capsuleObject = new GameObject($"PowerUpCapsule_{chosen}");
            var capsule = capsuleObject.AddComponent<PowerUpCapsule>();
            capsule.Initialize(brick.WorldPosition, chosen);
        }

        /// <summary>Interpolates the power-up drop-chance multiplier from 1x (at 100%+ bricks
        /// remaining) up to 2x by 25% remaining, then up to 3x by the stage's clear threshold -
        /// two linear segments joined at the 25% breakpoint, clamped at both ends. Runs after
        /// BrickGridManager.NotifyBrickDestroyed has already decremented RemainingDestructibleCount
        /// for the brick currently being destroyed, so this reflects the count *after* it.</summary>
        private static float ComputeDropScale(BrickGridManager manager)
        {
            int initial = manager.InitialDestructibleCount;
            if (initial <= 0) return 1f;

            const float midFraction = 0.25f;
            const float midMultiplier = 2f;
            const float endMultiplier = 3f;

            float remainingFraction = Mathf.Clamp01((float)manager.RemainingDestructibleCount / initial);
            float thresholdFraction = Mathf.Min(midFraction * 0.999f, Mathf.Clamp01((float)manager.ClearThreshold / initial));

            if (remainingFraction >= midFraction)
            {
                float t = Mathf.InverseLerp(1f, midFraction, remainingFraction);
                return Mathf.Lerp(1f, midMultiplier, t);
            }

            float lowT = Mathf.InverseLerp(midFraction, thresholdFraction, remainingFraction);
            return Mathf.Lerp(midMultiplier, endMultiplier, Mathf.Clamp01(lowT));
        }

        /// <summary>Rolls shardDropChance and, on success, spawns a ShardPickup worth a random
        /// amount (shardDropAmountMin-Max inclusive) at the brick's position. Exposed as
        /// protected for the same reason as TryDropPowerUp above.</summary>
        protected void TryDropShards(Brick brick)
        {
            if (shardDropChance <= 0f) return;
            if (Random.value > shardDropChance) return;

            int amount = Random.Range(shardDropAmountMin, shardDropAmountMax + 1);
            if (amount <= 0) return;

            var shardObject = new GameObject("ShardPickup");
            var shard = shardObject.AddComponent<ShardPickup>();
            shard.Initialize(brick.WorldPosition, amount);
        }

        /// <summary>
        /// Called once, right before the brick's GameObject is actually destroyed - after its
        /// hit-flash (see Brick.flashDuration) has finished playing. Distinct from OnDestroyed,
        /// which fires immediately when the brick is hit. Override for effects that should be
        /// visually delayed until the brick is about to disappear, like a chain-reacting
        /// explosion's fuse (see ExplodingBrickType).
        /// </summary>
        public virtual void OnFlashComplete(Brick brick)
        {
        }
    }
}
