#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Computes geodesic (surface) distances from a start vertex using Dijkstra's algorithm
    /// over the mesh adjacency graph with edge lengths as weights.
    /// </summary>
    internal static class GeodesicDistanceCalculator
    {
        internal sealed class Workspace
        {
            private float[] _distances = System.Array.Empty<float>();
            private int[] _generations = System.Array.Empty<int>();
            private int _generation;
            private int[] _heapVertices = System.Array.Empty<int>();
            private float[] _heapDistances = System.Array.Empty<float>();
            private int _heapCount;
            private int[] _visited = System.Array.Empty<int>();
            private int _visitedCount;
            internal int VisitedCount => _visitedCount;
            internal int GetVisitedVertex(int index) => _visited[index];

            internal void Begin(int vertexCount)
            {
                if (_distances.Length < vertexCount)
                {
                    _distances = new float[vertexCount];
                    _generations = new int[vertexCount];
                }
                if (_heapVertices.Length < Mathf.Max(4, vertexCount))
                {
                    int size = Mathf.Max(4, vertexCount);
                    _heapVertices = new int[size];
                    _heapDistances = new float[size];
                }
                _heapCount = 0;
                if (_visited.Length < vertexCount) _visited = new int[vertexCount];
                _visitedCount = 0;
                if (++_generation == int.MaxValue)
                {
                    System.Array.Clear(_generations, 0, _generations.Length);
                    _generation = 1;
                }
            }

            internal bool TryGetDistance(int vertex, out float distance)
            {
                if (vertex >= 0 && vertex < _generations.Length &&
                    _generations[vertex] == _generation)
                {
                    distance = _distances[vertex];
                    return true;
                }
                distance = 0f;
                return false;
            }

            internal void SetDistance(int vertex, float distance)
            {
                if (_generations[vertex] != _generation)
                    _visited[_visitedCount++] = vertex;
                _generations[vertex] = _generation;
                _distances[vertex] = distance;
            }

            internal void Push(int vertex, float distance)
            {
                if (_heapCount == _heapVertices.Length)
                {
                    int size = _heapCount * 2;
                    System.Array.Resize(ref _heapVertices, size);
                    System.Array.Resize(ref _heapDistances, size);
                }
                int child = _heapCount++;
                while (child > 0)
                {
                    int parent = (child - 1) >> 1;
                    if (_heapDistances[parent] < distance ||
                        (_heapDistances[parent] == distance && _heapVertices[parent] <= vertex)) break;
                    _heapVertices[child] = _heapVertices[parent];
                    _heapDistances[child] = _heapDistances[parent];
                    child = parent;
                }
                _heapVertices[child] = vertex;
                _heapDistances[child] = distance;
            }

            internal bool TryPop(out int vertex, out float distance)
            {
                if (_heapCount == 0)
                {
                    vertex = -1;
                    distance = 0f;
                    return false;
                }
                vertex = _heapVertices[0];
                distance = _heapDistances[0];
                int last = --_heapCount;
                if (last == 0) return true;
                int movedVertex = _heapVertices[last];
                float movedDistance = _heapDistances[last];
                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= last) break;
                    int right = left + 1;
                    int child = right < last &&
                        (_heapDistances[right] < _heapDistances[left] ||
                         (_heapDistances[right] == _heapDistances[left] &&
                          _heapVertices[right] < _heapVertices[left])) ? right : left;
                    if (_heapDistances[child] > movedDistance ||
                        (_heapDistances[child] == movedDistance &&
                         _heapVertices[child] >= movedVertex)) break;
                    _heapVertices[parent] = _heapVertices[child];
                    _heapDistances[parent] = _heapDistances[child];
                    parent = child;
                }
                _heapVertices[parent] = movedVertex;
                _heapDistances[parent] = movedDistance;
                return true;
            }
        }

        internal static bool ComputeDistances(
            int startVertex,
            float maxDistance,
            MeshAdjacency adjacency,
            Vector3[] vertices,
            Workspace workspace)
        {
            if (adjacency == null || vertices == null || workspace == null ||
                startVertex < 0 || startVertex >= adjacency.VertexCount ||
                startVertex >= vertices.Length || float.IsNaN(maxDistance) || maxDistance < 0f)
                return false;

            workspace.Begin(vertices.Length);
            workspace.SetDistance(startVertex, 0f);
            workspace.Push(startVertex, 0f);
            while (workspace.TryPop(out int current, out float currentDistance))
            {
                if (!workspace.TryGetDistance(current, out float best) || currentDistance != best)
                    continue;
                for (int edge = adjacency.GetNeighborStart(current);
                     edge < adjacency.GetNeighborEnd(current); edge++)
                {
                    int neighbor = adjacency.GetNeighbor(edge);
                    if (neighbor < 0 || neighbor >= vertices.Length) continue;
                    float edgeLength = (vertices[neighbor] - vertices[current]).magnitude;
                    if (float.IsNaN(edgeLength) || float.IsInfinity(edgeLength)) continue;
                    float candidate = currentDistance + edgeLength;
                    if (candidate > maxDistance) continue;
                    if (!workspace.TryGetDistance(neighbor, out float oldDistance) || candidate < oldDistance)
                    {
                        workspace.SetDistance(neighbor, candidate);
                        workspace.Push(neighbor, candidate);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Computes geodesic distances from startVertex to all reachable vertices within maxDistance.
        /// </summary>
        /// <param name="startVertex">The vertex to compute distances from.</param>
        /// <param name="maxDistance">Maximum geodesic distance to compute.</param>
        /// <param name="adjacency">Per-vertex adjacency sets (from BuildAdjacency).</param>
        /// <param name="vertices">Mesh vertex positions.</param>
        /// <returns>Dictionary mapping vertex index to geodesic distance. Only includes vertices within maxDistance.</returns>
        public static Dictionary<int, float> ComputeDistances(
            int startVertex,
            float maxDistance,
            List<HashSet<int>> adjacency,
            Vector3[] vertices)
        {
            var distances = new Dictionary<int, float>();

            if (adjacency == null || vertices == null ||
                startVertex < 0 || startVertex >= adjacency.Count || startVertex >= vertices.Length)
            {
                return distances;
            }

            // Min-heap priority queue: (distance, vertexIndex)
            var pq = new SortedSet<(float dist, int vertex)>(Comparer<(float dist, int vertex)>.Create(
                (a, b) =>
                {
                    int cmp = a.dist.CompareTo(b.dist);
                    return cmp != 0 ? cmp : a.vertex.CompareTo(b.vertex);
                }));

            distances[startVertex] = 0f;
            pq.Add((0f, startVertex));

            while (pq.Count > 0)
            {
                var (currentDist, current) = pq.Min;
                pq.Remove((currentDist, current));

                if (current < 0 || current >= adjacency.Count || current >= vertices.Length ||
                    adjacency[current] == null)
                {
                    continue;
                }

                foreach (int neighbor in adjacency[current])
                {
                    if (neighbor < 0 || neighbor >= adjacency.Count || neighbor >= vertices.Length)
                    {
                        continue;
                    }

                    float edgeLength = (vertices[neighbor] - vertices[current]).magnitude;
                    if (float.IsNaN(edgeLength) || float.IsInfinity(edgeLength))
                    {
                        continue;
                    }
                    float newDist = currentDist + edgeLength;

                    if (newDist > maxDistance)
                    {
                        continue;
                    }

                    if (!distances.TryGetValue(neighbor, out float existingDist) || newDist < existingDist)
                    {
                        // Remove old entry if it exists (SortedSet requires re-insert)
                        if (distances.ContainsKey(neighbor))
                        {
                            pq.Remove((existingDist, neighbor));
                        }

                        distances[neighbor] = newDist;
                        pq.Add((newDist, neighbor));
                    }
                }
            }

            return distances;
        }
    }
}
#endif
