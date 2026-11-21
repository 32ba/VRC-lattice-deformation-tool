#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Reflection-based helpers to access NDMF preview proxy renderers.
    /// Supports properties/fields containing "OriginalToProxy*" and a fallback
    /// method call GetProxyRenderer(Renderer).
    /// </summary>
    internal static class NDMFPreviewProxyUtility
    {
        private const BindingFlags k_BindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // Reflection metadata is immutable for a loaded Type, so each lookup is cached
        // per Type (including "not found" results) and reused for every Scene View event.
        private static readonly Dictionary<Type, MethodInfo> s_proxyLookupMethods = new Dictionary<Type, MethodInfo>();
        private static readonly Dictionary<Type, MethodInfo[]> s_proxyMapMethods = new Dictionary<Type, MethodInfo[]>();
        private static readonly Dictionary<Type, MemberInfo[]> s_proxyMapMembers = new Dictionary<Type, MemberInfo[]>();
        private static readonly Dictionary<Type, (PropertyInfo key, PropertyInfo value)> s_keyValueProperties =
            new Dictionary<Type, (PropertyInfo key, PropertyInfo value)>();
        private static Type s_previewSessionType;
        private static PropertyInfo s_previewSessionCurrentProperty;
        private static bool s_previewSessionCurrentPropertyResolved;

        [ExcludeFromCodeCoverage]
        public static bool TryGetProxyRenderer(Renderer original, out Renderer proxy)
        {
            return TryGetProxyRenderer(original, GetCurrentSession(), out proxy);
        }

        internal static bool TryGetProxyRenderer(Renderer original, object session, out Renderer proxy)
        {
            proxy = null;
            if (original == null)
            {
                return false;
            }

            foreach (var (orig, prox) in GetOriginalToProxyPairs(session))
            {
                if (orig == null || prox == null) continue;

                if (!ReferenceEquals(orig, original) && orig.gameObject != original.gameObject) continue;
                proxy = prox;
                return true;
            }

            return TryInvokeProxyLookup(session, original, out proxy);
        }

        [ExcludeFromCodeCoverage]
        private static bool TryInvokeProxyLookup(Renderer original, out Renderer proxy)
        {
            return TryInvokeProxyLookup(GetCurrentSession(), original, out proxy);
        }

        internal static bool TryInvokeProxyLookup(object session, Renderer original, out Renderer proxy)
        {
            proxy = null;
            if (session == null || original == null)
            {
                return false;
            }

            var method = GetProxyLookupMethod(session.GetType());
            if (method == null)
            {
                return false;
            }

            try
            {
                proxy = method.Invoke(session, new object[] { original }) as Renderer;
                return proxy != null;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo GetProxyLookupMethod(Type type)
        {
            if (s_proxyLookupMethods.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var method = type.GetMethod("GetProxyRenderer", k_BindingFlags, null, new[] { typeof(Renderer) }, null);
            if (method == null)
            {
                foreach (var m in type.GetMethods(k_BindingFlags))
                {
                    if (!m.Name.Contains("Proxy", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parameters = m.GetParameters();
                    if (parameters.Length != 1 || m.ReturnType != typeof(Renderer))
                    {
                        continue;
                    }

                    method = m;
                    break;
                }
            }

            s_proxyLookupMethods[type] = method;
            return method;
        }

        [ExcludeFromCodeCoverage]
        private static IEnumerable<(Renderer original, Renderer proxy)> GetOriginalToProxyPairs()
        {
            return GetOriginalToProxyPairs(GetCurrentSession());
        }

        internal static IEnumerable<(Renderer original, Renderer proxy)> GetOriginalToProxyPairs(object session)
        {
            if (session == null)
            {
                yield break;
            }

            foreach (var member in EnumerateProxyMapMembers(session))
            {
                object value = null;
                try
                {
                    value = member switch
                    {
                        PropertyInfo p => p.GetValue(session),
                        FieldInfo f => f.GetValue(session),
                        _ => null
                    };
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                foreach (var pair in ExtractPairs(value))
                {
                    yield return pair;
                }
            }

            // Fallback: if no members matched, attempt method-based lookup for renderer/object maps.
            foreach (var pair in EnumerateProxyPairsFromMethods(session))
            {
                yield return pair;
            }
        }

        internal static IEnumerable<(Renderer original, Renderer proxy)> EnumerateProxyPairsFromMethods(object session)
        {
            if (session == null)
            {
                yield break;
            }

            foreach (var method in GetProxyMapMethods(session.GetType()))
            {
                object value = null;
                try
                {
                    value = method.Invoke(session, null);
                }
                catch
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                foreach (var pair in ExtractPairs(value))
                {
                    yield return pair;
                }
            }
        }

        private static MethodInfo[] GetProxyMapMethods(Type type)
        {
            if (s_proxyMapMethods.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var methods = new List<MethodInfo>();
            foreach (var method in type.GetMethods(k_BindingFlags))
            {
                if (!method.Name.Contains("GetOriginalToProxy", StringComparison.OrdinalIgnoreCase) &&
                    !method.Name.Contains("OriginalToProxy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Expect zero-arg returning dictionary-like object.
                if (method.GetParameters().Length != 0)
                {
                    continue;
                }

                methods.Add(method);
            }

            var result = methods.ToArray();
            s_proxyMapMethods[type] = result;
            return result;
        }

        internal static IEnumerable<(Renderer original, Renderer proxy)> ExtractPairs(object value)
        {
            if (value is IDictionary dict)
            {
                foreach (DictionaryEntry entry in dict)
                {
                    if (TryBuildPair(entry.Key, entry.Value, out var pair))
                    {
                        yield return pair;
                    }
                }
                yield break;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    var type = item.GetType();
                    if (!s_keyValueProperties.TryGetValue(type, out var properties))
                    {
                        properties = (type.GetProperty("Key"), type.GetProperty("Value"));
                        s_keyValueProperties[type] = properties;
                    }

                    var keyProp = properties.key;
                    var valueProp = properties.value;
                    if (keyProp == null || valueProp == null)
                    {
                        continue;
                    }

                    var keyObj = keyProp.GetValue(item);
                    var valObj = valueProp.GetValue(item);
                    if (TryBuildPair(keyObj, valObj, out var pair))
                    {
                        yield return pair;
                    }
                }
            }
        }

        internal static bool TryBuildPair(object keyObj, object valObj, out (Renderer, Renderer) pair)
        {
            pair = (null, null);
            var keyRenderer = ExtractRenderer(keyObj);
            var valRenderer = ExtractRenderer(valObj);
            if (keyRenderer != null && valRenderer != null)
            {
                pair = (keyRenderer, valRenderer);
                return true;
            }

            return false;
        }

        [ExcludeFromCodeCoverage]
        private static Renderer ExtractRenderer(object obj)
        {
            // Destroyed Unity objects remain non-null as managed objects and can still be
            // present in NDMF's reflected preview maps until the session is rebuilt.
            // Calling GetComponent on such a stale GameObject/Component throws instead of
            // behaving like a missing proxy, so filter it before pattern matching.
            if (obj is UnityEngine.Object unityObject && unityObject == null)
            {
                return null;
            }

            switch (obj)
            {
                case Renderer r:
                    return r;
                case Component c:
                    return c.GetComponent<Renderer>();
                case GameObject go:
                    return go.GetComponent<Renderer>();
                default:
                    return null;
            }
        }

        internal static IEnumerable<MemberInfo> EnumerateProxyMapMembers(object session)
        {
            if (session == null)
            {
                yield break;
            }

            foreach (var member in GetProxyMapMembers(session.GetType()))
            {
                yield return member;
            }
        }

        private static MemberInfo[] GetProxyMapMembers(Type type)
        {
            if (s_proxyMapMembers.TryGetValue(type, out var cached))
            {
                return cached;
            }

            bool IsCandidate(string name) => name.IndexOf("OriginalToProxy", StringComparison.OrdinalIgnoreCase) >= 0;

            var members = new List<MemberInfo>();
            foreach (var prop in type.GetProperties(k_BindingFlags))
            {
                if (IsCandidate(prop.Name))
                {
                    members.Add(prop);
                }
            }

            foreach (var field in type.GetFields(k_BindingFlags))
            {
                if (IsCandidate(field.Name))
                {
                    members.Add(field);
                }
            }

            var result = members.ToArray();
            s_proxyMapMembers[type] = result;
            return result;
        }

        [ExcludeFromCodeCoverage]
        private static object GetCurrentSession()
        {
            var type = FindPreviewSessionType();
            if (type == null)
            {
                return null;
            }

            if (!s_previewSessionCurrentPropertyResolved)
            {
                s_previewSessionCurrentProperty = type.GetProperty(
                    "Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                s_previewSessionCurrentPropertyResolved = true;
            }

            var prop = s_previewSessionCurrentProperty;
            if (prop == null)
            {
                return null;
            }

            try
            {
                return prop.GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        [ExcludeFromCodeCoverage]
        private static Type FindPreviewSessionType()
        {
            // Only a found type is cached; a miss is searched again as before.
            if (s_previewSessionType != null)
            {
                return s_previewSessionType;
            }

            s_previewSessionType = FindPreviewSessionTypeUncached();
            return s_previewSessionType;
        }

        [ExcludeFromCodeCoverage]
        private static Type FindPreviewSessionTypeUncached()
        {
            var type = Type.GetType("nadena.dev.ndmf.preview.PreviewSession, nadena.dev.ndmf.preview");
            if (type != null)
            {
                return type;
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = asm.GetType("nadena.dev.ndmf.preview.PreviewSession");
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
        }
    }
}
#endif
