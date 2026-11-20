#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Applies the Mesh Deformer design system (Editor/UI/Styles) to a UI Toolkit tree: the shared
    /// size tokens, the colour tokens of the current Editor theme, and the component classes.
    /// </summary>
    internal static class MeshDeformerStyles
    {
        internal const string RootClass = "md-root";
        internal const string DarkThemeClass = "md-theme-dark";
        internal const string LightThemeClass = "md-theme-light";
        internal const string StyleDirectory = "Packages/net.32ba.lattice-deformation-tool/Editor/UI/Styles/";
        internal const string IconDirectory = "Packages/net.32ba.lattice-deformation-tool/Editor/UI/Icons/";

        internal static StyleSheet Tokens => Load("Tokens.uss");
        internal static StyleSheet DarkTokens => Load("Tokens.Dark.uss");
        internal static StyleSheet LightTokens => Load("Tokens.Light.uss");
        internal static StyleSheet Components => Load("Components.uss");

        /// <summary>Applies the styles for the Editor theme the user has selected.</summary>
        internal static void Apply(VisualElement root) => Apply(root, EditorGUIUtility.isProSkin);

        /// <summary>
        /// Applies the styles for the given theme. Calling it again switches the theme without
        /// duplicating style sheets, so it can follow an Editor skin change.
        /// </summary>
        internal static void Apply(VisualElement root, bool darkTheme)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));

            var themeTokens = darkTheme ? DarkTokens : LightTokens;
            var otherTokens = darkTheme ? LightTokens : DarkTokens;
            if (otherTokens != null) root.styleSheets.Remove(otherTokens);

            AddOnce(root, Tokens);
            AddOnce(root, themeTokens);
            AddOnce(root, Components);

            root.AddToClassList(RootClass);
            root.EnableInClassList(DarkThemeClass, darkTheme);
            root.EnableInClassList(LightThemeClass, !darkTheme);
        }

        private static void AddOnce(VisualElement root, StyleSheet sheet)
        {
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
        }

        private static StyleSheet Load(string fileName) =>
            AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleDirectory + fileName);
    }
}
#endif
