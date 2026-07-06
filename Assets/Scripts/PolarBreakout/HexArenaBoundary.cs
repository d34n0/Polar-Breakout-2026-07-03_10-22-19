using System.Collections.Generic;
using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Builds a real physical wall around the play-area boundary: an EdgeCollider2D loop tracing
    /// the outward-facing edges of every hex that touches the outer wall radius (a jagged
    /// hex-silhouette ring, not a perfect circle). BrickGridManager calls BuildBoundary at the end
    /// of every BuildLevel, so the wall always matches whatever hex layout was just built. Also
    /// draws a LineRenderer tracing the same loop, so the boundary is visible rather than just a
    /// physical collider.
    /// </summary>
    [RequireComponent(typeof(EdgeCollider2D))]
    [RequireComponent(typeof(LineRenderer))]
    public class HexArenaBoundary : MonoBehaviour
    {
        [Tooltip("Visual width of the boundary line, world units.")]
        public float lineWidth = 0.08f;
        [Tooltip("Leave unset for a plain yellow unlit line - assign a custom material to override.")]
        public Material materialOverride;

        private EdgeCollider2D _collider;
        private LineRenderer _lineRenderer;

        public void BuildBoundary(PolarGridSettings settings)
        {
            _collider ??= GetComponent<EdgeCollider2D>();
            _lineRenderer ??= GetComponent<LineRenderer>();
            ConfigureLineRenderer(_lineRenderer);

            // Deliberately the full hexSize, NOT hexSize-hexGap like the brick mesh/collider -
            // adjacent boundary hexes' outward corners only coincide (letting the loop-stitcher
            // below find shared endpoints) at their true, ungapped corners. Using the gapped
            // radius here would leave every hex's boundary edge floating in isolation, matching
            // its own hexGap-sized shrink but never touching its neighbor's edge.
            float hexRadius = settings.hexSize;
            var segments = new List<(Vector2 a, Vector2 b)>();

            foreach (var coord in settings.EnumerateValidCoordinates())
            {
                if (!settings.IsBoundaryHex(coord)) continue;

                Vector2 center = settings.HexToWorld(coord);
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

            var loops = StitchLoops(segments);

            if (loops.Count == 0)
            {
                Debug.LogWarning("HexArenaBoundary: no boundary loop found - is the grid empty?", this);
                _lineRenderer.positionCount = 0;
                return;
            }

            _collider.points = ClosedLoopArray(loops[0]);
            SetLoopPositions(_lineRenderer, loops[0]);

            for (int i = 1; i < loops.Count; i++)
            {
                var go = new GameObject("HexBoundaryLoop");
                go.transform.SetParent(transform, false);
                var edge = go.AddComponent<EdgeCollider2D>();
                edge.points = ClosedLoopArray(loops[i]);

                var extraLine = go.AddComponent<LineRenderer>();
                ConfigureLineRenderer(extraLine);
                SetLoopPositions(extraLine, loops[i]);
            }
        }

        /// <summary>Sets up a LineRenderer as a closed, world-space, unlit yellow loop (unless
        /// materialOverride is assigned) - shared by both the main boundary loop and any extra
        /// degenerate loops.</summary>
        private void ConfigureLineRenderer(LineRenderer line)
        {
            line.useWorldSpace = true;
            line.loop = true;
            line.widthMultiplier = lineWidth;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;

            if (materialOverride != null)
            {
                line.sharedMaterial = materialOverride;
                return;
            }

            line.sharedMaterial = PolarMeshUtility.GetProceduralUnlitMaterial();
            var propBlock = new MaterialPropertyBlock();
            propBlock.SetColor("_Color", Color.yellow);
            propBlock.SetColor("_BaseColor", Color.yellow);
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
