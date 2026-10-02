#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>One synchronous edit's borrowed geometry; it never captures or evaluates a mesh.</summary>
    internal readonly struct VertexTransformGeometry
    {
        internal readonly int Count;
        private readonly Vector3[] _local;
        private readonly Vector3[] _world;
        private readonly Matrix4x4 _localToWorld;
        internal readonly Matrix4x4 WorldToLocal;

        internal VertexTransformGeometry(int count, Vector3[] local, Vector3[] world, Transform transform)
        {
            Count = count;
            _local = local;
            _world = world;
            _localToWorld = transform.localToWorldMatrix;
            WorldToLocal = transform.worldToLocalMatrix;
        }

        internal bool HasPosition(int index) => _local != null && index >= 0 && index < Count;
        internal Vector3 WorldPosition(int index) =>
            SkinnedVertexHelper.LocalToWorld(index, _world, _local, _localToWorld);
    }

    /// <summary>Move/rotation/scale mathematics without input, Undo, preview, or payload writes.</summary>
    internal readonly struct VertexTransformOperation
    {
        private enum Kind { Move, Rotate, Scale }
        private readonly Kind _kind;
        private readonly Vector3 _vector;
        private readonly Vector3 _pivot;
        private readonly Quaternion _rotation;
        private readonly Quaternion _inverseRotation;
        internal bool IsMove => _kind == Kind.Move;

        private VertexTransformOperation(Kind kind, Vector3 vector, Vector3 pivot, Quaternion rotation)
        {
            _kind = kind;
            _vector = vector;
            _pivot = pivot;
            _rotation = rotation;
            _inverseRotation = Quaternion.Inverse(rotation);
        }

        internal static VertexTransformOperation Move(Vector3 localDelta) =>
            new VertexTransformOperation(Kind.Move, localDelta, Vector3.zero, Quaternion.identity);

        internal static VertexTransformOperation Rotate(Vector3 pivot, Quaternion delta) =>
            new VertexTransformOperation(Kind.Rotate, Vector3.zero, pivot, delta);

        internal static VertexTransformOperation Scale(Vector3 pivot, Quaternion orientation, Vector3 relativeScale) =>
            new VertexTransformOperation(Kind.Scale, relativeScale, pivot, orientation);

        internal Vector3 LocalDelta(in VertexTransformGeometry geometry, int index, bool proportional, float influence)
        {
            if (IsMove) return _vector;
            Vector3 world = geometry.WorldPosition(index);
            Vector3 transformed;
            if (_kind == Kind.Rotate)
            {
                Quaternion rotation = proportional
                    ? Quaternion.Slerp(Quaternion.identity, _rotation, influence) : _rotation;
                transformed = rotation * (world - _pivot) + _pivot;
            }
            else
            {
                Vector3 scale = proportional ? Vector3.Lerp(Vector3.one, _vector, influence) : _vector;
                Vector3 offset = _inverseRotation * (world - _pivot);
                transformed = _pivot + _rotation * Vector3.Scale(offset, scale);
            }
            return geometry.WorldToLocal.MultiplyVector(transformed - world);
        }
    }

    /// <summary>Applies one prepared operation through the component's displacement API.</summary>
    internal static class VertexTransformApplication
    {
        internal static void Apply(LatticeDeformer owner, in VertexTransformGeometry geometry,
            HashSet<int> selected, VertexProportionalInfluenceCache influences,
            SkinnedVertexHelper.RestSpaceDeltaConverter restSpace, in VertexTransformOperation operation)
        {
            foreach (int index in selected)
            {
                if (!operation.IsMove && !geometry.HasPosition(index)) continue;
                ApplyOne(owner, geometry, restSpace, operation, index, proportional: false, influence: 1f);
            }

            if (influences == null) return;
            for (int index = 0; index < geometry.Count; index++)
            {
                if (selected.Contains(index) || (!operation.IsMove && !geometry.HasPosition(index))) continue;
                float influence = influences.GetInfluence(index);
                if (influence <= 0f) continue;
                ApplyOne(owner, geometry, restSpace, operation, index, proportional: true, influence);
            }
        }

        private static void ApplyOne(LatticeDeformer owner, in VertexTransformGeometry geometry,
            SkinnedVertexHelper.RestSpaceDeltaConverter restSpace, in VertexTransformOperation operation,
            int index, bool proportional, float influence)
        {
            Vector3 delta = operation.LocalDelta(geometry, index, proportional, influence);
            if (restSpace != null) delta = restSpace.ConvertOrFallback(index, delta);
            // Move weights the converted delta; rotate/scale weight their operation
            // before converting. Preserve that distinction for posed meshes.
            if (proportional && operation.IsMove) delta *= influence;
            owner.AddDisplacement(index, delta);
        }
    }
}
#endif
