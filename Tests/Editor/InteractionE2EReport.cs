#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    [Serializable]
    internal sealed class InteractionE2EReport
    {
        [Serializable]
        internal sealed class Response
        {
            public string action;
            public string measurement = "input-to-final-proxy";
            public int editorUpdates;
            public double elapsedMs;
        }

        public int schemaVersion = 1;
        public string scenarioId;
        public string inputKind;
        public string evaluatedAtUtc;
        public string packageVersion;
        public string unityVersion;
        public string graphicsDevice;
        public string[] installedIntegrationPackages;
        public string[] usedIntegrationPackages = { "nadena.dev.ndmf" };
        public int operationCount;
        public int changedVertexCount;
        public double scenarioElapsedMs;
        public bool undoRedoEvaluated;
        public bool undoMatch;
        public bool redoMatch;
        public bool sourceUnchanged;
        public bool refusalReasonDisplayed;
        public string refusalReasonCode = "";
        public bool succeeded;
        public string failureReason = "Scenario did not complete.";
        public System.Collections.Generic.List<Response> responses = new();
        public System.Collections.Generic.List<string> observedErrors = new();

        internal static InteractionE2EReport Begin(string id, string kind)
        {
            var report = new InteractionE2EReport
            {
                scenarioId = id,
                inputKind = kind,
                evaluatedAtUtc = DateTime.UtcNow.ToString("O"),
                packageVersion = PackageInfo.FindForAssembly(typeof(LatticeDeformer).Assembly)?.version ?? "unknown",
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                installedIntegrationPackages = PackageInfo.GetAllRegisteredPackages()
                    .Where(p => p.name == "nadena.dev.ndmf" || p.name == "nadena.dev.modular-avatar" ||
                                p.name == "com.anatawa12.avatar-optimizer" || p.name.IndexOf("meshia", StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p.name).Select(p => p.name + "@" + p.version).ToArray()
            };
            // Replace the previous run immediately, including when setup later fails.
            report.Save();
            return report;
        }

        internal void Save()
        {
            string project = Path.GetDirectoryName(Application.dataPath);
            string json = JsonUtility.ToJson(this, true);
            SaveTo(Path.Combine(project, "Temp", "LatticeInteractionReports"), json);
            // CI collects artifacts after Unity exits. Keep a copy outside Unity's
            // temporary directory so editor cleanup cannot remove the evidence.
            if (Application.isBatchMode)
                SaveTo(Path.Combine(project, "TestResults", "LatticeInteractionReports"), json);
        }

        private void SaveTo(string directory, string json)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, scenarioId + ".json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, json);
            try { File.Copy(temporary, path, true); }
            finally { File.Delete(temporary); }
        }
    }
}
#endif
