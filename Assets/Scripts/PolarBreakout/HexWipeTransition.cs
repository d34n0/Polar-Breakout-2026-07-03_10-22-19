using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Full-screen hex "wipe" level transition: tiles the entire camera viewport with the same
    /// pointy-top hex shape the brick grid uses, sweeping a diagonal wavefront from the screen's
    /// top-left corner to its bottom-right. Each hex flashes white as the wave passes it - if the
    /// cell should hold a brick, the brick appears right as the flash fades (PlayBuildIn); if a
    /// brick already exists there, it disappears the same way (PlayTearDown). Each swept hex also
    /// gets a colored outline that lingers and fades on its own, independent timeline, tracing
    /// which cells the wave has already passed. A cell that actually gets/had a brick also plays
    /// a short, separately-timed glow on top of that outline (see the "Brick Reveal Glow" fields)
    /// - a swept cell with no brick never glows. Called from LevelManager.AdvanceToNextStage,
    /// around the card offer, with the paddle/ball already hidden by the existing round-
    /// transition dissolve sequence - callers are expected to hide/show the arena boundary
    /// themselves (see HexArenaBoundary.Hide/Show) since that's a presentation choice belonging
    /// to the caller's own sequencing, not this effect.
    /// </summary>
    public class HexWipeTransition : MonoBehaviour
    {
        [Tooltip("Falls back to Camera.main if unset.")]
        public Camera targetCamera;
        public BrickGridManager brickGridManager;
        [Tooltip("Optional. Plays AudioManager.wipeTransitionSound once per sweep (build-in or " +
                 "tear-down), not once per individual hex cell. Leave unset for a silent wipe.")]
        public AudioManager audioManager;

        [Header("Sweep Tiling")]
        [Tooltip("Hex size used to tile the full-screen sweep overlay, independent of the level's " +
                 "own brick hexSize (PolarGridSettings.hexSize) - this is a cosmetic full-screen " +
                 "effect, so it doesn't need to match brick-level granularity, and a coarser size " +
                 "here keeps the total swept cell count (and therefore the cost of the whole " +
                 "effect) low even when the level itself uses a small, dense hexSize. When a " +
                 "single sweep cell covers more than one brick, they all pop in/out together the " +
                 "instant that cell's own flash reveals it. 0 or less falls back to the level's " +
                 "own hexSize (tiles 1:1 with bricks, the original behavior).")]
        public float sweepHexSize = 1f;

        [Header("Flash")]
        [Tooltip("Leave unset for a runtime-generated white flash-and-fade. Assign a custom " +
                 "material (e.g. a dissolve shader) to override the look with zero code changes - " +
                 "driven every frame via _DissolveProgress (0 at flash start, 1 at flash end), the " +
                 "same convention DissolveEffect already uses, so the existing Dissolve.mat works " +
                 "as a drop-in replacement.")]
        public Material flashMaterialOverride;
        [Tooltip("How long a single hex's own flash lasts, seconds.")]
        public float perHexFlashDuration = 0.1f;
        [Tooltip("How long the whole diagonal sweep takes to cross the full screen, seconds.")]
        public float totalSweepDuration = 1.2f;
        [Tooltip("Randomizes each hex's start time by up to this many seconds (earlier or " +
                 "later), so hexes at roughly the same diagonal position don't all flash in " +
                 "perfect lockstep - reads as a scattered, 'digitally being built' look instead " +
                 "of a razor-sharp line, while the overall top-left-to-bottom-right sweep " +
                 "structure stays intact. 0 = a perfectly smooth diagonal.")]
        public float startTimeJitter = 0.15f;
        [Tooltip("Safety multiplier over the estimated number of concurrently-flashing hexes when sizing the reused cell pool.")]
        public float poolSizeSafetyFactor = 1.5f;
        [Tooltip("Shapes the flash's own fade-out (the second half of perHexFlashDuration) - " +
                 "x=0 is the instant it starts fading (fully opaque), x=1 is fully faded. " +
                 "Independent of the sweep's own flat, linear timing, so the fade can ease " +
                 "differently (e.g. ease-out) instead of matching that same constant speed. " +
                 "Defaults to a plain linear fade.")]
        public AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Outline")]
        [Tooltip("Leave unset for a plain colored unlit outline - assign a custom material to " +
                 "override, driven the same way as the flash (see flashMaterialOverride).")]
        public Material outlineMaterialOverride;
        public Color outlineColor = Color.white;
        [Tooltip("Width of each hex's outline, world units.")]
        public float outlineWidth = 0.04f;
        [Tooltip("How long a hex's outline lingers and fades after being swept, seconds - " +
                 "independent of perHexFlashDuration, so it can trail behind the moving flash band.")]
        public float outlineFadeDuration = 0.6f;
        [Tooltip("Shapes the outline's fade-out over outlineFadeDuration - x=0 right after the " +
                 "hex is swept (fully opaque), x=1 once fully faded. Defaults to a plain linear fade.")]
        public AnimationCurve outlineFadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        [Header("Brick Reveal Glow")]
        [Tooltip("Leave unset for a plain colored unlit glow - assign a custom material to " +
                 "override, driven the same way as the flash (see flashMaterialOverride). Only " +
                 "plays on cells that actually get/had a brick - a swept empty cell never glows.")]
        public Material glowMaterialOverride;
        public Color glowColor = Color.yellow;
        [Tooltip("Width of the glow outline, world units - independent of outlineWidth so the " +
                 "glow can read as a thicker halo around the thinner plain outline.")]
        public float glowWidth = 0.1f;
        [Tooltip("How long the glow lasts on a brick cell, seconds, starting the instant the " +
                 "brick is revealed (see reveal timing on ApplyCellVisual) - independent of " +
                 "outlineFadeDuration, so it can be a short flourish distinct from the longer, " +
                 "always-on trailing outline.")]
        public float glowDuration = 0.25f;
        [Tooltip("Shapes the glow's fade-out over glowDuration - x=0 right as the brick is " +
                 "revealed (fully opaque), x=1 once fully faded. Defaults to a plain linear fade.")]
        public AnimationCurve glowFadeCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        private const int MinPoolSize = 64;

        private class WipeCell
        {
            public Transform xform;
            public MeshFilter meshFilter;
            public MeshRenderer renderer;
            public MaterialPropertyBlock propBlock;
            public LineRenderer outline;
            public MaterialPropertyBlock outlinePropBlock;
            public LineRenderer glow;
            public MaterialPropertyBlock glowPropBlock;
            public HexCoordinate coord;
            public float startTime;
            public bool revealApplied;
            public bool hasGlow;
        }

        // Given a coordinate, perform whatever side effect this sweep needs (spawn a brick /
        // remove one) - the only thing that differs between PlayBuildIn and PlayTearDown; the
        // per-cell flash animation itself (see ApplyCellVisual) is identical either way. Returns
        // true if a brick was actually placed/removed at that cell, which is what triggers the
        // reveal glow - a swept cell with no brick never glows.
        private delegate bool RevealCallback(HexCoordinate coord);

        private readonly Queue<WipeCell> _freeCells = new Queue<WipeCell>();
        private int _totalCellsCreated;
        private Material _defaultMaterial;
        private Material _defaultOutlineMaterial;
        private Material _defaultGlowMaterial;
        private Mesh _hexMesh;
        private Vector3[] _hexOutlinePositions;
        private bool _sweepRunning;
        private PolarGridSettings _sweepSettings;

        /// <summary>Lazily builds (and keeps reusing/updating) a small auxiliary PolarGridSettings
        /// representing the sweep's own hex tiling - independent of the level's real brick hexSize
        /// (see sweepHexSize) so a full-screen sweep over a dense, small-hex level doesn't have to
        /// tile anywhere near as many cells. hexGap is always 0 here since this is a transient
        /// overlay, not a real brick, so it never needs the brick grid's own visual gap. Only
        /// hexSize is ever read off this instance (see EnumerateCoordinatesInRect/HexToWorld/
        /// WorldToHex) - outerWallRadius is irrelevant to those, so it's never set.</summary>
        private PolarGridSettings GetSweepSettings(PolarGridSettings levelSettings)
        {
            float effectiveHexSize = sweepHexSize > 0f ? sweepHexSize : levelSettings.hexSize;
            if (_sweepSettings == null) _sweepSettings = ScriptableObject.CreateInstance<PolarGridSettings>();
            _sweepSettings.hexSize = effectiveHexSize;
            _sweepSettings.hexGap = 0f;
            return _sweepSettings;
        }

        public IEnumerator PlayBuildIn(LevelSO level)
        {
            if (brickGridManager == null || level == null || level.gridSettings == null) yield break;

            var settings = level.gridSettings;
            // Matches BuildLevel's own assignment - other code (e.g. LevelManager's fallback when
            // its `levels` array is empty) reads BrickGridManager.level expecting it to reflect
            // whichever level is currently active.
            brickGridManager.level = level;
            brickGridManager.PrepareSharedGeometry(settings);

            var sweepSettings = GetSweepSettings(settings);

            // Grouped by the sweep's own (typically coarser) cell, not the level's fine brick grid
            // - when sweepHexSize is larger than the level's hexSize, several bricks can share one
            // sweep cell, and they all pop in together the instant that cell's own flash reveals it.
            var placementsByCell = new Dictionary<HexCoordinate, List<(HexCoordinate coord, BrickTypeSO type)>>();
            foreach (var (coord, type) in level.GetPlacements())
            {
                if (!settings.IsValidCoordinate(coord)) continue;
                HexCoordinate sweepCell = sweepSettings.WorldToHex(settings.HexToWorld(coord));
                if (!placementsByCell.TryGetValue(sweepCell, out var list))
                {
                    list = new List<(HexCoordinate, BrickTypeSO)>();
                    placementsByCell[sweepCell] = list;
                }
                list.Add((coord, type));
            }

            yield return RunSweep(sweepSettings, sweepCell =>
            {
                if (!placementsByCell.TryGetValue(sweepCell, out var list)) return false;
                foreach (var (coord, type) in list)
                    brickGridManager.SpawnBrickAt(settings, coord, type);
                return true;
            });

            // Unlike BuildLevel (which snapshots this itself), progressive per-cell spawning via
            // SpawnBrickAt above never touches InitialDestructibleCount - do it here instead, once
            // every placement has actually been spawned.
            brickGridManager.SnapshotInitialDestructibleCount();
        }

        public IEnumerator PlayTearDown()
        {
            if (brickGridManager == null || brickGridManager.level == null || brickGridManager.level.gridSettings == null)
                yield break;

            var settings = brickGridManager.level.gridSettings;
            var sweepSettings = GetSweepSettings(settings);

            // Same coarser-cell grouping as PlayBuildIn, so a sweep cell that covers several
            // bricks removes all of them together the instant that cell is revealed.
            var existingByCell = new Dictionary<HexCoordinate, List<HexCoordinate>>();
            foreach (var coord in brickGridManager.GetActiveBrickCoordinates())
            {
                HexCoordinate sweepCell = sweepSettings.WorldToHex(settings.HexToWorld(coord));
                if (!existingByCell.TryGetValue(sweepCell, out var list))
                {
                    list = new List<HexCoordinate>();
                    existingByCell[sweepCell] = list;
                }
                list.Add(coord);
            }

            yield return RunSweep(sweepSettings, sweepCell =>
            {
                if (!existingByCell.TryGetValue(sweepCell, out var list)) return false;
                foreach (var coord in list)
                    brickGridManager.RemoveBrickQuietly(coord);
                return true;
            });

            // Safety net: quietly clear anything left over outside the swept screen rect (e.g. on
            // an unusual aspect ratio, or a brick that was somehow never enumerated) so a level
            // transition can never leave a brick behind.
            brickGridManager.ClearAllBricksQuietly();
        }

        private IEnumerator RunSweep(PolarGridSettings settings, RevealCallback reveal)
        {
            if (_sweepRunning)
            {
                Debug.LogWarning("HexWipeTransition: a sweep is already running - ignoring overlapping call.", this);
                yield break;
            }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("HexWipeTransition: no camera available, skipping sweep.", this);
                yield break;
            }

            _sweepRunning = true;
            audioManager?.PlayWipeTransition();

            GetCameraRect(cam, out Vector2 rectMin, out Vector2 rectMax);

            var cells = new List<(HexCoordinate coord, Vector2 world, float startTime)>();
            float maxStart = Mathf.Max(0f, totalSweepDuration - perHexFlashDuration);
            foreach (var coord in settings.EnumerateCoordinatesInRect(rectMin, rectMax))
            {
                Vector2 world = settings.HexToWorld(coord);
                float fraction = DiagonalStartFraction(world, rectMin, rectMax);
                float baseStart = fraction * maxStart;

                // Jittered independently per hex (not per row/band) - scatters the flash order
                // within roughly the same diagonal position while the overall top-left-to-
                // bottom-right progression (baseStart itself) stays intact, clamped so no hex
                // starts before the sweep begins or later than the last un-jittered hex would.
                float jitter = startTimeJitter > 0f ? Random.Range(-startTimeJitter, startTimeJitter) : 0f;
                float startTime = Mathf.Clamp(baseStart + jitter, 0f, maxStart);

                cells.Add((coord, world, startTime));
            }
            cells.Sort((a, b) => a.startTime.CompareTo(b.startTime));

            EnsureMesh(settings);
            EnsurePool(cells.Count);

            yield return DriveSweep(cells, reveal);

            _sweepRunning = false;
        }

        private IEnumerator DriveSweep(List<(HexCoordinate coord, Vector2 world, float startTime)> cells, RevealCallback reveal)
        {
            int nextIndex = 0;
            var activeCells = new List<WipeCell>();
            float elapsed = 0f;

            while (nextIndex < cells.Count || activeCells.Count > 0)
            {
                while (nextIndex < cells.Count && cells[nextIndex].startTime <= elapsed)
                {
                    var (coord, world, startTime) = cells[nextIndex];
                    WipeCell cell = RentCell();
                    cell.xform.position = new Vector3(world.x, world.y, -0.1f);
                    cell.coord = coord;
                    cell.startTime = startTime;
                    cell.revealApplied = false;
                    cell.hasGlow = false;
                    activeCells.Add(cell);
                    nextIndex++;
                }

                for (int i = activeCells.Count - 1; i >= 0; i--)
                {
                    WipeCell cell = activeCells[i];
                    float localElapsed = elapsed - cell.startTime;
                    float progress = perHexFlashDuration > 0f
                        ? Mathf.Clamp01(localElapsed / perHexFlashDuration)
                        : 1f;
                    float outlineProgress = outlineFadeDuration > 0f
                        ? Mathf.Clamp01(localElapsed / outlineFadeDuration)
                        : 1f;

                    // Reveal happens once, at the midpoint of this cell's own flash, while the
                    // flash is still opaque and covering it - masking the brick popping in/out
                    // underneath regardless of which callback is wired in for this sweep. Only a
                    // cell that actually had a brick placed/removed (reveal returns true) glows.
                    if (!cell.revealApplied && progress >= 0.5f)
                    {
                        cell.hasGlow = reveal(cell.coord);
                        cell.revealApplied = true;
                    }

                    // Starts counting from the same instant the reveal fired (negative, and
                    // therefore clamped to 0, beforehand) - a short, independent timeline on top
                    // of the always-on outline fade above.
                    float glowProgress = 1f;
                    if (cell.hasGlow)
                    {
                        float glowElapsed = localElapsed - perHexFlashDuration * 0.5f;
                        glowProgress = glowDuration > 0f ? Mathf.Clamp01(glowElapsed / glowDuration) : 1f;
                    }

                    ApplyCellVisual(cell, progress, outlineProgress, glowProgress);

                    // A cell stays rented until the (usually quick) flash, the (usually longer)
                    // outline fade, AND any brick-reveal glow have all fully finished, so nothing
                    // gets cut short by the GameObject being recycled early.
                    if (progress >= 1f && outlineProgress >= 1f && (!cell.hasGlow || glowProgress >= 1f))
                    {
                        ReturnCell(cell);
                        activeCells.RemoveAt(i);
                    }
                }

                yield return null;
                // Unscaled so a pause (Time.timeScale = 0) mid-sweep can't desync the wave from
                // real time or strand cells mid-flash - matches DissolveEffect's own reasoning.
                elapsed += Time.unscaledDeltaTime;
            }
        }

        /// <summary>Ramps _DissolveProgress 0-&gt;1 for a custom shader (e.g. an assigned Dissolve
        /// material) to consume directly. The default runtime material has no such shader logic,
        /// so it's driven the simple way too: fully opaque white for the first half, then fading
        /// alpha to 0 for the second - shaped by fadeCurve/outlineFadeCurve rather than a flat
        /// linear ramp, so the fade-out can ease independently of the sweep's own constant pace.</summary>
        private void ApplyCellVisual(WipeCell cell, float progress, float outlineProgress, float glowProgress)
        {
            // 0 right as fading starts (midpoint of the flash), 1 once it's fully faded - fed
            // through fadeCurve rather than used directly, so the fade-out's shape is whatever
            // that curve says instead of a flat linear ramp.
            float fadeT = Mathf.Clamp01((progress - 0.5f) * 2f);
            float alpha = progress < 0.5f ? 1f : fadeCurve.Evaluate(fadeT);
            Color color = new Color(1f, 1f, 1f, alpha);

            cell.propBlock.SetFloat("_DissolveProgress", progress);
            cell.propBlock.SetColor("_Color", color);
            cell.propBlock.SetColor("_BaseColor", color);
            cell.renderer.SetPropertyBlock(cell.propBlock);

            // The outline traces which hexes the wave has already passed over - it appears
            // instantly alongside the flash, then lingers and fades on its own, independent
            // timeline (see outlineFadeDuration) and its own easing curve.
            float outlineAlpha = outlineColor.a * outlineFadeCurve.Evaluate(outlineProgress);
            Color outlineCol = new Color(outlineColor.r, outlineColor.g, outlineColor.b, outlineAlpha);
            cell.outlinePropBlock.SetFloat("_DissolveProgress", outlineProgress);
            cell.outlinePropBlock.SetColor("_Color", outlineCol);
            cell.outlinePropBlock.SetColor("_BaseColor", outlineCol);
            cell.outline.SetPropertyBlock(cell.outlinePropBlock);

            // The glow only ever renders on cells that actually got/had a brick (see
            // RevealCallback) - a swept empty cell's glow LineRenderer just stays disabled.
            cell.glow.enabled = cell.hasGlow;
            if (cell.hasGlow)
            {
                float glowAlpha = glowColor.a * glowFadeCurve.Evaluate(glowProgress);
                Color glowCol = new Color(glowColor.r, glowColor.g, glowColor.b, glowAlpha);
                cell.glowPropBlock.SetFloat("_DissolveProgress", glowProgress);
                cell.glowPropBlock.SetColor("_Color", glowCol);
                cell.glowPropBlock.SetColor("_BaseColor", glowCol);
                cell.glow.SetPropertyBlock(cell.glowPropBlock);
            }
        }

        /// <summary>0 at the rect's top-left corner, 1 at its bottom-right corner, linearly
        /// interpolated along that diagonal - each hex's flash start time is this fraction times
        /// the sweep's available start-time budget.</summary>
        private static float DiagonalStartFraction(Vector2 world, Vector2 rectMin, Vector2 rectMax)
        {
            Vector2 topLeft = new Vector2(rectMin.x, rectMax.y);
            Vector2 bottomRight = new Vector2(rectMax.x, rectMin.y);
            Vector2 diagonal = bottomRight - topLeft;
            float sqrLen = Mathf.Max(diagonal.sqrMagnitude, 0.0001f);
            float t = Vector2.Dot(world - topLeft, diagonal) / sqrLen;
            return Mathf.Clamp01(t);
        }

        /// <summary>The world-space rect the camera currently sees - read live from the Camera
        /// component (orthographic size + aspect) rather than any hardcoded value, so this stays
        /// correct across resolutions/aspect ratios. Assumes an orthographic camera, matching the
        /// project's actual Main Camera setup.</summary>
        private static void GetCameraRect(Camera cam, out Vector2 min, out Vector2 max)
        {
            float halfHeight = cam.orthographicSize;
            float halfWidth = halfHeight * cam.aspect;
            Vector2 center = cam.transform.position;
            min = center - new Vector2(halfWidth, halfHeight);
            max = center + new Vector2(halfWidth, halfHeight);
        }

        private void EnsureMesh(PolarGridSettings settings)
        {
            // Full hexSize (no hexGap shrink) - this is a transient full-screen overlay, not a
            // real brick, so it doesn't need the brick grid's own visual gap.
            _hexMesh = PolarMeshUtility.BuildHexMesh(settings.hexSize);

            Vector2[] outline = PolarMeshUtility.BuildHexOutlinePoints(settings.hexSize);
            _hexOutlinePositions = new Vector3[outline.Length];
            for (int i = 0; i < outline.Length; i++)
                _hexOutlinePositions[i] = new Vector3(outline[i].x, outline[i].y, 0f);
        }

        private Material GetDefaultMaterial()
        {
            if (_defaultMaterial == null)
                _defaultMaterial = PolarMeshUtility.CreateTransparentUnlitMaterial(Color.white);
            return _defaultMaterial;
        }

        private Material GetDefaultOutlineMaterial()
        {
            if (_defaultOutlineMaterial == null)
                _defaultOutlineMaterial = PolarMeshUtility.CreateTransparentUnlitMaterial(Color.white);
            return _defaultOutlineMaterial;
        }

        private Material GetDefaultGlowMaterial()
        {
            if (_defaultGlowMaterial == null)
                _defaultGlowMaterial = PolarMeshUtility.CreateTransparentUnlitMaterial(Color.white);
            return _defaultGlowMaterial;
        }

        private void EnsurePool(int totalCellCount)
        {
            int estimatedConcurrent = totalSweepDuration > 0f
                ? Mathf.CeilToInt(totalCellCount * (perHexFlashDuration / totalSweepDuration))
                : totalCellCount;
            int targetSize = Mathf.Max(MinPoolSize, Mathf.CeilToInt(estimatedConcurrent * poolSizeSafetyFactor));

            while (_totalCellsCreated < targetSize)
            {
                _freeCells.Enqueue(CreateCell());
                _totalCellsCreated++;
            }
        }

        private WipeCell CreateCell()
        {
            var go = new GameObject("HexWipeCell");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var outline = go.AddComponent<LineRenderer>();
            outline.useWorldSpace = false; // Follows this cell's own transform, like the fill mesh.
            outline.loop = true;
            outline.numCapVertices = 2;
            outline.numCornerVertices = 2;

            // A GameObject can only ever host one LineRenderer, so the glow needs its own child
            // GameObject rather than sitting alongside outline on go itself - SetParent(..., false)
            // keeps it at the parent's local origin, so its local-space positions line up exactly
            // the same as outline's.
            var glowGO = new GameObject("Glow");
            glowGO.transform.SetParent(go.transform, false);
            var glow = glowGO.AddComponent<LineRenderer>();
            glow.useWorldSpace = false;
            glow.loop = true;
            glow.numCapVertices = 2;
            glow.numCornerVertices = 2;

            var cell = new WipeCell
            {
                xform = go.transform,
                meshFilter = go.AddComponent<MeshFilter>(),
                renderer = go.AddComponent<MeshRenderer>(),
                propBlock = new MaterialPropertyBlock(),
                outline = outline,
                outlinePropBlock = new MaterialPropertyBlock(),
                glow = glow,
                glowPropBlock = new MaterialPropertyBlock(),
            };
            return cell;
        }

        /// <summary>Pulls a free cell from the pool (growing it if empty - a level transition
        /// should never silently drop a hex), assigns the current sweep's mesh/material, and
        /// activates its GameObject.</summary>
        private WipeCell RentCell()
        {
            WipeCell cell = _freeCells.Count > 0 ? _freeCells.Dequeue() : GrowPoolByOne();

            cell.meshFilter.mesh = _hexMesh;
            cell.renderer.sharedMaterial = flashMaterialOverride != null ? flashMaterialOverride : GetDefaultMaterial();

            cell.outline.positionCount = _hexOutlinePositions.Length;
            cell.outline.SetPositions(_hexOutlinePositions);
            cell.outline.widthMultiplier = outlineWidth;
            cell.outline.sharedMaterial = outlineMaterialOverride != null ? outlineMaterialOverride : GetDefaultOutlineMaterial();

            cell.glow.positionCount = _hexOutlinePositions.Length;
            cell.glow.SetPositions(_hexOutlinePositions);
            cell.glow.widthMultiplier = glowWidth;
            cell.glow.sharedMaterial = glowMaterialOverride != null ? glowMaterialOverride : GetDefaultGlowMaterial();
            // Only enabled once DriveSweep's reveal callback confirms this cell actually has a
            // brick - left off here so a freshly-rented cell never shows a stale glow.
            cell.glow.enabled = false;

            cell.xform.gameObject.SetActive(true);
            return cell;
        }

        private void ReturnCell(WipeCell cell)
        {
            cell.xform.gameObject.SetActive(false);
            _freeCells.Enqueue(cell);
        }

        private WipeCell GrowPoolByOne()
        {
            _totalCellsCreated++;
            return CreateCell();
        }
    }
}
