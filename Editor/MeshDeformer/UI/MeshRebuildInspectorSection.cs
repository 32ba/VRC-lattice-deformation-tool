#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class MeshRebuildInspectorSection
    {
        private static bool s_showOptions;
        private static bool s_showWeightTransferSettings;
        private readonly SerializedObject _serialized;
        private readonly SerializedProperty _recalcNormalsProp;
        private readonly SerializedProperty _normalsModeProp;
        private readonly SerializedProperty _recalcTangentsProp;
        private readonly SerializedProperty _recalcBoundsProp;
        private readonly SerializedProperty _recalcBoneWeightsProp;
        private readonly SerializedProperty _weightTransferSettingsProp;
        private readonly SerializedProperty _skinnedRendererProp;
        private VisualElement _normalsMode, _boneWeights, _boneWeightInfo, _weightTransfer, _inpainting;
        private PopupField<string> _normalsModePopup;
        private Button _previewButton;

        internal MeshRebuildInspectorSection(SerializedObject serialized)
        {
            _serialized = serialized;
            _recalcNormalsProp = serialized.FindProperty("_recalculateNormals");
            _normalsModeProp = serialized.FindProperty("_normalsRecalculationMode");
            _recalcTangentsProp = serialized.FindProperty("_recalculateTangents");
            _recalcBoundsProp = serialized.FindProperty("_recalculateBounds");
            _recalcBoneWeightsProp = serialized.FindProperty("_recalculateBoneWeights");
            _weightTransferSettingsProp = serialized.FindProperty("_weightTransferSettings");
            _skinnedRendererProp = serialized.FindProperty("_skinnedMeshRenderer");
        }

        /// <summary>Builds the rebuild options. Building and refreshing never write the target.</summary>
        internal VisualElement CreateView()
        {
            var root = MdUI.Foldout(LatticeLocalization.Tr(LocKey.MeshRebuildOptions),
                () => s_showOptions, open => s_showOptions = open, out var content);
            root.name = "mesh-rebuild-options";

            var toggles = MdUI.Element("md-toggle-row");
            AddToggle(toggles, _recalcNormalsProp, LocKey.Normals);
            AddToggle(toggles, _recalcTangentsProp, LocKey.Tangents);
            AddToggle(toggles, _recalcBoundsProp, LocKey.Bounds);
            content.Add(toggles);

            if (_normalsModeProp != null)
            {
                var choices = new List<string>
                {
                    LatticeLocalization.Tr(LocKey.NormalsLegacyUnityRecalculate),
                    LatticeLocalization.Tr(LocKey.NormalsPreserveSourceSmoothing)
                };
                // Display the legacy fallback without rewriting unknown saved values.
                _normalsModePopup = new PopupField<string>(LatticeLocalization.Tr(LocKey.NormalsMode), choices, 0)
                    { name = "normals-mode", tooltip = LatticeLocalization.Tooltip(LocKey.NormalsMode) };
                _normalsModePopup.RegisterValueChangedCallback(_ =>
                {
                    _serialized.Update();
                    _normalsModeProp.enumValueIndex = _normalsModePopup.index;
                    _serialized.ApplyModifiedProperties();
                });
                _normalsMode = _normalsModePopup;
                content.Add(_normalsMode);
            }

            if (_recalcBoneWeightsProp != null)
            {
                _boneWeights = new PropertyField(_recalcBoneWeightsProp, LatticeLocalization.Tr(LocKey.RecalculateBoneWeights))
                    { tooltip = LatticeLocalization.Tooltip(LocKey.RecalculateBoneWeights) };
                content.Add(_boneWeights);
            }
            _boneWeightInfo = MdUI.HelpBox(LatticeLocalization.Tr(LocKey.BoneWeightRequiresSMR));
            content.Add(_boneWeightInfo);

            if (_weightTransferSettingsProp != null)
            {
                _weightTransfer = MdUI.Foldout(LatticeLocalization.Tr(LocKey.WeightTransferSettings),
                    () => s_showWeightTransferSettings, open => s_showWeightTransferSettings = open, out var transfer);
                BuildWeightTransfer(transfer);
                content.Add(_weightTransfer);
            }

            _previewButton = MdUI.Button(string.Empty, () =>
            {
                LatticeDeformerPreviewFilter.ForcePreviewState(!LatticeDeformerPreviewFilter.PreviewToggleEnabled);
                Refresh();
            });
            _previewButton.name = "toggle-mesh-preview";
            content.Add(_previewButton);

            root.Bind(_serialized);
            root.TrackSerializedObjectValue(_serialized, _ => Refresh());
            Refresh();
            return root;
        }

        private void BuildWeightTransfer(VisualElement parent)
        {
            parent.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.Stage1InitialTransfer), "md-text-section"));
            AddRelative(parent, "maxTransferDistance", LocKey.MaxTransferDistance);
            AddRelative(parent, "normalAngleThreshold", LocKey.NormalAngleThreshold);
            parent.Add(MdUI.Text(LatticeLocalization.Tr(LocKey.Stage2WeightInpainting), "md-text-section"));
            AddRelative(parent, "enableInpainting", LocKey.EnableInpainting);
            _inpainting = MdUI.Element("md-indent");
            AddRelative(_inpainting, "maxIterations", LocKey.MaxIterations);
            AddRelative(_inpainting, "tolerance", LocKey.Tolerance);
            parent.Add(_inpainting);
        }

        private void AddRelative(VisualElement parent, string name, string key)
        {
            var property = _weightTransferSettingsProp.FindPropertyRelative(name);
            if (property == null) return;
            parent.Add(new PropertyField(property, LatticeLocalization.Tr(key))
                { tooltip = LatticeLocalization.Tooltip(key) });
        }

        private static void AddToggle(VisualElement parent, SerializedProperty property, string key)
        {
            if (property == null) return;
            var toggle = new Toggle { text = LatticeLocalization.Tr(key), tooltip = LatticeLocalization.Tooltip(key) };
            toggle.BindProperty(property);
            parent.Add(toggle);
        }

        /// <summary>Resyncs visibility and labels with the serialized values; never writes them.</summary>
        internal void Refresh()
        {
            if (_serialized == null || _serialized.targetObject == null) return;
            _serialized.Update();
            bool recalcNormals = _recalcNormalsProp != null && _recalcNormalsProp.boolValue;
            MdUI.SetVisible(_normalsMode, recalcNormals && _normalsModeProp != null);
            if (_normalsModePopup != null && _normalsModeProp != null)
            {
                _normalsModePopup.showMixedValue = _normalsModeProp.hasMultipleDifferentValues;
                _normalsModePopup.SetValueWithoutNotify(_normalsModePopup.choices[Mathf.Clamp(_normalsModeProp.enumValueIndex, 0, 1)]);
            }

            bool hasSkinnedRenderer = _skinnedRendererProp != null &&
                !_skinnedRendererProp.hasMultipleDifferentValues &&
                _skinnedRendererProp.objectReferenceValue != null;
            bool boneWeights = _recalcBoneWeightsProp != null && _recalcBoneWeightsProp.boolValue;
            _boneWeights?.SetEnabled(hasSkinnedRenderer);
            MdUI.SetVisible(_boneWeightInfo, !hasSkinnedRenderer && boneWeights);
            MdUI.SetVisible(_weightTransfer, boneWeights && hasSkinnedRenderer);
            var inpainting = _weightTransferSettingsProp?.FindPropertyRelative("enableInpainting");
            MdUI.SetVisible(_inpainting, inpainting != null && inpainting.boolValue);

            if (_previewButton != null)
                MdUI.SetButtonText(_previewButton, LatticeDeformerPreviewFilter.PreviewToggleEnabled
                    ? LatticeLocalization.Tr(LocKey.NDMFDisableMeshPreview)
                    : LatticeLocalization.Tr(LocKey.NDMFEnableMeshPreview));
        }
    }
}
#endif
