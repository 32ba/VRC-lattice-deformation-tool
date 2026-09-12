#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class SupportInspectorSection
    {
        private readonly UnityEditor.Editor _editor;
        private static bool s_showSupportInformation;
        private double _supportReportSavedUntil;
        internal SupportInspectorSection(UnityEditor.Editor editor) { _editor = editor; }
        internal void Draw()
        {
            if (_editor == null || _editor.target == null) return;
            EditorGUILayout.Space();
            s_showSupportInformation = EditorGUILayout.BeginFoldoutHeaderGroup(
                s_showSupportInformation,
                LatticeLocalization.Tr(LocKey.SupportInformation));
            if (s_showSupportInformation)
            {
                EditorGUILayout.HelpBox(
                    LatticeLocalization.Tr(LocKey.SupportInformationDescription),
                    MessageType.Info);
                using (new EditorGUI.DisabledScope(
                           _editor.targets.Length != 1 || _editor.target is not LatticeDeformer))
                {
                    if (GUILayout.Button(LatticeLocalization.Tr(LocKey.CopySupportInformation)) &&
                        _editor.target is LatticeDeformer deformer)
                    {
                        string path = EditorUtility.SaveFilePanel(
                            LatticeLocalization.Tr(LocKey.CopySupportInformation),
                            "",
                            $"MeshDeformer-Support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png",
                            "png");
                        if (!string.IsNullOrEmpty(path))
                        {
                            try
                            {
                                SupportReportFiles.Write(path, MeshDeformerSupportReport.GeneratePng(deformer));
                                _supportReportSavedUntil = EditorApplication.timeSinceStartup + 3d;
                            }
                            catch (Exception exception)
                            {
                                EditorUtility.DisplayDialog("Mesh Deformer", exception.Message, "OK");
                            }
                        }
                    }
                }
                if (EditorApplication.timeSinceStartup < _supportReportSavedUntil)
                {
                    EditorGUILayout.HelpBox(
                        LatticeLocalization.Tr(LocKey.SupportInformationCopied),
                        MessageType.Info);
                    _editor.Repaint();
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

    }
}
#endif
