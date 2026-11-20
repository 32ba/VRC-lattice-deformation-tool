#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Tests.Editor
{
    // Non-UI tests still need live selection and trackers. Suspend only the
    // presentation of existing Inspectors, restoring their exact display styles.
    internal sealed class InspectorPresentationScope : IDisposable
    {
        private readonly List<(VisualElement Root, StyleEnum<DisplayStyle> Display)> _inspectors = new();

        internal static EditorWindow[] InspectorWindows() => Resources.FindObjectsOfTypeAll<EditorWindow>()
            .Where(window => window.GetType().FullName == "UnityEditor.InspectorWindow" ||
                             window.GetType().FullName == "UnityEditor.PropertyEditor").ToArray();

        internal InspectorPresentationScope()
        {
            foreach (var inspector in InspectorWindows())
            {
                var root = inspector.rootVisualElement;
                _inspectors.Add((root, root.style.display));
                root.style.display = DisplayStyle.None;
            }
        }

        public void Dispose()
        {
            ActiveEditorTracker.sharedTracker.ForceRebuild();
            foreach (var (root, display) in _inspectors) root.style.display = display;
            _inspectors.Clear();
        }
    }
}
#endif
