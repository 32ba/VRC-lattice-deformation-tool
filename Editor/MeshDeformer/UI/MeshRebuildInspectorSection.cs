#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class MeshRebuildInspectorSection
    {
        private static bool s_showOptions;
        private static bool s_showWeightTransferSettings;
        private readonly SerializedProperty _recalcNormalsProp;
        private readonly SerializedProperty _normalsModeProp;
        private readonly SerializedProperty _recalcTangentsProp;
        private readonly SerializedProperty _recalcBoundsProp;
        private readonly SerializedProperty _recalcBoneWeightsProp;
        private readonly SerializedProperty _weightTransferSettingsProp;
        private readonly SerializedProperty _skinnedRendererProp;
        internal MeshRebuildInspectorSection(SerializedObject serialized)
        {
            _recalcNormalsProp = serialized.FindProperty("_recalculateNormals");
            _normalsModeProp = serialized.FindProperty("_normalsRecalculationMode");
            _recalcTangentsProp = serialized.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serialized.FindProperty("_recalculateBounds");
            _recalcBoneWeightsProp = serialized.FindProperty("_recalculateBoneWeights");
            _weightTransferSettingsProp = serialized.FindProperty("_weightTransferSettings");
            _skinnedRendererProp = serialized.FindProperty("_skinnedMeshRenderer");
        }

        internal void Draw()
        {
            s_showOptions = EditorGUILayout.BeginFoldoutHeaderGroup(s_showOptions, LatticeLocalization.Tr(LocKey.MeshRebuildOptions));
            if (s_showOptions)
            {
                EditorGUI.indentLevel++;

                // Compact horizontal toggles
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawToggle(_recalcNormalsProp, LocKey.Normals);
                    DrawToggle(_recalcTangentsProp, LocKey.Tangents);
                    DrawToggle(_recalcBoundsProp, LocKey.Bounds);
                }

                if (_recalcNormalsProp != null && _recalcNormalsProp.boolValue && _normalsModeProp != null)
                {
                    // Display the legacy fallback without rewriting unknown saved values.
                    bool mixed = EditorGUI.showMixedValue;
                    EditorGUI.showMixedValue = _normalsModeProp.hasMultipleDifferentValues;
                    EditorGUI.BeginChangeCheck();
                    int normalsMode = Mathf.Clamp(_normalsModeProp.enumValueIndex, 0, 1);
                    int selected = EditorGUILayout.Popup(
                        LatticeLocalization.Content(LocKey.NormalsMode),
                        normalsMode,
                        new[]
                        {
                            LatticeLocalization.Content(LocKey.NormalsLegacyUnityRecalculate),
                            LatticeLocalization.Content(LocKey.NormalsPreserveSourceSmoothing)
                        });
                    if (EditorGUI.EndChangeCheck()) _normalsModeProp.enumValueIndex = selected;
                    EditorGUI.showMixedValue = mixed;
                }

                // Bone weight recalculation (only for SkinnedMeshRenderer)
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

                // Weight transfer settings (shown only when bone weight recalculation is enabled)
                if (_recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue && hasSkinnedRenderer)
                {
                    s_showWeightTransferSettings = EditorGUILayout.Foldout(s_showWeightTransferSettings, LatticeLocalization.Tr(LocKey.WeightTransferSettings), true);
                    if (s_showWeightTransferSettings && _weightTransferSettingsProp != null)
                    {
                        EditorGUI.indentLevel++;
                        DrawWeightTransferSettings();
                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUILayout.Space();
                bool previewEnabled = LatticeDeformerPreviewFilter.PreviewToggleEnabled;
                string previewLabel = previewEnabled
                    ? LatticeLocalization.Tr(LocKey.NDMFDisableMeshPreview)
                    : LatticeLocalization.Tr(LocKey.NDMFEnableMeshPreview);
                if (GUILayout.Button(previewLabel))
                {
                    LatticeDeformerPreviewFilter.ForcePreviewState(!previewEnabled);
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawWeightTransferSettings()
        {
            if (_weightTransferSettingsProp == null)
            {
                return;
            }

            // Stage 1 settings
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage1InitialTransfer), EditorStyles.boldLabel);

            var maxDistProp = _weightTransferSettingsProp.FindPropertyRelative("maxTransferDistance");
            if (maxDistProp != null)
            {
                EditorGUILayout.PropertyField(
                    maxDistProp,
                    LatticeLocalization.Content(
                        LocKey.MaxTransferDistance,
                        "If weights stick to the wrong surface, try lowering this value or the Normal Angle Threshold for stricter matching."));
            }

            var normalThresholdProp = _weightTransferSettingsProp.FindPropertyRelative("normalAngleThreshold");
            if (normalThresholdProp != null)
            {
                EditorGUILayout.PropertyField(
                    normalThresholdProp,
                    LatticeLocalization.Content(
                        LocKey.NormalAngleThreshold,
                        "If weights stick to the wrong surface, try lowering this value or the Max Transfer Distance for stricter matching."));
            }

            EditorGUILayout.Space(4);

            // Stage 2 settings
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Stage2WeightInpainting), EditorStyles.boldLabel);

            var enableInpaintingProp = _weightTransferSettingsProp.FindPropertyRelative("enableInpainting");
            if (enableInpaintingProp != null)
            {
                EditorGUILayout.PropertyField(enableInpaintingProp, LatticeLocalization.Content(LocKey.EnableInpainting));

                if (enableInpaintingProp.boolValue)
                {
                    EditorGUI.indentLevel++;

                    var maxIterProp = _weightTransferSettingsProp.FindPropertyRelative("maxIterations");
                    if (maxIterProp != null)
                    {
                        EditorGUILayout.PropertyField(maxIterProp, LatticeLocalization.Content(LocKey.MaxIterations));
                    }

                    var toleranceProp = _weightTransferSettingsProp.FindPropertyRelative("tolerance");
                    if (toleranceProp != null)
                    {
                        EditorGUILayout.PropertyField(toleranceProp, LatticeLocalization.Content(LocKey.Tolerance));
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private static void DrawToggle(SerializedProperty property, string key)
        {
            if (property == null) return;
            bool mixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool selected = GUILayout.Toggle(property.boolValue, LatticeLocalization.Tr(key));
            if (EditorGUI.EndChangeCheck()) property.boolValue = selected;
            EditorGUI.showMixedValue = mixed;
        }
    }
}
#endif
