#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class SupportInspectorSection
    {
        private readonly UnityEditor.Editor _editor;
        private static bool s_showSupportInformation;
        internal SupportInspectorSection(UnityEditor.Editor editor) { _editor = editor; }

        internal VisualElement CreateView()
        {
            var root = MdUI.Foldout(LatticeLocalization.Tr(LocKey.SupportInformation),
                () => s_showSupportInformation, open => s_showSupportInformation = open, out var content);
            root.name = "support-information";
            content.Add(MdUI.HelpBox(LatticeLocalization.Tr(LocKey.SupportInformationDescription)));
            var saved = MdUI.HelpBox(LatticeLocalization.Tr(LocKey.SupportInformationCopied));
            MdUI.SetVisible(saved, false);
            var copy = MdUI.Button(LatticeLocalization.Tr(LocKey.CopySupportInformation), () =>
            {
                if (_editor == null || _editor.target is not LatticeDeformer deformer || _editor.targets.Length != 1) return;
                string path = EditorUtility.SaveFilePanel(LatticeLocalization.Tr(LocKey.CopySupportInformation), "",
                    $"MeshDeformer-Support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png", "png");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    SupportReportFiles.Write(path, MeshDeformerSupportReport.GeneratePng(deformer));
                    MdUI.SetVisible(saved, true);
                    saved.schedule.Execute(() => MdUI.SetVisible(saved, false)).ExecuteLater(3000);
                }
                catch (Exception exception)
                {
                    EditorUtility.DisplayDialog("Mesh Deformer", exception.Message, "OK");
                }
            });
            copy.SetEnabled(_editor != null && _editor.targets.Length == 1 && _editor.target is LatticeDeformer);
            content.Add(copy);
            content.Add(saved);
            return root;
        }
    }
}
#endif
