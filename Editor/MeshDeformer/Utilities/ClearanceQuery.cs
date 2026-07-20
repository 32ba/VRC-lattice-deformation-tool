#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal enum ClearanceSignMode
    {
        ReferenceNormal = 0,
        ClosedMesh = 1
    }

    internal readonly struct ClearanceQueryResult
    {
        internal static ClearanceQueryResult Invalid => default;

        internal readonly bool IsValid;
        internal readonly int TriangleIndex;
        internal readonly Vector3 ClosestPointWorld;
        internal readonly Vector3 BarycentricCoordinate;
        internal readonly Vector3 NormalWorld;
        internal readonly float Distance;
        internal readonly float SignedClearance;
        internal readonly bool IsInside;
        internal readonly bool IsClosedSurface;
        internal readonly ClearanceSignMode SignMode;
        internal readonly int VisitedTriangleCount;

        internal ClearanceQueryResult(
            int triangleIndex,
            Vector3 closestPointWorld,
            Vector3 barycentricCoordinate,
            Vector3 normalWorld,
            float distance,
            float signedClearance,
            bool isInside,
            bool isClosedSurface,
            ClearanceSignMode signMode,
            int visitedTriangleCount)
        {
            IsValid = true;
            TriangleIndex = triangleIndex;
            ClosestPointWorld = closestPointWorld;
            BarycentricCoordinate = barycentricCoordinate;
            NormalWorld = normalWorld;
            Distance = distance;
            SignedClearance = signedClearance;
            IsInside = isInside;
            IsClosedSurface = isClosedSurface;
            SignMode = signMode;
            VisitedTriangleCount = visitedTriangleCount;
        }
    }

    /// <summary>
    /// Immutable world-space triangle query accelerated by a binary AABB tree.
    /// </summary>
    internal sealed class ClearanceQuery
    {
        private const int LeafTriangleCount = 8;
        private const float GeometryEpsilon = 1e-12f;

        private readonly TriangleData[] _triangles;
        private readonly int[] _triangleOrder;
        private readonly BvhNode[] _nodes;

        internal int TriangleCount => _triangles.Length;
        internal bool IsClosedSurface { get; }

        private ClearanceQuery(
            TriangleData[] triangles,
            int[] triangleOrder,
            BvhNode[] nodes,
            bool isClosedSurface)
        {
            _triangles = triangles;
            _triangleOrder = triangleOrder;
            _nodes = nodes;
            IsClosedSurface = isClosedSurface;
        }

        internal static bool TryCreate(Mesh mesh, Matrix4x4 localToWorld, out ClearanceQuery query)
        {
            query = null;
            if (mesh == null || !IsFinite(localToWorld)) return false;

            Vector3[] vertices;
            Vector3[] normals;
            int[] indices;
            try
            {
                vertices = mesh.vertices;
                normals = mesh.normals;
                indices = mesh.triangles;
            }
            catch (Exception)
            {
                return false;
            }

            if (vertices == null || vertices.Length == 0 || indices == null || indices.Length < 3)
                return false;

            bool hasVertexNormals = normals != null && normals.Length == vertices.Length;
            Matrix4x4 normalMatrix = localToWorld.inverse.transpose;
            float determinantSign = localToWorld.determinant < 0f ? -1f : 1f;
            var triangles = new List<TriangleData>(indices.Length / 3);
            var edgeUseCounts = new Dictionary<ulong, int>();

            for (int triangleIndex = 0; triangleIndex + 2 < indices.Length; triangleIndex += 3)
            {
                int i0 = indices[triangleIndex];
                int i1 = indices[triangleIndex + 1];
                int i2 = indices[triangleIndex + 2];
                if ((uint)i0 >= vertices.Length || (uint)i1 >= vertices.Length || (uint)i2 >= vertices.Length)
                    continue;

                Vector3 a = localToWorld.MultiplyPoint3x4(vertices[i0]);
                Vector3 b = localToWorld.MultiplyPoint3x4(vertices[i1]);
                Vector3 c = localToWorld.MultiplyPoint3x4(vertices[i2]);
                if (!IsFinite(a) || !IsFinite(b) || !IsFinite(c)) continue;

                Vector3 geometricNormal = Vector3.Cross(b - a, c - a) * determinantSign;
                if (geometricNormal.sqrMagnitude <= GeometryEpsilon) continue;
                geometricNormal.Normalize();

                Vector3 n0 = geometricNormal;
                Vector3 n1 = geometricNormal;
                Vector3 n2 = geometricNormal;
                if (hasVertexNormals)
                {
                    n0 = TransformNormal(normalMatrix, normals[i0], geometricNormal);
                    n1 = TransformNormal(normalMatrix, normals[i1], geometricNormal);
                    n2 = TransformNormal(normalMatrix, normals[i2], geometricNormal);
                }

                triangles.Add(new TriangleData(
                    triangleIndex / 3,
                    a,
                    b,
                    c,
                    n0,
                    n1,
                    n2));
                IncrementEdge(edgeUseCounts, i0, i1);
                IncrementEdge(edgeUseCounts, i1, i2);
                IncrementEdge(edgeUseCounts, i2, i0);
            }

            if (triangles.Count == 0) return false;

            bool isClosed = edgeUseCounts.Count > 0;
            foreach (int useCount in edgeUseCounts.Values)
            {
                if (useCount != 2)
                {
                    isClosed = false;
                    break;
                }
            }

            TriangleData[] triangleArray = triangles.ToArray();
            var order = new int[triangleArray.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var nodes = new List<BvhNode>(triangleArray.Length * 2);
            BuildNode(triangleArray, order, nodes, 0, order.Length);
            query = new ClearanceQuery(triangleArray, order, nodes.ToArray(), isClosed);
            return true;
        }

        internal ClearanceQueryResult QueryPoint(
            Vector3 pointWorld,
            ClearanceSignMode signMode = ClearanceSignMode.ReferenceNormal)
        {
            if (!IsFinite(pointWorld) || _nodes.Length == 0) return ClearanceQueryResult.Invalid;

            float bestDistanceSq = float.PositiveInfinity;
            int bestTriangle = -1;
            Vector3 bestPoint = default;
            Vector3 bestBarycentric = default;
            int triangleTests = 0;

            var stack = new int[_nodes.Length];
            int stackCount = 0;
            stack[stackCount++] = 0;
            while (stackCount > 0)
            {
                int nodeIndex = stack[--stackCount];
                BvhNode node = _nodes[nodeIndex];
                if (DistanceToBoundsSquared(pointWorld, node.Min, node.Max) > bestDistanceSq)
                    continue;

                if (node.Count > 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        TriangleData triangle = _triangles[_triangleOrder[i]];
                        triangleTests++;
                        Vector3 closest = ClosestPointOnTriangle(
                            pointWorld,
                            triangle.A,
                            triangle.B,
                            triangle.C,
                            out Vector3 barycentric);
                        float distanceSq = (pointWorld - closest).sqrMagnitude;
                        if (distanceSq < bestDistanceSq)
                        {
                            bestDistanceSq = distanceSq;
                            bestTriangle = _triangleOrder[i];
                            bestPoint = closest;
                            bestBarycentric = barycentric;
                        }
                    }
                    continue;
                }

                float leftDistance = DistanceToBoundsSquared(
                    pointWorld, _nodes[node.Left].Min, _nodes[node.Left].Max);
                float rightDistance = DistanceToBoundsSquared(
                    pointWorld, _nodes[node.Right].Min, _nodes[node.Right].Max);
                if (leftDistance < rightDistance)
                {
                    stack[stackCount++] = node.Right;
                    stack[stackCount++] = node.Left;
                }
                else
                {
                    stack[stackCount++] = node.Left;
                    stack[stackCount++] = node.Right;
                }
            }

            if (bestTriangle < 0 || float.IsInfinity(bestDistanceSq))
                return ClearanceQueryResult.Invalid;

            TriangleData nearest = _triangles[bestTriangle];
            Vector3 normal = nearest.N0 * bestBarycentric.x +
                             nearest.N1 * bestBarycentric.y +
                             nearest.N2 * bestBarycentric.z;
            if (normal.sqrMagnitude <= GeometryEpsilon)
                normal = nearest.FaceNormal;
            else
                normal.Normalize();

            float distance = Mathf.Sqrt(Mathf.Max(0f, bestDistanceSq));
            bool useClosedSign = signMode == ClearanceSignMode.ClosedMesh && IsClosedSurface;
            bool inside = false;
            float signedClearance;
            if (distance <= 1e-6f)
            {
                signedClearance = 0f;
            }
            else if (useClosedSign)
            {
                inside = IsPointInside(pointWorld, out int insideTriangleTests);
                triangleTests += insideTriangleTests;
                signedClearance = inside ? -distance : distance;
            }
            else
            {
                signedClearance = Vector3.Dot(pointWorld - bestPoint, normal);
            }

            return new ClearanceQueryResult(
                nearest.OriginalIndex,
                bestPoint,
                bestBarycentric,
                normal,
                distance,
                signedClearance,
                inside,
                IsClosedSurface,
                useClosedSign ? ClearanceSignMode.ClosedMesh : ClearanceSignMode.ReferenceNormal,
                triangleTests);
        }

        private bool IsPointInside(Vector3 point, out int triangleTests)
        {
            triangleTests = 0;
            var direction = new Vector3(1f, 0.37139067f, 0.5291327f).normalized;
            var hits = new List<float>();
            var stack = new int[_nodes.Length];
            int stackCount = 0;
            stack[stackCount++] = 0;
            while (stackCount > 0)
            {
                BvhNode node = _nodes[stack[--stackCount]];
                if (!RayIntersectsBounds(point, direction, node.Min, node.Max)) continue;
                if (node.Count > 0)
                {
                    for (int i = node.Start; i < node.Start + node.Count; i++)
                    {
                        TriangleData triangle = _triangles[_triangleOrder[i]];
                        triangleTests++;
                        if (RayIntersectsTriangle(point, direction, triangle, out float distance))
                            hits.Add(distance);
                    }
                    continue;
                }
                stack[stackCount++] = node.Left;
                stack[stackCount++] = node.Right;
            }

            if (hits.Count == 0) return false;
            hits.Sort();
            int uniqueHits = 0;
            float previous = float.NegativeInfinity;
            for (int i = 0; i < hits.Count; i++)
            {
                float tolerance = 1e-5f * Mathf.Max(1f, Mathf.Abs(hits[i]));
                if (uniqueHits == 0 || hits[i] - previous > tolerance)
                {
                    uniqueHits++;
                    previous = hits[i];
                }
            }
            return (uniqueHits & 1) != 0;
        }

        private static int BuildNode(
            TriangleData[] triangles,
            int[] order,
            List<BvhNode> nodes,
            int start,
            int count)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Vector3 centroidMin = min;
            Vector3 centroidMax = max;
            for (int i = start; i < start + count; i++)
            {
                TriangleData triangle = triangles[order[i]];
                min = Vector3.Min(min, triangle.Min);
                max = Vector3.Max(max, triangle.Max);
                centroidMin = Vector3.Min(centroidMin, triangle.Centroid);
                centroidMax = Vector3.Max(centroidMax, triangle.Centroid);
            }

            int nodeIndex = nodes.Count;
            nodes.Add(default);
            if (count <= LeafTriangleCount)
            {
                nodes[nodeIndex] = BvhNode.Leaf(min, max, start, count);
                return nodeIndex;
            }

            Vector3 extent = centroidMax - centroidMin;
            int axis = extent.x >= extent.y && extent.x >= extent.z
                ? 0
                : extent.y >= extent.z ? 1 : 2;
            Array.Sort(
                order,
                start,
                count,
                Comparer<int>.Create((left, right) =>
                    Axis(triangles[left].Centroid, axis).CompareTo(Axis(triangles[right].Centroid, axis))));
            int leftCount = count / 2;
            int leftNode = BuildNode(triangles, order, nodes, start, leftCount);
            int rightNode = BuildNode(triangles, order, nodes, start + leftCount, count - leftCount);
            nodes[nodeIndex] = BvhNode.Branch(min, max, leftNode, rightNode);
            return nodeIndex;
        }

        private static Vector3 ClosestPointOnTriangle(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out Vector3 barycentric)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
            {
                barycentric = new Vector3(1f, 0f, 0f);
                return a;
            }

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
            {
                barycentric = new Vector3(0f, 1f, 0f);
                return b;
            }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                barycentric = new Vector3(1f - v, v, 0f);
                return a + v * ab;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
            {
                barycentric = new Vector3(0f, 0f, 1f);
                return c;
            }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                barycentric = new Vector3(1f - w, 0f, w);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                barycentric = new Vector3(0f, 1f - w, w);
                return b + w * (c - b);
            }

            float denominator = 1f / (va + vb + vc);
            float insideV = vb * denominator;
            float insideW = vc * denominator;
            barycentric = new Vector3(1f - insideV - insideW, insideV, insideW);
            return a + ab * insideV + ac * insideW;
        }

        private static float DistanceToBoundsSquared(Vector3 point, Vector3 min, Vector3 max)
        {
            float dx = point.x < min.x ? min.x - point.x : point.x > max.x ? point.x - max.x : 0f;
            float dy = point.y < min.y ? min.y - point.y : point.y > max.y ? point.y - max.y : 0f;
            float dz = point.z < min.z ? min.z - point.z : point.z > max.z ? point.z - max.z : 0f;
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool RayIntersectsBounds(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
        {
            float near = 0f;
            float far = float.PositiveInfinity;
            for (int axis = 0; axis < 3; axis++)
            {
                float component = Axis(direction, axis);
                float originComponent = Axis(origin, axis);
                float minComponent = Axis(min, axis);
                float maxComponent = Axis(max, axis);
                if (Mathf.Abs(component) < 1e-12f)
                {
                    if (originComponent < minComponent || originComponent > maxComponent) return false;
                    continue;
                }

                float inverse = 1f / component;
                float t0 = (minComponent - originComponent) * inverse;
                float t1 = (maxComponent - originComponent) * inverse;
                if (t0 > t1) (t0, t1) = (t1, t0);
                near = Mathf.Max(near, t0);
                far = Mathf.Min(far, t1);
                if (far < near) return false;
            }
            return far > 1e-6f;
        }

        private static bool RayIntersectsTriangle(
            Vector3 origin,
            Vector3 direction,
            TriangleData triangle,
            out float distance)
        {
            distance = 0f;
            Vector3 edge1 = triangle.B - triangle.A;
            Vector3 edge2 = triangle.C - triangle.A;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f) return false;
            float inverse = 1f / determinant;
            Vector3 t = origin - triangle.A;
            float u = Vector3.Dot(t, p) * inverse;
            if (u < -1e-6f || u > 1f + 1e-6f) return false;
            Vector3 q = Vector3.Cross(t, edge1);
            float v = Vector3.Dot(direction, q) * inverse;
            if (v < -1e-6f || u + v > 1f + 1e-6f) return false;
            distance = Vector3.Dot(edge2, q) * inverse;
            return distance > 1e-6f;
        }

        private static Vector3 TransformNormal(Matrix4x4 normalMatrix, Vector3 normal, Vector3 fallback)
        {
            if (!IsFinite(normal) || normal.sqrMagnitude <= GeometryEpsilon) return fallback;
            Vector3 transformed = normalMatrix.MultiplyVector(normal);
            if (!IsFinite(transformed) || transformed.sqrMagnitude <= GeometryEpsilon) return fallback;
            return transformed.normalized;
        }

        private static void IncrementEdge(Dictionary<ulong, int> edgeUseCounts, int a, int b)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            edgeUseCounts.TryGetValue(key, out int count);
            edgeUseCounts[key] = count + 1;
        }

        private static float Axis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++)
            {
                if (!IsFinite(value[i])) return false;
            }
            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct TriangleData
        {
            internal readonly int OriginalIndex;
            internal readonly Vector3 A;
            internal readonly Vector3 B;
            internal readonly Vector3 C;
            internal readonly Vector3 N0;
            internal readonly Vector3 N1;
            internal readonly Vector3 N2;
            internal readonly Vector3 FaceNormal;
            internal readonly Vector3 Min;
            internal readonly Vector3 Max;
            internal readonly Vector3 Centroid;

            internal TriangleData(
                int originalIndex,
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 n0,
                Vector3 n1,
                Vector3 n2)
            {
                OriginalIndex = originalIndex;
                A = a;
                B = b;
                C = c;
                N0 = n0;
                N1 = n1;
                N2 = n2;
                FaceNormal = Vector3.Cross(b - a, c - a).normalized;
                Min = Vector3.Min(a, Vector3.Min(b, c));
                Max = Vector3.Max(a, Vector3.Max(b, c));
                Centroid = (a + b + c) / 3f;
            }
        }

        private readonly struct BvhNode
        {
            internal readonly Vector3 Min;
            internal readonly Vector3 Max;
            internal readonly int Left;
            internal readonly int Right;
            internal readonly int Start;
            internal readonly int Count;

            private BvhNode(Vector3 min, Vector3 max, int left, int right, int start, int count)
            {
                Min = min;
                Max = max;
                Left = left;
                Right = right;
                Start = start;
                Count = count;
            }

            internal static BvhNode Leaf(Vector3 min, Vector3 max, int start, int count)
                => new BvhNode(min, max, -1, -1, start, count);

            internal static BvhNode Branch(Vector3 min, Vector3 max, int left, int right)
                => new BvhNode(min, max, left, right, 0, 0);
        }
    }

    /// <summary>
    /// Renderer-aware cache and batch entry point shared by visualization and correction tools.
    /// </summary>
    internal static class ClearanceQueryCache
    {
        private sealed class CacheEntry
        {
            internal Renderer Renderer;
            internal int StateHash;
            internal ClearanceQuery Query;
        }

        private static readonly Dictionary<int, CacheEntry> Entries = new Dictionary<int, CacheEntry>();

        internal static int BuildCount { get; private set; }

        internal static void Clear()
        {
            Entries.Clear();
            BuildCount = 0;
        }

        internal static bool TryGet(Renderer renderer, out ClearanceQuery query)
        {
            return TryGet(renderer, out query, out _);
        }

        internal static bool TryGet(Renderer renderer, out ClearanceQuery query, out int stateHash)
        {
            query = null;
            stateHash = 0;
            if (!TryCaptureMesh(renderer, out Mesh mesh, out Matrix4x4 localToWorld, out bool ownsMesh))
                return false;

            try
            {
                if (!TryComputeStateHash(mesh, localToWorld, out stateHash)) return false;
                int rendererId = renderer.GetInstanceID();
                if (Entries.TryGetValue(rendererId, out CacheEntry existing) &&
                    existing.Renderer == renderer &&
                    existing.StateHash == stateHash &&
                    existing.Query != null)
                {
                    query = existing.Query;
                    return true;
                }

                if (!ClearanceQuery.TryCreate(mesh, localToWorld, out query)) return false;
                Entries[rendererId] = new CacheEntry
                {
                    Renderer = renderer,
                    StateHash = stateHash,
                    Query = query
                };
                BuildCount++;
                return true;
            }
            finally
            {
                if (ownsMesh && mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        internal static ClearanceQueryResult[] QueryPoints(
            Renderer referenceRenderer,
            Vector3[] targetVertices,
            Matrix4x4 targetLocalToWorld,
            ClearanceSignMode signMode)
        {
            return QueryPoints(
                referenceRenderer,
                targetVertices,
                targetLocalToWorld,
                signMode,
                out _);
        }

        internal static ClearanceQueryResult[] QueryPoints(
            Renderer referenceRenderer,
            Vector3[] targetVertices,
            Matrix4x4 targetLocalToWorld,
            ClearanceSignMode signMode,
            out int referenceStateHash)
        {
            referenceStateHash = 0;
            if (targetVertices == null ||
                !TryGet(referenceRenderer, out ClearanceQuery query, out referenceStateHash))
                return Array.Empty<ClearanceQueryResult>();

            var results = new ClearanceQueryResult[targetVertices.Length];
            for (int i = 0; i < targetVertices.Length; i++)
            {
                Vector3 worldPoint = targetLocalToWorld.MultiplyPoint3x4(targetVertices[i]);
                results[i] = query.QueryPoint(worldPoint, signMode);
            }
            return results;
        }

        internal static bool TryGetRendererStateHash(Renderer renderer, out int stateHash)
        {
            stateHash = 0;
            if (!TryCaptureMesh(renderer, out Mesh mesh, out Matrix4x4 localToWorld, out bool ownsMesh))
                return false;
            try
            {
                return TryComputeStateHash(mesh, localToWorld, out stateHash);
            }
            finally
            {
                if (ownsMesh && mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        internal static ClearanceQueryResult[] QueryRenderer(
            Renderer targetRenderer,
            Renderer referenceRenderer,
            ClearanceSignMode signMode)
        {
            if (!TryGetWorldVertices(targetRenderer, out Vector3[] worldVertices))
                return Array.Empty<ClearanceQueryResult>();
            return QueryPoints(referenceRenderer, worldVertices, Matrix4x4.identity, signMode);
        }

        internal static bool TryGetWorldVertices(Renderer renderer, out Vector3[] worldVertices)
        {
            return TryGetWorldVertices(renderer, out worldVertices, out _, false);
        }

        internal static bool TryGetWorldVertices(
            Renderer renderer,
            out Vector3[] worldVertices,
            out int stateHash)
        {
            return TryGetWorldVertices(renderer, out worldVertices, out stateHash, true);
        }

        private static bool TryGetWorldVertices(
            Renderer renderer,
            out Vector3[] worldVertices,
            out int stateHash,
            bool computeStateHash)
        {
            worldVertices = Array.Empty<Vector3>();
            stateHash = 0;
            if (!TryCaptureMesh(renderer, out Mesh mesh, out Matrix4x4 localToWorld, out bool ownsMesh))
                return false;
            try
            {
                Vector3[] vertices;
                if (computeStateHash)
                {
                    if (!TryComputeStateHash(
                            mesh,
                            localToWorld,
                            out stateHash,
                            out vertices)) return false;
                }
                else
                {
                    try
                    {
                        vertices = mesh.vertices;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

                worldVertices = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                    worldVertices[i] = localToWorld.MultiplyPoint3x4(vertices[i]);
                return true;
            }
            finally
            {
                if (ownsMesh && mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static bool TryCaptureMesh(
            Renderer renderer,
            out Mesh mesh,
            out Matrix4x4 localToWorld,
            out bool ownsMesh)
        {
            mesh = null;
            localToWorld = Matrix4x4.identity;
            ownsMesh = false;
            if (renderer == null) return false;

            localToWorld = renderer.transform.localToWorldMatrix;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null) return false;
                mesh = new Mesh { name = "Clearance Query Baked Mesh" };
                ownsMesh = true;
                try
                {
                    skinned.BakeMesh(mesh);
                    return mesh.vertexCount > 0;
                }
                catch (Exception)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                    mesh = null;
                    ownsMesh = false;
                    return false;
                }
            }

            if (renderer is MeshRenderer)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
                return mesh != null;
            }

            return false;
        }

        private static bool TryComputeStateHash(Mesh mesh, Matrix4x4 matrix, out int hash)
        {
            return TryComputeStateHash(mesh, matrix, out hash, out _);
        }

        private static bool TryComputeStateHash(
            Mesh mesh,
            Matrix4x4 matrix,
            out int hash,
            out Vector3[] vertices)
        {
            hash = 17;
            vertices = Array.Empty<Vector3>();
            try
            {
                unchecked
                {
                    vertices = mesh.vertices;
                    Vector3[] normals = mesh.normals;
                    int[] triangles = mesh.triangles;
                    hash = hash * 31 + vertices.Length;
                    for (int i = 0; i < vertices.Length; i++) hash = hash * 31 + vertices[i].GetHashCode();
                    hash = hash * 31 + normals.Length;
                    for (int i = 0; i < normals.Length; i++) hash = hash * 31 + normals[i].GetHashCode();
                    hash = hash * 31 + triangles.Length;
                    for (int i = 0; i < triangles.Length; i++) hash = hash * 31 + triangles[i];
                    for (int i = 0; i < 16; i++) hash = hash * 31 + matrix[i].GetHashCode();
                }
                return true;
            }
            catch (Exception)
            {
                hash = 0;
                vertices = Array.Empty<Vector3>();
                return false;
            }
        }
    }
}
#endif
