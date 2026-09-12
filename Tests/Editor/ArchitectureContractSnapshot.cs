#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // Capture this file against the fixed baseline assembly, not the replacement
    // implementation. Normal test runs only read the checked-in snapshot.
    public static class ArchitectureContractSnapshot
    {
        public const string PackagePath = "Packages/net.32ba.lattice-deformation-tool";

        [Serializable]
        public sealed class Document
        {
            public int schemaVersion = 1;
            public string baselineCommit;
            public string unityVersion;
            public string[] publicApi;
            public string[] serializedFields;
            public string[] serializedPaths;
            public string[] enumValues;
            public string[] assetGuids;
        }

        public static void Export()
        {
            var args = Environment.GetCommandLineArgs();
            int outputAt = Array.IndexOf(args, "-latticeContractOutput");
            int commitAt = Array.IndexOf(args, "-latticeBaselineCommit");
            if (outputAt < 0 || outputAt + 1 >= args.Length || commitAt < 0 || commitAt + 1 >= args.Length)
                throw new ArgumentException("An explicit output path and baseline commit are required.");
            var document = Capture();
            document.baselineCommit = args[commitAt + 1];
            File.WriteAllText(args[outputAt + 1], JsonUtility.ToJson(document, true) + "\n");
            Debug.Log($"Architecture baseline: {document.publicApi.Length} API entries, " +
                      $"{document.serializedFields.Length} serialized fields, {document.enumValues.Length} enum values.");
        }

        public static Document Capture()
        {
            var runtime = typeof(LatticeDeformer).Assembly;
            var assemblies = new[] { runtime, Assembly.Load("net.32ba.lattice-deformation-tool.editor") };
            var api = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(IsExternallyVisible))
            {
                api.Add($"type {TypeName(type)} : {TypeName(type.BaseType)} " +
                        $"abstract={type.IsAbstract} sealed={type.IsSealed}");
                foreach (var implemented in type.GetInterfaces())
                    api.Add($"interface {TypeName(type)} : {TypeName(implemented)}");
                const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public |
                                           BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                foreach (var field in type.GetFields(flags).Where(f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly))
                    api.Add($"field {TypeName(type)}.{field.Name} : {TypeName(field.FieldType)} " +
                            $"public={field.IsPublic} static={field.IsStatic} readonly={field.IsInitOnly} const={field.IsLiteral}" +
                            (field.IsLiteral ? " value=" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture) : ""));
                foreach (var method in type.GetMethods(flags).Where(IsVisibleMethod))
                    api.Add($"method {TypeName(type)}.{method.Name}`{method.GetGenericArguments().Length}" +
                            $"({Parameters(method.GetParameters())}) : {TypeName(method.ReturnType)} " +
                            $"public={method.IsPublic} static={method.IsStatic} virtual={method.IsVirtual} " +
                            $"abstract={method.IsAbstract} final={method.IsFinal}");
                foreach (var constructor in type.GetConstructors(flags).Where(IsVisibleMethod))
                    api.Add($"constructor {TypeName(type)}({Parameters(constructor.GetParameters())}) public={constructor.IsPublic}");
            }

            var fields = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<Type>();
            var roots = runtime.GetTypes().Where(t => !t.IsAbstract &&
                (typeof(MonoBehaviour).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t))).ToArray();
            var pending = new Queue<Type>(roots);
            while (pending.Count > 0)
            {
                var type = pending.Dequeue();
                if (type.Assembly != runtime || !visited.Add(type) || type.IsEnum) continue;
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsNotSerialized || field.IsInitOnly ||
                        (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)) &&
                         !Attribute.IsDefined(field, typeof(SerializeReference)))) continue;
                    fields.Add($"{TypeName(type)}.{field.Name} : {TypeName(field.FieldType)}");
                    var nested = field.FieldType;
                    if (nested.IsArray) nested = nested.GetElementType();
                    else if (nested.IsGenericType && nested.GetGenericTypeDefinition() == typeof(List<>))
                        nested = nested.GetGenericArguments()[0];
                    pending.Enqueue(nested);
                }
                if (type.BaseType != null) pending.Enqueue(type.BaseType);
            }

            var enums = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var type in runtime.GetTypes().Where(t => t.IsEnum))
                foreach (string name in Enum.GetNames(type))
                    enums.Add($"{TypeName(type)}.{name} = " +
                              Convert.ToString(Convert.ChangeType(Enum.Parse(type, name), Enum.GetUnderlyingType(type)),
                                  CultureInfo.InvariantCulture));

            var guids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string directory in new[] { "Runtime", "Editor" })
                foreach (string file in Directory.GetFiles(Path.Combine(PackagePath, directory), "*.meta", SearchOption.AllDirectories))
                    if (file.EndsWith(".cs.meta", StringComparison.Ordinal) || file.EndsWith(".asmdef.meta", StringComparison.Ordinal))
                        guids.Add(AssetDatabase.AssetPathToGUID(file.Substring(0, file.Length - 5).Replace('\\', '/')));
            guids.Remove("");
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var root in roots) AddSerializedPaths(root, TypeName(root) + ":", runtime, paths, new HashSet<Type>());
            return new Document
            {
                unityVersion = Application.unityVersion,
                publicApi = api.ToArray(), serializedFields = fields.ToArray(), serializedPaths = paths.ToArray(),
                enumValues = enums.ToArray(), assetGuids = guids.ToArray()
            };
        }

        private static bool IsExternallyVisible(Type type) =>
            type.IsPublic || (type.IsNestedPublic && IsExternallyVisible(type.DeclaringType));

        private static void AddSerializedPaths(Type type, string prefix, Assembly runtime,
            ISet<string> paths, HashSet<Type> ancestors)
        {
            if (type.Assembly != runtime || type.IsEnum || !ancestors.Add(type)) return;
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                  BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.IsNotSerialized || field.IsInitOnly ||
                    (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField)) &&
                     !Attribute.IsDefined(field, typeof(SerializeReference)))) continue;
                string path = prefix + field.Name;
                paths.Add(path);
                var nested = field.FieldType;
                if (nested.IsArray || (nested.IsGenericType && nested.GetGenericTypeDefinition() == typeof(List<>)))
                {
                    paths.Add(path + ".Array.size");
                    path += ".Array.data[*]";
                    paths.Add(path);
                    nested = nested.IsArray ? nested.GetElementType() : nested.GetGenericArguments()[0];
                }
                if (!typeof(UnityEngine.Object).IsAssignableFrom(nested))
                    AddSerializedPaths(nested, path + ".", runtime, paths, ancestors);
            }
            ancestors.Remove(type);
        }

        private static bool IsVisibleMethod(MethodBase method) =>
            method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

        private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p =>
            $"{(p.IsOut ? "out " : p.IsIn ? "in " : "")}{TypeName(p.ParameterType)} {p.Name}" +
            (p.IsOptional ? "=" + Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) : "")));

        private static string TypeName(Type type)
        {
            if (type == null) return "none";
            if (type.IsGenericParameter) return type.Name;
            if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
            if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (!type.IsGenericType) return type.FullName;
            return type.GetGenericTypeDefinition().FullName + "<" +
                   string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        }
    }
}
#endif
