#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Picks the CJK font for the Mesh Deformer UI the way a web page lists a font-family stack:
    /// each display language names fonts in order of preference, and the ones installed on this
    /// machine become the fallbacks behind the Editor's own Latin font. Unity's default fallback
    /// (Yu Gothic UI on Windows) is thin at Inspector sizes, so the stack prefers sturdier faces.
    /// When none of the candidates is installed, the Editor default is left in place.
    /// </summary>
    internal static class MeshDeformerFonts
    {
        internal readonly struct Candidate
        {
            internal readonly string Family;
            internal readonly string RegularStyle;
            internal readonly string BoldStyle;

            internal Candidate(string family, string regularStyle = "Regular", string boldStyle = "Bold")
            {
                Family = family;
                RegularStyle = regularStyle;
                BoldStyle = boldStyle;
            }

            public override string ToString() => Family;
        }

        // Order matters: the first installed font draws the glyphs it has, later ones fill gaps.
        // Han characters are shaped differently per language, so each language puts its own fonts first.
        private static readonly Candidate[] s_japanese =
        {
            new("BIZ UDPGothic"),
            new("Meiryo UI"),
            new("Hiragino Sans", "W3", "W6"),
            new("Hiragino Kaku Gothic ProN", "W3", "W6"),
            new("Noto Sans CJK JP"),
            new("Noto Sans JP"),
        };

        private static readonly Candidate[] s_korean =
        {
            new("Malgun Gothic"),
            new("Apple SD Gothic Neo"),
            new("Noto Sans CJK KR"),
            new("Noto Sans KR"),
        };

        private static readonly Candidate[] s_chineseSimplified =
        {
            new("Microsoft YaHei UI"),
            new("PingFang SC", "Regular", "Semibold"),
            new("Noto Sans CJK SC"),
            new("Noto Sans SC"),
        };

        private static readonly Candidate[] s_chineseTraditional =
        {
            new("Microsoft JhengHei UI"),
            new("PingFang TC", "Regular", "Semibold"),
            new("Noto Sans CJK TC"),
            new("Noto Sans TC"),
        };

        private const int BoldWeightIndex = 7;
        private const int ItalicWeightIndex = 4;

        private static readonly Dictionary<string, FontAsset> s_stacks = new();
        private static readonly Dictionary<string, FontAsset> s_faces = new();

        /// <summary>Font candidates for a display language; empty keeps the Editor default.</summary>
        internal static IReadOnlyList<Candidate> CandidatesFor(LatticeLocalization.Language language) => language switch
        {
            LatticeLocalization.Language.Japanese => s_japanese,
            LatticeLocalization.Language.Korean => s_korean,
            LatticeLocalization.Language.ChineseSimplified => s_chineseSimplified,
            LatticeLocalization.Language.ChineseTraditional => s_chineseTraditional,
            // English text has no CJK glyphs of its own; Japanese layer names still read best this way.
            _ => s_japanese,
        };

        /// <summary>Sets the font for the current display language on a UI root.</summary>
        internal static void Apply(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var stack = StackFor(LatticeLocalization.CurrentLanguage);
            root.style.unityFontDefinition = stack != null
                ? new StyleFontDefinition(FontDefinition.FromSDFFont(stack))
                : new StyleFontDefinition(StyleKeyword.Null);
        }

        internal static FontAsset StackFor(LatticeLocalization.Language language) => CreateStack(CandidatesFor(language));

        /// <summary>
        /// Builds (and caches) a font whose Latin glyphs come from the Editor font and whose other
        /// glyphs come from the installed candidates, in order. Returns null when none is installed
        /// or the Editor font cannot be found.
        /// </summary>
        internal static FontAsset CreateStack(IReadOnlyList<Candidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            string key = string.Join("|", candidates);
            if (s_stacks.TryGetValue(key, out var cached) && cached != null) return cached;

            var fallbacks = new List<FontAsset>();
            foreach (var candidate in candidates)
            {
                var face = LoadFace(candidate.Family, candidate.RegularStyle);
                if (face == null) continue;
                var bold = LoadFace(candidate.Family, candidate.BoldStyle);
                if (bold != null) SetWeight(face, BoldWeightIndex, bold, null);
                fallbacks.Add(face);
            }
            if (fallbacks.Count == 0) return null;

            var primary = CreateLatinPrimary();
            if (primary == null) return null;
            primary.fallbackFontAssetTable = fallbacks;
            primary.name = "Mesh Deformer UI (" + key + ")";
            s_stacks[key] = primary;
            return primary;
        }

        private static FontAsset LoadFace(string family, string style)
        {
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(style)) return null;
            string key = family + "/" + style;
            if (s_faces.TryGetValue(key, out var cached)) return cached;
            FontAsset face = null;
            try { face = FontAsset.CreateFontAsset(family, style); }
            catch (Exception) { face = null; }
            // A missing family can resolve to some other face; accept only the one asked for.
            if (face != null && !string.Equals(face.faceInfo.familyName, family, StringComparison.OrdinalIgnoreCase))
            {
                UnityEngine.Object.DestroyImmediate(face);
                face = null;
            }
            if (face != null) face.hideFlags = HideFlags.HideAndDontSave;
            s_faces[key] = face;
            return face;
        }

        /// <summary>
        /// A dynamic copy of the Editor's UI font (Inter), so Latin text keeps the Editor look while
        /// this asset carries its own fallback list. The Editor's asset itself is never modified.
        /// </summary>
        private static FontAsset CreateLatinPrimary()
        {
            var editorFont = EditorDefaultFontAsset();
            if (editorFont == null || editorFont.sourceFontFile == null) return null;
            var primary = FontAsset.CreateFontAsset(editorFont.sourceFontFile, Mathf.RoundToInt(editorFont.faceInfo.pointSize),
                editorFont.atlasPadding, editorFont.atlasRenderMode, editorFont.atlasWidth, editorFont.atlasHeight,
                AtlasPopulationMode.Dynamic, true);
            if (primary == null) return null;
            primary.hideFlags = HideFlags.HideAndDontSave;
            var weights = editorFont.fontWeightTable;
            if (weights != null && weights.Length > BoldWeightIndex)
            {
                SetWeight(primary, BoldWeightIndex, weights[BoldWeightIndex].regularTypeface, weights[BoldWeightIndex].italicTypeface);
                SetWeight(primary, ItalicWeightIndex, weights[ItalicWeightIndex].regularTypeface, weights[ItalicWeightIndex].italicTypeface);
            }
            return primary;
        }

        private static void SetWeight(FontAsset target, int index, FontAsset regular, FontAsset italic)
        {
            var table = target.fontWeightTable;
            if (table == null || table.Length <= index) return;
            table[index].regularTypeface = regular;
            table[index].italicTypeface = italic;
        }

        private static FontAsset EditorDefaultFontAsset()
        {
            // UnityEditor.EditorTextSettings is internal; read its default asset defensively.
            try
            {
                var type = typeof(EditorGUIUtility).Assembly.GetType("UnityEditor.EditorTextSettings");
                var property = type?.GetProperty("defaultTextSettings",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return (property?.GetValue(null) as TextSettings)?.defaultFontAsset;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
#endif
