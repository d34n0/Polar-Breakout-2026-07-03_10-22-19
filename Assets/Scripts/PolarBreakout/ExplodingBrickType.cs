using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Flashes for <see cref="fuseDuration"/> seconds (like a lit fuse) before actually
    /// detonating, damaging every other brick within <see cref="explosionRadius"/> world units.
    /// If that blast reaches another exploding brick, it starts its own fuse in turn, so a dense
    /// cluster chain-reacts as a visible cascade of staggered explosions rather than an instant
    /// simultaneous wipe.
    ///
    /// The fuse delay is what keeps this safe from unbounded recursion, not an explicit
    /// worklist: OnFlashComplete fires from a coroutine once per brick, on its own schedule, so
    /// hitting a neighbor here just starts that neighbor's own independent fuse instead of
    /// recursing synchronously into another explosion - stack depth stays O(1) regardless of
    /// chain length.
    /// </summary>
    [CreateAssetMenu(fileName = "ExplodingBrickType", menuName = "PolarBreakout/Brick Types/Exploding")]
    public class ExplodingBrickType : BrickTypeSO
    {
        [Header("Explosion")]
        public float explosionRadius = 1.5f;
        [Tooltip("How long this brick flashes (see Brick.flashDuration) before it actually " +
                 "detonates and damages nearby bricks - roughly 0.2-0.5s reads as a short fuse.")]
        public float fuseDuration = 0.35f;
        [Tooltip("Spawned at the brick's position the moment it detonates - every ParticleSystem " +
                 "anywhere in its hierarchy is tinted to this brick type's color field above, so a " +
                 "GameObject rather than a single ParticleSystem so a composite multi-layer effect " +
                 "(e.g. Explosion.prefab's separate Fire/Flash/Sparks/Smoke children) can be used " +
                 "as-is. Distinct from the generic break-particle burst in BrickBreakEffects (which " +
                 "still plays too, once this brick is actually destroyed) - this one represents the " +
                 "blast itself. Leave unset for no dedicated explosion burst.")]
        public GameObject explosionParticlesPrefab;

        public override void OnFlashComplete(Brick brick)
        {
            SpawnExplosionParticles(brick);

            var nearby = brick.Manager.GetBricksInRadius(brick.WorldPosition, explosionRadius);
            foreach (var other in nearby)
            {
                if (other == brick || other.IsDestroyed) continue;
                // ball is null: this hit came from an explosion, not a direct ball strike - no
                // current BrickTypeSO.OnHit override reads it.
                other.Hit(null);
            }
        }

        private void SpawnExplosionParticles(Brick brick)
        {
            if (explosionParticlesPrefab == null) return;

            GameObject instance = Instantiate(explosionParticlesPrefab, brick.WorldPosition, Quaternion.identity);
            // Force full opacity - see the matching comment in BrickBreakEffects.SpawnBreakParticles.
            Color tint = color;
            tint.a = 1f;

            // Tints and plays every particle system in the prefab's hierarchy, not just a single
            // root one - see the matching comment in BrickBreakEffects.SpawnBreakParticles.
            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>())
            {
                var main = system.main;
                main.startColor = tint;
                system.Play();
            }
        }
    }
}
