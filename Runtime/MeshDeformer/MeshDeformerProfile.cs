using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum DeformerDataSource
    {
        Embedded = 0,
        Profile = 1
    }

    public enum ProfileCompatibilityStatus
    {
        ExactMatch = 0,
        CompatibleSourceDiffers = 1,
        TopologyMismatch = 2,
        InsufficientMetadata = 3
    }

    [Serializable]
    public sealed class MeshCompatibilityMetadata
    {
        private const int k_CurrentVersion = 1;

        [SerializeField] private int _version;
        [SerializeField] private int _vertexCount;
        [SerializeField] private long _indexCount;
        [SerializeField] private long _triangleCount;
        [SerializeField] private int _subMeshCount;
        [SerializeField] private int _bindPoseCount;
        [SerializeField] private string _blendShapeSignature = "";
        [SerializeField] private string _topologyHash = "";
        [SerializeField] private string _sourceAssetGuid = "";
        [SerializeField] private long _sourceAssetLocalId;

        public int VertexCount => _vertexCount;
        public long IndexCount => _indexCount;
        public long TriangleCount => _triangleCount;
        public int SubMeshCount => _subMeshCount;
        public int BindPoseCount => _bindPoseCount;
        public string BlendShapeSignature => _blendShapeSignature ?? "";
        public string TopologyHash => _topologyHash ?? "";
        public string SourceAssetGuid => _sourceAssetGuid ?? "";
        public long SourceAssetLocalId => _sourceAssetLocalId;
        public bool IsAvailable => _version == k_CurrentVersion && !string.IsNullOrEmpty(_topologyHash);

        public static MeshCompatibilityMetadata Capture(Mesh mesh, string assetGuid = "", long assetLocalId = 0)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            return new MeshCompatibilityMetadata
            {
                _version = k_CurrentVersion,
                _vertexCount = mesh.vertexCount,
                _indexCount = GetTotalIndexCount(mesh),
                _triangleCount = GetTriangleCount(mesh),
                _subMeshCount = mesh.subMeshCount,
                _bindPoseCount = mesh.bindposes?.Length ?? 0,
                _blendShapeSignature = ComputeBlendShapeSignature(mesh),
                _topologyHash = ComputeTopologyHash(mesh),
                _sourceAssetGuid = assetGuid ?? "",
                _sourceAssetLocalId = assetLocalId
            };
        }

        public ProfileCompatibilityStatus Evaluate(Mesh mesh, string assetGuid = "", long assetLocalId = 0)
        {
            if (!IsAvailable || mesh == null) return ProfileCompatibilityStatus.InsufficientMetadata;

            var current = Capture(mesh, assetGuid, assetLocalId);
            if (_vertexCount != current._vertexCount ||
                _indexCount != current._indexCount ||
                _triangleCount != current._triangleCount ||
                _subMeshCount != current._subMeshCount ||
                _bindPoseCount != current._bindPoseCount ||
                !string.Equals(BlendShapeSignature, current.BlendShapeSignature, StringComparison.Ordinal) ||
                !string.Equals(TopologyHash, current.TopologyHash, StringComparison.Ordinal))
            {
                return ProfileCompatibilityStatus.TopologyMismatch;
            }

            bool hasStoredIdentity = !string.IsNullOrEmpty(SourceAssetGuid);
            bool hasCurrentIdentity = !string.IsNullOrEmpty(assetGuid);
            bool sameIdentity = hasStoredIdentity && hasCurrentIdentity &&
                                string.Equals(SourceAssetGuid, assetGuid, StringComparison.Ordinal) &&
                                (_sourceAssetLocalId == 0 || assetLocalId == 0 || _sourceAssetLocalId == assetLocalId);
            return sameIdentity
                ? ProfileCompatibilityStatus.ExactMatch
                : ProfileCompatibilityStatus.CompatibleSourceDiffers;
        }

        internal void SetSourceAssetIdentity(string assetGuid, long assetLocalId)
        {
            _sourceAssetGuid = assetGuid ?? "";
            _sourceAssetLocalId = assetLocalId;
        }

        private static long GetTotalIndexCount(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++) count += (long)mesh.GetIndexCount(i);
            return count;
        }

        private static long GetTriangleCount(Mesh mesh)
        {
            long count = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles)
                {
                    count += (long)mesh.GetIndexCount(i) / 3L;
                }
            }
            return count;
        }

        private static string ComputeBlendShapeSignature(Mesh mesh)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(mesh.blendShapeCount);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    writer.Write(mesh.GetBlendShapeName(i) ?? "");
                    writer.Write(mesh.GetBlendShapeFrameCount(i));
                }
            }
            return ComputeSha256(stream.ToArray());
        }

        private static string ComputeTopologyHash(Mesh mesh)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                var vertices = mesh.vertices;
                writer.Write(vertices.Length);
                for (int i = 0; i < vertices.Length; i++)
                {
                    writer.Write(vertices[i].x);
                    writer.Write(vertices[i].y);
                    writer.Write(vertices[i].z);
                }

                writer.Write(mesh.subMeshCount);
                for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                {
                    writer.Write((int)mesh.GetTopology(subMesh));
                    var indices = mesh.GetIndices(subMesh);
                    writer.Write(indices.Length);
                    for (int i = 0; i < indices.Length; i++) writer.Write(indices[i]);
                }
            }
            return ComputeSha256(stream.ToArray());
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2"));
            return builder.ToString();
        }
    }

    [CreateAssetMenu(
        fileName = "MeshDeformerProfile",
        menuName = "32ba/Lattice Deformation Tool/Deformer Profile")]
    public sealed class MeshDeformerProfile : ScriptableObject
    {
        [SerializeField] private List<DeformerGroup> _groups = new List<DeformerGroup>();
        [SerializeField] private int _activeGroupIndex;
        [SerializeField] private MeshCompatibilityMetadata _compatibility;
        [SerializeField] private string _displayName = "";
        [SerializeField] private string _author = "";
        [SerializeField, TextArea] private string _description = "";
        [SerializeField, TextArea] private string _targetAsset = "";
        [SerializeField, TextArea] private string _notes = "";

        public IReadOnlyList<DeformerGroup> Groups => _groups ?? (_groups = new List<DeformerGroup>());
        public MeshCompatibilityMetadata Compatibility => _compatibility;
        public string DisplayName { get => _displayName ?? ""; set => _displayName = value ?? ""; }
        public string Author { get => _author ?? ""; set => _author = value ?? ""; }
        public string Description { get => _description ?? ""; set => _description = value ?? ""; }
        public string TargetAsset { get => _targetAsset ?? ""; set => _targetAsset = value ?? ""; }
        public string Notes { get => _notes ?? ""; set => _notes = value ?? ""; }

        public int ActiveGroupIndex
        {
            get
            {
                int count = _groups?.Count ?? 0;
                return count > 0 ? Mathf.Clamp(_activeGroupIndex, 0, count - 1) : 0;
            }
        }

        public void Capture(IReadOnlyList<DeformerGroup> groups, int activeGroupIndex, Mesh sourceMesh = null)
        {
            var payload = DeformerProfilePayload.From(groups, activeGroupIndex);
            var copy = DeformerProfilePayload.Clone(payload);
            _groups = copy.Groups;
            _activeGroupIndex = copy.ActiveGroupIndex;
            if (sourceMesh != null) _compatibility = MeshCompatibilityMetadata.Capture(sourceMesh);
        }

        public ProfileCompatibilityStatus EvaluateCompatibility(Mesh mesh, string assetGuid = "", long assetLocalId = 0)
        {
            return _compatibility == null
                ? ProfileCompatibilityStatus.InsufficientMetadata
                : _compatibility.Evaluate(mesh, assetGuid, assetLocalId);
        }

        public void SetSourceAssetIdentity(string assetGuid, long assetLocalId)
        {
            _compatibility?.SetSourceAssetIdentity(assetGuid, assetLocalId);
        }

        internal DeformerProfilePayload CreateIndependentPayload()
        {
            return DeformerProfilePayload.Clone(DeformerProfilePayload.From(_groups, _activeGroupIndex));
        }

        internal string GetContentFingerprint()
        {
            return JsonUtility.ToJson(DeformerProfilePayload.From(_groups, _activeGroupIndex));
        }

        internal string GetCompatibilityFingerprint()
        {
            return _compatibility == null ? "" : JsonUtility.ToJson(_compatibility);
        }
    }

    [Serializable]
    internal sealed class DeformerProfilePayload
    {
        [SerializeField] private List<DeformerGroup> _groups = new List<DeformerGroup>();
        [SerializeField] private int _activeGroupIndex;

        internal List<DeformerGroup> Groups => _groups ?? (_groups = new List<DeformerGroup>());
        internal int ActiveGroupIndex => _activeGroupIndex;

        internal static DeformerProfilePayload From(IReadOnlyList<DeformerGroup> groups, int activeGroupIndex)
        {
            var payload = new DeformerProfilePayload
            {
                _activeGroupIndex = activeGroupIndex
            };

            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    payload._groups.Add(groups[i]);
                }
            }

            return payload;
        }

        internal static DeformerProfilePayload Clone(DeformerProfilePayload source)
        {
            if (source == null) return new DeformerProfilePayload();
            var clone = JsonUtility.FromJson<DeformerProfilePayload>(JsonUtility.ToJson(source));
            return clone ?? new DeformerProfilePayload();
        }
    }
}
