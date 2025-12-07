#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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

        public static bool TryGetProxyRenderer(Renderer original, out Renderer proxy)
        {
            proxy = null;
            if (original == null)
            {
                return false;
            }

            foreach (var (orig, prox) in GetOriginalToProxyPairs())
            {
                if (orig == null || prox == null)
                {
                    continue;
                }

                if (ReferenceEquals(orig, original) || orig.gameObject == original.gameObject)
                {
                    proxy = prox;
                    return true;
                }
            }

            return TryInvokeProxyLookup(original, out proxy);
        }

        private static bool TryInvokeProxyLookup(Renderer original, out Renderer proxy)
        {
            proxy = null;
            var session = GetCurrentSession();
            if (session == null)
            {
                return false;
            }

            var type = session.GetType();
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

        private static IEnumerable<(Renderer original, Renderer proxy)> GetOriginalToProxyPairs()
        {
            var session = GetCurrentSession();
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

        private static IEnumerable<(Renderer original, Renderer proxy)> EnumerateProxyPairsFromMethods(object session)
        {
            if (session == null)
            {
                yield break;
            }

            var type = session.GetType();
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

        private static IEnumerable<(Renderer original, Renderer proxy)> ExtractPairs(object value)
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
                    var keyProp = type.GetProperty("Key");
                    var valueProp = type.GetProperty("Value");
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

        private static bool TryBuildPair(object keyObj, object valObj, out (Renderer, Renderer) pair)
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

        private static Renderer ExtractRenderer(object obj)
        {
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

        private static IEnumerable<MemberInfo> EnumerateProxyMapMembers(object session)
        {
            if (session == null)
            {
                yield break;
            }

            var type = session.GetType();

            bool IsCandidate(string name) => name.IndexOf("OriginalToProxy", StringComparison.OrdinalIgnoreCase) >= 0;

            foreach (var prop in type.GetProperties(k_BindingFlags))
            {
                if (IsCandidate(prop.Name))
                {
                    yield return prop;
                }
            }

            foreach (var field in type.GetFields(k_BindingFlags))
            {
                if (IsCandidate(field.Name))
                {
                    yield return field;
                }
            }
        }

        private static object GetCurrentSession()
        {
            var type = FindPreviewSessionType();
            if (type == null)
            {
                return null;
            }

            var prop = type.GetProperty("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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

        private static Type FindPreviewSessionType()
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
