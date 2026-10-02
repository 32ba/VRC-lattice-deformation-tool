#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Borrowed influence inputs for one brush application. Selection, culling,
    /// distance and falloff are independent of brush mode and payload writes.
    /// </summary>
    internal readonly struct BrushInfluenceQuery
    {
        private readonly Vector3[] _worldPositions;
        private readonly Matrix4x4 _localToWorld;
        private readonly Vector3 _center;
        private readonly float _radius;
        private readonly float _radiusSquared;
        private readonly BrushFalloffType _falloff;
        private readonly HashSet<int> _connected;
        private readonly Vector3[] _cullNormals;
        private readonly Vector3 _cameraForward;
        private readonly GeodesicDistanceCalculator.Workspace _surface;

        internal BrushInfluenceQuery(Vector3[] worldPositions, Matrix4x4 localToWorld,
            Vector3 center, float radius, BrushFalloffType falloff, HashSet<int> connected,
            Vector3[] cullNormals, Vector3 cameraForward, GeodesicDistanceCalculator.Workspace surface)
        {
            _worldPositions = worldPositions;
            _localToWorld = localToWorld;
            _center = center;
            _radius = radius;
            _radiusSquared = radius * radius;
            _falloff = falloff;
            _connected = connected;
            _cullNormals = cullNormals;
            _cameraForward = cameraForward;
            _surface = surface;
        }

        internal int CandidateCount(int vertexCount) => _surface != null ? _surface.VisitedCount : vertexCount;
        internal int CandidateAt(int iteration) => _surface != null ? _surface.GetVisitedVertex(iteration) : iteration;

        internal bool TryGetFalloff(int index, Vector3 localVertex, out float falloff)
        {
            falloff = 0f;
            if (_connected != null && !_connected.Contains(index)) return false;
            if (_cullNormals != null && index < _cullNormals.Length &&
                Vector3.Dot(_cullNormals[index], _cameraForward) > 0f) return false;

            float distance;
            if (_surface != null)
            {
                if (!_surface.TryGetDistance(index, out distance)) return false;
            }
            else
            {
                Vector3 world = _worldPositions != null && index >= 0 && index < _worldPositions.Length
                    ? _worldPositions[index] : _localToWorld.MultiplyPoint3x4(localVertex);
                float squared = (world - _center).sqrMagnitude;
                if (squared > _radiusSquared) return false;
                distance = Mathf.Sqrt(squared);
            }
            falloff = BrushDeformer.EvaluateFalloff(_falloff, distance / _radius);
            return true;
        }
    }
}
#endif
