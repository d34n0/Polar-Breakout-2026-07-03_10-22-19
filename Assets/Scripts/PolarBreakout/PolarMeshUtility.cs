using UnityEngine;

namespace PolarBreakout
{
    /// <summary>
    /// Procedural mesh building for the arena: pointy-top hexagon bricks (the current grid shape),
    /// arc-shaped ("orange slice") segments (still used by the paddle's own mesh), and the death
    /// zone's filled circle.
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
                // Wound so the computed normal faces -Z (toward the camera), matching the same
                // convention as PolarMeshUtility's other meshes - this mesh only ever worked
                // before because the material DeathZoneVisual auto-generates explicitly disables
                // culling; a custom material with normal back-face culling would cull this whole
                // disc, since it's the camera-facing side that's actually the "back" face.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % segments + 1;
                triangles[i * 3 + 2] = i + 1;
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

                // Wound so the computed normal faces -Z (toward the camera, which sits at
                // negative Z looking toward the arena at z=0) - this makes bricks/paddle
                // correctly front-facing under any material's standard back-face culling.
                // The old winding faced +Z (away from the camera) and only ever looked right
                // because the default brick material (Sprites/Default) doesn't cull at all;
                // any other material assigned as an override would be invisible.
                triangles[triIndex++] = innerCurr;
                triangles[triIndex++] = outerNext;
                triangles[triIndex++] = outerCurr;

                triangles[triIndex++] = innerCurr;
                triangles[triIndex++] = innerNext;
                triangles[triIndex++] = outerNext;
            }

            var mesh = new Mesh { name = "ArcBrickSegment" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            // Needed for any shader that reads a tangent-space normal map (e.g. the gemstone
            // brick shader's faceted normal) - without a valid tangent basis, transforming that
            // normal into world space produces garbage lighting instead of just looking flat.
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Corner i (0..5) of a pointy-top hexagon centered at `center`, going CCW
        /// starting just above the +X axis - angle (60*i - 30) degrees. Shared by the hex
        /// mesh/outline builders below and by HexArenaBoundary's boundary-edge tracing, so both
        /// always agree on exactly where each hex's corners sit.</summary>
        public static Vector2 HexCorner(Vector2 center, float radius, int i)
        {
            float angleRad = (60f * i - 30f) * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
        }

        /// <summary>Filled pointy-top hexagon (triangle fan from the center) - every hex brick in
        /// the grid is congruent, so BrickGridManager builds this once and shares it across every
        /// Brick instance rather than building a unique mesh per cell.</summary>
        public static Mesh BuildHexMesh(float radius)
        {
            var vertices = new Vector3[7];
            var uvs = new Vector2[7];
            var triangles = new int[18];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            // A pointy-top hex's true corner-to-corner span is narrower on X than on Y
            // (sqrt(3)*radius vs 2*radius) - dividing both axes by the same 2*radius would leave
            // horizontal UV margin (~7% on each side) even though the mesh's own left/right edges
            // sit there, so a texture whose hex art touches all four edges of its canvas (the
            // standard way hex sprites are authored) would render inset from the actual mesh
            // silhouette instead of lining up with it. Using each axis's own true extent maps the
            // mesh's bounding box exactly onto the full 0-1 UV square instead.
            float xExtent = radius * Mathf.Sqrt(3f);
            float yExtent = radius * 2f;

            for (int i = 0; i < 6; i++)
            {
                Vector2 corner = HexCorner(Vector2.zero, radius, i);
                vertices[i + 1] = new Vector3(corner.x, corner.y, 0f);
                uvs[i + 1] = new Vector2(corner.x / xExtent + 0.5f, corner.y / yExtent + 0.5f);
            }

            for (int i = 0; i < 6; i++)
            {
                // Same winding/facing convention as BuildFilledCircleMesh (-Z facing).
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % 6 + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh { name = "HexBrick" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>The 6 corners of a pointy-top hexagon centered at the origin, suitable for
        /// PolygonCollider2D.SetPath - shared across every Brick instance the same way
        /// BuildHexMesh is.</summary>
        public static Vector2[] BuildHexOutlinePoints(float radius)
        {
            var points = new Vector2[6];
            for (int i = 0; i < 6; i++)
                points[i] = HexCorner(Vector2.zero, radius, i);
            return points;
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
