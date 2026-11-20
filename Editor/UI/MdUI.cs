#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Net._32Ba.LatticeDeformationTool.Editor
{
    /// <summary>
    /// Builders for the design system components (Editor/UI/Styles/Components.uss). Each returns a
    /// plain UI Toolkit element carrying the component classes, so callers only add behaviour.
    /// </summary>
    internal static class MdUI
    {
        internal enum HelpKind { Info, Warning }

        internal static VisualElement Element(string classes = null, params VisualElement[] children)
        {
            var element = new VisualElement();
            AddClasses(element, classes);
            foreach (var child in children) if (child != null) element.Add(child);
            return element;
        }

        internal static Label Text(string text, string classes = null)
        {
            var label = new Label(text);
            AddClasses(label, classes);
            return label;
        }

        internal static VisualElement Icon(string name, string classes = null) =>
            Element("md-icon md-icon--" + name + (classes != null ? " " + classes : ""));

        internal static Button Button(string text, Action clicked, string classes = null, string icon = null)
        {
            var button = new Button(clicked) { text = string.Empty };
            AddClasses(button, "md-button" + (classes != null ? " " + classes : ""));
            if (icon != null) button.Add(Icon(icon));
            if (text != null) button.Add(new Label(text) { pickingMode = PickingMode.Ignore });
            return button;
        }

        internal static Button IconButton(string icon, Action clicked, string tooltip)
        {
            var button = new Button(clicked) { text = string.Empty, tooltip = tooltip };
            button.AddToClassList("md-icon-button");
            button.Add(Icon(icon));
            return button;
        }

        internal static void SetButtonText(Button button, string text)
        {
            var label = button.Q<Label>();
            if (label != null) label.text = text;
        }

        internal static VisualElement HelpBox(string text, HelpKind kind = HelpKind.Info)
        {
            string modifier = kind == HelpKind.Warning ? "warning" : "info";
            return Element("md-helpbox md-helpbox--" + modifier,
                Icon(modifier), Text(text, "md-helpbox__text"));
        }

        internal static VisualElement Section(string title, out VisualElement body, VisualElement headerAccessory = null)
        {
            body = Element("md-section__body");
            var header = Element("md-section__header", Text(title, "md-section__title"), headerAccessory);
            return Element("md-section", header, body);
        }

        /// <summary>A segmented control. <paramref name="selected"/> receives the clicked index.</summary>
        internal static VisualElement Segmented(string[] items, int selectedIndex, Action<int> selected,
            string classes = null, string[] icons = null)
        {
            var root = Element("md-segmented" + (classes != null ? " " + classes : ""));
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var item = new Button(() => selected?.Invoke(index)) { text = string.Empty };
                item.AddToClassList("md-segmented__item");
                if (icons != null && i < icons.Length && icons[i] != null) item.Add(Icon(icons[i]));
                item.Add(new Label(items[i]) { pickingMode = PickingMode.Ignore });
                item.EnableInClassList("md-segmented__item--selected", i == selectedIndex);
                root.Add(item);
            }
            return root;
        }

        internal static void SelectSegment(VisualElement segmented, int selectedIndex)
        {
            int i = 0;
            foreach (var child in segmented.Children())
                child.EnableInClassList("md-segmented__item--selected", i++ == selectedIndex);
        }

        internal static Button ActionCard(string mode, string icon, string title, string description,
            string example, Action clicked)
        {
            var card = new Button(clicked) { text = string.Empty, tooltip = description };
            card.AddToClassList("md-action-card");
            card.Add(Element("md-action-card__icon md-mode-" + mode, Icon(icon, "md-icon--large")));
            var body = Element("md-action-card__body",
                Text(title, "md-action-card__title"), Text(description, "md-action-card__description"));
            if (!string.IsNullOrEmpty(example)) body.Add(Text(example, "md-action-card__example"));
            card.Add(body);
            card.Add(Icon("chevron-right", "md-action-card__chevron"));
            return card;
        }

        /// <summary>A switch row bound to a getter and setter; call the returned refresh to resync.</summary>
        internal static VisualElement Switch(string label, string description, Func<bool> read, Action<bool> write,
            out Action refresh)
        {
            var text = Element("md-switch__text", Text(label, "md-switch__label"));
            if (!string.IsNullOrEmpty(description)) text.Add(Text(description, "md-switch__description"));
            var root = Element("md-switch", text, Element("md-switch__track", Element("md-switch__knob")));
            root.focusable = true;
            root.tooltip = description;
            void Sync() => root.EnableInClassList("md-switch--on", read());
            void Toggle()
            {
                if (!root.enabledInHierarchy) return;
                write(!read());
                Sync();
            }
            root.RegisterCallback<ClickEvent>(_ => Toggle());
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return) return;
                Toggle();
                evt.StopPropagation();
            });
            refresh = Sync;
            Sync();
            return root;
        }

        /// <summary>A collapsible row in an <c>md-foldout-list</c>; the open state is kept by the caller.</summary>
        internal static VisualElement Foldout(string title, Func<bool> isOpen, Action<bool> setOpen,
            out VisualElement content, string meta = null)
        {
            var root = Element("md-foldout-item");
            var header = new Button { text = string.Empty };
            header.AddToClassList("md-foldout");
            var icon = Icon("fold-right", "md-icon--small");
            header.Add(icon);
            header.Add(new Label(title) { pickingMode = PickingMode.Ignore });
            if (meta != null) header.Add(Text(meta, "md-foldout__meta"));
            var body = Element("md-foldout__content");
            void Sync()
            {
                bool open = isOpen();
                body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                icon.EnableInClassList("md-icon--fold-right", !open);
                icon.EnableInClassList("md-icon--fold-down", open);
            }
            header.clicked += () => { setOpen(!isOpen()); Sync(); };
            root.Add(header);
            root.Add(body);
            Sync();
            content = body;
            return root;
        }

        internal static VisualElement FieldRow(string label, VisualElement control, string value = null)
        {
            control.AddToClassList("md-field-row__control");
            var row = Element("md-field-row", Text(label, "md-field-row__label"), control);
            if (value != null) row.Add(Text(value, "md-field-row__value"));
            return row;
        }

        internal static VisualElement KeyHint(params (string key, string action)[] items)
        {
            var root = Element("md-key-hint");
            foreach (var (key, action) in items)
                root.Add(Element("md-key-hint__item", Text(key, "md-key"), Text(action)));
            return root;
        }

        internal static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void AddClasses(VisualElement element, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (string name in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                element.AddToClassList(name);
        }
    }
}
#endif
