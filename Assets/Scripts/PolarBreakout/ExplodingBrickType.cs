using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// On destruction, damages every other brick within <see cref="explosionRadius"/> world
    /// units. If that blast reaches another exploding brick, it explodes too, so a dense
    /// cluster can chain-react.
    ///
    /// Chaining is driven by an explicit worklist (a static queue), not by letting
    /// OnDestroyed call Hit() on neighbors directly - a brick's own OnDestroyed calling Hit()
    /// on a neighbor whose OnDestroyed calls Hit() on another neighbor is unbounded recursion
    /// through the call stack, and a big enough connected cluster of touching exploding bricks
    /// would blow it. Only the outermost OnDestroyed call actually drains the queue; any
    /// explosion triggered while that's already running just adds to the queue instead of
    /// recursing further, so total stack depth stays constant regardless of chain length.
    /// </summary>
    [CreateAssetMenu(fileName = "ExplodingBrickType", menuName = "PolarBreakout/Brick Types/Exploding")]
    public class ExplodingBrickType : BrickTypeSO
    {
        [Header("Explosion")]
        public float explosionRadius = 1.5f;

        private static readonly Queue<Brick> PendingExplosions = new Queue<Brick>();
        private static readonly HashSet<Brick> VisitedThisChain = new HashSet<Brick>();
        private static bool _processingChain;

        public override void OnDestroyed(Brick brick)
        {
            PendingExplosions.Enqueue(brick);

            if (_processingChain) return;

            _processingChain = true;
            try
            {
                while (PendingExplosions.Count > 0)
                {
                    Brick exploding = PendingExplosions.Dequeue();
                    // Use the dequeued brick's own ExplodingBrickType radius, not `this` one -
                    // the queue can mix bricks from different exploding asset instances (e.g.
                    // different radii per tier) once a chain is underway.
                    float radius = exploding.BrickType is ExplodingBrickType explodingType
                        ? explodingType.explosionRadius
                        : explosionRadius;
                    var nearby = exploding.Manager.GetBricksInRadius(exploding.WorldPosition, radius);
                    foreach (var other in nearby)
                    {
                        if (other == exploding || other.IsDestroyed || VisitedThisChain.Contains(other)) continue;
                        VisitedThisChain.Add(other);
                        // ball is null: this hit came from an explosion, not a direct ball
                        // strike - no current BrickTypeSO.OnHit override reads it.
                        other.Hit(null);
                    }
                }
            }
            finally
            {
                _processingChain = false;
                VisitedThisChain.Clear();
            }
        }
    }
}
