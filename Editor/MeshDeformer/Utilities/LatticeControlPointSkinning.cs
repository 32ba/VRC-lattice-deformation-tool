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
            internal readonly Vector3 Position;
            internal readonly BoneWeight BoneWeight;

            internal VertexSample(Vector3 position, BoneWeight boneWeight)
            {
                Position = position;
                BoneWeight = boneWeight;
            }
        }

        private Influence[][] _bindings = Array.Empty<Influence[]>();
        private Vector3[] _neutralControlPoints = Array.Empty<Vector3>();
        private Vector3[] _controlPointShapeOffsets = Array.Empty<Vector3>();
        private VertexSample[] _vertexSamples = Array.Empty<VertexSample>();
        private Vector3[] _shapeVertices = Array.Empty<Vector3>();
        private int[] _shapeSampleIndices = Array.Empty<int>();
        private Vector3[] _currentShapeSampleOffsets = Array.Empty<Vector3>();
        private Vector3[] _initialShapeSampleOffsets = Array.Empty<Vector3>();
        private Vector3[] _blendShapeLowerScratch = Array.Empty<Vector3>();
        private Vector3[] _blendShapeUpperScratch = Array.Empty<Vector3>();
        private Vector3[] _blendShapeNormalScratch = Array.Empty<Vector3>();
        private Vector3[] _blendShapeTangentScratch = Array.Empty<Vector3>();
        private int[] _blendShapeWeightIndices = Array.Empty<int>();
        private int _blendShapeWeightMappingHash;
        private int _initialShapeSampleHash;
        private Matrix4x4[] _matrices = Array.Empty<Matrix4x4>();
        private Matrix4x4[] _inverseMatrices = Array.Empty<Matrix4x4>();
        private int _bindingHash;
        private bool _bindingValid;
        private int _shapeOffsetHash;

        internal bool IsValid { get; private set; }
        internal Bounds PosedControlBounds { get; private set; }
        internal Bounds PosedMeshBounds { get; private set; }
        internal bool HasPoseBounds { get; private set; }
        internal int BindingRefreshCountForTests { get; private set; }
        internal int PoseRefreshCountForTests { get; private set; }

        internal bool TryGetBindingForTests(
            int controlIndex,
            out int[] boneIndices,
            out float[] weights)
        {
            if (controlIndex < 0 || controlIndex >= _bindings.Length ||
                _bindings[controlIndex] == null)
            {
                boneIndices = Array.Empty<int>();
                weights = Array.Empty<float>();
                return false;
            }

            Influence[] binding = _bindings[controlIndex];
            boneIndices = new int[binding.Length];
            weights = new float[binding.Length];
            for (int i = 0; i < binding.Length; i++)
            {
                boneIndices[i] = binding[i].BoneIndex;
                weights[i] = binding[i].Weight;
            }
            return true;
        }

        internal bool Update(
            SkinnedMeshRenderer renderer,
            Mesh sourceMesh,
            Mesh poseMesh,
            Bounds sourceBounds,
            Vector3Int gridSize,
            Matrix4x4 worldToSource,
            SkinnedMeshRenderer blendShapeWeightSource = null,
            float[] initialBlendShapeWeights = null)
        {
            if (renderer == null || sourceMesh == null || poseMesh == null)
            {
                IsValid = false;
                return false;
            }

            int bindingHash = ComputeBindingHash(
                sourceMesh,
                poseMesh,
                sourceBounds,
                gridSize);
            if (!_bindingValid || bindingHash != _bindingHash)
            {
                _bindingValid = BuildBindings(
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

            UpdateControlPointShapeOffsets(
                sourceMesh,
                sourceBounds,
                blendShapeWeightSource,
                initialBlendShapeWeights);

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

            correctedSourceLocal = _matrices[index].MultiplyPoint3x4(
                sourceLocal + _controlPointShapeOffsets[index]);
            return IsFinite(correctedSourceLocal);
        }

        internal bool TryInverseTransformPoint(int index, Vector3 correctedSourceLocal, out Vector3 sourceLocal)
        {
            if (!IsValid || index < 0 || index >= _inverseMatrices.Length)
            {
                sourceLocal = correctedSourceLocal;
                return false;
            }

            sourceLocal = _inverseMatrices[index].MultiplyPoint3x4(correctedSourceLocal) -
                          _controlPointShapeOffsets[index];
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
            _controlPointShapeOffsets = Array.Empty<Vector3>();
            _vertexSamples = Array.Empty<VertexSample>();
            _shapeVertices = Array.Empty<Vector3>();
            _shapeSampleIndices = Array.Empty<int>();
            _currentShapeSampleOffsets = Array.Empty<Vector3>();
            _initialShapeSampleOffsets = Array.Empty<Vector3>();
            _blendShapeLowerScratch = Array.Empty<Vector3>();
            _blendShapeUpperScratch = Array.Empty<Vector3>();
            _blendShapeNormalScratch = Array.Empty<Vector3>();
            _blendShapeTangentScratch = Array.Empty<Vector3>();
            _blendShapeWeightIndices = Array.Empty<int>();
            _blendShapeWeightMappingHash = 0;
            _initialShapeSampleHash = 0;
            _matrices = Array.Empty<Matrix4x4>();
            _inverseMatrices = Array.Empty<Matrix4x4>();
            _bindingHash = 0;
            _bindingValid = false;
            _shapeOffsetHash = 0;
            IsValid = false;
            HasPoseBounds = false;
        }

        private bool BuildBindings(
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
                poseVertices.Length == 0 ||
                poseWeights == null ||
                poseWeights.Length != poseVertices.Length)
            {
                return false;
            }
            // A topology-changing preview processor such as AAO may rebuild both the
            // vertices and the renderer's bone array. Always keep the proxy positions,
            // weights, bind poses, and bones together. Falling back to source weights
            // would interpret their indices against an unrelated proxy bone array.
            // When topology is unchanged, bind against the neutral source surface while
            // retaining the proxy's (possibly retargeted) bone indices. The preview mesh
            // may already contain active source BlendShapes; using those moved vertices
            // here would select different bones as a Shape slider changes.
            Vector3[] bindingPositions = poseVertices.Length == vertices.Length
                ? vertices
                : poseVertices;
            var vertexSamples = BuildVertexSamples(bindingPositions, poseWeights);
            Bounds baseBounds = CalculateBounds(vertices);
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
                        Vector3 bindingPoint = MapPointBetweenBounds(
                            neutral,
                            bounds,
                            baseBounds);
                        if (!TryBuildNearestInfluences(
                                vertexSamples,
                                bindingPoint,
                                out bindings[index]))
                        {
                            return false;
                        }
                    }
                }
            }

            _bindings = bindings;
            _neutralControlPoints = neutralControlPoints;
            _controlPointShapeOffsets = new Vector3[controlCount];
            _shapeOffsetHash = int.MinValue;
            _vertexSamples = vertexSamples;
            _shapeVertices = vertices;
            _shapeSampleIndices = BuildShapeSampleIndices(vertices);
            _currentShapeSampleOffsets = new Vector3[_shapeSampleIndices.Length];
            _initialShapeSampleOffsets = new Vector3[_shapeSampleIndices.Length];
            _initialShapeSampleHash = int.MinValue;
            _blendShapeLowerScratch = new Vector3[vertices.Length];
            _blendShapeUpperScratch = new Vector3[vertices.Length];
            _blendShapeNormalScratch = new Vector3[vertices.Length];
            _blendShapeTangentScratch = new Vector3[vertices.Length];
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
                Vector3 point = _matrices[i].MultiplyPoint3x4(
                    _neutralControlPoints[i] + _controlPointShapeOffsets[i]);
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
                    vertices[vertexIndex],
                    boneWeights[vertexIndex]);
            }

            return samples;
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

        private void UpdateControlPointShapeOffsets(
            Mesh sourceMesh,
            Bounds bounds,
            SkinnedMeshRenderer weightSource,
            float[] initialWeights)
        {
            EnsureBlendShapeWeightMapping(
                sourceMesh,
                weightSource != null ? weightSource.sharedMesh : null);
            int hash = ComputeShapeOffsetHash(
                sourceMesh,
                weightSource,
                initialWeights,
                _blendShapeWeightIndices,
                _blendShapeWeightMappingHash);
            if (_controlPointShapeOffsets.Length == _neutralControlPoints.Length &&
                hash == _shapeOffsetHash)
            {
                return;
            }

            _shapeOffsetHash = hash;
            if (_controlPointShapeOffsets.Length != _neutralControlPoints.Length)
                _controlPointShapeOffsets = new Vector3[_neutralControlPoints.Length];
            Array.Clear(_controlPointShapeOffsets, 0, _controlPointShapeOffsets.Length);
            if (sourceMesh == null || weightSource == null || sourceMesh.blendShapeCount == 0)
                return;

            if (_shapeVertices.Length == 0 || _shapeSampleIndices.Length == 0)
                return;
            Array.Clear(_currentShapeSampleOffsets, 0, _currentShapeSampleOffsets.Length);
            int initialSampleHash = ComputeInitialShapeSampleHash(sourceMesh, initialWeights);
            bool refreshInitialOffsets = initialSampleHash != _initialShapeSampleHash;
            if (refreshInitialOffsets)
            {
                _initialShapeSampleHash = initialSampleHash;
                Array.Clear(_initialShapeSampleOffsets, 0, _initialShapeSampleOffsets.Length);
            }
            int shapeCount = sourceMesh.blendShapeCount;
            for (int shape = 0; shape < shapeCount; shape++)
            {
                int weightIndex = shape < _blendShapeWeightIndices.Length
                    ? _blendShapeWeightIndices[shape]
                    : -1;
                float weight = weightIndex >= 0
                    ? weightSource.GetBlendShapeWeight(weightIndex)
                    : 0f;
                bool zeroHasShapeDelta =
                    sourceMesh.GetBlendShapeFrameCount(shape) > 0 &&
                    sourceMesh.GetBlendShapeFrameWeight(shape, 0) <= 0f;
                if (weightIndex >= 0 && IsFinite(weight) &&
                    (Mathf.Abs(weight) > 1e-5f || zeroHasShapeDelta))
                {
                    AccumulateBlendShapeSampleOffsets(
                        sourceMesh,
                        shape,
                        weight,
                        _currentShapeSampleOffsets);
                }
                float initialWeight = initialWeights != null && shape < initialWeights.Length
                    ? initialWeights[shape]
                    : 0f;
                if (refreshInitialOffsets && IsFinite(initialWeight) &&
                    (Mathf.Abs(initialWeight) > 1e-5f || zeroHasShapeDelta))
                {
                    AccumulateBlendShapeSampleOffsets(
                        sourceMesh,
                        shape,
                        initialWeight,
                        _initialShapeSampleOffsets);
                }
            }

            Bounds baseBounds = CalculateBounds(_shapeVertices);
            for (int control = 0; control < _neutralControlPoints.Length; control++)
            {
                Vector3 sourcePoint = MapPointBetweenBounds(
                    _neutralControlPoints[control],
                    bounds,
                    baseBounds);
                Vector3 currentOffset = InterpolateNearestOffset(
                    _shapeVertices,
                    _currentShapeSampleOffsets,
                    _shapeSampleIndices,
                    sourcePoint);
                Vector3 initialOffset = InterpolateNearestOffset(
                    _shapeVertices,
                    _initialShapeSampleOffsets,
                    _shapeSampleIndices,
                    sourcePoint);
                _controlPointShapeOffsets[control] = currentOffset - initialOffset;
            }
        }

        private static Bounds CalculateBounds(Vector3[] vertices)
        {
            var bounds = new Bounds(vertices[0], Vector3.zero);
            for (int vertex = 1; vertex < vertices.Length; vertex++)
                bounds.Encapsulate(vertices[vertex]);
            return bounds;
        }

        private static Vector3 MapPointBetweenBounds(Vector3 point, Bounds from, Bounds to)
        {
            Vector3 normalized = new Vector3(
                Mathf.Abs(from.size.x) > 1e-8f ? (point.x - from.min.x) / from.size.x : 0.5f,
                Mathf.Abs(from.size.y) > 1e-8f ? (point.y - from.min.y) / from.size.y : 0.5f,
                Mathf.Abs(from.size.z) > 1e-8f ? (point.z - from.min.z) / from.size.z : 0.5f);
            return to.min + Vector3.Scale(to.size, normalized);
        }

        private static int[] BuildShapeSampleIndices(Vector3[] vertices)
        {
            const int maxSamples = 1024;
            int stride = Mathf.Max(1, Mathf.CeilToInt((float)vertices.Length / maxSamples));
            var indices = new HashSet<int>();
            for (int vertex = 0; vertex < vertices.Length; vertex += stride)
                indices.Add(vertex);
            indices.Add(vertices.Length - 1);

            int[] extremeIndices = { 0, 0, 0, 0, 0, 0 };
            for (int vertex = 1; vertex < vertices.Length; vertex++)
            {
                if (vertices[vertex].x < vertices[extremeIndices[0]].x) extremeIndices[0] = vertex;
                if (vertices[vertex].x > vertices[extremeIndices[1]].x) extremeIndices[1] = vertex;
                if (vertices[vertex].y < vertices[extremeIndices[2]].y) extremeIndices[2] = vertex;
                if (vertices[vertex].y > vertices[extremeIndices[3]].y) extremeIndices[3] = vertex;
                if (vertices[vertex].z < vertices[extremeIndices[4]].z) extremeIndices[4] = vertex;
                if (vertices[vertex].z > vertices[extremeIndices[5]].z) extremeIndices[5] = vertex;
            }
            for (int i = 0; i < extremeIndices.Length; i++)
                indices.Add(extremeIndices[i]);

            var sorted = new List<int>(indices);
            sorted.Sort();
            return sorted.ToArray();
        }

        private static Vector3 InterpolateNearestOffset(
            Vector3[] vertices,
            Vector3[] sampleOffsets,
            int[] sampleIndices,
            Vector3 point)
        {
            const int neighborCount = 4;
            var nearest = new int[neighborCount] { -1, -1, -1, -1 };
            var distances = new float[neighborCount]
            {
                float.PositiveInfinity, float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity
            };
            for (int sample = 0; sample < sampleIndices.Length; sample++)
            {
                int vertex = sampleIndices[sample];
                float distance = (vertices[vertex] - point).sqrMagnitude;
                if (!IsFinite(distance) || distance >= distances[neighborCount - 1])
                    continue;
                int insertion = neighborCount - 1;
                while (insertion > 0 && distance < distances[insertion - 1])
                {
                    distances[insertion] = distances[insertion - 1];
                    nearest[insertion] = nearest[insertion - 1];
                    insertion--;
                }
                distances[insertion] = distance;
                nearest[insertion] = sample;
            }
            if (nearest[0] < 0)
                return Vector3.zero;
            if (distances[0] <= 1e-12f)
                return sampleOffsets[nearest[0]];
            Vector3 result = Vector3.zero;
            float total = 0f;
            for (int i = 0; i < neighborCount && nearest[i] >= 0; i++)
            {
                float spatialWeight = 1f / Mathf.Max(Mathf.Sqrt(distances[i]), 1e-6f);
                result += sampleOffsets[nearest[i]] * spatialWeight;
                total += spatialWeight;
            }
            return total > 1e-6f ? result / total : Vector3.zero;
        }

        private void AccumulateBlendShapeSampleOffsets(
            Mesh mesh,
            int shapeIndex,
            float weight,
            Vector3[] accumulated)
        {
            int frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            if (frameCount <= 0)
                return;
            float firstWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, 0);
            if (frameCount == 1 || weight <= firstWeight)
            {
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    0,
                    _blendShapeLowerScratch,
                    _blendShapeNormalScratch,
                    _blendShapeTangentScratch);
                float scale = Mathf.Abs(firstWeight) > Mathf.Epsilon ? weight / firstWeight : 0f;
                AccumulateSamples(_blendShapeLowerScratch, null, scale, accumulated);
                return;
            }
            int last = frameCount - 1;
            float lastWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, last);
            if (weight >= lastWeight)
            {
                // Unity does not linearly extrapolate the last two delta arrays here.
                // Past the final frame it scales only the final delta by the progress
                // through the final frame interval (verified against BakeMesh).
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    last,
                    _blendShapeLowerScratch,
                    _blendShapeNormalScratch,
                    _blendShapeTangentScratch);
                float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, last - 1);
                float scale = Mathf.Abs(lastWeight - lowerWeight) > Mathf.Epsilon
                    ? (weight - lowerWeight) / (lastWeight - lowerWeight)
                    : 1f;
                AccumulateSamples(_blendShapeLowerScratch, null, scale, accumulated);
                return;
            }
            for (int frame = 1; frame < frameCount; frame++)
            {
                float upperWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, frame);
                if (weight > upperWeight)
                    continue;
                int lowerFrame = frame - 1;
                float lowerWeight = mesh.GetBlendShapeFrameWeight(shapeIndex, lowerFrame);
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    lowerFrame,
                    _blendShapeLowerScratch,
                    _blendShapeNormalScratch,
                    _blendShapeTangentScratch);
                mesh.GetBlendShapeFrameVertices(
                    shapeIndex,
                    frame,
                    _blendShapeUpperScratch,
                    _blendShapeNormalScratch,
                    _blendShapeTangentScratch);
                float t = Mathf.InverseLerp(lowerWeight, upperWeight, weight);
                AccumulateSamples(
                    _blendShapeLowerScratch,
                    _blendShapeUpperScratch,
                    t,
                    accumulated);
                return;
            }
        }

        private void AccumulateSamples(
            Vector3[] lower,
            Vector3[] upper,
            float scaleOrInterpolation,
            Vector3[] accumulated)
        {
            for (int sample = 0; sample < _shapeSampleIndices.Length; sample++)
            {
                int vertex = _shapeSampleIndices[sample];
                Vector3 delta = upper == null
                    ? lower[vertex] * scaleOrInterpolation
                    : Vector3.LerpUnclamped(lower[vertex], upper[vertex], scaleOrInterpolation);
                accumulated[sample] += delta;
            }
        }

        private void EnsureBlendShapeWeightMapping(Mesh sourceMesh, Mesh weightMesh)
        {
            unchecked
            {
                int hash = sourceMesh != null ? sourceMesh.GetInstanceID() : 0;
                hash = hash * 31 + (sourceMesh != null ? EditorUtility.GetDirtyCount(sourceMesh) : 0);
                hash = hash * 31 + (sourceMesh != null ? sourceMesh.blendShapeCount : 0);
                hash = hash * 31 + (weightMesh != null ? weightMesh.GetInstanceID() : 0);
                hash = hash * 31 + (weightMesh != null ? EditorUtility.GetDirtyCount(weightMesh) : 0);
                hash = hash * 31 + (weightMesh != null ? weightMesh.blendShapeCount : 0);
                if (hash == _blendShapeWeightMappingHash &&
                    _blendShapeWeightIndices.Length == (sourceMesh != null
                        ? sourceMesh.blendShapeCount
                        : 0))
                {
                    return;
                }

                _blendShapeWeightMappingHash = hash;
                if (sourceMesh == null || weightMesh == null)
                {
                    _blendShapeWeightIndices = Array.Empty<int>();
                    return;
                }

                _blendShapeWeightIndices = new int[sourceMesh.blendShapeCount];
                for (int shape = 0; shape < _blendShapeWeightIndices.Length; shape++)
                {
                    string shapeName = sourceMesh.GetBlendShapeName(shape);
                    _blendShapeWeightIndices[shape] = weightMesh.GetBlendShapeIndex(shapeName);
                }
            }
        }

        private static int ComputeShapeOffsetHash(
            Mesh sourceMesh,
            SkinnedMeshRenderer renderer,
            float[] initialWeights,
            int[] weightIndices,
            int mappingHash)
        {
            unchecked
            {
                int hash = sourceMesh != null ? sourceMesh.GetInstanceID() : 0;
                if (sourceMesh == null || renderer == null)
                    return hash;
                hash = hash * 31 + mappingHash;
                int count = sourceMesh.blendShapeCount;
                for (int shape = 0; shape < count; shape++)
                {
                    int weightIndex = weightIndices != null && shape < weightIndices.Length
                        ? weightIndices[shape]
                        : -1;
                    hash = hash * 31 + weightIndex;
                    hash = hash * 31 + (weightIndex >= 0
                        ? renderer.GetBlendShapeWeight(weightIndex).GetHashCode()
                        : 0);
                    hash = hash * 31 + (initialWeights != null && shape < initialWeights.Length
                        ? initialWeights[shape].GetHashCode()
                        : 0);
                }
                return hash;
            }
        }

        private static int ComputeInitialShapeSampleHash(Mesh sourceMesh, float[] initialWeights)
        {
            unchecked
            {
                int hash = sourceMesh != null ? sourceMesh.GetInstanceID() : 0;
                hash = hash * 31 + (sourceMesh != null ? EditorUtility.GetDirtyCount(sourceMesh) : 0);
                int count = sourceMesh != null ? sourceMesh.blendShapeCount : 0;
                hash = hash * 31 + count;
                for (int shape = 0; shape < count; shape++)
                {
                    hash = hash * 31 + (initialWeights != null && shape < initialWeights.Length
                        ? initialWeights[shape].GetHashCode()
                        : 0);
                }
                return hash;
            }
        }

        private static int ComputeBindingHash(
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
                hash = hash * 31 + poseMesh.vertexCount;
                // Preview meshes are updated in place while source BlendShape weights
                // animate. Their vertices and dirty count change every frame, but the
                // vertex-to-bone relationship does not. Rebinding here makes control
                // points jump between nearby bones while a shape slider is dragged.
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
