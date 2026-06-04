#if LATTICE_VRCSDK3_AVATAR
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Validation;
using AvatarValidationSdk3 = VRC.SDK3.Validation.AvatarValidation;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    [InitializeOnLoad]
    internal static class LatticeDeformerWhitelist
    {
        private const string ComponentTypeName = "Net._32Ba.LatticeDeformationTool.LatticeDeformer";

        [ExcludeFromCodeCoverage]
        static LatticeDeformerWhitelist()
        {
            try
            {
                AppendToWhitelist(nameof(AvatarValidation.ComponentTypeWhiteListCommon));
                AppendToWhitelist(nameof(AvatarValidation.ComponentTypeWhiteListSdk3));

                var combinedField = typeof(AvatarValidationSdk3).GetField("CombinedComponentTypeWhiteList", BindingFlags.NonPublic | BindingFlags.Static);
                combinedField?.SetValue(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Lattice Deformer whitelist registration failed: {ex.Message}");
            }
        }

        [ExcludeFromCodeCoverage]
        private static void AppendToWhitelist(string fieldName)
        {
            var field = typeof(AvatarValidation).GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field == null)
            {
                return;
            }

            if (field.GetValue(null) is not string[] entries)
            {
                return;
            }

            field.SetValue(null, AppendComponentType(entries));
        }

        internal static string[] AppendComponentType(string[] entries)
        {
            if (entries == null || entries.Contains(ComponentTypeName))
            {
                return entries;
            }

            var updated = new string[entries.Length + 1];
            entries.CopyTo(updated, 0);
            updated[^1] = ComponentTypeName;
            return updated;
        }
    }
}
#endif
