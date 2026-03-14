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

            if (adjacency == null || vertices == null || startVertex < 0 || startVertex >= adjacency.Count)
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
                pq.Remove(pq.Min);

                // Skip if we've already found a shorter path
                if (distances.TryGetValue(current, out float knownDist) && currentDist > knownDist)
                {
                    continue;
                }

                if (current >= adjacency.Count || adjacency[current] == null)
                {
                    continue;
                }

                foreach (int neighbor in adjacency[current])
                {
                    float edgeLength = (vertices[neighbor] - vertices[current]).magnitude;
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
