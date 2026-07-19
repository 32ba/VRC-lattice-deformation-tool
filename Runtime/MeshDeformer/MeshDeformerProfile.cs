using System;
using System.Collections.Generic;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool
{
    public enum DeformerDataSource
    {
        Embedded = 0,
        Profile = 1
    }

    [CreateAssetMenu(
        fileName = "MeshDeformerProfile",
        menuName = "32ba/Lattice Deformation Tool/Deformer Profile")]
    public sealed class MeshDeformerProfile : ScriptableObject
    {
        [SerializeField] private List<DeformerGroup> _groups = new List<DeformerGroup>();
        [SerializeField] private int _activeGroupIndex;

        public IReadOnlyList<DeformerGroup> Groups => _groups ?? (_groups = new List<DeformerGroup>());

        public int ActiveGroupIndex
        {
            get
            {
                int count = _groups?.Count ?? 0;
                return count > 0 ? Mathf.Clamp(_activeGroupIndex, 0, count - 1) : 0;
            }
        }

        public void Capture(IReadOnlyList<DeformerGroup> groups, int activeGroupIndex)
        {
            var payload = DeformerProfilePayload.From(groups, activeGroupIndex);
            var copy = DeformerProfilePayload.Clone(payload);
            _groups = copy.Groups;
            _activeGroupIndex = copy.ActiveGroupIndex;
        }

        internal DeformerProfilePayload CreateIndependentPayload()
        {
            return DeformerProfilePayload.Clone(DeformerProfilePayload.From(_groups, _activeGroupIndex));
        }

        internal string GetContentFingerprint()
        {
            return JsonUtility.ToJson(DeformerProfilePayload.From(_groups, _activeGroupIndex));
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
