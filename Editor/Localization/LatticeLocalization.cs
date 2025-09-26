#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class LatticeLocalization
    {
        internal enum Language
        {
            English = 0,
            Japanese = 1,
            Korean = 2,
            ChineseSimplified = 3,
            ChineseTraditional = 4
        }

        private const string k_EditorPrefsKey = "Net32Ba.LatticeLocalization.Language";

        private sealed class CatalogCache
        {
            internal IReadOnlyDictionary<string, string> Entries;
            internal double LastLoadTime;
        }

        private static readonly Dictionary<Language, CatalogCache> s_catalogs;
        private static readonly Dictionary<Language, string> s_catalogRelativePaths = new()
        {
            { Language.English, "Editor/Localization/en.po" },
            { Language.Japanese, "Editor/Localization/ja.po" },
            { Language.Korean, "Editor/Localization/ko.po" },
            { Language.ChineseSimplified, "Editor/Localization/zh-Hans.po" },
            { Language.ChineseTraditional, "Editor/Localization/zh-Hant.po" }
        };
        private static string s_packageRootPath;
        private static readonly string[] s_displayNames =
        {
            "English",
            "日本語",
            "한국어",
            "简体中文",
            "繁體中文"
        };

        static LatticeLocalization()
        {
            s_catalogs = new Dictionary<Language, CatalogCache>();
        }

        internal static event Action LanguageChanged;

        internal static Language CurrentLanguage
        {
            get
            {
                if (!Enum.TryParse(EditorPrefs.GetString(k_EditorPrefsKey, Language.English.ToString()), out Language stored))
                {
                    stored = Language.English;
                }

                return stored;
            }
            set
            {
                if (CurrentLanguage == value)
                {
                    return;
                }

                EditorPrefs.SetString(k_EditorPrefsKey, value.ToString());

                EnsureCatalogLoaded(value, forceReload: true);
                LanguageChanged?.Invoke();
            }
        }

        internal static string[] DisplayNames => s_displayNames;

        internal static GUIContent Content(string text)
        {
            return new GUIContent(Tr(text));
        }

        internal static GUIContent Content(string text, string tooltip)
        {
            return new GUIContent(Tr(text), string.IsNullOrEmpty(tooltip) ? null : Tr(tooltip));
        }

        internal static string Tr(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var language = CurrentLanguage;
            if (language == Language.English)
            {
                return text;
            }

            var catalog = EnsureCatalogLoaded(language);
            if (catalog != null && catalog.TryGetValue(text, out var translated) && !string.IsNullOrEmpty(translated))
            {
                return translated;
            }

            return text;
        }

        private static IReadOnlyDictionary<string, string> EnsureCatalogLoaded(Language language, bool forceReload = false)
        {
            if (!s_catalogRelativePaths.TryGetValue(language, out var relativePath))
            {
                return null;
            }

            if (!s_catalogs.TryGetValue(language, out var cache) || cache == null)
            {
                cache = new CatalogCache();
                s_catalogs[language] = cache;
            }

            if (forceReload)
            {
                cache.Entries = LoadCatalog(relativePath);
                cache.LastLoadTime = EditorApplication.timeSinceStartup;
                return cache.Entries;
            }

            if (cache.Entries == null || cache.Entries.Count == 0)
            {
                double now = EditorApplication.timeSinceStartup;

                if (cache.Entries == null || now - cache.LastLoadTime > 1.0d)
                {
                    cache.Entries = LoadCatalog(relativePath);
                    cache.LastLoadTime = now;
                }
            }

            return cache.Entries;
        }

        private static IReadOnlyDictionary<string, string> LoadCatalog(string relativePath)
        {
            try
            {
                var absolutePath = ResolveCatalogPath(relativePath);
                if (string.IsNullOrEmpty(absolutePath) || !File.Exists(absolutePath))
                {
                    Debug.LogWarning($"Localization catalogue not found: {relativePath}");
                    return new Dictionary<string, string>();
                }

                return ParsePo(File.ReadAllLines(absolutePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load localization catalogue at '{relativePath}': {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private static string ResolveCatalogPath(string relativePath)
        {
            var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);

            var packageRoot = GetPackageRoot();
            if (!string.IsNullOrEmpty(packageRoot))
            {
                var candidate = Path.Combine(packageRoot, normalizedRelative);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetPackageRoot()
        {
            if (!string.IsNullOrEmpty(s_packageRootPath) && Directory.Exists(s_packageRootPath))
            {
                return s_packageRootPath;
            }

            try
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LatticeLocalization).Assembly);
                if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath) && Directory.Exists(packageInfo.resolvedPath))
                {
                    s_packageRootPath = packageInfo.resolvedPath;
                    return s_packageRootPath;
                }

                var guides = AssetDatabase.FindAssets("LatticeLocalization t:MonoScript");
                if (guides != null)
                {
                    foreach (var guid in guides)
                    {
                        var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }

                        var projectRoot = Path.GetDirectoryName(Application.dataPath);
                        if (string.IsNullOrEmpty(projectRoot))
                        {
                            continue;
                        }

                        var absoluteScriptPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
                        var directory = Path.GetDirectoryName(absoluteScriptPath);
                        if (string.IsNullOrEmpty(directory))
                        {
                            continue;
                        }

                        var editorFolder = Path.GetDirectoryName(directory);
                        if (string.IsNullOrEmpty(editorFolder))
                        {
                            continue;
                        }

                        var packageRoot = Path.GetDirectoryName(editorFolder);
                        if (!string.IsNullOrEmpty(packageRoot) && Directory.Exists(packageRoot))
                        {
                            s_packageRootPath = packageRoot;
                            return s_packageRootPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to resolve localization package root: {ex.Message}");
            }

            return s_packageRootPath;
        }

        private static IReadOnlyDictionary<string, string> ParsePo(IReadOnlyList<string> lines)
        {
            var catalog = new Dictionary<string, string>();
            string currentId = null;
            string currentValue = null;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("msgid "))
                {
                    currentId = ExtractPoString(lines, ref i, "msgid");
                }
                else if (trimmed.StartsWith("msgstr "))
                {
                    currentValue = ExtractPoString(lines, ref i, "msgstr");

                    if (!string.IsNullOrEmpty(currentId))
                    {
                        catalog[currentId] = currentValue ?? string.Empty;
                    }

                    currentId = null;
                    currentValue = null;
                }
            }

            return catalog;
        }

        private static string ExtractPoString(IReadOnlyList<string> lines, ref int index, string token)
        {
            var builder = new StringBuilder();
            ExtractLine(lines[index], token, builder);

            while (index + 1 < lines.Count)
            {
                var next = lines[index + 1].Trim();
                if (!next.StartsWith("\""))
                {
                    break;
                }

                index++;
                ExtractLine(lines[index], null, builder);
            }

            return builder.ToString();
        }

        private static void ExtractLine(string line, string token, StringBuilder builder)
        {
            var startIndex = line.IndexOf('"');
            if (startIndex < 0)
            {
                return;
            }

            var endIndex = line.LastIndexOf('"');
            if (endIndex <= startIndex)
            {
                return;
            }

            var segment = line.Substring(startIndex + 1, endIndex - startIndex - 1);
            builder.Append(Unescape(segment));
        }

        private static string Unescape(string value)
        {
            return value
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"");
        }
    }
}
#endif
