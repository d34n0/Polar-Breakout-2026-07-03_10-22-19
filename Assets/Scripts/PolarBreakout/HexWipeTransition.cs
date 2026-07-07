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
    /// which cells the wave has already passed. Called from LevelManager.AdvanceToNextStage,
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

        private const int MinPoolSize = 64;

        private class WipeCell
        {
            public Transform xform;
            public MeshFilter meshFilter;
            public MeshRenderer renderer;
            public MaterialPropertyBlock propBlock;
            public LineRenderer outline;
            public MaterialPropertyBlock outlinePropBlock;
            public HexCoordinate coord;
            public float startTime;
            public bool revealApplied;
        }

        // Given a coordinate, perform whatever side effect this sweep needs (spawn a brick /
        // remove one) - the only thing that differs between PlayBuildIn and PlayTearDown; the
        // per-cell flash animation itself (see ApplyCellVisual) is identical either way.
        private delegate void RevealCallback(HexCoordinate coord);

        private readonly Queue<WipeCell> _freeCells = new Queue<WipeCell>();
        private int _totalCellsCreated;
        private Material _defaultMaterial;
        private Material _defaultOutlineMaterial;
        private Mesh _hexMesh;
        private Vector3[] _hexOutlinePositions;
        private bool _sweepRunning;

        public IEnumerator PlayBuildIn(LevelSO level)
        {
            if (brickGridManager == null || level == null || level.gridSettings == null) yield break;

            var settings = level.gridSettings;
            // Matches BuildLevel's own assignment - other code (e.g. LevelManager's fallback when
            // its `levels` array is empty) reads BrickGridManager.level expecting it to reflect
            // whichever level is currently active.
            brickGridManager.level = level;
            brickGridManager.PrepareSharedGeometry(settings);

            var placements = new Dictionary<HexCoordinate, BrickTypeSO>();
            foreach (var (coord, type) in level.GetPlacements())
                if (settings.IsValidCoordinate(coord)) placements[coord] = type;

            yield return RunSweep(settings, coord =>
            {
                if (placements.TryGetValue(coord, out var type))
                    brickGridManager.SpawnBrickAt(settings, coord, type);
            });
        }

        public IEnumerator PlayTearDown()
        {
            if (brickGridManager == null || brickGridManager.level == null || brickGridManager.level.gridSettings == null)
                yield break;

            var settings = brickGridManager.level.gridSettings;
            var existing = new HashSet<HexCoordinate>(brickGridManager.GetActiveBrickCoordinates());

            yield return RunSweep(settings, coord =>
            {
                if (existing.Contains(coord))
                    brickGridManager.RemoveBrickQuietly(coord);
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
                    // underneath regardless of which callback is wired in for this sweep.
                    if (!cell.revealApplied && progress >= 0.5f)
                    {
                        reveal(cell.coord);
                        cell.revealApplied = true;
                    }

                    ApplyCellVisual(cell, progress, outlineProgress);

                    // A cell stays rented until BOTH the (usually quick) flash and the (usually
                    // longer) outline fade have fully finished, so the outline can keep trailing
                    // behind the moving flash band without its GameObject being recycled early.
                    if (progress >= 1f && outlineProgress >= 1f)
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
        private void ApplyCellVisual(WipeCell cell, float progress, float outlineProgress)
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

            var cell = new WipeCell
            {
                xform = go.transform,
                meshFilter = go.AddComponent<MeshFilter>(),
                renderer = go.AddComponent<MeshRenderer>(),
                propBlock = new MaterialPropertyBlock(),
                outline = outline,
                outlinePropBlock = new MaterialPropertyBlock(),
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
