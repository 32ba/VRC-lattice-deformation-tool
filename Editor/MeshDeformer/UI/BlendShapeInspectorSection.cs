#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>Owns BlendShape settings, import menus and temporary test display.</summary>
    internal sealed class BlendShapeInspectorSection : IDisposable
    {
        private readonly UnityEditor.Editor _owner;
        private readonly Action _onPropertyChanges, _onImported;
        private readonly SerializedProperty _groupsProp, _activeGroupIndexProp;
        private BlendShapeTestSession _blendShapeTestSession;
        private bool _disposed;
        private event Action TestModeChanged;
        private UnityEngine.Object target => _owner != null ? _owner.target : null;
        private SerializedObject serializedObject => _owner.serializedObject;

        internal BlendShapeInspectorSection(UnityEditor.Editor owner, Action onPropertyChanges, Action onImported)
        {
            _owner = owner; _onPropertyChanges = onPropertyChanges; _onImported = onImported;
            _groupsProp = serializedObject.FindProperty("_groups");
            _activeGroupIndexProp = serializedObject.FindProperty("_activeGroupIndex");
        }

        internal bool IsTestModeActive => _blendShapeTestSession != null && _blendShapeTestSession.IsActive;

        private bool CanShowGroup(int index)
        {
            if (_disposed || target is not LatticeDeformer d || d == null) return false;
            var raw = SerializedDeformerReader.Read(d);
            return raw.DataSource == DeformerDataSource.Embedded && raw.EmbeddedGroups != null &&
                index >= 0 && index < raw.EmbeddedGroups.Count && raw.EmbeddedGroups[index] != null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ExitTestMode();
            TestModeChanged = null;
        }

        /// <summary>BlendShape output settings of one group. Reads only; edits go through bindings.</summary>
        internal VisualElement CreateGroupView(int groupIndex)
        {
            var root = MdUI.Element("md-blendshape md-detail");
            if (!CanShowGroup(groupIndex)) return root;
            serializedObject.Update();
            if (_groupsProp == null || groupIndex < 0 || groupIndex >= _groupsProp.arraySize) return root;

            var groupProp = _groupsProp.GetArrayElementAtIndex(groupIndex);
            var outputProp = groupProp.FindPropertyRelative("_blendShapeOutput");
            var nameProp = groupProp.FindPropertyRelative("_blendShapeName");
            var curveProp = groupProp.FindPropertyRelative("_blendShapeCurve");
            if (outputProp == null) return root;

            var output = new PropertyField(outputProp, LatticeLocalization.Tr(LocKey.BlendShapeOutput));
            root.Add(output);
            var details = MdUI.Element("md-blendshape__details");
            if (nameProp != null) details.Add(new PropertyField(nameProp, LatticeLocalization.Tr(LocKey.BlendShapeName)));
            if (curveProp != null) details.Add(new PropertyField(curveProp, LatticeLocalization.Tr(LocKey.Curve)));
            int activeGroup = _activeGroupIndexProp != null ? _activeGroupIndexProp.intValue : 0;
            if (groupIndex == activeGroup) details.Add(CreateTestModeView());
            root.Add(details);
            root.Bind(serializedObject);

            string outputPath = outputProp.propertyPath;
            string namePath = nameProp?.propertyPath;
            void Sync() => MdUI.SetVisible(details, IsBlendShapeOutput(serializedObject.FindProperty(outputPath)));
            root.TrackPropertyValue(outputProp, property =>
            {
                if (_disposed || target is not LatticeDeformer deformer || deformer == null) return;
                if (IsBlendShapeOutput(property) && namePath != null)
                {
                    var name = serializedObject.FindProperty(namePath);
                    if (name != null && string.IsNullOrWhiteSpace(name.stringValue))
                    {
                        name.stringValue = deformer.gameObject.name;
                        if (serializedObject.ApplyModifiedProperties()) _onPropertyChanges();
                    }
                }
                Sync();
            });
            Sync();
            return root;
        }

        private static bool IsBlendShapeOutput(SerializedProperty output) =>
            output != null && output.intValue == (int)BlendShapeOutputMode.OutputAsBlendShape;

        private VisualElement CreateTestModeView()
        {
            var root = MdUI.Element("md-blendshape__test");
            void Build()
            {
                root.Clear();
                if (_disposed || target is not LatticeDeformer deformer || deformer == null) return;
                var smr = deformer.GetComponent<SkinnedMeshRenderer>();
                if (smr == null) return;
                if (!IsTestModeActive)
                {
                    root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.EnterTestMode),
                        () => EnterTestMode(deformer, smr), "md-button--small"));
                    return;
                }
                root.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.BlendShapeTestMode)));
                var weight = new Slider(LatticeLocalization.Tr(LocKey.TestWeight), 0f, 100f) { showInputField = true };
                weight.SetValueWithoutNotify(_blendShapeTestSession.Weight);
                weight.RegisterValueChangedCallback(evt =>
                {
                    if (!IsTestModeActive) return;
                    _blendShapeTestSession.SetWeight(evt.newValue);
                    SceneView.RepaintAll();
                });
                root.Add(weight);
                root.Add(MdUI.Button(LatticeLocalization.Tr(LocKey.ExitTestMode), ExitTestMode, "md-button--small"));
            }
            root.RegisterCallback<AttachToPanelEvent>(_ => TestModeChanged += Build);
            root.RegisterCallback<DetachFromPanelEvent>(_ => TestModeChanged -= Build);
            Build();
            return root;
        }

        internal void EnterTestMode(LatticeDeformer deformer, SkinnedMeshRenderer smr)
        {
            if (_disposed || target != deformer) return;
            _blendShapeTestSession?.Dispose();
            _blendShapeTestSession = BlendShapeTestSession.TryBegin(deformer, smr);
            SceneView.RepaintAll();
            TestModeChanged?.Invoke();
        }

        internal void ExitTestMode()
        {
            _blendShapeTestSession?.Dispose();
            _blendShapeTestSession = null;
            SceneView.RepaintAll();
            TestModeChanged?.Invoke();
        }

        internal void Refresh(LatticeDeformer deformer)
        {
            if (_blendShapeTestSession != null && _blendShapeTestSession.Owns(deformer))
                _blendShapeTestSession.Refresh();
        }

        /// <summary>The BlendShape import row; empty when the source mesh has no BlendShapes.</summary>
        internal VisualElement CreateImportView()
        {
            var root = MdUI.Element("md-blendshape-import md-detail");
            if (_disposed || target is not LatticeDeformer deformer || deformer == null) return root;
            var source = SerializedDeformerReader.Read(deformer).SourceMesh;
            if (source == null || source.blendShapeCount == 0) return root;
            Button select = null;
            select = MdUI.Button(LatticeLocalization.Tr(LocKey.Select), () =>
            {
                var snapshot = BlendShapeImportMenu.TryCreate(deformer);
                if (snapshot == null) return;
                var menu = new GenericMenu();
                for (int i = 0; i < snapshot.Count; i++)
                    menu.AddItem(new GUIContent(snapshot.GetName(i)), false, CreateImportAction(snapshot, i));
                menu.DropDown(select.worldBound);
            }, "md-button--small");
            select.name = "blendshape-import";
            select.Add(MdUI.Icon("caret-down", "md-icon--small"));
            root.Add(MdUI.FieldRow(LatticeLocalization.Tr(LocKey.ImportBlendShape), select));
            return root;
        }

        internal GenericMenu.MenuFunction CreateImportAction(BlendShapeImportMenu menu, int shape)
        {
            return () =>
            {
                if (_disposed || menu == null || target == null || target != menu.Owner ||
                    !menu.Import(shape, "Import BlendShape")) return;
                serializedObject.Update();
                _onImported();
            };
        }
    }
}
#endif
