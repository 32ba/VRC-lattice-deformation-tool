#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Builds Scene View overlay controls bound to tool settings through getters and setters. The
    /// tool settings can also change from shortcuts, so <see cref="Sync"/> re-reads every control
    /// without writing anything back.
    /// </summary>
    internal sealed class OverlayControls
    {
        private readonly List<Action> _sync = new();

        internal void Sync()
        {
            foreach (var sync in _sync) sync();
        }

        /// <summary>Registers a refresh and runs it once.</summary>
        internal void Track(Action sync)
        {
            _sync.Add(sync);
            sync();
        }

        internal void EnabledWhen(VisualElement element, Func<bool> condition) =>
            Track(() => element.SetEnabled(condition()));

        internal void VisibleWhen(VisualElement element, Func<bool> condition) =>
            Track(() => MdUI.SetVisible(element, condition()));

        internal static VisualElement Group(string label, params VisualElement[] children)
        {
            var group = MdUI.Element("md-overlay__group");
            if (!string.IsNullOrEmpty(label)) group.Add(MdUI.Text(label, "md-overlay__group-label"));
            foreach (var child in children) if (child != null) group.Add(child);
            return group;
        }

        internal VisualElement Segmented(string[] labels, Func<int> read, Action<int> write,
            string[] icons = null, bool strong = false, string[] tooltips = null)
        {
            VisualElement segmented = null;
            segmented = MdUI.Segmented(labels, read(), index =>
            {
                write(index);
                MdUI.SelectSegment(segmented, read());
            }, strong ? "md-segmented--strong" : null, icons);
            if (tooltips != null)
            {
                int i = 0;
                foreach (var item in segmented.Children())
                {
                    if (i < tooltips.Length) item.tooltip = tooltips[i];
                    i++;
                }
            }
            Track(() => MdUI.SelectSegment(segmented, read()));
            return segmented;
        }

        internal VisualElement Switch(string label, Func<bool> read, Action<bool> write, string description = null)
        {
            var element = MdUI.Switch(label, description, read, write, out var refresh);
            Track(refresh);
            return element;
        }

        internal VisualElement Slider(string label, float min, float max, Func<float> read, Action<float> write,
            string tooltip = null)
        {
            var slider = new Slider(min, max) { showInputField = true, tooltip = tooltip };
            slider.SetValueWithoutNotify(read());
            slider.RegisterValueChangedCallback(evt => write(Mathf.Clamp(evt.newValue, min, max)));
            Track(() =>
            {
                float value = read();
                if (!Mathf.Approximately(slider.value, value)) slider.SetValueWithoutNotify(value);
            });
            var row = MdUI.FieldRow(label, slider);
            row.tooltip = tooltip;
            return row;
        }

        internal VisualElement Popup(string label, string[] items, Func<int> read, Action<int> write,
            string tooltip = null)
        {
            var popup = new PopupField<string>(new List<string>(items), Mathf.Clamp(read(), 0, items.Length - 1))
                { tooltip = tooltip };
            popup.RegisterValueChangedCallback(_ => write(Mathf.Clamp(popup.index, 0, items.Length - 1)));
            Track(() =>
            {
                int index = Mathf.Clamp(read(), 0, items.Length - 1);
                if (popup.index != index) popup.SetValueWithoutNotify(items[index]);
            });
            return MdUI.FieldRow(label, popup);
        }

        internal Label LiveText(Func<string> read, string classes = "md-text-small")
        {
            var label = MdUI.Text(read(), classes);
            Track(() =>
            {
                string text = read();
                if (label.text != text) label.text = text;
            });
            return label;
        }

        internal static VisualElement Row(params VisualElement[] children) =>
            MdUI.Element("md-button-row", children);

        internal static VisualElement Hint(string text) => MdUI.Text(text, "md-text-small md-overlay__hint");

        internal static string[] Texts(GUIContent[] contents)
        {
            var texts = new string[contents.Length];
            for (int i = 0; i < contents.Length; i++) texts[i] = contents[i].text;
            return texts;
        }

        internal static string[] Tr(params string[] keys)
        {
            var texts = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) texts[i] = LatticeLocalization.Tr(keys[i]);
            return texts;
        }

        internal static string[] Tooltips(params string[] keys)
        {
            var texts = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) texts[i] = LatticeLocalization.Tooltip(keys[i]);
            return texts;
        }
    }
}
#endif
