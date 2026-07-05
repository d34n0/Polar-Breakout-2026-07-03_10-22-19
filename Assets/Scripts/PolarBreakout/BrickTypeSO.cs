using UnityEngine;

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
        public Sprite sprite;
        [Tooltip("Optional. Overrides the brick prefab's default material for bricks of this " +
                 "type, so different types can use entirely different shaders (e.g. a glowing " +
                 "shader for exploding bricks) rather than just a color tint. Leave unset to use " +
                 "whatever material is already on Brick.prefab.")]
        public Material materialOverride;

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
        }

        /// <summary>Rolls powerUpDropChance and, on success, spawns a PowerUpCapsule at the
        /// brick's position. Exposed as protected so subclasses that override OnDestroyed for
        /// other behavior (explosions, etc.) can still opt into the same roll.</summary>
        protected void TryDropPowerUp(Brick brick)
        {
            if (powerUpDropChance <= 0f) return;
            if (possiblePowerUps == null || possiblePowerUps.Length == 0) return;
            if (Random.value > powerUpDropChance) return;

            PowerUpType chosen = possiblePowerUps[Random.Range(0, possiblePowerUps.Length)];
            var capsuleObject = new GameObject($"PowerUpCapsule_{chosen}");
            var capsule = capsuleObject.AddComponent<PowerUpCapsule>();
            capsule.Initialize(brick.WorldPosition, chosen);
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
