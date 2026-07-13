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

        private EdgeCollider2D _collider;
        private LineRenderer _lineRenderer;
        private LineRenderer _outerCircleLine;
        private EdgeCollider2D _outerCircleCollider;

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

            (_outerCircleLine, _outerCircleCollider) = GetOrCreateOuterCircleObjects();
            BuildSmoothOuterBoundary(_outerCircleLine, _outerCircleCollider, settings, maxCornerDistance + outerCirclePadding);

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
        /// brick placements, so hiding never needs a rebuild.</summary>
        public void Hide()
        {
            if (_collider != null) _collider.enabled = false;
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_outerCircleCollider != null) _outerCircleCollider.enabled = false;
            if (_outerCircleLine != null) _outerCircleLine.enabled = false;
        }

        /// <summary>Restores whichever shape activeBoundary currently points to - the counterpart
        /// to Hide(), without a full geometry rebuild.</summary>
        public void Show() => ApplyActiveBoundary();

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
