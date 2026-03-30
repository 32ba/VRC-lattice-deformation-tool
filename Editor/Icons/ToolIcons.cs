#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Loads tool icons from Editor/Icons/ PNG files (Tabler Icons, MIT license).
    /// Falls back to Unity built-in icons if the PNG file is not found.
    /// </summary>
    internal static class ToolIcons
    {
        private static readonly Dictionary<string, Texture2D> s_cache = new();
        private static string s_iconDir;

        // Icon name constants
        public const string Move = "move";
        public const string Rotate = "rotate";
        public const string Scale = "scale";
        public const string Brush = "brush";
        public const string Smooth = "smooth";
        public const string VertexSelect = "vertex-select";
        public const string Normal = "normal";
        public const string Mirror = "mirror";
        public const string Eye = "eye";
        public const string EyeOff = "eye-off";
        public const string Settings = "settings";
        public const string Invert = "invert";
        public const string BackfaceCull = "backface-cull";
        public const string Proportional = "proportional";
        public const string Clear = "clear";
        public const string Reset = "reset";
        public const string Connected = "connected";
        public const string SurfaceDistance = "surface-distance";
        public const string Pivot = "pivot";
        public const string Global = "global";
        public const string Local = "local";

        // Built-in fallback mapping
        private static readonly Dictionary<string, string> s_builtinFallback = new()
        {
            { Move, "MoveTool" },
            { Rotate, "RotateTool" },
            { Scale, "ScaleTool" },
            { Brush, "TerrainInspector.TerrainToolSplat" },
            { Smooth, "TerrainInspector.TerrainToolSmoothHeight" },
            { VertexSelect, "EditCollider" },
            { Normal, "TerrainInspector.TerrainToolSetHeight" },
            { Mirror, "d_PreMatQuad" },
            { Eye, "d_ViewToolOrbit" },
            { Settings, "d_Settings" },
            { Clear, "d_TreeEditor.Trash" },
            { Reset, "d_Refresh" },
        };

        /// <summary>
        /// Gets the icon texture by name. Loads from Editor/Icons/{name}.png,
        /// falling back to Unity built-in icons if not found.
        /// </summary>
        public static Texture2D Get(string name)
        {
            if (s_cache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var tex = LoadFromPackage(name);
            if (tex == null)
                tex = LoadBuiltinFallback(name);

            if (tex != null)
                s_cache[name] = tex;

            return tex;
        }

        /// <summary>
        /// Creates a GUIContent with icon + localized text + tooltip.
        /// </summary>
        public static GUIContent Content(string iconName, string locKey)
        {
            var tex = Get(iconName);
            var text = LatticeLocalization.Tr(locKey);
            var tooltip = LatticeLocalization.Tooltip(locKey);
            return tex != null
                ? new GUIContent(text, tex, tooltip)
                : new GUIContent(text, tooltip);
        }

        private static Texture2D LoadFromPackage(string name)
        {
            if (s_iconDir == null)
            {
                // Find the Editor/Icons directory relative to this script
                var guids = AssetDatabase.FindAssets("t:Script ToolIcons");
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Editor/Icons/ToolIcons"))
                    {
                        s_iconDir = Path.GetDirectoryName(path).Replace('\\', '/');
                        break;
                    }
                }
                if (s_iconDir == null)
                    s_iconDir = "";
            }

            if (string.IsNullOrEmpty(s_iconDir))
                return null;

            string assetPath = $"{s_iconDir}/{name}.png";
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (source == null) return null;

            // Resize to 16x16 for IMGUI toolbar/toggle icon size
            const int iconSize = 16;
            var rt = RenderTexture.GetTemporary(iconSize, iconSize, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var resized = new Texture2D(iconSize, iconSize, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            resized.ReadPixels(new Rect(0, 0, iconSize, iconSize), 0, 0);
            resized.Apply(false, true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return resized;
        }

        private static Texture2D LoadBuiltinFallback(string name)
        {
            if (!s_builtinFallback.TryGetValue(name, out var builtinName))
                return null;

            var content = EditorGUIUtility.IconContent(builtinName);
            return content?.image as Texture2D;
        }
    }
}
#endif
