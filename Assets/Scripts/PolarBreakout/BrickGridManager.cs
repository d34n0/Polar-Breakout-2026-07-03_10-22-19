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

        private void Start()
        {
            if (level != null) BuildLevel(level);
        }

        public void BuildLevel(LevelSO levelToBuild)
        {
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
        }

        public Brick GetBrickAt(PolarCoordinate coord)
        {
            _activeBricks.TryGetValue(coord, out var brick);
            return brick;
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
