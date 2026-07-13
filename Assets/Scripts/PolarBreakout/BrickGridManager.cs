using System;
using System.Collections.Generic;

using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Spawns and tracks brick instances for a LevelSO, converting hex coordinates to world
    /// positions via the level's PolarGridSettings (hex grid, despite the class name - see that
    /// file for why the name stuck around).
    /// </summary>
    public class BrickGridManager : MonoBehaviour
    {
        public LevelSO level;
        public Brick brickPrefab;
        [Tooltip("Optional. Routes each brick's hitSound/destroyedSound (see BrickTypeSO) - " +
                 "read by Brick.Hit via Manager.audioManager. Leave unset for silent bricks.")]
        public AudioManager audioManager;

        private readonly Dictionary<HexCoordinate, Brick> _activeBricks = new Dictionary<HexCoordinate, Brick>();

        // Every hex brick at a given hexSize/hexGap is congruent, so the mesh/collider outline
        // is built once per BuildLevel call and shared across every Brick instance, rather than
        // each brick building its own (as the old per-ring arc bricks had to).
        private Mesh _sharedHexMesh;
        private Vector2[] _sharedHexOutline;

        public int RemainingDestructibleCount { get; private set; }

        /// <summary>Snapshot of RemainingDestructibleCount taken once BuildLevel finishes
        /// spawning every placement - the denominator for the soft clear threshold (see
        /// ClearThreshold) and the power-up drop-rate surge (see BrickTypeSO.ComputeDropScale).</summary>
        public int InitialDestructibleCount { get; private set; }

        /// <summary>OnLevelCleared fires once RemainingDestructibleCount drops to this value or
        /// below, not strictly zero - lets LevelManager implement a "soft clear" (advance once
        /// only a handful of bricks remain, sweeping the rest via HexWipeTransition) instead of
        /// requiring a literal 100% clear. Defaults to 0, preserving the exact old behavior for
        /// any caller (tests, non-gauntlet scenes) that never calls SetClearThreshold.</summary>
        public int ClearThreshold { get; private set; }

        public void SetClearThreshold(int threshold) => ClearThreshold = Mathf.Max(0, threshold);

        // Guards OnLevelCleared against firing more than once per level - once RemainingDestructibleCount
        // is at or below a nonzero ClearThreshold, every further brick destroyed would otherwise
        // re-satisfy the "<= ClearThreshold" check and re-invoke the event.
        private bool _levelClearedFired;

        /// <summary>Re-arms the OnLevelCleared guard for a freshly (re)built level. BuildLevel
        /// already does this itself - only needed by callers that spawn a level's bricks some
        /// other way (see HexWipeTransition.PlayBuildIn, whose progressive per-cell spawn via
        /// SpawnBrickAt never goes through BuildLevel), so that level can still ever clear instead
        /// of staying latched from whatever level last set the guard.</summary>
        public void ResetLevelClearedGuard() => _levelClearedFired = false;

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

        /// <summary>Set by LevelManager (in Awake, so it's guaranteed to land before this
        /// component's own Start runs) when a HexWipeTransition is wired up, so the very first
        /// level plays an animated build-in instead of this component's own default instant
        /// auto-build.</summary>
        public bool skipAutoBuildOnStart;

        private void Start()
        {
            if (!_hasBuilt && !skipAutoBuildOnStart && level != null) BuildLevel(level);
        }

        public void BuildLevel(LevelSO levelToBuild)
        {
            _hasBuilt = true;
            _levelClearedFired = false;
            ClearGrid();
            level = levelToBuild;

            var settings = level.gridSettings;
            if (settings == null)
            {
                Debug.LogError("LevelSO has no PolarGridSettings assigned.", level);
                return;
            }

            PrepareSharedGeometry(settings);

            foreach (var (coord, brickType) in level.GetPlacements())
            {
                if (!settings.IsValidCoordinate(coord))
                {
                    Debug.LogWarning($"Skipping brick at {coord}: outside grid bounds.");
                    continue;
                }
                SpawnBrick(settings, coord, brickType);
            }

            SnapshotInitialDestructibleCount();
            GetComponent<HexArenaBoundary>()?.BuildBoundary(settings);
        }

        /// <summary>Snapshots RemainingDestructibleCount into InitialDestructibleCount - called at
        /// the end of BuildLevel's own spawn loop, and also by HexWipeTransition.PlayBuildIn once
        /// its progressive per-cell sweep finishes spawning every placement via SpawnBrickAt
        /// (which, unlike BuildLevel, doesn't otherwise touch this snapshot at all).</summary>
        public void SnapshotInitialDestructibleCount() => InitialDestructibleCount = RemainingDestructibleCount;

        private void SpawnBrick(PolarGridSettings settings, HexCoordinate coord, BrickTypeSO type)
        {
            Brick brick = Instantiate(brickPrefab, transform);
            brick.transform.localPosition = settings.HexToWorld(coord);
            brick.transform.localRotation = Quaternion.identity;
            brick.Initialize(this, settings, coord, type, _sharedHexMesh, _sharedHexOutline);
            _activeBricks[coord] = brick;

            if (!type.isIndestructible)
                RemainingDestructibleCount++;
        }

        /// <summary>Builds _sharedHexMesh/_sharedHexOutline for the given settings without
        /// spawning anything - lets a caller (e.g. HexWipeTransition.PlayBuildIn) ready the shared
        /// geometry once, then spawn individual bricks progressively via SpawnBrickAt instead of
        /// BuildLevel's normal all-at-once loop.</summary>
        public void PrepareSharedGeometry(PolarGridSettings settings)
        {
            float hexRadius = Mathf.Max(0.01f, settings.hexSize - settings.hexGap);
            _sharedHexMesh = PolarMeshUtility.BuildHexMesh(hexRadius);
            _sharedHexOutline = PolarMeshUtility.BuildHexOutlinePoints(hexRadius);
        }

        /// <summary>Public per-cell counterpart to BuildLevel's internal spawn loop - spawns
        /// exactly one brick at coord using whatever shared mesh/outline PrepareSharedGeometry (or
        /// BuildLevel) already built. No-ops with a warning if neither has run yet.</summary>
        public void SpawnBrickAt(PolarGridSettings settings, HexCoordinate coord, BrickTypeSO type)
        {
            if (_sharedHexMesh == null)
            {
                Debug.LogWarning("SpawnBrickAt called before PrepareSharedGeometry/BuildLevel - no shared mesh.", this);
                return;
            }
            SpawnBrick(settings, coord, type);
        }

        /// <summary>Removes the brick at coord (if any) without firing OnBrickDestroyed/
        /// OnLevelCleared or touching RemainingDestructibleCount - a presentation-only clear for
        /// HexWipeTransition's tear-down wipe, which isn't player-driven destruction and shouldn't
        /// award score or trigger level-cleared side effects.</summary>
        public void RemoveBrickQuietly(HexCoordinate coord)
        {
            if (_activeBricks.TryGetValue(coord, out var brick) && brick != null)
            {
                _activeBricks.Remove(coord);
                Destroy(brick.gameObject);
            }
        }

        /// <summary>Bulk version of RemoveBrickQuietly - destroys every remaining active brick
        /// with no events/score side effects. Called once at the end of HexWipeTransition's
        /// tear-down as a safety net, so a brick sitting outside the swept screen rect (e.g. on an
        /// unusual aspect ratio) can never survive a level transition.</summary>
        public void ClearAllBricksQuietly()
        {
            foreach (var kv in _activeBricks)
                if (kv.Value != null) Destroy(kv.Value.gameObject);
            _activeBricks.Clear();
        }

        /// <summary>Snapshot (not a live view) of every active brick's coordinate - used by
        /// HexWipeTransition's tear-down wipe to know which swept cells need the "brick
        /// disappears" reveal branch.</summary>
        public List<HexCoordinate> GetActiveBrickCoordinates() => new List<HexCoordinate>(_activeBricks.Keys);

        public void NotifyBrickDestroyed(Brick brick)
        {
            _activeBricks.Remove(brick.Coordinate);

            if (!brick.BrickType.isIndestructible)
            {
                RemainingDestructibleCount--;
                if (!_levelClearedFired && RemainingDestructibleCount <= ClearThreshold)
                {
                    _levelClearedFired = true;
                    OnLevelCleared?.Invoke();
                }
            }

            OnBrickDestroyed?.Invoke(brick);
        }

        public Brick GetBrickAt(HexCoordinate coord)
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
