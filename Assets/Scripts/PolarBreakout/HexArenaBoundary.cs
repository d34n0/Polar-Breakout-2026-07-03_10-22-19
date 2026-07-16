using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Builds two candidate walls around the play-area boundary - a jagged EdgeCollider2D loop
    /// tracing the outward-facing edges of every boundary hex, and a smooth EdgeCollider2D (a
    /// circle, or a flat-top hexagon when the grid settings' arena shape is Hexagon) sized to
    /// contain every boundary hex corner - and switches which one is actually active (both as the
    /// real physics wall and as the visible LineRenderer) via <see cref="activeBoundary"/>.
    /// BrickGridManager calls BuildBoundary at the end of every BuildLevel, so both walls always
    /// match whatever hex layout was just built.
    /// </summary>
    [RequireComponent(typeof(EdgeCollider2D))]
    [RequireComponent(typeof(LineRenderer))]
    public class HexArenaBoundary : MonoBehaviour
    {
        public enum BoundaryShape { Jagged, Smooth }

        [Tooltip("Which wall is actually active - the real physics collider and the visible line. The other shape is built but disabled.")]
        public BoundaryShape activeBoundary = BoundaryShape.Jagged;

        [Header("Jagged Hex Boundary")]
        [Tooltip("Visual width of the boundary line, world units.")]
        public float lineWidth = 0.08f;
        public Color lineColor = Color.yellow;
        [Tooltip("Leave unset for a plain colored unlit line - assign a custom material to override.")]
        public Material materialOverride;

        [Header("Smooth Outer Boundary")]
        [Tooltip("Extra clearance beyond the farthest boundary hex corner, world units.")]
        public float outerCirclePadding = 0.15f;
        [Tooltip("Visual width of the outer boundary line, world units.")]
        public float outerCircleWidth = 0.06f;
        [Tooltip("Number of segments approximating the circle - higher looks smoother. Ignored when the grid settings' arena shape is Hexagon, which always draws a flat 6-sided outline.")]
        public int outerCircleSegments = 96;
        public Color outerCircleColor = Color.yellow;
        [Tooltip("Leave unset for a plain colored unlit line - assign a custom material to override.")]
        public Material outerCircleMaterialOverride;

        [Header("Interior Fill")]
        [Tooltip("When enabled, builds a solid-colored mesh filling the arena's interior - the " +
                 "same circle/hexagon silhouette as the smooth outer boundary above (see " +
                 "BuildSmoothOuterBoundary) - so the background art only shows outside the play " +
                 "field instead of bleeding into it and competing with the bricks. Regenerated " +
                 "every BuildBoundary call alongside the boundary lines themselves, so it always " +
                 "matches whatever grid settings/arena shape the level just built.")]
        public bool buildInteriorFill = true;
        [Tooltip("Color (including alpha) of the interior fill - darker/more opaque reads as more " +
                 "focus on the play area, since it dims the background art showing through from " +
                 "behind it.")]
        public Color interiorFillColor = new Color(0f, 0f, 0f, 0.55f);
        [Tooltip("Leave unset for a plain colored unlit fill (a fresh transparent material - " +
                 "shared instances would all end up showing whichever level built last, since " +
                 "PolarMeshUtility.GetProceduralUnlitMaterial's opaque shared instance isn't " +
                 "suited to per-level alpha) - assign a custom material to override.")]
        public Material interiorFillMaterialOverride;
        [Tooltip("Local Z depth the interior fill renders at, relative to this component's own " +
                 "transform - only needs to clear every brick/paddle/ball (which render at z=0) by " +
                 "enough to win the depth test, NOT reach all the way back to the background art's " +
                 "own z. The main camera is perspective, not orthographic, so a mesh built with the " +
                 "same world-space radius as the boundary line (z=-0.05) projects visibly SMALLER " +
                 "the farther back it sits - keep this small (a few hundredths) or the fill will no " +
                 "longer line up with the boundary line/wall.")]
        public float interiorFillDepth = 0.05f;
        [Tooltip("Renderer.sortingOrder for the interior fill. It and the background art (BG, a " +
                 "SpriteRenderer using the built-in Sprites/Default shader) are both alpha-blended, " +
                 "so neither writes to the depth buffer - Unity draws transparent renderers in " +
                 "sortingOrder order rather than depth-testing them against each other, unlike the " +
                 "opaque bricks/paddle/ball (which DO depth-test normally regardless of this " +
                 "value). Must sit above BG's own sortingOrder (-100) so the fill draws over it, " +
                 "and below the default 0 every other renderer uses (explosion/muzzle-flash " +
                 "particles included) so THEY draw over the fill instead of being masked by it.")]
        public int interiorFillSortingOrder = -50;
        [Tooltip("How long the interior fill takes to fade out when Hide() is called, seconds - a " +
                 "smooth fade reads better than an instant cut during the level-transition wipe " +
                 "(see LevelManager, which calls Hide() right before HexWipeTransition." +
                 "PlayTearDown()). Only applies to the default generated fill material, not " +
                 "interiorFillMaterialOverride - an override is used as-is (the same convention as " +
                 "every other override in this class), so Hide() just disables it instantly " +
                 "instead of guessing at how to fade an arbitrary custom material. Show()/a fresh " +
                 "BuildBoundary rebuild snap it back to full opacity immediately rather than also " +
                 "fading in, since by the time either runs the new level's own build-in sweep has " +
                 "already finished revealing everything else.")]
        public float interiorFillFadeDuration = 0.6f;

        private EdgeCollider2D _collider;
        private LineRenderer _lineRenderer;
        private LineRenderer _outerCircleLine;
        private EdgeCollider2D _outerCircleCollider;
        private MeshFilter _interiorFillMeshFilter;
        private MeshRenderer _interiorFillMeshRenderer;
        private Material _interiorFillMaterial;
        private Coroutine _interiorFillFadeRoutine;

        private void OnValidate() => ApplyActiveBoundary();

        public void BuildBoundary(PolarGridSettings settings)
        {
            _collider ??= GetComponent<EdgeCollider2D>();
            _lineRenderer ??= GetComponent<LineRenderer>();
            ConfigureLineRenderer(_lineRenderer, materialOverride, lineColor, lineWidth);

            // Deliberately the full hexSize, NOT hexSize-hexGap like the brick mesh/collider -
            // adjacent boundary hexes' outward corners only coincide (letting the loop-stitcher
            // below find shared endpoints) at their true, ungapped corners. Using the gapped
            // radius here would leave every hex's boundary edge floating in isolation, matching
            // its own hexGap-sized shrink but never touching its neighbor's edge.
            float hexRadius = settings.hexSize;
            var segments = new List<(Vector2 a, Vector2 b)>();
            float maxCornerDistance = 0f;

            foreach (var coord in settings.EnumerateValidCoordinates())
            {
                if (!settings.IsBoundaryHex(coord)) continue;

                Vector2 center = settings.HexToWorld(coord);

                // Every corner of a boundary hex, not just the ones on a wall-facing edge, is a
                // candidate for the farthest point the outer circle needs to clear - a hex can
                // have a non-wall corner poke out just as far depending on its orientation.
                for (int i = 0; i < 6; i++)
                    maxCornerDistance = Mathf.Max(maxCornerDistance, PolarMeshUtility.HexCorner(center, hexRadius, i).magnitude);

                for (int dir = 0; dir < 6; dir++)
                {
                    if (settings.IsValidCoordinate(coord.Neighbor(dir))) continue;

                    // Direction dir's world-space angle is -60*dir degrees (from HexToWorld's
                    // neighbor-offset deltas); corner k sits at angle 60k-30. The edge whose
                    // midpoint angle matches direction dir's angle is bounded by corners
                    // ((-dir) mod 6) and ((1-dir) mod 6) - NOT the naive {dir, dir+1} pattern,
                    // which only coincidentally lines up for dir 0. Verified by direct trig.
                    int cornerA = Mod6(-dir);
                    int cornerB = Mod6(1 - dir);
                    Vector2 a = PolarMeshUtility.HexCorner(center, hexRadius, cornerA);
                    Vector2 b = PolarMeshUtility.HexCorner(center, hexRadius, cornerB);
                    segments.Add((a, b));
                }
            }

            foreach (Transform child in transform)
                if (child.name == "HexBoundaryLoop") Destroy(child.gameObject);

            float outerRadius = maxCornerDistance + outerCirclePadding;
            (_outerCircleLine, _outerCircleCollider) = GetOrCreateOuterCircleObjects();
            BuildSmoothOuterBoundary(_outerCircleLine, _outerCircleCollider, settings, outerRadius);
            BuildInteriorFill(settings, outerRadius);

            var loops = StitchLoops(segments);

            if (loops.Count == 0)
            {
                Debug.LogWarning("HexArenaBoundary: no boundary loop found - is the grid empty?", this);
                _lineRenderer.positionCount = 0;
            }
            else
            {
                _collider.points = ClosedLoopArray(loops[0]);
                SetLoopPositions(_lineRenderer, loops[0]);

                for (int i = 1; i < loops.Count; i++)
                {
                    var go = new GameObject("HexBoundaryLoop");
                    go.transform.SetParent(transform, false);
                    var edge = go.AddComponent<EdgeCollider2D>();
                    edge.points = ClosedLoopArray(loops[i]);

                    var extraLine = go.AddComponent<LineRenderer>();
                    ConfigureLineRenderer(extraLine, materialOverride, lineColor, lineWidth);
                    SetLoopPositions(extraLine, loops[i]);
                }
            }

            ApplyActiveBoundary();
        }

        /// <summary>Enables whichever shape is selected (both its collider and its line) and
        /// disables the other - safe to call before BuildBoundary has ever run (e.g. from
        /// OnValidate right after adding the component), since every reference is null-checked.</summary>
        private void ApplyActiveBoundary()
        {
            bool jaggedActive = activeBoundary != BoundaryShape.Smooth;

            if (_collider != null) _collider.enabled = jaggedActive;
            if (_lineRenderer != null) _lineRenderer.enabled = jaggedActive;
            if (_outerCircleCollider != null) _outerCircleCollider.enabled = !jaggedActive;
            if (_outerCircleLine != null) _outerCircleLine.enabled = !jaggedActive;
        }

        /// <summary>Disables both boundary shapes' colliders and lines regardless of
        /// activeBoundary - used by HexWipeTransition to hide the boundary for the whole
        /// transition span. Safe since the boundary's geometry depends only on grid settings, not
        /// brick placements, so hiding never needs a rebuild. Also fades the interior fill out
        /// (see interiorFillFadeDuration) rather than cutting it instantly, since unlike the thin
        /// boundary line it covers most of the screen and an instant cut there would be jarring.</summary>
        public void Hide()
        {
            if (_collider != null) _collider.enabled = false;
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_outerCircleCollider != null) _outerCircleCollider.enabled = false;
            if (_outerCircleLine != null) _outerCircleLine.enabled = false;

            if (_interiorFillFadeRoutine != null) StopCoroutine(_interiorFillFadeRoutine);

            if (_interiorFillMeshRenderer == null) return;
            if (interiorFillMaterialOverride != null || !gameObject.activeInHierarchy)
            {
                // Can't safely fade an arbitrary override material's alpha (see the field's own
                // tooltip), and can't start a coroutine on an inactive GameObject either - both
                // fall back to the old instant cut.
                _interiorFillMeshRenderer.enabled = false;
                return;
            }

            _interiorFillFadeRoutine = StartCoroutine(FadeInteriorFillOut());
        }

        /// <summary>Restores whichever shape activeBoundary currently points to - the counterpart
        /// to Hide(), without a full geometry rebuild.</summary>
        public void Show() => ApplyActiveBoundary();

        private IEnumerator FadeInteriorFillOut()
        {
            float startAlpha = interiorFillColor.a;
            float elapsed = 0f;
            while (elapsed < interiorFillFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = interiorFillFadeDuration > 0f ? Mathf.Clamp01(elapsed / interiorFillFadeDuration) : 1f;
                SetInteriorFillAlpha(Mathf.Lerp(startAlpha, 0f, t));
                yield return null;
            }

            SetInteriorFillAlpha(0f);
            _interiorFillMeshRenderer.enabled = false;
            _interiorFillFadeRoutine = null;
        }

        private void SetInteriorFillAlpha(float alpha)
        {
            if (_interiorFillMaterial == null) return;
            Color color = interiorFillColor;
            color.a = alpha;
            _interiorFillMaterial.SetColor("_Color", color);
            _interiorFillMaterial.SetColor("_BaseColor", color);
        }

        private (LineRenderer line, EdgeCollider2D collider) GetOrCreateOuterCircleObjects()
        {
            var existing = transform.Find("HexOuterCircleBorder");
            if (existing != null)
                return (existing.GetComponent<LineRenderer>(), existing.GetComponent<EdgeCollider2D>());

            var go = new GameObject("HexOuterCircleBorder");
            go.transform.SetParent(transform, false);
            return (go.AddComponent<LineRenderer>(), go.AddComponent<EdgeCollider2D>());
        }

        /// <summary>Draws a smooth outline of the given circumradius, centered on this component's
        /// own position, and assigns the same points (closed) to its EdgeCollider2D - an
        /// alternative real physics wall to the jagged hex loop, selectable via activeBoundary.
        /// A perfect circle when settings.arenaShape is Circle; a flat-top regular hexagon
        /// (matching PolarGridSettings.IsWithinHexagon's orientation) when it's Hexagon.</summary>
        private void BuildSmoothOuterBoundary(LineRenderer line, EdgeCollider2D collider, PolarGridSettings settings, float radius)
        {
            ConfigureLineRenderer(line, outerCircleMaterialOverride, outerCircleColor, outerCircleWidth);

            int segments = settings.arenaShape == PolarGridSettings.ArenaShape.Hexagon
                ? 6
                : Mathf.Max(3, outerCircleSegments);
            var positions = new Vector3[segments];
            var colliderPoints = new Vector2[segments + 1];
            for (int i = 0; i < segments; i++)
            {
                float angleRad = i * Mathf.PI * 2f / segments;
                Vector2 point = new Vector2(Mathf.Cos(angleRad) * radius, Mathf.Sin(angleRad) * radius);
                positions[i] = new Vector3(point.x, point.y, -0.05f);
                colliderPoints[i] = point;
            }
            colliderPoints[segments] = colliderPoints[0];

            line.positionCount = positions.Length;
            line.SetPositions(positions);
            collider.points = colliderPoints;
        }

        /// <summary>Builds (or updates) a flat-shaded fan mesh filling the same circle/hexagon
        /// silhouette as BuildSmoothOuterBoundary - reuses PolarMeshUtility.BuildFilledCircleMesh
        /// directly, since passing segments=6 produces the identical flat-top hexagon
        /// BuildSmoothOuterBoundary's own hex case draws (same angle formula), so the fill and the
        /// line it sits under always agree on shape without duplicating the math. Local-positioned
        /// at interiorFillDepth along Z (not baked into the mesh itself) so it layers behind
        /// gameplay (z=0) but in front of background art sitting further back.</summary>
        private void BuildInteriorFill(PolarGridSettings settings, float radius)
        {
            // A rebuild always wins over an in-flight Hide() fade - otherwise that coroutine
            // would finish moments later and disable the fresh fill BuildBoundary just set up.
            if (_interiorFillFadeRoutine != null)
            {
                StopCoroutine(_interiorFillFadeRoutine);
                _interiorFillFadeRoutine = null;
            }

            if (!buildInteriorFill)
            {
                if (_interiorFillMeshRenderer != null) _interiorFillMeshRenderer.enabled = false;
                return;
            }

            if (_interiorFillMeshFilter == null || _interiorFillMeshRenderer == null)
            {
                var go = new GameObject("InteriorFill");
                go.transform.SetParent(transform, false);
                _interiorFillMeshFilter = go.AddComponent<MeshFilter>();
                _interiorFillMeshRenderer = go.AddComponent<MeshRenderer>();
                _interiorFillMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _interiorFillMeshRenderer.receiveShadows = false;
            }

            _interiorFillMeshRenderer.sortingOrder = interiorFillSortingOrder;
            _interiorFillMeshFilter.transform.localPosition = new Vector3(0f, 0f, interiorFillDepth);

            int segments = settings.arenaShape == PolarGridSettings.ArenaShape.Hexagon
                ? 6
                : Mathf.Max(3, outerCircleSegments);
            _interiorFillMeshFilter.mesh = PolarMeshUtility.BuildFilledCircleMesh(radius, segments);

            if (interiorFillMaterialOverride != null)
            {
                _interiorFillMeshRenderer.sharedMaterial = interiorFillMaterialOverride;
            }
            else
            {
                // A fresh transparent material per boundary, not the shared opaque procedural
                // instance every other unlit line/quad in this project reuses - that single shared
                // instance can only ever show one color/alpha at a time, and this fill specifically
                // needs its own alpha-blended surface type to darken (not replace) whatever's
                // behind it.
                if (_interiorFillMaterial == null)
                    _interiorFillMaterial = PolarMeshUtility.CreateTransparentUnlitMaterial(interiorFillColor);
                else
                {
                    _interiorFillMaterial.SetColor("_Color", interiorFillColor);
                    _interiorFillMaterial.SetColor("_BaseColor", interiorFillColor);
                }
                _interiorFillMeshRenderer.sharedMaterial = _interiorFillMaterial;
            }

            _interiorFillMeshRenderer.enabled = true;
        }

        /// <summary>Sets up a LineRenderer as a closed, world-space, unlit colored loop (unless
        /// an override material is assigned) - shared by the main boundary loop, any extra
        /// degenerate loops, and the cosmetic outer circle.</summary>
        private static void ConfigureLineRenderer(LineRenderer line, Material overrideMaterial, Color color, float width)
        {
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;

            if (overrideMaterial != null)
            {
                line.sharedMaterial = overrideMaterial;
                return;
            }

            line.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();
            var propBlock = new MaterialPropertyBlock();
            propBlock.SetColor("_Color", color);
            propBlock.SetColor("_BaseColor", color);
            line.SetPropertyBlock(propBlock);
        }

        /// <summary>Nudges the line slightly toward the camera (-Z) so it draws in front of the
        /// brick meshes/paddle sitting at z=0, rather than z-fighting with them.</summary>
        private static void SetLoopPositions(LineRenderer line, List<Vector2> loop)
        {
            var positions = new Vector3[loop.Count];
            for (int i = 0; i < loop.Count; i++)
                positions[i] = new Vector3(loop[i].x, loop[i].y, -0.05f);
            line.positionCount = positions.Length;
            line.SetPositions(positions);
        }

        private static int Mod6(int value) => ((value % 6) + 6) % 6;

        private static Vector2[] ClosedLoopArray(List<Vector2> loop)
        {
            var array = new Vector2[loop.Count + 1];
            for (int i = 0; i < loop.Count; i++) array[i] = loop[i];
            array[loop.Count] = loop[0];
            return array;
        }

        /// <summary>Stitches unordered (a,b) boundary-edge segments into closed loops by matching
        /// shared endpoints (rounded to absorb float error). A simply-connected circle-clipped hex
        /// blob produces exactly one loop; anything extra is returned as additional loops rather
        /// than forced into the same one.</summary>
        private static List<List<Vector2>> StitchLoops(List<(Vector2 a, Vector2 b)> segments)
        {
            string Key(Vector2 p) => $"{Mathf.Round(p.x * 10000f)}:{Mathf.Round(p.y * 10000f)}";

            var pointByKey = new Dictionary<string, Vector2>();
            var neighborsByKey = new Dictionary<string, List<string>>();

            void Register(Vector2 p)
            {
                string key = Key(p);
                if (!pointByKey.ContainsKey(key))
                {
                    pointByKey[key] = p;
                    neighborsByKey[key] = new List<string>();
                }
            }

            foreach (var (a, b) in segments)
            {
                Register(a);
                Register(b);
                string keyA = Key(a);
                string keyB = Key(b);
                neighborsByKey[keyA].Add(keyB);
                neighborsByKey[keyB].Add(keyA);
            }

            var usedEdges = new HashSet<(string, string)>();
            var loops = new List<List<Vector2>>();

            foreach (var startKey in neighborsByKey.Keys)
            {
                foreach (var firstNeighborKey in neighborsByKey[startKey])
                {
                    if (usedEdges.Contains((startKey, firstNeighborKey))) continue;

                    var loopKeys = new List<string> { startKey };
                    usedEdges.Add((startKey, firstNeighborKey));
                    usedEdges.Add((firstNeighborKey, startKey));

                    string currentKey = firstNeighborKey;
                    while (currentKey != startKey)
                    {
                        loopKeys.Add(currentKey);

                        string nextKey = null;
                        foreach (var candidate in neighborsByKey[currentKey])
                        {
                            if (usedEdges.Contains((currentKey, candidate))) continue;
                            nextKey = candidate;
                            break;
                        }
                        if (nextKey == null) break; // dead end - shouldn't happen for a closed blob

                        usedEdges.Add((currentKey, nextKey));
                        usedEdges.Add((nextKey, currentKey));
                        currentKey = nextKey;
                    }

                    if (currentKey == startKey && loopKeys.Count >= 3)
                        loops.Add(loopKeys.ConvertAll(k => pointByKey[k]));
                }
            }

            return loops;
        }
    }
}
