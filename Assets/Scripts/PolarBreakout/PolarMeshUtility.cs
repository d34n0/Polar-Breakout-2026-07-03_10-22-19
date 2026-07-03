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

        /// <summary>
        /// Builds a fresh transparent unlit Material (not shared/cached - callers like the
        /// death zone visual only ever need one instance). Setting the URP Unlit shader's
        /// surface-type properties by hand mirrors what the Shader GUI does when you toggle
        /// "Surface Type: Transparent" in the Inspector, since there's no public API for it.
        /// </summary>
        public static Material CreateTransparentUnlitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Standard");
            var material = new Material(shader);

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f); // Transparent
                material.SetFloat("_Blend", 0f);   // Alpha
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);

            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            return material;
        }

        /// <summary>Simple filled disc (triangle fan from the center), used for the death zone
        /// warning visual - not curved-brick-specific like the rest of this class, but this is
        /// the shared home for procedural mesh building in this project.</summary>
        public static Mesh BuildFilledCircleMesh(float radius, int segments = 48)
        {
            segments = Mathf.Max(3, segments);
            var vertices = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            var triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float angleRad = i * Mathf.PI * 2f / segments;
                float cos = Mathf.Cos(angleRad);
                float sin = Mathf.Sin(angleRad);
                vertices[i + 1] = new Vector3(cos * radius, sin * radius, 0f);
                uvs[i + 1] = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);
            }

            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "FilledCircle" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
