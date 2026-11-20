#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    // Reads one display snapshot. Projection is supplied by the Scene View caller.
    internal readonly struct VertexPickingQuery
    {
        private readonly Vector3[] _local, _world, _normals;
        private readonly Matrix4x4 _matrix;
        private readonly Vector3? _camera;
        private readonly bool _cull;
        private readonly Func<Vector3, Vector2> _project;
        internal VertexPickingQuery(Vector3[] local, Vector3[] world, Vector3[] normals,
            Matrix4x4 matrix, Vector3? camera, bool cull, Func<Vector3, Vector2> project)
        {
            _local = local; _world = world; _normals = normals; _matrix = matrix;
            _camera = camera; _cull = cull; _project = project;
        }
        private Vector3 WorldPoint(int index) => SkinnedVertexHelper.LocalToWorld(index, _world, _local, _matrix);
        private bool FrontFacing(int index)
        {
            if (!_cull || _normals == null || index < 0 || index >= _normals.Length || !_camera.HasValue)
                return true;
            Vector3 normal = _matrix.MultiplyVector(_normals[index]).normalized;
            Vector3 view = (_camera.Value - WorldPoint(index)).normalized;
            return Vector3.Dot(normal, view) > 0f;
        }
        internal int Nearest(int count, Vector2 point, float maximumDistance)
        {
            int nearest = -1;
            float distance = maximumDistance;
            for (int i = 0; i < count; i++)
            {
                if (!FrontFacing(i)) continue;
                float candidate = Vector2.Distance(_project(WorldPoint(i)), point);
                if (candidate < distance) { distance = candidate; nearest = i; }
            }
            return nearest;
        }
        internal List<int> Inside(int count, Rect rectangle)
        {
            var result = new List<int>();
            for (int i = 0; i < count; i++)
                if (FrontFacing(i) && rectangle.Contains(_project(WorldPoint(i)))) result.Add(i);
            return result;
        }
        internal static Rect Rectangle(Vector2 a, Vector2 b) => new Rect(
            Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
#endif
