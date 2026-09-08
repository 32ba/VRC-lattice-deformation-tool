#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal sealed class ProfileInspectorSection
    {
        private readonly UnityEditor.Editor _editor;
        private readonly Action _onChanged;
        private readonly SerializedProperty _dataSource;
        private readonly SerializedProperty _profile;
        private bool _operationFailed;

        internal ProfileInspectorSection(UnityEditor.Editor editor, Action onChanged)
        {
            _editor = editor;
            _onChanged = onChanged;
            _dataSource = editor.serializedObject.FindProperty("_dataSource");
            _profile = editor.serializedObject.FindProperty("_profile");
        }

        internal void Draw()
        {
            if (_editor == null || _editor.target == null) return;
            EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.DeformerProfile), EditorStyles.boldLabel);
            var options = new[] {LatticeLocalization.Content(LocKey.EmbeddedData), LatticeLocalization.Content(LocKey.ProfileData)};
            if (_editor.targets.Length != 1 || _editor.target is not LatticeDeformer deformer)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Popup(LatticeLocalization.Content(LocKey.DataSource), _dataSource.enumValueIndex, options);
                    EditorGUILayout.ObjectField(LatticeLocalization.Content(LocKey.DeformerProfile),
                        _profile.objectReferenceValue, typeof(MeshDeformerProfile), false);
                }
                return;
            }

            EditorGUI.BeginChangeCheck();
            var source = (DeformerDataSource)EditorGUILayout.Popup(LatticeLocalization.Content(LocKey.DataSource),
                (int)deformer.DataSource, options);
            var profile = (MeshDeformerProfile)EditorGUILayout.ObjectField(LatticeLocalization.Content(LocKey.DeformerProfile),
                deformer.Profile, typeof(MeshDeformerProfile), false);
            if (EditorGUI.EndChangeCheck())
                Run(() => ProfileAuthoringService.ChangeSource(deformer, source, profile,
                    LatticeLocalization.Tr(LocKey.DeformerProfile)), deformer, true);

            DrawCompatibility(deformer);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(LatticeLocalization.Content(LocKey.CreateProfile))) CreateAsset(deformer);
                using (new EditorGUI.DisabledScope(deformer.Profile == null))
                {
                    if (GUILayout.Button(LatticeLocalization.Content(LocKey.SaveToProfile)))
                        Run(() => ProfileAuthoringService.SaveCurrent(deformer, deformer.Profile,
                            LatticeLocalization.Tr(LocKey.SaveToProfile)), deformer, false);
                    if (GUILayout.Button(LatticeLocalization.Content(LocKey.CopyProfileToInstance)))
                        Run(() => ProfileAuthoringService.CopyToEmbedded(deformer,
                            LatticeLocalization.Tr(LocKey.CopyProfileToInstance)), deformer, true);
                }
            }
            if (deformer.DataSource == DeformerDataSource.Profile && deformer.Profile == null)
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.ProfileRequired), MessageType.Warning);
            if (_operationFailed)
                EditorGUILayout.HelpBox(LatticeLocalization.Tr(LocKey.ProfileOperationFailed), MessageType.Error);
        }

        private void CreateAsset(LatticeDeformer deformer)
        {
            string path = EditorUtility.SaveFilePanelInProject(LatticeLocalization.Tr(LocKey.CreateProfile),
                "MeshDeformerProfile", "asset", LatticeLocalization.Tr(LocKey.CreateProfile));
            if (string.IsNullOrEmpty(path)) return;
            Run(() => ProfileAuthoringService.CreateAsset(deformer, path,
                LatticeLocalization.Tr(LocKey.CreateProfile), out _), deformer, true);
        }

        private void Run(Func<bool> operation, LatticeDeformer deformer, bool refresh)
        {
            try
            {
                _operationFailed = !operation();
                if (_operationFailed || !refresh) return;
                deformer.Deform(LatticePreviewUtility.ShouldAssignRuntimeMesh());
                _editor.serializedObject.Update();
                _onChanged?.Invoke();
            }
            catch (Exception exception)
            {
                _operationFailed = true;
                EditorUtility.DisplayDialog(LatticeLocalization.Tr(LocKey.DeformerProfile), exception.Message, "OK");
            }
        }

        private static void DrawCompatibility(LatticeDeformer deformer)
        {
            if (deformer.Profile == null) return;
            var status = ProfileAuthoringService.ReadCompatibility(deformer, deformer.Profile);
            string key = status switch
            {
                ProfileCompatibilityStatus.ExactMatch => LocKey.ProfileExactMatch,
                ProfileCompatibilityStatus.CompatibleSourceDiffers => LocKey.ProfileCompatibleSourceDiffers,
                ProfileCompatibilityStatus.TopologyMismatch => LocKey.ProfileTopologyMismatchBlocked,
                _ => LocKey.ProfileInsufficientMetadata
            };
            var severity = status == ProfileCompatibilityStatus.TopologyMismatch ? MessageType.Error :
                status == ProfileCompatibilityStatus.InsufficientMetadata ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(LatticeLocalization.Tr(key), severity);
        }
    }
}
#endif
