using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.CodeCoverage;
using UnityEngine;
using UnityEngine.TestTools;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    public static class LatticeCoverageMcpRunner
    {
        public const string DefaultResultsPath = "Temp/LatticeCoverage";

        [MenuItem("Tools/Lattice Deformation Tool/Coverage/Configure")]
        public static void ConfigureFromMenu()
        {
            Debug.Log(Configure());
        }

        [MenuItem("Tools/Lattice Deformation Tool/Coverage/Start Recording")]
        public static void StartRecordingFromMenu()
        {
            Debug.Log(StartRecording());
        }

        [MenuItem("Tools/Lattice Deformation Tool/Coverage/Stop Recording")]
        public static void StopRecordingFromMenu()
        {
            Debug.Log(StopRecording());
        }

        public static string Configure()
        {
            var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packagePath = Path.Combine(projectPath, "Packages", "net.32ba.lattice-deformation-tool")
                .Replace("\\", "/");
            var resultsPath = Path.Combine(projectPath, DefaultResultsPath).Replace("\\", "/");
            var targetAssemblies = BuildTargetAssemblies(projectPath);

            Directory.CreateDirectory(resultsPath);

            SetCoveragePreference("Path", resultsPath, true);
            SetCoveragePreference("HistoryPath", Path.Combine(resultsPath, "History").Replace("\\", "/"), true);
            SetCoveragePreference("IncludeAssemblies", targetAssemblies, false);
            SetCoveragePreference("PathsToInclude", string.Empty, true);
            SetCoveragePreference("PathsToExclude", $"{packagePath}/Tests/**,**/Tests/**", true);
            SetCoveragePreference("GenerateHTMLReport", true);
            SetCoveragePreference("GenerateAdditionalReports", true);
            SetCoveragePreference("GenerateBadge", true);
            SetCoveragePreference("GenerateAdditionalMetrics", true);
            SetCoveragePreference("GenerateTestReferences", true);
            SetCoveragePreference("AutoGenerateReport", true);
            SetCoveragePreference("OpenReportWhenGenerated", false);
            SetCoveragePreference("EnableCodeCoverage", true);

            Coverage.enabled = true;
            CompilationPipeline.codeOptimization = CodeOptimization.Debug;
            EditorPrefs.SetBool("ScriptDebugInfoEnabled", true);
            CodeCoverage.VerbosityLevel = LogVerbosityLevel.Info;
            DisableBurstCompilation();

            return $"Configured Unity Code Coverage for MCP. ResultsPath={resultsPath}; TargetAssemblies={targetAssemblies}";
        }

        public static string StartRecording()
        {
            Configure();
            CodeCoverage.StartRecording();
            return "Unity Code Coverage recording started.";
        }

        public static string StopRecording()
        {
            CodeCoverage.StopRecording();
            Coverage.enabled = false;
            SetCoveragePreference("EnableCodeCoverage", false);
            return "Unity Code Coverage recording stopped and disabled. Report generation requested.";
        }

        public static string GetStatus()
        {
            var projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var resultsPath = Path.Combine(projectPath, DefaultResultsPath).Replace("\\", "/");
            var targetAssemblies = BuildTargetAssemblies(projectPath);
            var xmlCount = Directory.Exists(resultsPath)
                ? Directory.GetFiles(resultsPath, "*.xml", SearchOption.AllDirectories).Length
                : 0;
            var htmlCount = Directory.Exists(resultsPath)
                ? Directory.GetFiles(resultsPath, "*.html", SearchOption.AllDirectories).Length
                : 0;
            return string.Join(
                "\n",
                $"Coverage.enabled={Coverage.enabled}",
                $"CodeOptimization={CompilationPipeline.codeOptimization}",
                $"ScriptDebugInfoEnabled={EditorPrefs.GetBool("ScriptDebugInfoEnabled", false)}",
                $"BurstCompilation={EditorPrefs.GetBool("BurstCompilation", false)}",
                $"ResultsPath={resultsPath}",
                $"CoverageXmlFiles={xmlCount}",
                $"CoverageHtmlFiles={htmlCount}",
                $"TargetAssemblies={targetAssemblies}",
                "If tests pass but CoverageXmlFiles remains 0 and Unity logs 'Visited sequence points not found', restart the Editor after Configure() and rerun the MCP test command.");
        }

        private static string BuildTargetAssemblies(string projectPath)
        {
            var assemblies = new List<string>
            {
                "net.32ba.lattice-deformation-tool",
                "net.32ba.lattice-deformation-tool.editor"
            };

            if (IsVrchatAvatarSdkAvailable(projectPath))
            {
                assemblies.Add("net.32ba.lattice-deformation-tool.vrchat");
            }

            return string.Join(",", assemblies);
        }

        private static bool IsVrchatAvatarSdkAvailable(string projectPath)
        {
            var embeddedPackage = Path.Combine(projectPath, "Packages", "com.vrchat.avatars", "package.json");
            if (File.Exists(embeddedPackage))
            {
                return true;
            }

            var lockPath = Path.Combine(projectPath, "Packages", "packages-lock.json");
            if (File.Exists(lockPath) && File.ReadAllText(lockPath).Contains("\"com.vrchat.avatars\""))
            {
                return true;
            }

            var packageCache = Path.Combine(projectPath, "Library", "PackageCache");
            return Directory.Exists(packageCache) &&
                Directory.GetDirectories(packageCache, "com.vrchat.avatars@*").Length > 0;
        }

        private static void SetCoveragePreference(string key, bool value)
        {
            InvokeCoveragePreference("SetBool", key, value);
        }

        private static void SetCoveragePreference(string key, string value, bool pathValue)
        {
            InvokeCoveragePreference(pathValue ? "SetStringForPaths" : "SetString", key, value);
        }

        private static void InvokeCoveragePreference(string methodName, string key, object value)
        {
            var preferencesType = Type.GetType("UnityEditor.TestTools.CodeCoverage.CoveragePreferences, Unity.TestTools.CodeCoverage.Editor");
            if (preferencesType == null)
            {
                throw new InvalidOperationException("Unity Code Coverage preferences type was not found.");
            }

            var instance = preferencesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (instance == null)
            {
                throw new InvalidOperationException("Unity Code Coverage preferences instance was not found.");
            }

            var valueType = value is bool ? typeof(bool) : typeof(string);
            var method = FindCoveragePreferenceMethod(preferencesType, methodName, valueType);
            if (method == null)
            {
                throw new InvalidOperationException($"Unity Code Coverage preference method was not found: {methodName}");
            }

            var parameters = method.GetParameters();
            var arguments = new object[parameters.Length];
            arguments[0] = key;
            arguments[1] = value;
            for (var i = 2; i < arguments.Length; i++)
            {
                arguments[i] = Type.Missing;
            }

            method.Invoke(instance, arguments);
        }

        private static MethodInfo FindCoveragePreferenceMethod(Type preferencesType, string methodName, Type valueType)
        {
            foreach (var method in preferencesType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length < 2 ||
                    parameters[0].ParameterType != typeof(string) ||
                    parameters[1].ParameterType != valueType)
                {
                    continue;
                }

                return method;
            }

            return null;
        }

        private static void DisableBurstCompilation()
        {
            var burstCompilerType = Type.GetType("Unity.Burst.BurstCompiler, Unity.Burst");
            var options = burstCompilerType?.GetProperty("Options", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var enableProperty = options?.GetType().GetProperty("EnableBurstCompilation", BindingFlags.Public | BindingFlags.Instance);
            if (enableProperty != null && enableProperty.CanWrite)
            {
                enableProperty.SetValue(options, false);
            }

            EditorPrefs.SetBool("BurstCompilation", false);
        }
    }
}
