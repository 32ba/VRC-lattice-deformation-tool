#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    /// <summary>
    /// Generates procedural meshes that simulate real avatar and clothing geometry
    /// for practical deformation testing.
    /// </summary>
    internal static class TestMeshFactory
    {
        /// <summary>
        /// Creates a cylinder mesh (simulates a limb or tube-shaped clothing).
        /// </summary>
        /// <param name="segments">Number of radial segments (higher = smoother)</param>
        /// <param name="rings">Number of height rings</param>
        /// <param name="radius">Cylinder radius</param>
        /// <param name="height">Cylinder height along Y axis, centered at origin</param>
        public static Mesh CreateCylinder(int segments = 16, int rings = 8,
            float radius = 0.05f, float height = 0.4f)
        {
            var mesh = new Mesh { name = "TestCylinder" };
            int vertexCount = segments * rings;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];

            float halfHeight = height * 0.5f;
            for (int ring = 0; ring < rings; ring++)
            {
                float y = -halfHeight + height * ring / (rings - 1);
                for (int seg = 0; seg < segments; seg++)
                {
                    float angle = 2f * Mathf.PI * seg / segments;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    int idx = ring * segments + seg;
                    vertices[idx] = new Vector3(x, y, z);
                    normals[idx] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                }
            }

            var triangles = new List<int>();
            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int current = ring * segments + seg;
                    int next = ring * segments + (seg + 1) % segments;
                    int above = (ring + 1) * segments + seg;
                    int aboveNext = (ring + 1) * segments + (seg + 1) % segments;

                    triangles.Add(current);
                    triangles.Add(above);
                    triangles.Add(next);

                    triangles.Add(next);
                    triangles.Add(above);
                    triangles.Add(aboveNext);
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates two concentric cylinders as a single mesh (simulates body + clothing).
        /// The inner cylinder represents a body, the outer one represents clothing.
        /// They share no edges — separate mesh islands.
        /// </summary>
        /// <param name="innerRadius">Body radius</param>
        /// <param name="outerRadius">Clothing radius</param>
        /// <param name="gap">Gap between body and clothing surface</param>
        /// <returns>
        /// Mesh with two islands. Vertex indices [0, innerCount) are inner (body),
        /// [innerCount, totalCount) are outer (clothing).
        /// </returns>
        public static Mesh CreateConcentricCylinders(int segments = 16, int rings = 8,
            float innerRadius = 0.04f, float outerRadius = 0.055f, float height = 0.4f)
        {
            var inner = CreateCylinder(segments, rings, innerRadius, height);
            var outer = CreateCylinder(segments, rings, outerRadius, height);

            int innerVertCount = inner.vertexCount;
            int outerVertCount = outer.vertexCount;
            int totalVerts = innerVertCount + outerVertCount;

            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            inner.vertices.CopyTo(vertices, 0);
            outer.vertices.CopyTo(vertices, innerVertCount);
            inner.normals.CopyTo(normals, 0);
            outer.normals.CopyTo(normals, innerVertCount);

            var innerTris = inner.triangles;
            var outerTris = outer.triangles;
            var triangles = new int[innerTris.Length + outerTris.Length];
            innerTris.CopyTo(triangles, 0);
            for (int i = 0; i < outerTris.Length; i++)
            {
                triangles[innerTris.Length + i] = outerTris[i] + innerVertCount;
            }

            var mesh = new Mesh { name = "TestConcentricCylinders" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            Object.DestroyImmediate(inner);
            Object.DestroyImmediate(outer);

            return mesh;
        }

        /// <summary>
        /// Creates a simplified symmetric humanoid mesh.
        /// Two arm-like cylinders placed symmetrically along X axis,
        /// connected to a central torso cylinder.
        /// All vertices on the left (negative X) have mirror counterparts on the right.
        /// </summary>
        public static Mesh CreateSymmetricHumanoid(int segmentsPerPart = 8, int ringsPerPart = 6)
        {
            // Torso: vertical cylinder at origin
            var torso = CreateCylinder(segmentsPerPart, ringsPerPart, 0.08f, 0.3f);
            // Left arm: horizontal cylinder offset to -X
            var leftArm = CreateOrientedCylinder(segmentsPerPart, ringsPerPart,
                0.03f, 0.2f, new Vector3(-0.18f, 0.1f, 0f), Vector3.left);
            // Right arm: mirror of left
            var rightArm = CreateOrientedCylinder(segmentsPerPart, ringsPerPart,
                0.03f, 0.2f, new Vector3(0.18f, 0.1f, 0f), Vector3.right);

            var mesh = CombineMeshes("TestSymmetricHumanoid", torso, leftArm, rightArm);

            Object.DestroyImmediate(torso);
            Object.DestroyImmediate(leftArm);
            Object.DestroyImmediate(rightArm);

            return mesh;
        }

        /// <summary>
        /// Creates a mesh with multiple isolated islands (simulates multi-part clothing).
        /// </summary>
        public static Mesh CreateMultiIslandMesh(int islandCount = 3, int segmentsPerIsland = 8,
            int ringsPerIsland = 4)
        {
            var parts = new Mesh[islandCount];
            for (int i = 0; i < islandCount; i++)
            {
                float offsetY = (i - (islandCount - 1) * 0.5f) * 0.15f;
                var part = CreateCylinder(segmentsPerIsland, ringsPerIsland, 0.03f, 0.1f);

                // Offset each island
                var verts = part.vertices;
                for (int v = 0; v < verts.Length; v++)
                {
                    verts[v] += new Vector3(0f, offsetY, 0f);
                }
                part.vertices = verts;
                parts[i] = part;
            }

            var mesh = CombineMeshes("TestMultiIslandMesh", parts);

            foreach (var p in parts) Object.DestroyImmediate(p);

            return mesh;
        }

        /// <summary>
        /// Creates a cylinder mesh with pre-baked BlendShapes that simulate
        /// common avatar shape keys (shrink, expand, move).
        /// </summary>
        public static Mesh CreateCylinderWithBlendShapes(int segments = 16, int rings = 8,
            float radius = 0.05f, float height = 0.4f)
        {
            var mesh = CreateCylinder(segments, rings, radius, height);
            int vertexCount = mesh.vertexCount;
            var vertices = mesh.vertices;

            // BlendShape 1: "Shrink" — pull all vertices inward
            var shrinkDeltas = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var radialDir = new Vector3(vertices[i].x, 0f, vertices[i].z).normalized;
                shrinkDeltas[i] = -radialDir * radius * 0.3f;
            }
            mesh.AddBlendShapeFrame("Shrink", 100f, shrinkDeltas, null, null);

            // BlendShape 2: "Expand" — push all vertices outward
            var expandDeltas = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var radialDir = new Vector3(vertices[i].x, 0f, vertices[i].z).normalized;
                expandDeltas[i] = radialDir * radius * 0.5f;
            }
            mesh.AddBlendShapeFrame("Expand", 100f, expandDeltas, null, null);

            // BlendShape 3: "MoveUp" — shift upper half upward
            var moveUpDeltas = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                if (vertices[i].y > 0f)
                {
                    moveUpDeltas[i] = new Vector3(0f, 0.05f, 0f);
                }
            }
            mesh.AddBlendShapeFrame("MoveUp", 100f, moveUpDeltas, null, null);

            return mesh;
        }

        /// <summary>
        /// Gets vertex count ranges for inner/outer islands in a concentric cylinder mesh.
        /// </summary>
        public static void GetConcentricCylinderRanges(int segments, int rings,
            out int innerStart, out int innerEnd, out int outerStart, out int outerEnd)
        {
            int countPerCylinder = segments * rings;
            innerStart = 0;
            innerEnd = countPerCylinder;
            outerStart = countPerCylinder;
            outerEnd = countPerCylinder * 2;
        }

        // ---- Helpers ----

        private static Mesh CreateOrientedCylinder(int segments, int rings,
            float radius, float length, Vector3 center, Vector3 direction)
        {
            var mesh = CreateCylinder(segments, rings, radius, length);
            var vertices = mesh.vertices;
            var normals = mesh.normals;

            // Rotate from Y-up to the desired direction
            var rotation = Quaternion.FromToRotation(Vector3.up, direction);
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = rotation * vertices[i] + center;
                normals[i] = rotation * normals[i];
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CombineMeshes(string name, params Mesh[] meshes)
        {
            int totalVerts = 0;
            int totalTris = 0;
            foreach (var m in meshes)
            {
                totalVerts += m.vertexCount;
                totalTris += m.triangles.Length;
            }

            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            var triangles = new int[totalTris];

            int vertOffset = 0;
            int triOffset = 0;
            foreach (var m in meshes)
            {
                m.vertices.CopyTo(vertices, vertOffset);
                m.normals.CopyTo(normals, vertOffset);
                var tris = m.triangles;
                for (int i = 0; i < tris.Length; i++)
                {
                    triangles[triOffset + i] = tris[i] + vertOffset;
                }
                vertOffset += m.vertexCount;
                triOffset += tris.Length;
            }

            var combined = new Mesh { name = name };
            combined.vertices = vertices;
            combined.normals = normals;
            combined.triangles = triangles;
            combined.RecalculateBounds();
            return combined;
        }
    }
}
#endif
