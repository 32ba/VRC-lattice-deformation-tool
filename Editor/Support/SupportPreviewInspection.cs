#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using nadena.dev.ndmf.preview;
namespace Net._32Ba.LatticeDeformationTool.Editor
{
    internal static class SupportPreviewInspection
    {
        internal static bool TryReadFilters(out IReadOnlyList<string> result)
        {
            result = Array.Empty<string>();
            object session = GetCurrentPreviewSession();
            object proxySession = GetMemberValue(session, "_proxySession");
            object filters = GetMemberValue(proxySession, "Filters") ??
                             GetMemberValue(proxySession, "_filters");
            if (filters is not IEnumerable enumerable)
            {
                return false;
            }

            var descriptions = new List<string>();
            foreach (object filter in enumerable)
            {
                if (filter == null) continue;
                Type type = filter.GetType();
                string description = type.FullName ?? type.Name;
                object placement = filter is LatticeDeformerPreviewFilter lattice
                    ? lattice.FilterPlacement : GetMemberValue(filter, "_placement");
                if (placement != null) description += ", placement=" + placement;
                descriptions.Add(description);
            }

            result = descriptions;
            return true;
        }

        private static object GetCurrentPreviewSession()
        {
            try
            {
                return PreviewSession.Current;
            }
            catch
            {
                return null;
            }
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                Type type = instance.GetType();
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null) return property.GetValue(instance);
                return type.GetField(name, flags)?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

    }
}
#endif
