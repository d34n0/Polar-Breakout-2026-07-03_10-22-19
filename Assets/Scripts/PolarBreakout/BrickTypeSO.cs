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
        public bool isIndestructible = false;

        [Header("Visuals")]
        public Color color = Color.white;
        public Sprite sprite;
        [Tooltip("Optional. Overrides the brick prefab's default material for bricks of this " +
                 "type, so different types can use entirely different shaders (e.g. a glowing " +
                 "shader for exploding bricks) rather than just a color tint. Leave unset to use " +
                 "whatever material is already on Brick.prefab.")]
        public Material materialOverride;

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
        /// Called once, right when the brick is destroyed. Override for
        /// explosions, power-up drops, chain reactions into neighboring bricks, etc.
        /// </summary>
        public virtual void OnDestroyed(Brick brick)
        {
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
