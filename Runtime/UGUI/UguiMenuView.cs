using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Threads.UiFrames;
using Threads.UiFrames.UGUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Threads.UiMenus.UGUI
{
    /// <summary>
    /// A menu in uGUI, built in code: a title, the page (a <c>um-page</c> frame whose cards are the
    /// rows), messages, and the Apply / Revert / Cancel bar. Create through <see cref="UguiMenus.Open"/>.
    /// <para>As in UI Toolkit: row controls that take arrows or clicks (sliders, toggles, choice
    /// arrows, buttons) are <c>uf-pointer-only</c>, so with keys each row is one stop (Left/Right
    /// change it, Enter acts on it); text, number and flag rows keep their fields as keyboard items.</para>
    /// <para>Rows carry the classes <c>um-row</c> and <c>um-row--&lt;kind&gt;</c> (plus UI Frames'
    /// state classes) on their <see cref="UiFrameClasses"/>, for state visuals of your own.</para>
    /// </summary>
    public sealed class UguiMenuView : MenuView
    {
        public const string RowClass = "um-row";
        public const string DirtyClass = "um-row--dirty";
        public const string ConfirmClass = "um-confirm";

        sealed class RowUi
        {
            public Action<object> Show;
            public Text Label;
            public Button Confirmable;
            public Text ButtonText;
            public string ButtonLabel;
        }

        readonly UguiMenuStyle _style;
        readonly MenuViewRegistry _views;
        readonly Action _rescan;
        readonly Action<GameObject> _select;
        readonly Dictionary<GameObject, RowUi> _ui = new Dictionary<GameObject, RowUi>();
        readonly List<GameObject> _shown = new List<GameObject>();
        readonly DefaultControls.Resources _resources = new DefaultControls.Resources();
        bool _closed;

        internal UguiMenuView(RectTransform parent, MenuStack stack, MenuSchemaCache schemas, ISettingsStore store,
                              MenuViewRegistry views, UguiMenuStyle style, Action rescan, Action<GameObject> select)
            : base(stack, schemas, store)
        {
            _style = style ?? new UguiMenuStyle();
            _views = views;
            _rescan = rescan;
            _select = select;
            var s = _style;

            Root = New("Menu", parent);
            Stretch(Root);
            Root.gameObject.AddComponent<Image>().color = s.background;
            var layout = Root.gameObject.AddComponent<VerticalLayoutGroup>();
            var pad = Mathf.RoundToInt(s.padding);
            layout.padding = new RectOffset(pad, pad, pad, pad);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TitleText = NewText(Root, "Title", "", s.titleSize, s.text, FontStyle.Bold);
            Size(TitleText.gameObject, height: s.titleSize + 10);

            Page = New("Page", Root);
            Page.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f); // presses on gaps reach the page
            Page.gameObject.AddComponent<RectMask2D>();
            Page.gameObject.AddComponent<UiFrameClasses>().SetClasses(MenuPageKind.Marker);
            var pageSize = Size(Page.gameObject, height: 60f);
            pageSize.flexibleHeight = 1f;

            RowsContainer = New("Rows", Page);
            RowsContainer.anchorMin = new Vector2(0, 1);
            RowsContainer.anchorMax = new Vector2(1, 1);
            RowsContainer.pivot = new Vector2(0.5f, 1);
            RowsContainer.sizeDelta = Vector2.zero;
            RowsContainer.anchoredPosition = Vector2.zero;
            var rows = RowsContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 2f;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            RowsContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RowsContainer.gameObject.AddComponent<UiFrameClasses>().SetClasses(FrameTags.Cards);

            // Rows hidden by conditions wait here, out of the page (so keys never reach them).
            _hiddenRows = New("Hidden Rows", Root);
            _hiddenRows.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _hiddenRows.gameObject.SetActive(false);

            Messages = NewText(Root, "Messages", "", s.fontSize - 1, s.warning);
            Messages.gameObject.SetActive(false);

            Bar = New("Bar", Root);
            var bar = Bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            bar.childAlignment = TextAnchor.MiddleRight;
            bar.spacing = 8f;
            bar.childControlWidth = bar.childControlHeight = true;
            bar.childForceExpandWidth = bar.childForceExpandHeight = false;
            Size(Bar.gameObject, height: s.rowHeight + 4);
            ApplyButton = NewButton(Bar, "Apply", "Apply", () => Apply(), out _);
            RevertButton = NewButton(Bar, "Revert", "Revert", Revert, out _);
            CancelButton = NewButton(Bar, "Cancel", "Cancel", Cancel, out _);

            Question = New("Question", Root);
            Question.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Stretch(Question);
            Question.gameObject.AddComponent<Image>().color = s.dim;
            var box = New("Box", Question);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(380, 120);
            box.gameObject.AddComponent<Image>().color = s.box;
            var boxLayout = box.gameObject.AddComponent<VerticalLayoutGroup>();
            boxLayout.padding = new RectOffset(16, 16, 16, 16);
            boxLayout.spacing = 10f;
            boxLayout.childControlWidth = boxLayout.childControlHeight = true;
            boxLayout.childForceExpandWidth = true;
            boxLayout.childForceExpandHeight = false;
            Size(NewText(box, "Text", "Apply your changes?", s.fontSize, s.text).gameObject, height: s.fontSize + 8);
            var buttons = New("Buttons", box);
            var bl = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            bl.childAlignment = TextAnchor.MiddleRight;
            bl.spacing = 8f;
            bl.childControlWidth = bl.childControlHeight = true;
            bl.childForceExpandWidth = bl.childForceExpandHeight = false;
            Size(buttons.gameObject, height: s.rowHeight);
            QuestionApply = NewButton(buttons, "Question Apply", "Apply", () => Decide(LeaveChoice.Apply), out _);
            QuestionDiscard = NewButton(buttons, "Question Discard", "Discard", () => Decide(LeaveChoice.Discard), out _);
            QuestionStay = NewButton(buttons, "Question Stay", "Stay", () => Decide(null), out _);
            Question.gameObject.SetActive(false);

            _views.Register(Page.gameObject, this);
        }

        readonly RectTransform _hiddenRows;

        public RectTransform Root { get; }
        public Text TitleText { get; }
        /// <summary>The <c>um-page</c> frame.</summary>
        public RectTransform Page { get; }
        /// <summary>The page's <c>uf-cards</c>: the rows shown.</summary>
        public RectTransform RowsContainer { get; }
        public Text Messages { get; }
        public RectTransform Bar { get; }
        public Button ApplyButton { get; }
        public Button RevertButton { get; }
        public Button CancelButton { get; }
        public RectTransform Question { get; }
        public Button QuestionApply { get; }
        public Button QuestionDiscard { get; }
        public Button QuestionStay { get; }
        public UguiMenuStyle Style => _style;
        /// <summary>Select the first row whenever a page is shown (so keys work at once).</summary>
        public bool SelectOnShow { get; set; } = true;

        /// <summary>The row of an entry (by name or path) on the current page, or null.</summary>
        public GameObject RowElement(string nameOrPath)
        {
            var entry = Session?.Schema.Find(nameOrPath);
            return entry == null ? null : PageRows.FirstOrDefault(r => r.Entry == entry)?.Native as GameObject;
        }

        // ---------------- MenuView ----------------

        protected override void ClearPage()
        {
            foreach (var row in _ui.Keys)
            {
                if (!row) continue;
                row.SetActive(false);
                row.transform.SetParent(null, false); // out of the page now; destroyed at the end of the frame
                Object.Destroy(row);
            }
            _ui.Clear();
            _shown.Clear();
        }

        protected override object CreateRow(MenuEntry entry, string heading)
        {
            var s = _style;
            var kind = entry.Kind.ToString().ToLowerInvariant();
            var row = New("Row " + entry.Path, _hiddenRows);
            row.gameObject.AddComponent<Image>().color = s.row;
            row.gameObject.AddComponent<UiFrameClasses>().SetClasses(RowClass, "um-row--" + kind);
            var layout = row.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 2, 2);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            if (!string.IsNullOrEmpty(heading))
            {
                var h = NewText(row, "Heading", heading, s.fontSize, s.muted, FontStyle.Bold);
                Size(h.gameObject, height: s.fontSize + 14).minHeight = s.fontSize + 14;
                h.alignment = TextAnchor.LowerLeft;
            }

            var line = New("Line", row);
            var ll = line.gameObject.AddComponent<HorizontalLayoutGroup>();
            ll.spacing = 8f;
            ll.childAlignment = TextAnchor.MiddleLeft;
            ll.childControlWidth = ll.childControlHeight = true;
            ll.childForceExpandWidth = ll.childForceExpandHeight = false;
            Size(line.gameObject, height: s.rowHeight).minHeight = s.rowHeight;

            var ui = new RowUi();
            var isButton = entry.Kind == MenuEntryKind.Button || entry.Kind == MenuEntryKind.Link;
            if (!isButton)
            {
                ui.Label = NewText(line, "Label", entry.Label, s.fontSize, s.text);
                var le = Size(ui.Label.gameObject, width: 160f, height: s.rowHeight);
                le.flexibleWidth = 1f;
            }

            var control = New("Control", line);
            var cl = control.gameObject.AddComponent<HorizontalLayoutGroup>();
            cl.spacing = 6f;
            cl.childAlignment = isButton ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
            cl.childControlWidth = cl.childControlHeight = true;
            cl.childForceExpandWidth = cl.childForceExpandHeight = false;
            var size = Size(control.gameObject, width: 240f, height: s.rowHeight);
            size.flexibleWidth = isButton ? 1f : 1.4f;

            Build(entry, row.gameObject, control, ui);
            _ui[row.gameObject] = ui;
            return row.gameObject;
        }

        protected override void ShowRows(IReadOnlyList<object> rows)
        {
            var wanted = rows.Cast<GameObject>().ToList();
            if (wanted.SequenceEqual(_shown)) return; // unchanged: keep selection and layout
            foreach (var r in _shown) if (r && !wanted.Contains(r)) r.transform.SetParent(_hiddenRows, false);
            for (var i = 0; i < wanted.Count; i++)
            {
                var t = wanted[i].transform;
                if (t.parent != RowsContainer) t.SetParent(RowsContainer, false);
                t.SetSiblingIndex(i);
            }
            _shown.Clear();
            _shown.AddRange(wanted);
            LayoutRebuilder.MarkLayoutForRebuild(RowsContainer);
        }

        protected override void UpdateRow(MenuEntry entry, object row, object value, bool dirty)
        {
            var go = (GameObject)row;
            go.GetComponent<UiFrameClasses>()?.SetClass(DirtyClass, dirty);
            if (!_ui.TryGetValue(go, out var ui)) return;
            if (ui.Label) ui.Label.fontStyle = dirty ? FontStyle.Italic : FontStyle.Normal;
            ui.Show?.Invoke(value);
        }

        protected override void UpdateChrome(string title, bool dirty)
        {
            TitleText.text = title;
            if (IsAsking) return; // the bar waits for the answer
            ApplyButton.interactable = dirty;
            RevertButton.interactable = dirty;
        }

        protected override void ShowMessages(IReadOnlyList<string> errors, IReadOnlyList<string> restartNeeded)
        {
            var lines = new List<string>(errors);
            if (restartNeeded.Count > 0) lines.Add("Restart to use the new " + string.Join(", ", restartNeeded) + ".");
            Messages.text = string.Join("\n", lines);
            Messages.gameObject.SetActive(lines.Count > 0);
        }

        protected override void ShowQuestion(bool show)
        {
            Question.gameObject.SetActive(show);
            Question.SetAsLastSibling();
            var dirty = Session != null && Session.IsDirty;
            ApplyButton.interactable = !show && dirty;
            RevertButton.interactable = !show && dirty;
            CancelButton.interactable = !show;
            _select?.Invoke(show ? QuestionApply.gameObject : Page.gameObject);
        }

        protected override void ShowConfirm(object row, string message, bool show)
        {
            var go = (GameObject)row;
            go.GetComponent<UiFrameClasses>()?.SetClass(ConfirmClass, show);
            if (!_ui.TryGetValue(go, out var ui) || !ui.Confirmable) return;
            ui.ButtonText.text = show ? message : ui.ButtonLabel;
            ui.Confirmable.image.color = show ? _style.danger : _style.control;
        }

        protected override void OnClosed() => Close();

        protected override void OnPageShown()
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(Root);
            _rescan?.Invoke();
            if (!SelectOnShow || _select == null) return;
            _select(_shown.FirstOrDefault() ?? Page.gameObject);
        }

        public override void Dispose()
        {
            base.Dispose();
            Close();
        }

        void Close()
        {
            if (_closed) return;
            _closed = true;
            _views.Unregister(Page ? Page.gameObject : null);
            if (!Root) return;
            Root.SetParent(null, false); // out of the canvas now; destroyed at the end of the frame
            Object.Destroy(Root.gameObject);
            _rescan?.Invoke();
        }

        // ---------------- Controls ----------------

        void Build(MenuEntry entry, GameObject row, RectTransform control, RowUi ui)
        {
            var s = _style;
            switch (entry.Kind)
            {
                case MenuEntryKind.Toggle:
                {
                    var t = NewToggle(control, "Toggle", null);
                    PointerOnly(t);
                    t.onValueChanged.AddListener(v => Edit(entry, v));
                    ui.Show = v => t.SetIsOnWithoutNotify(v is bool b && b);
                    break;
                }
                case MenuEntryKind.Slider:
                {
                    var go = DefaultControls.CreateSlider(_resources);
                    go.name = "Slider";
                    go.transform.SetParent(control, false);
                    var slider = go.GetComponent<Slider>();
                    slider.minValue = (float)(entry.Min ?? 0);
                    slider.maxValue = (float)(entry.Max ?? 1);
                    slider.wholeNumbers = entry.IsInteger;
                    Colour(go, "Background", s.control);
                    Colour(go, "Fill", s.accent);
                    Colour(go, "Handle", s.text);
                    var le = Size(go, width: 160f, height: 20f);
                    le.flexibleWidth = 1f;
                    PointerOnly(slider);
                    slider.onValueChanged.AddListener(v => Edit(entry, v));
                    var value = NewText(control, "Value", "", s.fontSize, s.text);
                    value.alignment = TextAnchor.MiddleRight;
                    Size(value.gameObject, width: 64f, height: s.rowHeight);
                    ui.Show = v =>
                    {
                        slider.SetValueWithoutNotify((float)Convert.ToDouble(v ?? 0, CultureInfo.InvariantCulture));
                        value.text = MenuText.Format(entry, v);
                    };
                    break;
                }
                case MenuEntryKind.Number:
                case MenuEntryKind.Text:
                {
                    var field = NewInputField(control, entry);
                    field.onEndEdit.AddListener(text => Edit(entry, Parse(entry, text)));
                    ui.Show = v => field.SetTextWithoutNotify(entry.Kind == MenuEntryKind.Text
                        ? v as string ?? ""
                        : Convert.ToString(v, CultureInfo.CurrentCulture));
                    if (entry.Kind == MenuEntryKind.Number && !string.IsNullOrEmpty(entry.Unit))
                        Size(NewText(control, "Unit", entry.Unit, s.fontSize, s.muted).gameObject, width: 30f, height: s.rowHeight);
                    break;
                }
                case MenuEntryKind.Choice:
                {
                    var prev = NewButton(control, "Previous", "‹", () => Adjust(row, -1), out _, 36f);
                    var value = NewText(control, "Value", "", s.fontSize, s.text);
                    value.alignment = TextAnchor.MiddleCenter;
                    Size(value.gameObject, width: 140f, height: s.rowHeight);
                    var next = NewButton(control, "Next", "›", () => Adjust(row, 1), out _, 36f);
                    PointerOnly(prev);
                    PointerOnly(next);
                    ui.Show = v => value.text = Session?.Choices(entry).FirstOrDefault(c => Equals(c.Value, v)).Label
                                                ?? Convert.ToString(v, CultureInfo.CurrentCulture);
                    break;
                }
                case MenuEntryKind.Flags:
                {
                    var toggles = new List<(Toggle Toggle, long Bit)>();
                    foreach (var (flag, label) in Session.Choices(entry))
                    {
                        var bit = Convert.ToInt64(flag, CultureInfo.InvariantCulture);
                        if (bit == 0) continue; // "None"
                        var t = NewToggle(control, "Flag " + flag, label);
                        t.onValueChanged.AddListener(on =>
                        {
                            var current = Convert.ToInt64(Session.Get(entry) ?? 0, CultureInfo.InvariantCulture);
                            Edit(entry, Enum.ToObject(entry.ValueType, on ? current | bit : current & ~bit));
                        });
                        toggles.Add((t, bit));
                    }
                    ui.Show = v =>
                    {
                        var current = Convert.ToInt64(v ?? 0, CultureInfo.InvariantCulture);
                        foreach (var (t, bit) in toggles) t.SetIsOnWithoutNotify((current & bit) == bit);
                    };
                    break;
                }
                case MenuEntryKind.Button:
                case MenuEntryKind.Link:
                {
                    var text = entry.Kind == MenuEntryKind.Link ? entry.Label + "  ›" : entry.Label;
                    var b = NewButton(control, "Button", text, () => Activate(row), out var label, 200f);
                    PointerOnly(b);
                    ui.Confirmable = b;
                    ui.ButtonText = label;
                    ui.ButtonLabel = text;
                    break;
                }
                case MenuEntryKind.Info:
                {
                    var value = NewText(control, "Value", "", s.fontSize, s.muted);
                    value.alignment = TextAnchor.MiddleRight;
                    var le = Size(value.gameObject, width: 120f, height: s.rowHeight);
                    le.flexibleWidth = 1f;
                    ui.Show = v => value.text = MenuText.Format(entry, v);
                    break;
                }
            }
        }

        static object Parse(MenuEntry entry, string text)
        {
            if (entry.Kind == MenuEntryKind.Text) return text;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var d) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return entry.IsInteger ? (object)(long)Math.Round(d) : d;
            return text; // refused by the session; the row shows the old value again
        }

        /// <summary>Mouse and touch only: keys reach the row instead, and a click does not select it.</summary>
        static void PointerOnly(Selectable s)
        {
            var c = s.GetComponent<UiFrameClasses>();
            if (!c) c = s.gameObject.AddComponent<UiFrameClasses>();
            c.SetClass(FrameTags.PointerOnly, true);
            s.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        Toggle NewToggle(RectTransform parent, string name, string label)
        {
            var go = DefaultControls.CreateToggle(_resources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var toggle = go.GetComponent<Toggle>();
            Colour(go, "Background", _style.toggle);
            Colour(go, "Checkmark", _style.accent);
            foreach (var g in go.GetComponentsInChildren<Image>(true))
                if (g.name == "Checkmark")
                {
                    // An inset mark, so the box shows round it when on and alone when off.
                    var rt = g.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.sizeDelta = new Vector2(-8f, -8f);
                    rt.anchoredPosition = Vector2.zero;
                }
            var text = go.GetComponentInChildren<Text>(true);
            if (string.IsNullOrEmpty(label))
            {
                if (text) text.gameObject.SetActive(false);
                Size(go, width: 24f, height: 24f);
            }
            else
            {
                StyleText(text, _style.fontSize, _style.text);
                text.text = label;
                Size(go, width: 30f + label.Length * _style.fontSize * 0.55f, height: 24f);
            }
            return toggle;
        }

        InputField NewInputField(RectTransform parent, MenuEntry entry)
        {
            var go = DefaultControls.CreateInputField(_resources);
            go.name = "Field";
            go.transform.SetParent(parent, false);
            var field = go.GetComponent<InputField>();
            field.image.color = _style.control;
            foreach (var t in go.GetComponentsInChildren<Text>(true)) StyleText(t, _style.fontSize, _style.text);
            if (field.placeholder is Text placeholder) placeholder.text = "";
            if (entry.Kind == MenuEntryKind.Number)
                field.contentType = entry.IsInteger ? InputField.ContentType.IntegerNumber : InputField.ContentType.DecimalNumber;
            else if (entry.Multiline)
                field.lineType = InputField.LineType.MultiLineNewline;
            var le = Size(go, width: 160f, height: entry.Multiline ? _style.rowHeight * 2 : _style.rowHeight - 6);
            le.flexibleWidth = 1f;
            return field;
        }

        Button NewButton(RectTransform parent, string name, string text, Action click, out Text label, float width = 110f)
        {
            var go = DefaultControls.CreateButton(_resources);
            go.name = name;
            go.transform.SetParent(parent, false);
            var b = go.GetComponent<Button>();
            b.image.color = _style.control;
            label = go.GetComponentInChildren<Text>(true);
            StyleText(label, _style.fontSize, _style.text);
            label.text = text;
            b.onClick.AddListener(() => click());
            Size(go, width: width, height: _style.rowHeight - 4);
            return b;
        }

        Text NewText(RectTransform parent, string name, string text, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var rt = New(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            StyleText(t, size, color);
            t.fontStyle = style;
            t.text = text;
            t.alignment = TextAnchor.MiddleLeft;
            return t;
        }

        void StyleText(Text t, int size, Color color)
        {
            if (!t) return;
            t.font = _style.Font;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
        }

        static void Colour(GameObject root, string child, Color color)
        {
            foreach (var g in root.GetComponentsInChildren<Image>(true))
                if (g.name == child) g.color = color;
        }

        static LayoutElement Size(GameObject go, float width = -1f, float height = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (!le) le = go.AddComponent<LayoutElement>();
            if (width >= 0f) le.preferredWidth = width;
            if (height >= 0f) le.preferredHeight = height;
            return le;
        }

        static RectTransform New(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
