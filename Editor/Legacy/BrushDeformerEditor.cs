#if UNITY_EDITOR
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.SceneManagement;
using UnityEngine;
using Net._32Ba.LatticeDeformationTool;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [CustomEditor(typeof(BrushDeformer))]
    [ExcludeFromCodeCoverage]
    public sealed class BrushDeformerEditor : UnityEditor.Editor
    {
        private SerializedProperty _skinnedRendererProp;
        private SerializedProperty _meshFilterProp;
        private SerializedProperty _recalcNormalsProp;
        private SerializedProperty _recalcTangentsProp;
        private SerializedProperty _recalcBoundsProp;
        private SerializedProperty _recalcBoneWeightsProp;
        private SerializedProperty _weightTransferSettingsProp;

        private string _migrationMessage;
        private MessageType _migrationMessageType;

        private static bool s_showOptions = false;
        private static bool s_showWeightTransferSettings = false;

        private void OnEnable()
        {
            _skinnedRendererProp = serializedObject.FindProperty("_skinnedMeshRenderer");
            _meshFilterProp = serializedObject.FindProperty("_meshFilter");
            _recalcNormalsProp = serializedObject.FindProperty("_recalculateNormals");
            _recalcTangentsProp = serializedObject.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serializedObject.FindProperty("_recalculateBounds");
            _recalcBoneWeightsProp = serializedObject.FindProperty("_recalculateBoneWeights");
            _weightTransferSettingsProp = serializedObject.FindProperty("_weightTransferSettings");

            AutoAssignLocalRendererReferences();
            LatticeLocalization.LanguageChanged += Repaint;
            ReleaseChecker.OnUpdateCheckCompleted += Repaint;
        }

        private void OnDisable()
        {
            LatticeLocalization.LanguageChanged -= Repaint;
            ReleaseChecker.OnUpdateCheckCompleted -= Repaint;
        }

        public override void OnInspectorGUI()
        {
            AutoAssignLocalRendererReferences();
            serializedObject.Update();

            DrawLegacyMigration();
            EditorGUILayout.Space();

            bool hasSkinnedAssigned = _skinnedRendererProp != null && !_skinnedRendererProp.hasMultipleDifferentValues && _skinnedRendererProp.objectReferenceValue != null;
            bool hasMeshAssigned = _meshFilterProp != null && !_meshFilterProp.hasMultipleDifferentValues && _meshFilterProp.objectReferenceValue != null;

            DrawLanguageSelector();
            EditorGUILayout.Space();
            ReleaseNotificationGUI.Draw();

            using (new EditorGUI.DisabledScope(hasMeshAssigned))
            {
                EditorGUILayout.PropertyField(_skinnedRendererProp, LatticeLocalization.Content(LocKey.SkinnedMeshSource));
            }
            using (new EditorGUI.DisabledScope(hasSkinnedAssigned))
            {
                EditorGUILayout.PropertyField(_meshFilterProp, LatticeLocalization.Content(LocKey.StaticMeshSource));
            }

            EditorGUILayout.Space();

            var deformer = (BrushDeformer)target;
            int dispCount = deformer.DisplacementCount;
            bool hasBrushData = deformer.HasDisplacements();
            EditorGUILayout.LabelField(LatticeLocalization.Content(LocKey.BrushDeformation), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.VertexCount), dispCount.ToString());
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.HasDisplacements), hasBrushData ? LatticeLocalization.Tr(LocKey.Yes) : LatticeLocalization.Tr(LocKey.No));

            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.ClearAllDisplacements)))
            {
                ClearAllDisplacements(
                    deformer,
                    LatticePreviewUtility.ShouldAssignRuntimeMesh());
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, LatticeLocalization.Tr(LocKey.MeshRebuildOptions));
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_recalcNormalsProp);
                EditorGUILayout.PropertyField(_recalcTangentsProp);
                EditorGUILayout.PropertyField(_recalcBoundsProp);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            bool hasSkinnedRenderer = _skinnedRendererProp != null &&
                !_skinnedRendererProp.hasMultipleDifferentValues &&
                _skinnedRendererProp.objectReferenceValue != null;

            using (new EditorGUI.DisabledScope(!hasSkinnedRenderer))
            {
                EditorGUILayout.PropertyField(_recalcBoneWeightsProp, LatticeLocalization.Content(LocKey.RecalculateBoneWeights));
            }

            if (!hasSkinnedRenderer && _recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue)
            {
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.BoneWeightRequiresSMR), MessageType.Info);
            }

            if (_recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue && hasSkinnedRenderer)
            {
                EditorGUI.indentLevel++;
                s_showWeightTransferSettings = EditorGUILayout.Foldout(s_showWeightTransferSettings, LatticeLocalization.Tr(LocKey.WeightTransferSettings), true);
                if (s_showWeightTransferSettings && _weightTransferSettingsProp != null)
                {
                    EditorGUI.indentLevel++;
                    DrawWeightTransferSettings();
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
            }

            bool modified = serializedObject.ApplyModifiedProperties();
            if (modified)
            {
                foreach (var instance in EnumerateTargets())
                {
                    bool assignRuntimeMesh = LatticePreviewUtility.ShouldAssignRuntimeMesh();
                    instance.Deform(assignRuntimeMesh);
                }
                LatticePreviewUtility.RequestSceneRepaint();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(LatticeLocalization.Tr(LocKey.OpenBrushEditor)))
            {
                ToolManager.SetActiveTool<MeshDeformerTool>();
                LatticePreviewUtility.RequestSceneRepaint();
            }
        }

        private void DrawLegacyMigration()
        {
            EditorGUILayout.HelpBox(
                LatticeLocalization.Tr(LocKey.LegacyBrushMigrationWarning),
                MessageType.Warning);

            if (GUILayout.Button(LatticeLocalization.Content(LocKey.MigrateLegacyBrush)))
            {
                bool succeeded = TryMigrateAll(EnumerateTargets(), out string failure);

                if (succeeded)
                {
                    _migrationMessage = LatticeLocalization.Tr(LocKey.LegacyBrushMigrationSucceeded);
                    _migrationMessageType = MessageType.Info;
                }
                else
                {
                    _migrationMessage = string.Format(
                        LatticeLocalization.Tr(LocKey.LegacyBrushMigrationFailed),
                        failure ?? "Unknown error");
                    _migrationMessageType = MessageType.Error;
                }

                serializedObject.UpdateIfRequiredOrScript();
                Repaint();
            }

            if (!string.IsNullOrEmpty(_migrationMessage))
            {
                EditorGUILayout.HelpBox(_migrationMessage, _migrationMessageType);
            }
        }

        internal static bool TryMigrateAll(
            IEnumerable<BrushDeformer> instances,
            out string failure)
        {
            failure = null;
            if (instances == null)
            {
                failure = "No legacy Brush Deformer was selected.";
                return false;
            }

            Undo.IncrementCurrentGroup();
            int batchUndoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Migrate Legacy Brush Deformers");
            bool foundInstance = false;
            var runtimePreviewSnapshots = new Dictionary<BrushDeformer, RuntimePreviewSnapshot>();

            try
            {
                foreach (var instance in instances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    foundInstance = true;
                    if (TryCaptureRuntimePreview(instance, out var runtimePreview))
                    {
                        runtimePreviewSnapshots[instance] = runtimePreview;
                    }
                    if (LegacyBrushDeformerMigration.TryMigrate(instance, out _, out var error))
                    {
                        continue;
                    }

                    failure = $"{instance.name}: {error}";
                    RollBackBatch(
                        batchUndoGroup,
                        runtimePreviewSnapshots,
                        ref failure);
                    return false;
                }

                if (!foundInstance)
                {
                    failure = "No legacy Brush Deformer was selected.";
                    RollBackBatch(
                        batchUndoGroup,
                        runtimePreviewSnapshots,
                        ref failure);
                    return false;
                }

                Undo.CollapseUndoOperations(batchUndoGroup);
                return true;
            }
            catch (System.Exception ex)
            {
                failure = ex.Message;
                RollBackBatch(
                    batchUndoGroup,
                    runtimePreviewSnapshots,
                    ref failure);
                return false;
            }
        }

        private static bool TryCaptureRuntimePreview(
            BrushDeformer instance,
            out RuntimePreviewSnapshot snapshot)
        {
            snapshot = default;
            var runtimeMesh = instance != null ? instance.RuntimeMesh : null;
            if (runtimeMesh == null)
            {
                return false;
            }

            var serialized = new SerializedObject(instance);
            serialized.UpdateIfRequiredOrScript();
            var skinnedRenderer = serialized.FindProperty("_skinnedMeshRenderer")
                ?.objectReferenceValue as SkinnedMeshRenderer;
            var meshFilter = serialized.FindProperty("_meshFilter")
                ?.objectReferenceValue as MeshFilter;
            bool isAssigned = (skinnedRenderer != null &&
                               ReferenceEquals(skinnedRenderer.sharedMesh, runtimeMesh)) ||
                              (meshFilter != null &&
                               ReferenceEquals(meshFilter.sharedMesh, runtimeMesh));
            if (!isAssigned)
            {
                return false;
            }

            var sourceMesh = instance.SourceMesh;
            if (sourceMesh == null)
            {
                sourceMesh = serialized.FindProperty("_serializedSourceMesh")
                    ?.objectReferenceValue as Mesh;
            }

            snapshot = new RuntimePreviewSnapshot(
                instance,
                skinnedRenderer,
                meshFilter,
                sourceMesh);
            return true;
        }

        private static void RollBackBatch(
            int batchUndoGroup,
            IReadOnlyDictionary<BrushDeformer, RuntimePreviewSnapshot> runtimePreviewSnapshots,
            ref string failure)
        {
            try
            {
                Undo.RevertAllDownToGroup(batchUndoGroup);
            }
            catch (System.Exception ex)
            {
                failure = $"{failure} Rollback failed: {ex.Message}";
                return;
            }

            foreach (var snapshot in runtimePreviewSnapshots.Values)
            {
                var instance = snapshot.Instance;
                if (instance == null || !instance.enabled)
                {
                    continue;
                }

                try
                {
                    if (snapshot.SourceMesh == null)
                    {
                        failure = $"{failure} Runtime preview restoration failed for " +
                                  $"{instance.name}: the original source mesh is missing.";
                        continue;
                    }

                    if (snapshot.SkinnedRenderer != null)
                    {
                        snapshot.SkinnedRenderer.sharedMesh = snapshot.SourceMesh;
                    }
                    if (snapshot.MeshFilter != null)
                    {
                        snapshot.MeshFilter.sharedMesh = snapshot.SourceMesh;
                    }

                    instance.CacheSourceMesh();
                    if (instance.Deform(true) == null)
                    {
                        failure = $"{failure} Runtime preview restoration failed for " +
                                  $"{instance.name}: the preview mesh could not be rebuilt.";
                    }
                }
                catch (System.Exception ex)
                {
                    failure = $"{failure} Runtime preview restoration failed for " +
                              $"{instance.name}: {ex.Message}";
                }
            }
        }

        private readonly struct RuntimePreviewSnapshot
        {
            internal RuntimePreviewSnapshot(
                BrushDeformer instance,
                SkinnedMeshRenderer skinnedRenderer,
                MeshFilter meshFilter,
                Mesh sourceMesh)
            {
                Instance = instance;
                SkinnedRenderer = skinnedRenderer;
                MeshFilter = meshFilter;
                SourceMesh = sourceMesh;
            }

            internal BrushDeformer Instance { get; }
            internal SkinnedMeshRenderer SkinnedRenderer { get; }
            internal MeshFilter MeshFilter { get; }
            internal Mesh SourceMesh { get; }
        }

        internal static void ClearAllDisplacements(
            BrushDeformer deformer,
            bool assignRuntimeMesh)
        {
            if (deformer == null)
            {
                return;
            }

            Undo.RecordObject(deformer, LatticeLocalization.Tr(LocKey.ClearAllDisplacements));
            deformer.ClearDisplacements();
            EditorUtility.SetDirty(deformer);
            if (PrefabUtility.IsPartOfPrefabInstance(deformer))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(deformer);
            }
            var scene = deformer.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            deformer.Deform(assignRuntimeMesh);
            LatticePreviewUtility.RequestSceneRepaint();
        }

        private void AutoAssignLocalRendererReferences()
        {
            if (targets == null || targets.Length == 0) return;

            foreach (var deformer in EnumerateTargets())
            {
                var localSkinned = deformer.GetComponent<SkinnedMeshRenderer>();
                var localMesh = deformer.GetComponent<MeshFilter>();
                if (localSkinned == null && localMesh == null) continue;

                var serialized = new SerializedObject(deformer);
                serialized.UpdateIfRequiredOrScript();

                var skinnedProp = serialized.FindProperty("_skinnedMeshRenderer");
                var meshProp = serialized.FindProperty("_meshFilter");
                bool changed = false;

                if (localSkinned != null)
                {
                    if (skinnedProp != null && skinnedProp.objectReferenceValue != localSkinned)
                    {
                        skinnedProp.objectReferenceValue = localSkinned;
                        changed = true;
                    }
                    if (meshProp != null && meshProp.objectReferenceValue != null)
                    {
                        meshProp.objectReferenceValue = null;
                        changed = true;
                    }
                }
                else if (localMesh != null)
                {
                    if (meshProp != null && meshProp.objectReferenceValue != localMesh)
                    {
                        meshProp.objectReferenceValue = localMesh;
                        changed = true;
                    }
                    if (skinnedProp != null && skinnedProp.objectReferenceValue != null)
                    {
                        skinnedProp.objectReferenceValue = null;
                        changed = true;
                    }
                }

                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private IEnumerable<BrushDeformer> EnumerateTargets()
        {
            if (targets == null) yield break;
            foreach (var obj in targets)
            {
                if (obj is BrushDeformer deformer) yield return deformer;
            }
        }

        private void DrawLanguageSelector()
        {
            int current = (int)LatticeLocalization.CurrentLanguage;
            int next = EditorGUILayout.Popup(LatticeLocalization.Content(LocKey.ToolLanguage), current, LatticeLocalization.DisplayNames);
            if (next != current)
            {
                next = Mathf.Clamp(next, 0, LatticeLocalization.DisplayNames.Length - 1);
                LatticeLocalization.CurrentLanguage = (LatticeLocalization.Language)next;
            }
        }

        private void DrawWeightTransferSettings()
        {
            if (_weightTransferSettingsProp == null) return;

            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage1InitialTransfer), EditorStyles.boldLabel);
            var maxDistProp = _weightTransferSettingsProp.FindPropertyRelative("maxTransferDistance");
            if (maxDistProp != null)
                EditorGUILayout.PropertyField(maxDistProp, LatticeLocalization.Content(LocKey.MaxTransferDistance,
                    "If weights stick to the wrong surface, try lowering this value or the Normal Angle Threshold for stricter matching."));

            var normalThresholdProp = _weightTransferSettingsProp.FindPropertyRelative("normalAngleThreshold");
            if (normalThresholdProp != null)
                EditorGUILayout.PropertyField(normalThresholdProp, LatticeLocalization.Content(LocKey.NormalAngleThreshold,
                    "If weights stick to the wrong surface, try lowering this value or the Max Transfer Distance for stricter matching."));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage2WeightInpainting), EditorStyles.boldLabel);
            var enableInpaintingProp = _weightTransferSettingsProp.FindPropertyRelative("enableInpainting");
            if (enableInpaintingProp != null)
            {
                EditorGUILayout.PropertyField(enableInpaintingProp, LatticeLocalization.Content(LocKey.EnableInpainting));
                if (enableInpaintingProp.boolValue)
                {
                    EditorGUI.indentLevel++;
                    var maxIterProp = _weightTransferSettingsProp.FindPropertyRelative("maxIterations");
                    if (maxIterProp != null) EditorGUILayout.PropertyField(maxIterProp, LatticeLocalization.Content(LocKey.MaxIterations));
                    var toleranceProp = _weightTransferSettingsProp.FindPropertyRelative("tolerance");
                    if (toleranceProp != null) EditorGUILayout.PropertyField(toleranceProp, LatticeLocalization.Content(LocKey.Tolerance));
                    EditorGUI.indentLevel--;
                }
            }
        }
    }
}
#endif
