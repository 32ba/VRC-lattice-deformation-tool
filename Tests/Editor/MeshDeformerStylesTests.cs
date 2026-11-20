#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerStylesTests
    {
        [Test]
        public void StyleSheets_AreImported()
        {
            Assert.That(MeshDeformerStyles.Tokens, Is.Not.Null);
            Assert.That(MeshDeformerStyles.DarkTokens, Is.Not.Null);
            Assert.That(MeshDeformerStyles.LightTokens, Is.Not.Null);
            Assert.That(MeshDeformerStyles.Components, Is.Not.Null);
            Assert.That(MeshDeformerStyles.Layout, Is.Not.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Apply_AddsSharedThemeAndComponentSheetsOnce(bool dark)
        {
            var root = new VisualElement();
            MeshDeformerStyles.Apply(root, dark);
            MeshDeformerStyles.Apply(root, dark);

            var expectedTheme = dark ? MeshDeformerStyles.DarkTokens : MeshDeformerStyles.LightTokens;
            var otherTheme = dark ? MeshDeformerStyles.LightTokens : MeshDeformerStyles.DarkTokens;
            Assert.That(root.styleSheets.count, Is.EqualTo(4));
            Assert.That(root.styleSheets.Contains(MeshDeformerStyles.Tokens), Is.True);
            Assert.That(root.styleSheets.Contains(expectedTheme), Is.True);
            Assert.That(root.styleSheets.Contains(otherTheme), Is.False);
            Assert.That(root.styleSheets.Contains(MeshDeformerStyles.Components), Is.True);
            Assert.That(root.styleSheets.Contains(MeshDeformerStyles.Layout), Is.True);
            Assert.That(root.ClassListContains(MeshDeformerStyles.RootClass), Is.True);
            Assert.That(root.ClassListContains(MeshDeformerStyles.DarkThemeClass), Is.EqualTo(dark));
            Assert.That(root.ClassListContains(MeshDeformerStyles.LightThemeClass), Is.EqualTo(!dark));
        }

        [Test]
        public void Apply_SwitchesThemeWithoutLeavingTheOldTokens()
        {
            var root = new VisualElement();
            MeshDeformerStyles.Apply(root, true);
            MeshDeformerStyles.Apply(root, false);

            Assert.That(root.styleSheets.count, Is.EqualTo(4));
            Assert.That(root.styleSheets.Contains(MeshDeformerStyles.DarkTokens), Is.False);
            Assert.That(root.styleSheets.Contains(MeshDeformerStyles.LightTokens), Is.True);
            Assert.That(root.ClassListContains(MeshDeformerStyles.DarkThemeClass), Is.False);
            Assert.That(root.ClassListContains(MeshDeformerStyles.LightThemeClass), Is.True);
        }

        [Test]
        public void Apply_FollowsTheEditorSkin()
        {
            var root = new VisualElement();
            MeshDeformerStyles.Apply(root);
            Assert.That(root.ClassListContains(MeshDeformerStyles.DarkThemeClass), Is.EqualTo(EditorGUIUtility.isProSkin));
        }

        [Test]
        public void Apply_RejectsMissingRoot()
        {
            Assert.That(() => MeshDeformerStyles.Apply(null, true), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ThemeTokens_DefineTheSameVariables()
        {
            Assert.That(Variables("Tokens.Dark.uss"), Is.EquivalentTo(Variables("Tokens.Light.uss")));
        }

        [TestCase("Components.uss")]
        [TestCase("Layout.uss")]
        public void StyleSheets_UseOnlyDefinedVariables(string fileName)
        {
            var defined = Variables("Tokens.uss").Concat(Variables("Tokens.Dark.uss")).ToHashSet();
            var used = Regex.Matches(Read(fileName), @"var\((--md-[A-Za-z0-9-]+)\)")
                .Cast<Match>().Select(m => m.Groups[1].Value).Distinct();
            Assert.That(used.Where(v => !defined.Contains(v)), Is.Empty);
        }

        [TestCase("Components.uss")]
        [TestCase("Layout.uss")]
        public void StyleSheets_ReferenceOnlyImportedIcons(string fileName)
        {
            var icons = Regex.Matches(Read(fileName), @"url\(""\.\./Icons/([A-Za-z0-9-]+\.png)""\)")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            Assert.That(icons, Is.Not.Empty);
            foreach (string icon in icons)
            {
                Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(MeshDeformerStyles.IconDirectory + icon),
                    Is.Not.Null, icon);
            }
        }

        private static string Read(string fileName) =>
            File.ReadAllText(Path.GetFullPath(MeshDeformerStyles.StyleDirectory + fileName));

        private static string[] Variables(string fileName) =>
            Regex.Matches(Read(fileName), @"(--md-[A-Za-z0-9-]+)\s*:").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
    }
}
#endif
