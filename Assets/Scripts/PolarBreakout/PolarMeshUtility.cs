using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Builds procedural meshes for arc-shaped ("orange slice") brick segments.
    /// Each brick is a curved quad strip between an inner and outer radius,
    /// spanning a start and end angle, subdivided so the curve reads as a curve.
    /// </summary>
    public static class PolarMeshUtility
    {
        private static Material _proceduralUnlitMaterial;

        /// <summary>
        /// A single shared Material for runtime-built quads (power-up capsules, bullets) that
        /// - unlike Brick.prefab's MeshRenderer - never got a material serialized in the Editor.
        /// Resources.GetBuiltinResource&lt;Material&gt;("Default-Material.mat") looks like the
        /// obvious fix but isn't reliable once URP is the active render pipeline (URP doesn't
        /// ship that builtin resource under the same name), so this instead finds URP's own
        /// Unlit shader directly. Colors are still applied per-instance via
        /// MaterialPropertyBlock, so every caller sharing this one Material is fine.
        /// </summary>
        public static Material GetProceduralUnlitMaterial()
        {
            if (_proceduralUnlitMaterial != null) return _proceduralUnlitMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            _proceduralUnlitMaterial = new Material(shader);

            // The quads these capsules/bullets build use a fixed CCW winding that doesn't
            // reliably face the 2D scene camera once rotated (Bullet rotates to match its
            // travel angle) or depending on which side of the arena they're on. Disabling
            // culling means the quad renders regardless of which way it's facing, instead of
            // silently vanishing when its back face happens to point at the camera.
            if (_proceduralUnlitMaterial.HasProperty("_Cull"))
                _proceduralUnlitMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            return _proceduralUnlitMaterial;
        }

        public static Mesh BuildArcSegmentMesh(
            float innerRadius,
            float outerRadius,
            float startAngleDeg,
            float endAngleDeg,
            float degreesPerSubdivision = 4f)
        {
            // Guard against degenerate input (gaps larger than the brick itself, etc).
            outerRadius = Mathf.Max(outerRadius, innerRadius + 0.001f);
            float span = endAngleDeg - startAngleDeg;

            int subdivisions = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(span) / Mathf.Max(0.01f, degreesPerSubdivision)));

            int vertCount = (subdivisions + 1) * 2;
            var vertices = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            var triangles = new int[subdivisions * 6];

            for (int i = 0; i <= subdivisions; i++)
            {
                float t = (float)i / subdivisions;
                float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                float rad = angleDeg * Mathf.Deg2Rad;
                float cos = Mathf.Cos(rad);
                float sin = Mathf.Sin(rad);

                int innerIdx = i * 2;
                int outerIdx = i * 2 + 1;

                vertices[innerIdx] = new Vector3(cos * innerRadius, sin * innerRadius, 0f);
                vertices[outerIdx] = new Vector3(cos * outerRadius, sin * outerRadius, 0f);

                uvs[innerIdx] = new Vector2(t, 0f);
                uvs[outerIdx] = new Vector2(t, 1f);
            }

            int triIndex = 0;
            for (int i = 0; i < subdivisions; i++)
            {
                int innerCurr = i * 2;
                int outerCurr = i * 2 + 1;
                int innerNext = (i + 1) * 2;
                int outerNext = (i + 1) * 2 + 1;

                triangles[triIndex++] = innerCurr;
                triangles[triIndex++] = outerCurr;
                triangles[triIndex++] = outerNext;

                triangles[triIndex++] = innerCurr;
                triangles[triIndex++] = outerNext;
                triangles[triIndex++] = innerNext;
            }

            var mesh = new Mesh { name = "ArcBrickSegment" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Same arc shape as BuildArcSegmentMesh, but as a single closed polygon outline
        /// (outer edge forward, then inner edge backward) - suitable for PolygonCollider2D.SetPath.
        /// </summary>
        public static Vector2[] BuildArcOutlinePoints(
            float innerRadius,
            float outerRadius,
            float startAngleDeg,
            float endAngleDeg,
            float degreesPerSubdivision = 4f)
        {
            outerRadius = Mathf.Max(outerRadius, innerRadius + 0.001f);
            float span = endAngleDeg - startAngleDeg;
            int subdivisions = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(span) / Mathf.Max(0.01f, degreesPerSubdivision)));

            var points = new Vector2[(subdivisions + 1) * 2];

            for (int i = 0; i <= subdivisions; i++)
            {
                float t = (float)i / subdivisions;
                float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                float rad = angleDeg * Mathf.Deg2Rad;
                points[i] = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * outerRadius;
            }

            for (int i = 0; i <= subdivisions; i++)
            {
                float t = 1f - (float)i / subdivisions;
                float angleDeg = Mathf.Lerp(startAngleDeg, endAngleDeg, t);
                float rad = angleDeg * Mathf.Deg2Rad;
                points[subdivisions + 1 + i] = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * innerRadius;
            }

            return points;
        }
    }
}
