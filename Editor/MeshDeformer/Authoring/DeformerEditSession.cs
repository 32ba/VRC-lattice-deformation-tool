#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Owns the complete Undo snapshot and lifetime of one multi-frame edit.
    /// Group/layer references are identity tokens, never a borrowed evaluation view.
    /// Geometry and visible-pose buffers remain with the specialized handler.
    /// </summary>
    internal sealed class DeformerEditSession : IDisposable
    {
        private LatticeDeformer _owner;
        private readonly DeformerGroup _group;
        private readonly LatticeLayer _layer;
        private readonly LatticeAsset _settings;
        private readonly MeshDeformerLayerType _type;
        private readonly Mesh _source;
        private readonly SkinnedMeshRenderer _skinned;
        private readonly MeshFilter _filter;
        private readonly int _groupIndex, _layerIndex, _vertexCount, _topologyHash, _undoGroup;
        private bool _changed;
        private readonly string _label;

        internal bool IsActive => _owner != null;

        private DeformerEditSession(LatticeDeformer owner, in SerializedDeformerData data, LatticeLayer layer,
            Mesh source, int topologyHash, string label)
        {
            _owner = owner; _group = data.ActiveGroup; _layer = layer;
            _settings = layer.SerializedSettings; _type = layer.Type;
            _groupIndex = data.ActiveGroupIndex; _layerIndex = data.ActiveLayerIndex;
            _skinned = data.SkinnedRenderer; _filter = data.MeshFilter;
            _source = source; _vertexCount = source.vertexCount; _topologyHash = topologyHash;
            _label = label;
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            // RecordObject only captures the first frame's diff. A complete snapshot
            // also covers vertices/control points first touched by a later frame.
            Undo.RegisterCompleteObjectUndo(owner, label);
            Undo.undoRedoPerformed += Abandon;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        internal static DeformerEditSession TryBegin(LatticeDeformer owner, MeshDeformerLayerType type, string label)
        {
            if (owner == null || !owner.HasValidSerializedAuthoringData) return null;
            var data = SerializedDeformerReader.Read(owner);
            if (data.DataSource != DeformerDataSource.Embedded || data.ActiveLayers == null ||
                data.ActiveLayerIndex < 0 || data.ActiveLayerIndex >= data.ActiveLayers.Count) return null;
            var layer = data.ActiveLayers[data.ActiveLayerIndex];
            if (layer == null || layer.Type != type) return null;
            var source = data.SourceMesh;
            if (source == null || source.vertexCount == 0 || !HasExpectedRendererMesh(owner, data, source) ||
                (data.SourceVertexCount > 0 && data.SourceVertexCount != source.vertexCount)) return null;
            int topologyHash = SourceMeshTopology.Calculate(source);
            if (topologyHash == 0 || (data.SourceTopologyHash != 0 && data.SourceTopologyHash != topologyHash)) return null;
            return new DeformerEditSession(owner, data, layer, source, topologyHash, label);
        }

        // This cheap identity check is safe on Layout/Repaint. Full source and
        // payload validation is reserved for writes, never performed just to draw.
        internal bool MatchesTarget(LatticeDeformer owner)
        {
            if (!IsActive || !ReferenceEquals(owner, _owner) || _source == null) return false;
            var data = SerializedDeformerReader.Read(owner);
            return data.DataSource == DeformerDataSource.Embedded &&
                data.ActiveGroupIndex == _groupIndex && data.ActiveLayerIndex == _layerIndex &&
                ReferenceEquals(data.ActiveGroup, _group) && data.ActiveLayers != null &&
                _layerIndex >= 0 && _layerIndex < data.ActiveLayers.Count &&
                ReferenceEquals(data.ActiveLayers[_layerIndex], _layer) &&
                _layer.Type == _type && ReferenceEquals(_layer.SerializedSettings, _settings) &&
                ReferenceEquals(data.SourceMesh, _source) &&
                ReferenceEquals(data.SkinnedRenderer, _skinned) && ReferenceEquals(data.MeshFilter, _filter) &&
                _source.vertexCount == _vertexCount && HasExpectedRendererMesh(owner, data, _source);
        }

        internal bool TryPrepareWrite(LatticeDeformer owner)
        {
            if (MatchesTarget(owner) && Undo.GetCurrentGroup() == _undoGroup && owner.HasValidSerializedAuthoringData &&
                SourceMeshTopology.Calculate(_source) == _topologyHash)
            {
                // Prefab override Undo records are frame-based even when the object
                // has a complete snapshot. Keep each write in the gesture's group.
                if (PrefabUtility.IsPartOfPrefabInstance(owner)) Undo.RecordObject(owner, _label);
                return true;
            }
            Dispose();
            return false;
        }

        internal void RecordChange()
        {
            _changed = true;
            // Pair the frame's Undo record with its final Prefab override values.
            if (_owner != null) LatticePrefabUtility.MarkModified(_owner);
        }

        private static bool HasExpectedRendererMesh(LatticeDeformer owner, in SerializedDeformerData data, Mesh source)
        {
            Mesh displayed = data.SkinnedRenderer != null ? data.SkinnedRenderer.sharedMesh :
                data.MeshFilter != null ? data.MeshFilter.sharedMesh : null;
            return displayed != null && (ReferenceEquals(displayed, source) || ReferenceEquals(displayed, owner.RuntimeMesh));
        }

        internal bool TryCancel()
        {
            // Never roll back an intervening user operation. Such an interruption
            // finishes the gesture; its existing Undo entry remains independently usable.
            int currentGroup = Undo.GetCurrentGroup();
            // Unity starts a new, empty group before dispatching a KeyDown event.
            // Accept exactly that one boundary for Escape, not an arbitrary range
            // of later groups. An intervening operation adds another boundary.
            bool escapeBoundary = Event.current != null && Event.current.rawType == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape && currentGroup == _undoGroup + 1;
            if (!IsActive || (currentGroup != _undoGroup && !escapeBoundary) || !MatchesTarget(_owner) ||
                !_owner.HasValidSerializedAuthoringData || SourceMeshTopology.Calculate(_source) != _topologyHash)
            {
                Dispose();
                return false;
            }
            var owner = _owner;
            Abandon();
            Undo.RevertAllDownToGroup(_undoGroup);
            if (owner != null) LatticePreviewUtility.RefreshInteractiveDeformation(owner);
            return true;
        }

        public void Dispose()
        {
            var owner = _owner;
            Abandon();
            if (owner == null) return;
            if (_changed) LatticePrefabUtility.MarkModified(owner);
            Undo.FlushUndoRecordObjects();
            // A single complete snapshot needs no collapse. Collapsing here could
            // absorb another tool's operation performed while this gesture was open.
            if (Undo.GetCurrentGroup() == _undoGroup) Undo.IncrementCurrentGroup();
        }

        internal void Abandon()
        {
            Undo.undoRedoPerformed -= Abandon;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            _owner = null;
        }
    }
}
#endif
