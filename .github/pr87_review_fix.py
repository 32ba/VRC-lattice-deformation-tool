from pathlib import Path

p = Path('Runtime/MeshDeformer/LatticeDeformer.cs')
s = p.read_text()
old = '''        public void SetDisplacement(int index, Vector3 displacement)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                Vector3 before = layer.GetBrushDisplacement(index);
                layer.SetBrushDisplacement(index, displacement);
                if (layer.GetBrushDisplacement(index) != before) NotifyDeformationDataChanged();
            }
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                Vector3 before = layer.GetBrushDisplacement(index);
                layer.AddBrushDisplacement(index, delta);
                if (layer.GetBrushDisplacement(index) != before) NotifyDeformationDataChanged();
            }
        }
'''
new = '''        public void SetDisplacement(int index, Vector3 displacement)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                Vector3 before = layer.GetBrushDisplacement(index);
                layer.SetBrushDisplacement(index, displacement);
                Vector3 after = layer.GetBrushDisplacement(index);
                if (!ExactlyEqual(before, after)) NotifyDeformationDataChanged();
            }
        }

        public void AddDisplacement(int index, Vector3 delta)
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                Vector3 before = layer.GetBrushDisplacement(index);
                layer.AddBrushDisplacement(index, delta);
                Vector3 after = layer.GetBrushDisplacement(index);
                if (!ExactlyEqual(before, after)) NotifyDeformationDataChanged();
            }
        }

        private static bool ExactlyEqual(Vector3 left, Vector3 right)
        {
            return left.x == right.x && left.y == right.y && left.z == right.z;
        }
'''
if old not in s:
    raise RuntimeError('brush facade block not found')
s = s.replace(old, new, 1)

old = '''        public void ClearDisplacements()
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush && layer.HasBrushDisplacements())
            {
                layer.ClearBrushDisplacements();
                NotifyDeformationDataChanged();
            }
        }
'''
new = '''        public void ClearDisplacements()
        {
            if (!EnsureGroups()) return;
            if (TryGetActiveLayer(out var layer) && layer.Type == MeshDeformerLayerType.Brush)
            {
                Vector3[] displacements = layer.BrushDisplacements;
                bool changed = false;
                for (int i = 0; i < displacements.Length; i++)
                {
                    Vector3 value = displacements[i];
                    if (value.x != 0f || value.y != 0f || value.z != 0f)
                    {
                        changed = true;
                        break;
                    }
                }

                layer.ClearBrushDisplacements();
                if (changed) NotifyDeformationDataChanged();
            }
        }
'''
if old not in s:
    raise RuntimeError('clear facade block not found')
s = s.replace(old, new, 1)

start = s.index('        private List<DeformerGroup> GetGroupStorage()')
end = s.index('        // Legacy compat — still called from EnsureLayerModelReady before group migration', start)
replacement = '''        private List<DeformerGroup> GetGroupStorage()
        {
            if (_groups == null) _groups = new List<DeformerGroup>();
            if (_dataSource != DeformerDataSource.Profile || _profile == null)
            {
                return _groups;
            }

            if (EvaluateProfileCompatibility(_profile) == ProfileCompatibilityStatus.TopologyMismatch)
            {
                _profileGroups = null;
                _profileFingerprint = null;
                _profileContentRevision = int.MinValue;
                if (_blockedProfileGroups == null)
                {
                    _blockedProfileGroups = new List<DeformerGroup>
                    {
                        new DeformerGroup
                        {
                            Name = "Incompatible Profile",
                            Enabled = false
                        }
                    };
                }
                return _blockedProfileGroups;
            }

            if (_groups.Count > 0)
            {
                _groups.Clear();
            }

            string fingerprint = _profile.GetContentFingerprint();
            if (_profileGroups == null ||
                !string.Equals(_profileFingerprint, fingerprint, StringComparison.Ordinal))
            {
                var payload = _profile.CreateIndependentPayload();
                _profileGroups = payload.Groups;
                _blockedProfileGroups = null;
                _activeGroupIndex = payload.ActiveGroupIndex;
                _profileFingerprint = fingerprint;
                _profileContentRevision = _profile.ContentRevision;
                InvalidateCache();
            }

            return _profileGroups;
        }

'''
s = s[:start] + replacement + s[end:]
p.write_text(s)

Path('Editor/MeshDeformer/Utilities/TopologySeamUtility.cs').write_text(r'''#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class TopologySeamUtility
    {
        private readonly struct PositionKey : IEquatable<PositionKey>
        {
            private readonly int _x;
            private readonly int _y;
            private readonly int _z;

            internal PositionKey(Vector3 value)
            {
                _x = FloatBits(value.x);
                _y = FloatBits(value.y);
                _z = FloatBits(value.z);
            }

            private static int FloatBits(float value)
            {
                if (value == 0f) return 0;
                return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            }

            public bool Equals(PositionKey other) =>
                _x == other._x && _y == other._y && _z == other._z;
            public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_x, _y, _z);
        }

        private readonly struct TriangleKey : IEquatable<TriangleKey>
        {
            private readonly int _a;
            private readonly int _b;
            private readonly int _c;

            internal TriangleKey(int a, int b, int c)
            {
                if (a > b) Swap(ref a, ref b);
                if (b > c) Swap(ref b, ref c);
                if (a > b) Swap(ref a, ref b);
                _a = a;
                _b = b;
                _c = c;
            }

            private static void Swap(ref int left, ref int right)
            {
                int temporary = left;
                left = right;
                right = temporary;
            }

            public bool Equals(TriangleKey other) =>
                _a == other._a && _b == other._b && _c == other._c;
            public override bool Equals(object obj) => obj is TriangleKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(_a, _b, _c);
        }

        private sealed class EdgeStats
        {
            internal int Count;
            internal int Forward;
            internal int Reverse;
            internal int A;
            internal int B;
        }

        private sealed class Topology
        {
            internal Dictionary<int, List<int>> Members;
            internal Dictionary<ulong, EdgeStats> Edges;
            internal bool HasDuplicateTriangle;
        }

        internal static bool IsClosedSurface(Vector3[] vertices, IReadOnlyList<int> indices)
        {
            Topology topology;
            if (!TryBuild(vertices, indices, out topology) ||
                topology.HasDuplicateTriangle || topology.Edges.Count == 0)
            {
                return false;
            }

            foreach (EdgeStats edge in topology.Edges.Values)
            {
                if (edge.Count != 2 || edge.Forward != 1 || edge.Reverse != 1)
                    return false;
            }
            return true;
        }

        internal static void MarkOpenBoundaryVertices(
            Vector3[] vertices,
            IReadOnlyList<int> indices,
            bool[] boundaryVertices)
        {
            Topology topology;
            if (boundaryVertices == null || vertices == null ||
                boundaryVertices.Length < vertices.Length ||
                !TryBuild(vertices, indices, out topology))
            {
                return;
            }

            if (topology.HasDuplicateTriangle)
            {
                MarkRawOpenBoundaryVertices(indices, boundaryVertices, vertices.Length);
                return;
            }

            foreach (EdgeStats edge in topology.Edges.Values)
            {
                if (edge.Count != 1) continue;
                MarkMembers(topology.Members, edge.A, boundaryVertices);
                MarkMembers(topology.Members, edge.B, boundaryVertices);
            }
        }

        private static bool TryBuild(Vector3[] vertices, IReadOnlyList<int> indices, out Topology topology)
        {
            topology = null;
            if (vertices == null || indices == null || vertices.Length == 0 || indices.Count < 3)
                return false;

            var logicalIds = new int[vertices.Length];
            var positionIds = new Dictionary<PositionKey, int>();
            var members = new Dictionary<int, List<int>>();
            int nextId = 0;
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                var key = new PositionKey(vertices[vertex]);
                int id;
                if (!positionIds.TryGetValue(key, out id))
                {
                    id = nextId++;
                    positionIds.Add(key, id);
                    members.Add(id, new List<int>());
                }
                logicalIds[vertex] = id;
                members[id].Add(vertex);
            }

            var edges = new Dictionary<ulong, EdgeStats>();
            var triangles = new HashSet<TriangleKey>();
            bool duplicateTriangle = false;
            for (int index = 0; index + 2 < indices.Count; index += 3)
            {
                int i0 = indices[index];
                int i1 = indices[index + 1];
                int i2 = indices[index + 2];
                if ((uint)i0 >= (uint)vertices.Length ||
                    (uint)i1 >= (uint)vertices.Length ||
                    (uint)i2 >= (uint)vertices.Length)
                    continue;

                int a = logicalIds[i0];
                int b = logicalIds[i1];
                int c = logicalIds[i2];
                if (a == b || b == c || c == a) continue;
                if (!triangles.Add(new TriangleKey(a, b, c))) duplicateTriangle = true;
                AddEdge(edges, a, b);
                AddEdge(edges, b, c);
                AddEdge(edges, c, a);
            }

            topology = new Topology
            {
                Members = members,
                Edges = edges,
                HasDuplicateTriangle = duplicateTriangle
            };
            return edges.Count > 0;
        }

        private static void AddEdge(Dictionary<ulong, EdgeStats> edges, int from, int to)
        {
            uint min = (uint)Math.Min(from, to);
            uint max = (uint)Math.Max(from, to);
            ulong key = ((ulong)min << 32) | max;
            EdgeStats stats;
            if (!edges.TryGetValue(key, out stats))
            {
                stats = new EdgeStats { A = (int)min, B = (int)max };
                edges.Add(key, stats);
            }
            stats.Count++;
            if (from < to) stats.Forward++; else stats.Reverse++;
        }

        private static void MarkMembers(
            Dictionary<int, List<int>> members,
            int logicalId,
            bool[] boundaryVertices)
        {
            List<int> vertices;
            if (!members.TryGetValue(logicalId, out vertices)) return;
            for (int i = 0; i < vertices.Count; i++) boundaryVertices[vertices[i]] = true;
        }

        private static void MarkRawOpenBoundaryVertices(
            IReadOnlyList<int> indices,
            bool[] boundaryVertices,
            int vertexCount)
        {
            var counts = new Dictionary<ulong, int>();
            for (int index = 0; index + 2 < indices.Count; index += 3)
            {
                int a = indices[index];
                int b = indices[index + 1];
                int c = indices[index + 2];
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount || (uint)c >= (uint)vertexCount)
                    continue;
                CountRawEdge(counts, a, b);
                CountRawEdge(counts, b, c);
                CountRawEdge(counts, c, a);
            }

            foreach (KeyValuePair<ulong, int> edge in counts)
            {
                if (edge.Value != 1) continue;
                boundaryVertices[(int)(edge.Key >> 32)] = true;
                boundaryVertices[(int)(edge.Key & uint.MaxValue)] = true;
            }
        }

        private static void CountRawEdge(Dictionary<ulong, int> counts, int a, int b)
        {
            uint min = (uint)Math.Min(a, b);
            uint max = (uint)Math.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }
    }
}
#endif
''')
Path('Editor/MeshDeformer/Utilities/TopologySeamUtility.cs.meta').write_text('''fileFormatVersion: 2\nguid: 6f6a1b9ef9d2472a81f4b6dd7c13c007\n''')

p = Path('Editor/MeshDeformer/Utilities/ClearanceQuery.cs')
s = p.read_text()
s = s.replace('''            var triangles = new List<TriangleData>(indices.Length / 3);
            var edgeUseCounts = new Dictionary<ulong, int>();
            int[] topologyVertexIds = BuildTopologyVertexIds(vertices, localToWorld);
''', '''            var triangles = new List<TriangleData>(indices.Length / 3);
            var validTopologyIndices = new List<int>(indices.Length);
''', 1)
old = '''                IncrementEdge(edgeUseCounts, topologyVertexIds[i0], topologyVertexIds[i1]);
                IncrementEdge(edgeUseCounts, topologyVertexIds[i1], topologyVertexIds[i2]);
                IncrementEdge(edgeUseCounts, topologyVertexIds[i2], topologyVertexIds[i0]);
'''
new = '''                validTopologyIndices.Add(i0);
                validTopologyIndices.Add(i1);
                validTopologyIndices.Add(i2);
'''
if old not in s:
    raise RuntimeError('clearance edge block not found')
s = s.replace(old, new, 1)
old = '''            bool isClosed = edgeUseCounts.Count > 0;
            foreach (int useCount in edgeUseCounts.Values)
            {
                if (useCount != 2)
                {
                    isClosed = false;
                    break;
                }
            }
'''
new = '''            bool isClosed = TopologySeamUtility.IsClosedSurface(vertices, validTopologyIndices);
'''
if old not in s:
    raise RuntimeError('clearance closed block not found')
s = s.replace(old, new, 1)
hstart = s.index('        private static int[] BuildTopologyVertexIds(')
hend = s.index('        private static int BuildNode(', hstart)
s = s[:hstart] + s[hend:]
p.write_text(s)

p = Path('Editor/MeshDeformer/Utilities/FitCorrectionGenerator.cs')
s = p.read_text()
start = s.index('        private static void BuildTopology(')
end = s.index('        private static void AddNeighbor(', start)
replacement = '''        private static void BuildTopology(
            Mesh mesh,
            out List<int>[] adjacency,
            out bool[] boundaryVertices)
        {
            int vertexCount = mesh != null ? mesh.vertexCount : 0;
            adjacency = new List<int>[vertexCount];
            boundaryVertices = new bool[vertexCount];
            for (int vertex = 0; vertex < vertexCount; vertex++)
                adjacency[vertex] = new List<int>();

            Vector3[] vertices = mesh != null ? mesh.vertices : Array.Empty<Vector3>();
            int[] triangles = mesh != null ? mesh.triangles : Array.Empty<int>();
            for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
            {
                int a = triangles[triangle];
                int b = triangles[triangle + 1];
                int c = triangles[triangle + 2];
                if ((uint)a >= (uint)vertexCount || (uint)b >= (uint)vertexCount ||
                    (uint)c >= (uint)vertexCount)
                    continue;
                AddNeighbor(adjacency, a, b);
                AddNeighbor(adjacency, b, c);
                AddNeighbor(adjacency, c, a);
            }

            TopologySeamUtility.MarkOpenBoundaryVertices(vertices, triangles, boundaryVertices);
        }

'''
s = s[:start] + replacement + s[end:]
count_start = s.find('        private static void CountEdge(', s.index('        private static void AddNeighbor('))
if count_start >= 0:
    count_end = s.index('        private static void SmoothDisplacements(', count_start)
    s = s[:count_start] + s[count_end:]
p.write_text(s)

p = Path('Tests/Editor/HardeningAuditRegressionTests.cs')
s = p.read_text()
marker = '        private static Mesh CreateIndexedCube()\n'
tests = r'''        [Test]
        public void PublicBrushFacade_TinyMutationAndClearUseExactRevisionSemantics()
        {
            var go = new GameObject("tiny-revision-test");
            var mesh = CreateSimpleTriangle();
            try
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>();
                var deformer = go.AddComponent<LatticeDeformer>();
                deformer.Reset();
                int layer = deformer.AddLayer("Brush", MeshDeformerLayerType.Brush);
                deformer.ActiveLayerIndex = layer;
                deformer.EnsureDisplacementCapacity();

                int beforeSet = deformer.DeformationDataRevision;
                deformer.SetDisplacement(0, new Vector3(1e-7f, 0f, 0f));
                Assert.That(deformer.DeformationDataRevision, Is.Not.EqualTo(beforeSet));

                int beforeClear = deformer.DeformationDataRevision;
                deformer.ClearDisplacements();
                Assert.That(deformer.GetDisplacement(0), Is.EqualTo(Vector3.zero));
                Assert.That(deformer.DeformationDataRevision, Is.Not.EqualTo(beforeClear));

                int afterClear = deformer.DeformationDataRevision;
                deformer.ClearDisplacements();
                Assert.That(deformer.DeformationDataRevision, Is.EqualTo(afterClear));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ProfileNestedMutation_RefreshesIndependentInstanceCopy()
        {
            var mesh = CreateSimpleTriangle();
            var sourceGo = new GameObject("profile-source");
            var targetGo = new GameObject("profile-target");
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                var source = CreateSimpleDeformer(sourceGo, mesh);
                int brush = source.AddLayer("Brush", MeshDeformerLayerType.Brush);
                source.ActiveLayerIndex = brush;
                source.EnsureDisplacementCapacity();
                source.SetDisplacement(0, Vector3.up);
                profile.Capture(source.Groups, source.ActiveGroupIndex, mesh);

                var target = CreateSimpleDeformer(targetGo, mesh);
                Assert.That(target.UseProfile(profile), Is.True);
                Assert.That(target.Groups[0].Enabled, Is.True);

                profile.Groups[0].Enabled = false;
                Assert.That(target.Groups[0].Enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(sourceGo);
                UnityEngine.Object.DestroyImmediate(targetGo);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ProfileCompatibility_RechecksMeshMutatedInPlace()
        {
            var mesh = CreateSimpleTriangle();
            var sourceGo = new GameObject("compat-source");
            var targetGo = new GameObject("compat-target");
            var profile = ScriptableObject.CreateInstance<MeshDeformerProfile>();
            try
            {
                var source = CreateSimpleDeformer(sourceGo, mesh);
                profile.Capture(source.Groups, source.ActiveGroupIndex, mesh);
                var target = CreateSimpleDeformer(targetGo, mesh);
                Assert.That(target.UseProfile(profile), Is.True);
                Assert.That(target.GroupCount, Is.GreaterThan(0));

                mesh.triangles = new[] { 0, 2, 1 };
                Assert.That(target.EvaluateProfileCompatibility(), Is.EqualTo(ProfileCompatibilityStatus.TopologyMismatch));
                Assert.That(target.Groups[0].Name, Is.EqualTo("Incompatible Profile"));
                Assert.That(target.Groups[0].Enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
                UnityEngine.Object.DestroyImmediate(sourceGo);
                UnityEngine.Object.DestroyImmediate(targetGo);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ClosedMesh_CoincidentDoubleSidedSheetRemainsOpen()
        {
            var mesh = CreateDoubleSidedSheet();
            try
            {
                Assert.That(ClearanceQuery.TryCreate(mesh, Matrix4x4.identity, out var query), Is.True);
                Assert.That(query.IsClosedSurface, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FitTopology_NearbyOpenComponentsKeepTheirBoundaries()
        {
            var vertices = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(0f, 0f, 5e-6f), new Vector3(1f, 0f, 5e-6f), new Vector3(0f, 1f, 5e-6f)
            };
            var triangles = new[] { 0, 1, 2, 3, 4, 5 };
            var boundaries = new bool[vertices.Length];

            TopologySeamUtility.MarkOpenBoundaryVertices(vertices, triangles, boundaries);

            Assert.That(boundaries, Is.All.True);
        }

        private static LatticeDeformer CreateSimpleDeformer(GameObject go, Mesh mesh)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            var deformer = go.AddComponent<LatticeDeformer>();
            deformer.Reset();
            return deformer;
        }

        private static Mesh CreateSimpleTriangle()
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateDoubleSidedSheet()
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                Vector3.zero, Vector3.right, Vector3.up,
                Vector3.zero, Vector3.right, Vector3.up
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 5, 4 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

'''
if marker not in s:
    raise RuntimeError('test insertion marker not found')
s = s.replace(marker, tests + marker, 1)
p.write_text(s)
