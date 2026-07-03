using System;
using System.Collections.Generic;

using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawns and tracks brick instances for a LevelSO, converting polar
    /// coordinates to world positions via the level's PolarGridSettings.
    /// </summary>
    public class BrickGridManager : MonoBehaviour
    {
        public LevelSO level;
        public Brick brickPrefab;

        private readonly Dictionary<PolarCoordinate, Brick> _activeBricks = new Dictionary<PolarCoordinate, Brick>();

        public int RemainingDestructibleCount { get; private set; }
        public event Action OnLevelCleared;

        /// <summary>Fired once per brick right when it's destroyed (never for indestructible
        /// bricks, since those never reach here) - ScoreManager listens for this to award
        /// each brick's BrickTypeSO.scoreValue.</summary>
        public event Action<Brick> OnBrickDestroyed;

        // Start() is deferred to the next frame after this component is added/enabled, so a
        // caller that manually calls BuildLevel() right after AddComponent<BrickGridManager>()
        // (as the ability tests do) would otherwise have Start() redundantly rebuild - and
        // thus reset - the level out from under it the moment control returns to Unity's
        // frame loop, discarding any destruction that already happened that frame.
        private bool _hasBuilt;

        private void Start()
        {
            if (!_hasBuilt && level != null) BuildLevel(level);
        }

        public void BuildLevel(LevelSO levelToBuild)
        {
            _hasBuilt = true;
            ClearGrid();
            level = levelToBuild;

            var settings = level.gridSettings;
            if (settings == null)
            {
                Debug.LogError("LevelSO has no PolarGridSettings assigned.", level);
                return;
            }

            foreach (var (coord, brickType) in level.GetPlacements())
            {
                if (!settings.IsValidCoordinate(coord))
                {
                    Debug.LogWarning($"Skipping brick at {coord}: outside grid bounds.");
                    continue;
                }
                SpawnBrick(settings, coord, brickType);
            }
        }

        private void SpawnBrick(PolarGridSettings settings, PolarCoordinate coord, BrickTypeSO type)
        {
            // Brick geometry (position, curvature, angular span) is baked directly
            // into its mesh in local space, so the prefab spawns at the manager's
            // own transform. Keep this GameObject at the arena's center (0,0,0)
            // with identity rotation for everything to line up.
            Brick brick = Instantiate(brickPrefab, transform);
            brick.transform.localPosition = Vector3.zero;
            brick.transform.localRotation = Quaternion.identity;
            brick.Initialize(this, settings, coord, type);
            _activeBricks[coord] = brick;

            if (!type.isIndestructible)
                RemainingDestructibleCount++;
        }

        public void NotifyBrickDestroyed(Brick brick)
        {
            _activeBricks.Remove(brick.Coordinate);

            if (!brick.BrickType.isIndestructible)
            {
                RemainingDestructibleCount--;
                if (RemainingDestructibleCount <= 0)
                    OnLevelCleared?.Invoke();
            }

            OnBrickDestroyed?.Invoke(brick);
        }

        public Brick GetBrickAt(PolarCoordinate coord)
        {
            _activeBricks.TryGetValue(coord, out var brick);
            return brick;
        }

        /// <summary>
        /// Snapshot (not a live view) of every active brick within <paramref name="radius"/>
        /// world units of <paramref name="worldPos"/>. Used by effects like exploding bricks
        /// that may destroy several bricks in one pass - a live/lazy view over the same
        /// dictionary they're being removed from would be fragile.
        /// </summary>
        public List<Brick> GetBricksInRadius(Vector2 worldPos, float radius)
        {
            var result = new List<Brick>();
            float sqrRadius = radius * radius;
            foreach (var brick in _activeBricks.Values)
            {
                if (brick == null || brick.IsDestroyed) continue;
                if ((brick.WorldPosition - worldPos).sqrMagnitude <= sqrRadius)
                    result.Add(brick);
            }
            return result;
        }

        private void ClearGrid()
        {
            foreach (var kv in _activeBricks)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _activeBricks.Clear();
            RemainingDestructibleCount = 0;
        }
    }
}
