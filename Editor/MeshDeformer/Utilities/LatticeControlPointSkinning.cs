#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Binds neutral lattice control points to the closest source triangles and evaluates
    /// one blended skinning matrix per control point. The expensive surface binding is
    /// rebuilt only when mesh topology or lattice layout changes.
    /// </summary>
    internal sealed class LatticeControlPointSkinning
    {
        private readonly struct Influence
        {
            internal readonly int BoneIndex;
            internal readonly float Weight;

            internal Influence(int boneIndex, float weight)
            {
                BoneIndex = boneIndex;
                Weight = weight;
            }
        }

        private readonly struct VertexSample
        {
            internal readonly int VertexIndex;
            internal readonly Vector3 Position;
            internal readonly BoneWeight BoneWeight;

            internal VertexSample(int vertexIndex, Vector3 position, BoneWeight boneWeight)
            {
                VertexIndex = vertexIndex;
                Position = position;
                BoneWeight = boneWeight;
            }
        }

        private Influence[][] _bindings = Array.Empty<Influence[]>();
        private Vector3[] _neutralControlPoints = Array.Empty<Vector3>();
        private VertexSample[] _vertexSamples = Array.Empty<VertexSample>();
        private Matrix4x4[] _matrices = Array.Empty<Matrix4x4>();
        private Matrix4x4[] _inverseMatrices = Array.Empty<Matrix4x4>();
        private int _bindingHash;
        private bool _bindingValid;

        internal bool IsValid { get; private set; }
        internal Bounds PosedControlBounds { get; private set; }
        internal Bounds PosedMeshBounds { get; private set; }
        internal bool HasPoseBounds { get; private set; }
        internal int BindingRefreshCountForTests { get; private set; }
        internal int PoseRefreshCountForTests { get; private set; }

        internal bool Update(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh,
            Mesh poseMesh,
            Bounds sourceBounds,
            Vector3Int gridSize,
            Matrix4x4 worldToSource)
        {
            if (renderer == null || sourceMesh == null || poseMesh == null)
            {
                IsValid = false;
                return false;
            }

            int bindingHash = ComputeBindingHash(
                renderer,
                sourceMesh,
                poseMesh,
                sourceBounds,
                gridSize);
            if (!_bindingValid || bindingHash != _bindingHash)
            {
                _bindingValid = BuildBindings(
                    renderer,
                    sourceMesh,
                    poseMesh,
                    sourceBounds,
                    gridSize);
                _bindingHash = bindingHash;
                BindingRefreshCountForTests++;
            }

            if (!_bindingValid)
            {
                IsValid = false;
                return false;
            }

            IsValid = BuildPoseMatrices(renderer, poseMesh.bindposes, worldToSource);
            PoseRefreshCountForTests++;
            return IsValid;
        }

        internal bool TryTransformPoint(int index, Vector3 sourceLocal, out Vector3 correctedSourceLocal)
        {
            if (!IsValid || index < 0 || index >= _matrices.Length)
            {
                correctedSourceLocal = sourceLocal;
                return false;
            }

            correctedSourceLocal = _matrices[index].MultiplyPoint3x4(sourceLocal);
            return IsFinite(correctedSourceLocal);
        }

        internal bool TryInverseTransformPoint(int index, Vector3 correctedSourceLocal, out Vector3 sourceLocal)
        {
            if (!IsValid || index < 0 || index >= _inverseMatrices.Length)
            {
                sourceLocal = correctedSourceLocal;
                return false;
            }

            sourceLocal = _inverseMatrices[index].MultiplyPoint3x4(correctedSourceLocal);
            return IsFinite(sourceLocal);
        }

        internal bool TryInverseTransformVector(int index, Vector3 correctedSourceVector, out Vector3 sourceVector)
        {
            if (!IsValid || index < 0 || index >= _inverseMatrices.Length)
            {
                sourceVector = correctedSourceVector;
                return false;
            }

            sourceVector = _inverseMatrices[index].MultiplyVector(correctedSourceVector);
            return IsFinite(sourceVector);
        }

        internal void Reset()
        {
            _bindings = Array.Empty<Influence[]>();
            _neutralControlPoints = Array.Empty<Vector3>();
            _vertexSamples = Array.Empty<VertexSample>();
            _matrices = Array.Empty<Matrix4x4>();
            _inverseMatrices = Array.Empty<Matrix4x4>();
            _bindingHash = 0;
            _bindingValid = false;
            IsValid = false;
            HasPoseBounds = false;
        }

        private bool BuildBindings(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh,
            Mesh poseMesh,
            Bounds bounds,
            Vector3Int gridSize)
        {
            int nx = Mathf.Max(1, gridSize.x);
            int ny = Mathf.Max(1, gridSize.y);
            int nz = Mathf.Max(1, gridSize.z);
            int controlCount = nx * ny * nz;
            var vertices = sourceMesh.vertices;
            var boneWeights = sourceMesh.boneWeights;
            if (vertices == null || vertices.Length == 0 ||
                boneWeights == null || boneWeights.Length != vertices.Length)
            {
                return false;
            }

            var bindings = new Influence[controlCount][];
            var neutralControlPoints = new Vector3[controlCount];
            var poseVertices = poseMesh.vertices;
            var poseWeights = poseMesh.boneWeights;
            if (poseVertices == null ||
                poseVertices.Length != vertices.Length ||
                poseWeights == null ||
                poseWeights.Length != poseVertices.Length)
            {
                poseVertices = vertices;
                poseWeights = boneWeights;
            }
            var vertexSamples = BuildVertexSamples(poseVertices, poseWeights);
            ApplyBlendShapes(renderer, poseMesh, vertexSamples);
            int index = 0;
            for (int z = 0; z < nz; z++)
            {
                float tz = nz > 1 ? (float)z / (nz - 1) : 0f;
                for (int y = 0; y < ny; y++)
                {
                    float ty = ny > 1 ? (float)y / (ny - 1) : 0f;
                    for (int x = 0; x < nx; x++, index++)
                    {
                        float tx = nx > 1 ? (float)x / (nx - 1) : 0f;
                        Vector3 neutral = bounds.min + Vector3.Scale(
                            bounds.size,
                            new Vector3(tx, ty, tz));
                        neutralControlPoints[index] = neutral;
                        if (!TryBuildNearestInfluences(
                                vertexSamples,
                                neutral,
                                out bindings[index]))
                        {
                            return false;
                        }
                    }
                }
            }

            _bindings = bindings;
            _neutralControlPoints = neutralControlPoints;
            _vertexSamples = vertexSamples;
            _matrices = new Matrix4x4[controlCount];
            _inverseMatrices = new Matrix4x4[controlCount];
            return true;
        }

        private bool BuildPoseMatrices(
            SkinnedMeshRenderer renderer,
            Matrix4x4[] bindposes,
            Matrix4x4 worldToSource)
        {
            var bones = renderer.bones;
            if (bones == null || bindposes == null || _bindings.Length == 0)
            {
                return false;
            }

            for (int controlIndex = 0; controlIndex < _bindings.Length; controlIndex++)
            {
                Matrix4x4 blended = default;
                var influences = _bindings[controlIndex];
                for (int influenceIndex = 0; influenceIndex < influences.Length; influenceIndex++)
                {
                    var influence = influences[influenceIndex];
                    if (influence.BoneIndex < 0 ||
                        influence.BoneIndex >= bones.Length ||
                        influence.BoneIndex >= bindposes.Length ||
                        bones[influence.BoneIndex] == null)
                    {
                        return false;
                    }

                    Matrix4x4 boneMatrix =
                        worldToSource *
                        bones[influence.BoneIndex].localToWorldMatrix *
                        bindposes[influence.BoneIndex];
                    AddWeighted(ref blended, boneMatrix, influence.Weight);
                }

                if (!IsFinite(blended) ||
                    !IsFinite(blended.determinant) ||
                    Mathf.Abs(blended.determinant) <= 1e-8f)
                {
                    return false;
                }

                Matrix4x4 inverse = blended.inverse;
                if (!IsFinite(inverse))
                {
                    return false;
                }

                _matrices[controlIndex] = blended;
                _inverseMatrices[controlIndex] = inverse;
            }

            HasPoseBounds = TryBuildPoseBounds(
                bones,
                bindposes,
                worldToSource,
                out Bounds posedControlBounds,
                out Bounds posedMeshBounds);
            PosedControlBounds = posedControlBounds;
            PosedMeshBounds = posedMeshBounds;
            return true;
        }

        private bool TryBuildPoseBounds(
            Transform[] bones,
            Matrix4x4[] bindposes,
            Matrix4x4 worldToSource,
            out Bounds controlBounds,
            out Bounds meshBounds)
        {
            controlBounds = default;
            meshBounds = default;
            bool hasControl = false;
            for (int i = 0; i < _neutralControlPoints.Length; i++)
            {
                Vector3 point = _matrices[i].MultiplyPoint3x4(_neutralControlPoints[i]);
                if (!IsFinite(point))
                {
                    return false;
                }

                Encapsulate(ref controlBounds, ref hasControl, point);
            }

            bool hasMesh = false;
            for (int i = 0; i < _vertexSamples.Length; i++)
            {
                if (!TrySkinVertex(
                        _vertexSamples[i],
                        bones,
                        bindposes,
                        worldToSource,
                        out Vector3 point))
                {
                    return false;
                }

                Encapsulate(ref meshBounds, ref hasMesh, point);
            }

            return hasControl &&
                   hasMesh &&
                   controlBounds.size.sqrMagnitude > 1e-12f &&
                   meshBounds.size.sqrMagnitude > 1e-12f;
        }

        private static VertexSample[] BuildVertexSamples(
            Vector3[] vertices,
            BoneWeight[] boneWeights)
        {
            const int maxRegularSamples = 1024;
            int stride = Mathf.Max(1, Mathf.CeilToInt((float)vertices.Length / maxRegularSamples));
            var indices = new HashSet<int>();
            for (int i = 0; i < vertices.Length; i += stride)
            {
                indices.Add(i);
            }
            indices.Add(vertices.Length - 1);

            int[] extremeIndices = { 0, 0, 0, 0, 0, 0 };
            for (int i = 1; i < vertices.Length; i++)
            {
                if (vertices[i].x < vertices[extremeIndices[0]].x) extremeIndices[0] = i;
                if (vertices[i].x > vertices[extremeIndices[1]].x) extremeIndices[1] = i;
                if (vertices[i].y < vertices[extremeIndices[2]].y) extremeIndices[2] = i;
                if (vertices[i].y > vertices[extremeIndices[3]].y) extremeIndices[3] = i;
                if (vertices[i].z < vertices[extremeIndices[4]].z) extremeIndices[4] = i;
                if (vertices[i].z > vertices[extremeIndices[5]].z) extremeIndices[5] = i;
            }

            for (int i = 0; i < extremeIndices.Length; i++)
            {
                indices.Add(extremeIndices[i]);
            }

            var sorted = new List<int>(indices);
            sorted.Sort();
            var samples = new VertexSample[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                int vertexIndex = sorted[i];
                samples[i] = new VertexSample(
                    vertexIndex,
                    vertices[vertexIndex],
                    boneWeights[vertexIndex]);
            }

            return samples;
        }

        private static void ApplyBlendShapes(
            SkinnedMeshRenderer renderer,
            Mesh mesh,
            VertexSample[] samples)
        {
            if (renderer == null ||
                mesh == null ||
                samples == null ||
                samples.Length == 0 ||
                mesh.blendShapeCount == 0)
            {
                return;
            }

            int vertexCount = mesh.vertexCount;
            var deltaVertices = new Vector3[vertexCount];
            var deltaNormals = new Vector3[vertexCount];
            var deltaTangents = new Vector3[vertexCount];
            var lowerSampleDeltas = new Vector3[samples.Length];

            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = renderer.GetBlendShapeWeight(shape);
                if (!IsFinite(weight) || Mathf.Abs(weight) <= 1e-5f)
                {
                    continue;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(shape);
                if (frameCount <= 0)
                {
                    continue;
                }

                int lowerFrame = 0;
                int upperFrame = 0;
                float t = 0f;
                float firstWeight = mesh.GetBlendShapeFrameWeight(shape, 0);
                if (weight <= firstWeight || frameCount == 1)
                {
                    t = Mathf.Abs(firstWeight) > Mathf.Epsilon
                        ? weight / firstWeight
                        : 0f;
                }
                else
                {
                    lowerFrame = frameCount - 1;
                    upperFrame = lowerFrame;
                    t = 1f;
                    for (int frame = 1; frame < frameCount; frame++)
                    {
                        float upperWeight = mesh.GetBlendShapeFrameWeight(shape, frame);
                        if (weight <= upperWeight)
                        {
                            lowerFrame = frame - 1;
                            upperFrame = frame;
                            float lowerWeight = mesh.GetBlendShapeFrameWeight(shape, lowerFrame);
                            t = Mathf.Abs(upperWeight - lowerWeight) > Mathf.Epsilon
                                ? Mathf.InverseLerp(lowerWeight, upperWeight, weight)
                                : 0f;
                            break;
                        }
                    }
                }

                mesh.GetBlendShapeFrameVertices(
                    shape,
                    lowerFrame,
                    deltaVertices,
                    deltaNormals,
                    deltaTangents);
                for (int i = 0; i < samples.Length; i++)
                {
                    lowerSampleDeltas[i] = deltaVertices[samples[i].VertexIndex];
                }

                if (upperFrame != lowerFrame)
                {
                    mesh.GetBlendShapeFrameVertices(
                        shape,
                        upperFrame,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents);
                }

                for (int i = 0; i < samples.Length; i++)
                {
                    Vector3 delta = upperFrame == lowerFrame
                        ? lowerSampleDeltas[i] * t
                        : Vector3.LerpUnclamped(
                            lowerSampleDeltas[i],
                            deltaVertices[samples[i].VertexIndex],
                            t);
                    samples[i] = new VertexSample(
                        samples[i].VertexIndex,
                        samples[i].Position + delta,
                        samples[i].BoneWeight);
                }
            }
        }

        private static bool TrySkinVertex(
            VertexSample sample,
            Transform[] bones,
            Matrix4x4[] bindposes,
            Matrix4x4 worldToSource,
            out Vector3 result)
        {
            result = Vector3.zero;
            float total = 0f;
            if (!AddSkinnedInfluence(
                    ref result, ref total, sample.Position,
                    sample.BoneWeight.boneIndex0, sample.BoneWeight.weight0,
                    bones, bindposes, worldToSource) ||
                !AddSkinnedInfluence(
                    ref result, ref total, sample.Position,
                    sample.BoneWeight.boneIndex1, sample.BoneWeight.weight1,
                    bones, bindposes, worldToSource) ||
                !AddSkinnedInfluence(
                    ref result, ref total, sample.Position,
                    sample.BoneWeight.boneIndex2, sample.BoneWeight.weight2,
                    bones, bindposes, worldToSource) ||
                !AddSkinnedInfluence(
                    ref result, ref total, sample.Position,
                    sample.BoneWeight.boneIndex3, sample.BoneWeight.weight3,
                    bones, bindposes, worldToSource))
            {
                return false;
            }

            if (!IsFinite(total) || total <= 1e-6f)
            {
                return false;
            }

            result /= total;
            return IsFinite(result);
        }

        private static bool AddSkinnedInfluence(
            ref Vector3 result,
            ref float total,
            Vector3 position,
            int boneIndex,
            float weight,
            Transform[] bones,
            Matrix4x4[] bindposes,
            Matrix4x4 worldToSource)
        {
            if (weight <= 0f)
            {
                return true;
            }

            if (!IsFinite(weight) ||
                boneIndex < 0 ||
                boneIndex >= bones.Length ||
                boneIndex >= bindposes.Length ||
                bones[boneIndex] == null)
            {
                return false;
            }

            Matrix4x4 matrix =
                worldToSource *
                bones[boneIndex].localToWorldMatrix *
                bindposes[boneIndex];
            result += matrix.MultiplyPoint3x4(position) * weight;
            total += weight;
            return true;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool hasPoint, Vector3 point)
        {
            if (!hasPoint)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasPoint = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }

        private static bool TryBuildNearestInfluences(
            VertexSample[] samples,
            Vector3 point,
            out Influence[] influences)
        {
            influences = null;
            if (samples == null ||
                samples.Length == 0 ||
                !IsFinite(point))
            {
                return false;
            }

            const int neighborCount = 4;
            var nearestIndices = new int[neighborCount] { -1, -1, -1, -1 };
            var nearestDistances = new float[neighborCount]
            {
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            };
            for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
            {
                float distance = (samples[sampleIndex].Position - point).sqrMagnitude;
                if (!IsFinite(distance) || distance >= nearestDistances[neighborCount - 1])
                {
                    continue;
                }

                int insertion = neighborCount - 1;
                while (insertion > 0 && distance < nearestDistances[insertion - 1])
                {
                    nearestDistances[insertion] = nearestDistances[insertion - 1];
                    nearestIndices[insertion] = nearestIndices[insertion - 1];
                    insertion--;
                }

                nearestDistances[insertion] = distance;
                nearestIndices[insertion] = sampleIndex;
            }

            if (nearestIndices[0] < 0)
            {
                return false;
            }

            var accumulated = new Dictionary<int, float>(12);
            if (nearestDistances[0] <= 1e-12f)
            {
                AddVertexWeights(
                    accumulated,
                    samples[nearestIndices[0]].BoneWeight,
                    1f);
            }
            else
            {
                float spatialTotal = 0f;
                var spatialWeights = new float[neighborCount];
                for (int i = 0; i < neighborCount && nearestIndices[i] >= 0; i++)
                {
                    spatialWeights[i] = 1f / Mathf.Max(Mathf.Sqrt(nearestDistances[i]), 1e-6f);
                    spatialTotal += spatialWeights[i];
                }

                if (!IsFinite(spatialTotal) || spatialTotal <= 1e-6f)
                {
                    return false;
                }

                for (int i = 0; i < neighborCount && nearestIndices[i] >= 0; i++)
                {
                    AddVertexWeights(
                        accumulated,
                        samples[nearestIndices[i]].BoneWeight,
                        spatialWeights[i] / spatialTotal);
                }
            }

            float total = 0f;
            foreach (var pair in accumulated)
            {
                if (pair.Key >= 0 && IsFinite(pair.Value) && pair.Value > 0f)
                {
                    total += pair.Value;
                }
            }

            if (!IsFinite(total) || total <= 1e-6f)
            {
                return false;
            }

            var boneIndices = new List<int>(accumulated.Keys);
            boneIndices.Sort();
            var result = new List<Influence>(boneIndices.Count);
            for (int i = 0; i < boneIndices.Count; i++)
            {
                int boneIndex = boneIndices[i];
                float weight = accumulated[boneIndex] / total;
                if (boneIndex >= 0 && IsFinite(weight) && weight > 1e-6f)
                {
                    result.Add(new Influence(boneIndex, weight));
                }
            }

            if (result.Count == 0)
            {
                return false;
            }

            influences = result.ToArray();
            return true;
        }

        private static void AddVertexWeights(
            Dictionary<int, float> accumulated,
            BoneWeight weight,
            float barycentricWeight)
        {
            AddInfluence(accumulated, weight.boneIndex0, weight.weight0 * barycentricWeight);
            AddInfluence(accumulated, weight.boneIndex1, weight.weight1 * barycentricWeight);
            AddInfluence(accumulated, weight.boneIndex2, weight.weight2 * barycentricWeight);
            AddInfluence(accumulated, weight.boneIndex3, weight.weight3 * barycentricWeight);
        }

        private static void AddInfluence(
            Dictionary<int, float> accumulated,
            int boneIndex,
            float weight)
        {
            if (boneIndex < 0 || !IsFinite(weight) || weight <= 0f)
            {
                return;
            }

            accumulated.TryGetValue(boneIndex, out float current);
            accumulated[boneIndex] = current + weight;
        }

        private static int ComputeBindingHash(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh,
            Mesh poseMesh,
            Bounds bounds,
            Vector3Int gridSize)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + sourceMesh.GetInstanceID();
                hash = hash * 31 + EditorUtility.GetDirtyCount(sourceMesh);
                hash = hash * 31 + sourceMesh.vertexCount;
                hash = hash * 31 + sourceMesh.subMeshCount;
                hash = hash * 31 + poseMesh.GetInstanceID();
                hash = hash * 31 + EditorUtility.GetDirtyCount(poseMesh);
                hash = hash * 31 + poseMesh.vertexCount;
                int blendShapeCount = poseMesh.blendShapeCount;
                hash = hash * 31 + blendShapeCount;
                for (int i = 0; i < blendShapeCount; i++)
                {
                    hash = hash * 31 + renderer.GetBlendShapeWeight(i).GetHashCode();
                }
                hash = hash * 31 + bounds.GetHashCode();
                hash = hash * 31 + gridSize.GetHashCode();
                return hash;
            }
        }

        private static void AddWeighted(ref Matrix4x4 target, Matrix4x4 value, float weight)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    target[row, column] += value[row, column] * weight;
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Matrix4x4 value)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!IsFinite(value[row, column]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
#endif
