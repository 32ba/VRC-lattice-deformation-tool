#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Owns temporary test-mode assignments and their restoration. The component
    /// owns its generated mesh; this session borrows it and never destroys it.
    /// At most one session controls a renderer, including across locked Inspectors.
    /// </summary>
    internal sealed class BlendShapeTestSession : IDisposable
    {
        private static readonly Dictionary<SkinnedMeshRenderer, BlendShapeTestSession> s_sessions = new();
        private LatticeDeformer _deformer;
        private SkinnedMeshRenderer _renderer;
        private Mesh _originalMesh;
        private Mesh _testMesh;
        private string[] _originalNames;
        private float[] _originalWeights;
        private float[] _serializedWeights;
        private GameObject _prefabRoot;
        private readonly HashSet<UnityEngine.Object> _prefabTargets = new();
        private readonly List<PropertyModification> _originalOverrides = new();

        internal bool IsActive { get; private set; }
        internal float Weight { get; private set; }

        private BlendShapeTestSession(LatticeDeformer deformer, SkinnedMeshRenderer renderer)
        {
            _deformer = deformer;
            _renderer = renderer;
            _originalMesh = renderer.sharedMesh;
            int count = _originalMesh != null ? _originalMesh.blendShapeCount : 0;
            _originalNames = new string[count];
            _originalWeights = new float[count];
            for (int i = 0; i < count; i++)
            {
                _originalNames[i] = _originalMesh.GetBlendShapeName(i);
                _originalWeights[i] = renderer.GetBlendShapeWeight(i);
            }
            using (var serialized = new SerializedObject(renderer))
            {
                var weights = serialized.FindProperty("m_BlendShapeWeights");
                _serializedWeights = new float[weights?.arraySize ?? 0];
                for (int i = 0; i < _serializedWeights.Length; i++)
                    _serializedWeights[i] = weights.GetArrayElementAtIndex(i).floatValue;
            }
            CapturePrefabOverrides();
        }

        internal static BlendShapeTestSession TryBegin(LatticeDeformer deformer, SkinnedMeshRenderer renderer)
        {
            if (deformer == null || renderer == null ||
                deformer.GetComponent<SkinnedMeshRenderer>() != renderer || !deformer.CanStartAuthoringEdit)
                return null;

            if (s_sessions.TryGetValue(renderer, out var previous))
                previous.Dispose();
            var session = new BlendShapeTestSession(deformer, renderer);
            session.IsActive = true;
            s_sessions.Add(renderer, session);
            AssemblyReloadEvents.beforeAssemblyReload += session.Dispose;
            try
            {
                deformer.InvalidateCache();
                session._testMesh = deformer.Deform(true);
                if (session._testMesh == null || !ReferenceEquals(renderer.sharedMesh, session._testMesh))
                {
                    session.Dispose();
                    return null;
                }
                return session;
            }
            catch
            {
                session._testMesh = deformer.RuntimeMesh;
                session.Dispose();
                throw;
            }
        }

        internal bool Owns(LatticeDeformer deformer) => IsActive && ReferenceEquals(_deformer, deformer);

        internal void SetWeight(float value)
        {
            if (!IsActive) return;
            Weight = value;
            ApplyWeight();
        }

        internal void Refresh()
        {
            if (!IsActive || _deformer == null || _renderer == null) return;
            var runtimeMesh = _deformer.RuntimeMesh;
            if (!ReferenceEquals(_renderer.sharedMesh, _testMesh) &&
                !ReferenceEquals(_renderer.sharedMesh, runtimeMesh))
            {
                Dispose();
                return;
            }
            if (runtimeMesh == null || ReadActiveGroup()?.BlendShapeOutput != BlendShapeOutputMode.OutputAsBlendShape)
                return;
            _testMesh = runtimeMesh;
            _renderer.sharedMesh = runtimeMesh;
            ApplyWeight();
        }

        private void ApplyWeight()
        {
            if (_deformer == null || _renderer == null || _testMesh == null) return;
            if (!ReferenceEquals(_renderer.sharedMesh, _testMesh))
            {
                Dispose();
                return;
            }
            string name = ReadActiveGroup()?.EffectiveBlendShapeName(_deformer.gameObject.name) ?? _deformer.gameObject.name;
            int index = _testMesh.GetBlendShapeIndex(name);
            if (index >= 0) _renderer.SetBlendShapeWeight(index, Weight);
        }

        public void Dispose()
        {
            if (!IsActive) return;
            IsActive = false;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            if (!ReferenceEquals(_renderer, null) && s_sessions.TryGetValue(_renderer, out var current) &&
                ReferenceEquals(current, this))
                s_sessions.Remove(_renderer);
            try
            {
                // An external assignment or a later owner is not ours to restore.
                if (_renderer == null) return;
                // Disable/Destroy may restore the component's mesh before the
                // Inspector ends. Its destroyed test mesh still identifies that
                // release; an external assignment while it is alive does not.
                bool ownerReleasedMesh = !ReferenceEquals(_testMesh, null) && _testMesh == null &&
                    (_deformer == null || _deformer.RuntimeMesh == null);
                bool ownsAssignment = ReferenceEquals(_renderer.sharedMesh, _testMesh) ||
                    (ownerReleasedMesh && ReferenceEquals(_renderer.sharedMesh, _originalMesh));
                if (!ownsAssignment) return;
                _renderer.sharedMesh = _originalMesh != null ? _originalMesh : null;
                RestoreWeights();
                RestorePrefabOverrides();
            }
            finally
            {
                Weight = 0f;
                _deformer = null;
                _renderer = null;
                _originalMesh = null;
                _testMesh = null;
                _originalNames = Array.Empty<string>();
                _originalWeights = Array.Empty<float>();
                _serializedWeights = Array.Empty<float>();
                _prefabRoot = null;
                _prefabTargets.Clear();
                _originalOverrides.Clear();
            }
        }

        private bool OriginalNamesUnchanged()
        {
            if (_originalMesh == null) return true;
            if (_originalMesh.blendShapeCount != _originalNames.Length) return false;
            for (int i = 0; i < _originalNames.Length; i++)
                if (_originalMesh.GetBlendShapeName(i) != _originalNames[i]) return false;
            return true;
        }

        private DeformerGroup ReadActiveGroup()
        {
            var data = _deformer.ReadResolvedData();
            return data.Groups != null && data.ActiveGroupIndex >= 0 && data.ActiveGroupIndex < data.Groups.Count
                ? data.Groups[data.ActiveGroupIndex] : null;
        }

        private void RestoreWeights()
        {
            float[] restored = _serializedWeights;
            if (!OriginalNamesUnchanged())
            {
                restored = new float[_originalMesh.blendShapeCount];
                for (int i = 0; i < _originalNames.Length; i++)
                {
                    if (string.IsNullOrEmpty(_originalNames[i])) continue;
                    int index = _originalMesh.GetBlendShapeIndex(_originalNames[i]);
                    if (index >= 0) restored[index] = _originalWeights[i];
                }
            }
            // SetBlendShapeWeight expands this array, even for originally empty
            // storage. Restore the full payload, not only its original entries.
            using var serialized = new SerializedObject(_renderer);
            var weights = serialized.FindProperty("m_BlendShapeWeights");
            if (weights == null) return;
            weights.arraySize = restored.Length;
            for (int i = 0; i < restored.Length; i++)
                weights.GetArrayElementAtIndex(i).floatValue = restored[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void CapturePrefabOverrides()
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(_renderer)) return;
            _prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(_renderer.gameObject);
            UnityEngine.Object source = _renderer;
            while (source != null && _prefabTargets.Add(source))
                source = PrefabUtility.GetCorrespondingObjectFromSource(source);
            foreach (var entry in PrefabUtility.GetPropertyModifications(_prefabRoot) ?? Array.Empty<PropertyModification>())
                if (IsOwnedProperty(entry))
                    _originalOverrides.Add(new PropertyModification
                    {
                        target = entry.target, propertyPath = entry.propertyPath,
                        value = entry.value, objectReference = entry.objectReference
                    });
        }

        private bool IsOwnedProperty(PropertyModification entry) => entry != null &&
            _prefabTargets.Contains(entry.target) &&
            (entry.propertyPath == "m_Mesh" || entry.propertyPath.StartsWith("m_BlendShapeWeights", StringComparison.Ordinal));

        private void RestorePrefabOverrides()
        {
            if (_prefabRoot == null) return;
            PrefabUtility.RecordPrefabInstancePropertyModifications(_renderer);
            var merged = new List<PropertyModification>();
            foreach (var entry in PrefabUtility.GetPropertyModifications(_prefabRoot) ?? Array.Empty<PropertyModification>())
                if (!IsOwnedProperty(entry)) merged.Add(entry);
            bool namesUnchanged = OriginalNamesUnchanged();
            if (namesUnchanged) merged.AddRange(_originalOverrides);
            PrefabUtility.SetPropertyModifications(_prefabRoot, merged.ToArray());
            if (!namesUnchanged)
            {
                // Original array-index overrides no longer name the same shapes.
                // Rebuild only these overrides from the restored named values.
                _renderer.sharedMesh = _originalMesh;
                RestoreWeights();
                PrefabUtility.RecordPrefabInstancePropertyModifications(_renderer);
            }
        }
    }
}
#endif
