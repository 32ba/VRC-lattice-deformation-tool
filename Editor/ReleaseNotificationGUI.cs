#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class ReleaseNotificationGUI
    {
        internal static void Draw()
        {
            if (!ReleaseChecker.HasNewVersion || string.IsNullOrEmpty(ReleaseChecker.LatestVersion))
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(LatticeLocalization.Tr(LocKey.UpdateAvailable), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.Format(
                    LatticeLocalization.Tr(LocKey.UpdateCurrentToLatest),
                    VersionUtility.FormatVersion(ReleaseChecker.GetCurrentVersion()),
                    VersionUtility.FormatVersion(ReleaseChecker.LatestVersion)));

                if (GUILayout.Button(LatticeLocalization.Tr(LocKey.UpdateOpenBoothPage)))
                {
                    ReleaseChecker.OpenReleasePage();
                }
            }

            EditorGUILayout.Space();
        }
    }
}
#endif
