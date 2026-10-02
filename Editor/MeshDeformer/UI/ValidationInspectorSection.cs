#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class ValidationInspectorSection
    {
        private readonly UnityEditor.Editor _editor;
        private readonly Action _onChanged;
        internal InspectorValidationState State { get; } = new InspectorValidationState();
        internal ValidationInspectorSection(UnityEditor.Editor editor, Action onChanged)
        { _editor = editor; _onChanged = onChanged; }
        internal void Draw()
        {
            if (_editor == null || _editor.target == null) return;
            if (_editor.targets.Length != 1 || _editor.target is not LatticeDeformer deformer) return;
            var diagnostics = State.Read(deformer);
            if (diagnostics.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.Validation), EditorStyles.boldLabel);
            foreach (var diagnostic in diagnostics)
            {
                var messageType = diagnostic.Severity switch
                {
                    MeshDeformerDiagnosticSeverity.Error => MessageType.Error,
                    MeshDeformerDiagnosticSeverity.Warning => MessageType.Warning,
                    _ => MessageType.Info
                };
                EditorGUILayout.HelpBox(diagnostic.FormatForLog(), messageType);
                if (diagnostic.Fix != null &&
                    GUILayout.Button($"{LatticeLocalization.Tr(LocKey.ValidationFix)}: {diagnostic.FixLabel}"))
                {
                    diagnostic.Fix();
                    _editor.serializedObject.Update();
                    _onChanged();
                    GUIUtility.ExitGUI();
                }
            }
        }

    }
}
#endif
