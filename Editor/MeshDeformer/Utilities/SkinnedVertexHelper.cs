#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Computes world-space vertex positions that match the visual rendering.
    /// For SkinnedMeshRenderer, uses BakeMesh on the NDMF preview proxy renderer
    /// (which has the deformed mesh + bone skinning applied).
    /// </summary>
    internal static class SkinnedVertexHelper
    {
        private static Mesh s_bakeMesh;
        private static readonly List<Vector3> s_bakedVertices = new List<Vector3>();
        internal static bool StoreMovesInRestSpace { get; set; }
        internal static int WorldPositionBakeCountForTests { get; set; }

        static SkinnedVertexHelper()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseStaticResources;
        }

        [ExcludeFromCodeCoverage]
        internal static void ReleaseStaticResources()
        {
            if (s_bakeMesh != null)
            {
                Object.DestroyImmediate(s_bakeMesh);
                s_bakeMesh = null;
            }
            s_bakedVertices.Clear();
        }

        /// <summary>
        /// Computes world-space positions matching the rendered output.
        /// Returns world-space positions for SkinnedMeshRenderer (via proxy BakeMesh),
        /// or null for MeshRenderer (caller should use localToWorldMatrix).
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static Vector3[] ComputeWorldPositions(
            LatticeDeformer deformer,
            Vector3[] localVertices,
            Vector3[] reusableResult = null)
        {
            if (deformer == null || localVertices == null || localVertices.Length == 0)
                return null;

            if (!TryGetSkinnedRenderer(deformer, out var proxySMR))
                return null;

            // BakeMesh on the proxy (which has the deformed mesh + correct bones)
            if (s_bakeMesh == null)
            {
                s_bakeMesh = new Mesh();
                s_bakeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            UnityEngine.Profiling.Profiler.BeginSample("VertexSelection.BakeMesh");
            try
            {
                WorldPositionBakeCountForTests++;
                proxySMR.BakeMesh(s_bakeMesh);
            }
            finally
            {
                UnityEngine.Profiling.Profiler.EndSample();
            }

            if (s_bakeMesh.vertexCount != localVertices.Length)
                return null;

            // BakeMesh returns vertices in the SMR's local space (without scale).
            // Convert to world space using the proxy's transform.
            s_bakedVertices.Clear();
            s_bakeMesh.GetVertices(s_bakedVertices);
            var proxyTransform = proxySMR.transform;
            var matrix = proxyTransform.localToWorldMatrix;
            int count = s_bakedVertices.Count;
            var result = reusableResult != null && reusableResult.Length == count
                ? reusableResult
                : new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                result[i] = matrix.MultiplyPoint3x4(s_bakedVertices[i]);
            }

            return result;
        }

        /// <summary>
        /// Captures the posed mesh once and shares it between brush world-position
        /// visualization and raycasting for the current Scene GUI event.
        /// </summary>
        [ExcludeFromCodeCoverage]
        internal static bool TryCaptureBrushSnapshot(
            LatticeDeformer deformer,
            Vector3[] localVertices,
            Vector3[] reusableWorldPositions,
            out Vector3[] worldPositions,
            out Mesh bakedMesh,
            out Matrix4x4 bakedMeshMatrix)
        {
            worldPositions = null;
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;
            if (deformer == null || !TryGetSkinnedRendererReference(deformer, out var renderer))
                return false;

            return TryCaptureBrushSnapshot(
                renderer,
                localVertices,
                reusableWorldPositions,
                out worldPositions,
                out bakedMesh,
                out bakedMeshMatrix);
        }

        [ExcludeFromCodeCoverage]
        internal static bool TryCaptureBrushSnapshot(
            SkinnedMeshRenderer renderer,
            Vector3[] localVertices,
            Vector3[] reusableWorldPositions,
            out Vector3[] worldPositions,
            out Mesh bakedMesh,
            out Matrix4x4 bakedMeshMatrix)
        {
            worldPositions = null;
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;
            if (renderer == null || localVertices == null || localVertices.Length == 0)
                return false;

            if (s_bakeMesh == null)
            {
                s_bakeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            }

            WorldPositionBakeCountForTests++;
            renderer.BakeMesh(s_bakeMesh);
            if (s_bakeMesh.vertexCount != localVertices.Length) return false;

            s_bakedVertices.Clear();
            s_bakeMesh.GetVertices(s_bakedVertices);
            int count = s_bakedVertices.Count;
            worldPositions = reusableWorldPositions != null && reusableWorldPositions.Length == count
                ? reusableWorldPositions
                : new Vector3[count];
            bakedMeshMatrix = renderer.transform.localToWorldMatrix;
            for (int i = 0; i < count; i++)
                worldPositions[i] = bakedMeshMatrix.MultiplyPoint3x4(s_bakedVertices[i]);
            bakedMesh = s_bakeMesh;
            return true;
        }

        /// <summary>
        /// Returns the baked mesh and its transform for raycasting against the posed mesh.
        /// For SkinnedMeshRenderer, bakes the proxy (or original) SMR.
        /// Returns false for MeshRenderer (caller should raycast against source mesh + localToWorldMatrix).
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static bool TryGetBakedMeshForRaycast(LatticeDeformer deformer,
            out Mesh bakedMesh, out Matrix4x4 bakedMeshMatrix)
        {
            bakedMesh = null;
            bakedMeshMatrix = Matrix4x4.identity;

            if (deformer == null)
                return false;

            if (!TryGetSkinnedRenderer(deformer, out var targetSMR))
                return false;

            if (s_bakeMesh == null)
            {
                s_bakeMesh = new Mesh();
                s_bakeMesh.hideFlags = HideFlags.HideAndDontSave;
            }

            targetSMR.BakeMesh(s_bakeMesh);
            bakedMesh = s_bakeMesh;
            bakedMeshMatrix = targetSMR.transform.localToWorldMatrix;
            return true;
        }

        /// <summary>
        /// Converts a delta measured on the posed renderer back into the source mesh's rest space.
        /// Returns false and preserves the input delta when skinning data is unavailable or singular.
        /// </summary>
        internal static bool TryConvertDisplayedDeltaToRestSpace(
            LatticeDeformer deformer,
            int vertexIndex,
            Vector3 posedLocalDelta,
            out Vector3 restSpaceDelta)
        {
            restSpaceDelta = posedLocalDelta;
            var converter = CreateRestSpaceDeltaConverter(deformer);
            return converter != null &&
                   converter.TryConvert(vertexIndex, posedLocalDelta, out restSpaceDelta);
        }

        internal static Vector3 ConvertMoveDeltaForStorage(
            LatticeDeformer deformer,
            int vertexIndex,
            Vector3 localDelta)
        {
            if (!StoreMovesInRestSpace)
                return localDelta;

            var converter = CreateRestSpaceDeltaConverter(deformer);
            return converter != null ? converter.ConvertOrFallback(vertexIndex, localDelta) : localDelta;
        }

        internal static RestSpaceDeltaConverter CreateRestSpaceDeltaConverter(
            LatticeDeformer deformer)
        {
            if (deformer == null || !TryGetSkinnedRenderer(deformer, out var renderer))
            {
                return null;
            }

            var mesh = deformer.SourceMesh != null ? deformer.SourceMesh : renderer.sharedMesh;
            if (mesh == null) return null;

            var weights = mesh.boneWeights;
            var bindPoses = mesh.bindposes;
            var bones = renderer.bones;
            if (weights == null || weights.Length != mesh.vertexCount ||
                bindPoses == null || bindPoses.Length == 0 ||
                bones == null || bones.Length == 0)
            {
                return null;
            }

            int matrixCount = Mathf.Min(bones.Length, bindPoses.Length);
            var matrices = new Matrix4x4[matrixCount];
            var valid = new bool[matrixCount];
            var rendererWorldToLocal = renderer.transform.worldToLocalMatrix;
            for (int boneIndex = 0; boneIndex < matrixCount; boneIndex++)
            {
                if (bones[boneIndex] == null) continue;
                var matrix = rendererWorldToLocal *
                             bones[boneIndex].localToWorldMatrix *
                             bindPoses[boneIndex];
                bool finite = true;
                for (int row = 0; row < 4 && finite; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        if (!IsFinite(matrix[row, column]))
                        {
                            finite = false;
                            break;
                        }
                    }
                }
                matrices[boneIndex] = matrix;
                valid[boneIndex] = finite;
            }

            return new RestSpaceDeltaConverter(weights, matrices, valid);
        }

        internal sealed class RestSpaceDeltaConverter
        {
            private readonly BoneWeight[] _weights;
            private readonly Matrix4x4[] _matrices;
            private readonly bool[] _validMatrices;

            internal RestSpaceDeltaConverter(
                BoneWeight[] weights,
                Matrix4x4[] matrices,
                bool[] validMatrices)
            {
                _weights = weights;
                _matrices = matrices;
                _validMatrices = validMatrices;
            }

            internal Vector3 ConvertOrFallback(int vertexIndex, Vector3 posedLocalDelta)
            {
                return TryConvert(vertexIndex, posedLocalDelta, out var converted)
                    ? converted
                    : posedLocalDelta;
            }

            internal bool TryConvert(
                int vertexIndex,
                Vector3 posedLocalDelta,
                out Vector3 restSpaceDelta)
            {
                restSpaceDelta = posedLocalDelta;
                if (vertexIndex < 0 || vertexIndex >= _weights.Length) return false;

                var boneWeight = _weights[vertexIndex];
                var blended = new Matrix4x4();
                float totalWeight = 0f;
                if (!TryAccumulate(boneWeight.boneIndex0, boneWeight.weight0, ref blended, ref totalWeight) ||
                    !TryAccumulate(boneWeight.boneIndex1, boneWeight.weight1, ref blended, ref totalWeight) ||
                    !TryAccumulate(boneWeight.boneIndex2, boneWeight.weight2, ref blended, ref totalWeight) ||
                    !TryAccumulate(boneWeight.boneIndex3, boneWeight.weight3, ref blended, ref totalWeight) ||
                    !IsFinite(totalWeight) || totalWeight <= 1e-6f)
                {
                    return false;
                }

                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        blended[row, column] /= totalWeight;
                    }
                }

                float determinant = blended.determinant;
                if (!IsFinite(determinant) || Mathf.Abs(determinant) <= 1e-8f)
                    return false;

                var converted = blended.inverse.MultiplyVector(posedLocalDelta);
                if (!IsFinite(converted.x) || !IsFinite(converted.y) || !IsFinite(converted.z))
                    return false;

                restSpaceDelta = converted;
                return true;
            }

            private bool TryAccumulate(
                int boneIndex,
                float weight,
                ref Matrix4x4 blended,
                ref float totalWeight)
            {
                if (!IsFinite(weight) || weight < 0f) return false;
                if (weight <= 1e-6f) return true;
                if (boneIndex < 0 || boneIndex >= _matrices.Length || !_validMatrices[boneIndex])
                    return false;

                var matrix = _matrices[boneIndex];
                for (int row = 0; row < 4; row++)
                {
                    for (int column = 0; column < 4; column++)
                    {
                        blended[row, column] += matrix[row, column] * weight;
                    }
                }
                totalWeight += weight;
                return true;
            }
        }

        internal sealed class RestSpaceDeltaConverterCache
        {
            private LatticeDeformer _deformer;
            private SkinnedMeshRenderer _renderer;
            private Mesh _mesh;
            private int _meshDirtyCount;
            private int _rendererDirtyCount;
            private Matrix4x4 _rendererWorldToLocal;
            private Transform[] _bones;
            private Matrix4x4[] _boneMatrices;
            private RestSpaceDeltaConverter _converter;
            private int _proxyMappingRevision = -1;

            internal RestSpaceDeltaConverter Get(LatticeDeformer deformer)
            {
                if (IsCurrent(deformer)) return _converter;
                Rebuild(deformer);
                return _converter;
            }

            internal void Clear()
            {
                _deformer = null;
                _renderer = null;
                _mesh = null;
                _bones = null;
                _boneMatrices = null;
                _converter = null;
                _proxyMappingRevision = -1;
            }

            private bool IsCurrent(LatticeDeformer deformer)
            {
                if (_converter == null || deformer == null || deformer != _deformer ||
                    _renderer == null || _mesh == null ||
                    _proxyMappingRevision != LatticePreviewUtility.ProxyMappingRevision ||
                    !ReferenceEquals(
                        deformer.SourceMesh != null ? deformer.SourceMesh : _renderer.sharedMesh,
                        _mesh) ||
                    EditorUtility.GetDirtyCount(_mesh) != _meshDirtyCount ||
                    EditorUtility.GetDirtyCount(_renderer) != _rendererDirtyCount ||
                    _renderer.transform.worldToLocalMatrix != _rendererWorldToLocal ||
                    _bones == null || _boneMatrices == null || _bones.Length != _boneMatrices.Length)
                    return false;

                for (int i = 0; i < _bones.Length; i++)
                {
                    Matrix4x4 matrix = _bones[i] != null
                        ? _bones[i].localToWorldMatrix
                        : default;
                    if (matrix != _boneMatrices[i]) return false;
                }
                return true;
            }

            private void Rebuild(LatticeDeformer deformer)
            {
                Clear();
                if (deformer == null || !TryGetSkinnedRenderer(deformer, out var renderer)) return;
                Mesh mesh = deformer.SourceMesh != null ? deformer.SourceMesh : renderer.sharedMesh;
                if (mesh == null) return;

                var converter = CreateRestSpaceDeltaConverter(deformer);
                if (converter == null) return;
                var bones = renderer.bones;
                _deformer = deformer;
                _renderer = renderer;
                _mesh = mesh;
                _meshDirtyCount = EditorUtility.GetDirtyCount(mesh);
                _rendererDirtyCount = EditorUtility.GetDirtyCount(renderer);
                _rendererWorldToLocal = renderer.transform.worldToLocalMatrix;
                _proxyMappingRevision = LatticePreviewUtility.ProxyMappingRevision;
                _bones = bones;
                _boneMatrices = new Matrix4x4[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    _boneMatrices[i] = bones[i] != null ? bones[i].localToWorldMatrix : default;
                _converter = converter;
            }
        }

        private static bool TryGetSkinnedRenderer(
            LatticeDeformer deformer,
            out SkinnedMeshRenderer skinnedRenderer)
        {
            return TryGetSkinnedRendererReference(deformer, out skinnedRenderer) &&
                   skinnedRenderer.bones != null &&
                   skinnedRenderer.bones.Length > 0;
        }

        private static bool TryGetSkinnedRendererReference(
            LatticeDeformer deformer,
            out SkinnedMeshRenderer skinnedRenderer)
        {
            skinnedRenderer = null;
            if (deformer == null) return false;

            var originalRenderer = deformer.GetComponent<Renderer>();
            if (originalRenderer == null) return false;

            if (NDMFPreviewProxyUtility.TryGetProxyRenderer(originalRenderer, out var proxyRenderer))
                skinnedRenderer = proxyRenderer as SkinnedMeshRenderer;
            if (skinnedRenderer == null)
                skinnedRenderer = originalRenderer as SkinnedMeshRenderer;
            return skinnedRenderer != null;
        }

        internal static int ComputePoseStateHash(
            SkinnedMeshRenderer renderer,
            Transform[] bones)
        {
            if (renderer == null) return 0;

            unchecked
            {
                int hash = 17;
                hash = hash * 31 + renderer.GetInstanceID();
                hash = hash * 31 + renderer.transform.localToWorldMatrix.GetHashCode();
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? renderer.sharedMesh.GetInstanceID()
                    : 0);
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? EditorUtility.GetDirtyCount(renderer.sharedMesh)
                    : 0);
                hash = hash * 31 + (renderer.sharedMesh != null
                    ? renderer.sharedMesh.bounds.GetHashCode()
                    : 0);
                hash = hash * 31 + (renderer.rootBone != null
                    ? renderer.rootBone.localToWorldMatrix.GetHashCode()
                    : 0);

                int boneCount = bones?.Length ?? 0;
                hash = hash * 31 + boneCount;
                for (int i = 0; i < boneCount; i++)
                {
                    Transform bone = bones[i];
                    hash = hash * 31 + (bone != null
                        ? bone.localToWorldMatrix.GetHashCode()
                        : 0);
                }

                int blendShapeCount = renderer.sharedMesh != null
                    ? renderer.sharedMesh.blendShapeCount
                    : 0;
                hash = hash * 31 + blendShapeCount;
                for (int i = 0; i < blendShapeCount; i++)
                    hash = hash * 31 + renderer.GetBlendShapeWeight(i).GetHashCode();
                return hash;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Converts a local-space vertex position to world space,
        /// using the pre-computed world positions if available (SkinnedMeshRenderer),
        /// or localToWorldMatrix otherwise (MeshRenderer).
        /// </summary>
        public static Vector3 LocalToWorld(int vertexIndex, Vector3[] worldPositions,
            Vector3[] localVertices, Matrix4x4 localToWorld)
        {
            if (worldPositions != null && vertexIndex >= 0 && vertexIndex < worldPositions.Length)
                return worldPositions[vertexIndex];

            if (localVertices != null && vertexIndex >= 0 && vertexIndex < localVertices.Length)
                return localToWorld.MultiplyPoint3x4(localVertices[vertexIndex]);

            return Vector3.zero;
        }

        /// <summary>
        /// Converts a local-space vertex to world space using pre-computed world positions
        /// or falls back to localToWorldMatrix. Use this when you already have the local vertex value.
        /// </summary>
        public static Vector3 LocalToWorld(int vertexIndex, Vector3[] worldPositions,
            Vector3 localVertex, Matrix4x4 localToWorld)
        {
            if (worldPositions != null && vertexIndex >= 0 && vertexIndex < worldPositions.Length)
                return worldPositions[vertexIndex];

            return localToWorld.MultiplyPoint3x4(localVertex);
        }
    }
}
#endif
