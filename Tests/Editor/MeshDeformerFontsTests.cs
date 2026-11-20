#if UNITY_EDITOR
using System;
using System.Linq;
using Net._32Ba.LatticeDeformationTool.Editor;
using NUnit.Framework;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    public sealed class MeshDeformerFontsTests
    {
        private LatticeLocalization.Language _language;

        [SetUp]
        public void SetUp() => _language = LatticeLocalization.CurrentLanguage;

        [TearDown]
        public void TearDown() => LatticeLocalization.CurrentLanguage = _language;

        [Test]
        public void EveryLanguage_HasItsOwnCandidatesFirst()
        {
            Assert.That(First(LatticeLocalization.Language.Japanese), Is.EqualTo("BIZ UDPGothic"));
            Assert.That(First(LatticeLocalization.Language.Korean), Is.EqualTo("Malgun Gothic"));
            Assert.That(First(LatticeLocalization.Language.ChineseSimplified), Is.EqualTo("Microsoft YaHei UI"));
            Assert.That(First(LatticeLocalization.Language.ChineseTraditional), Is.EqualTo("Microsoft JhengHei UI"));
            // English text has no CJK glyphs of its own; CJK layer names use the Japanese order.
            Assert.That(First(LatticeLocalization.Language.English), Is.EqualTo("BIZ UDPGothic"));
        }

        [Test]
        public void EveryLanguage_ListsFontsForWindowsMacAndLinux()
        {
            foreach (LatticeLocalization.Language language in Enum.GetValues(typeof(LatticeLocalization.Language)))
            {
                var families = MeshDeformerFonts.CandidatesFor(language).Select(c => c.Family).ToArray();
                Assert.That(families.Length, Is.GreaterThanOrEqualTo(3), language.ToString());
                Assert.That(families, Is.Unique, language.ToString());
                Assert.That(families.Any(f => f.StartsWith("Noto Sans", StringComparison.Ordinal)), Is.True, language + " has no Linux font");
            }
        }

        [Test]
        public void CreateStack_ReturnsNullWhenNoCandidateIsInstalled()
        {
            var missing = new[] { new MeshDeformerFonts.Candidate("Mesh Deformer Missing Font 0f3a") };
            Assert.That(MeshDeformerFonts.CreateStack(missing), Is.Null);
            Assert.That(MeshDeformerFonts.CreateStack(Array.Empty<MeshDeformerFonts.Candidate>()), Is.Null);
        }

        [Test]
        public void CreateStack_SkipsMissingFontsAndKeepsTheInstalledOnesInOrder()
        {
            var installed = MeshDeformerFonts.CandidatesFor(LatticeLocalization.Language.Japanese)
                .FirstOrDefault(c => MeshDeformerFonts.CreateStack(new[] { c }) != null);
            if (installed.Family == null) Assert.Ignore("No Japanese UI font is installed on this machine.");

            var stack = MeshDeformerFonts.CreateStack(new[] { new MeshDeformerFonts.Candidate("Mesh Deformer Missing Font 0f3a"), installed });

            Assert.That(stack, Is.Not.Null);
            Assert.That(stack.fallbackFontAssetTable.Select(f => f.faceInfo.familyName), Is.EqualTo(new[] { installed.Family }));
            Assert.That(stack, Is.SameAs(MeshDeformerFonts.CreateStack(new[] { new MeshDeformerFonts.Candidate("Mesh Deformer Missing Font 0f3a"), installed })));
        }

        [Test]
        public void Apply_FollowsTheDisplayLanguage()
        {
            LatticeLocalization.CurrentLanguage = LatticeLocalization.Language.Japanese;
            var expected = MeshDeformerFonts.StackFor(LatticeLocalization.Language.Japanese);
            var root = new VisualElement();

            MeshDeformerFonts.Apply(root);

            if (expected == null)
            {
                // Nothing installed: the Editor default font stays in charge.
                Assert.That(root.style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
                return;
            }
            Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.SameAs(expected));
            Assert.That(expected.fallbackFontAssetTable, Is.Not.Empty);
            Assert.That(expected.fallbackFontAssetTable[0].faceInfo.familyName,
                Is.EqualTo(MeshDeformerFonts.CandidatesFor(LatticeLocalization.Language.Japanese)
                    .Select(c => c.Family).First(f => expected.fallbackFontAssetTable.Any(x => x.faceInfo.familyName == f))));
        }

        [Test]
        public void StylesApply_SetsTheFontStack()
        {
            var root = new VisualElement();
            MeshDeformerStyles.Apply(root, true);
            var expected = MeshDeformerFonts.StackFor(LatticeLocalization.CurrentLanguage);
            if (expected == null) Assert.Ignore("No candidate font is installed on this machine.");
            Assert.That(root.style.unityFontDefinition.value.fontAsset, Is.SameAs(expected));
        }

        private static string First(LatticeLocalization.Language language) =>
            MeshDeformerFonts.CandidatesFor(language)[0].Family;
    }
}
#endif
