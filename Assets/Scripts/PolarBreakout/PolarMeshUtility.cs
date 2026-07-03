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
    }
}
