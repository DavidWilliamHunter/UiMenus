using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Threads.UiFrames;
using UnityEngine.UIElements;

namespace Threads.UiMenus.Toolkit
{
    /// <summary>
    /// A menu in UI Toolkit: a title, the page (<c>.um-page</c>, a vertical UI Frames strip whose
    /// cards are the rows), messages, and the Apply / Revert / Cancel bar. Create through
    /// <see cref="ToolkitMenus.Open"/>.
    /// <para>Row controls that take arrows or clicks (sliders, toggles, choice arrows, buttons) are
    /// <c>uf-pointer-only</c>, so with keys each row is one stop: Left/Right change it, Enter acts on
    /// it. Text, number and flag rows keep their fields as keyboard items (Enter goes in).</para>
    /// <para>Classes for styling: <c>um-menu</c>, <c>um-title</c>, <c>um-page</c>, <c>um-row</c>,
    /// <c>um-row--&lt;kind&gt;</c>, <c>um-row--dirty</c>, <c>um-confirm</c>, <c>um-messages</c>,
    /// <c>um-bar</c>, <c>um-question</c>, and the UI Frames state classes on rows.</para>
    /// </summary>
    public sealed class ToolkitMenuView : MenuView
    {
        public const string MenuClass = "um-menu";
        public const string TitleClass = "um-title";
        public const string RowClass = "um-row";
        public const string DirtyClass = "um-row--dirty";
        public const string ConfirmClass = "um-confirm";
        public const string HiddenClass = "um-hidden";

        sealed class RowUi
        {
            public Action<object> Show;
            public Button Confirmable;
            public string ButtonText;
        }

        readonly MenuViewRegistry _views;
        readonly Action _rescan;
        readonly Action<VisualElement> _select;
        readonly Dictionary<VisualElement, RowUi> _ui = new Dictionary<VisualElement, RowUi>();
        readonly List<VisualElement> _shown = new List<VisualElement>();

        internal ToolkitMenuView(VisualElement parent, MenuStack stack, MenuSchemaCache schemas, ISettingsStore store,
                                 MenuViewRegistry views, StyleSheet styleSheet, Action rescan, Action<VisualElement> select)
            : base(stack, schemas, store)
        {
            _views = views;
            _rescan = rescan;
            _select = select;

            Root = new VisualElement { name = "menu" };
            Root.AddToClassList(MenuClass);
            if (styleSheet != null) Root.styleSheets.Add(styleSheet);

            TitleLabel = new Label { name = "menu-title" };
            TitleLabel.AddToClassList(TitleClass);
            Root.Add(TitleLabel);

            Page = new VisualElement { name = "menu-page" };
            Page.AddToClassList(MenuPageKind.Marker);
            RowsContainer = new VisualElement { name = "menu-rows" };
            RowsContainer.AddToClassList(FrameTags.Cards);
            Page.Add(RowsContainer);
            Root.Add(Page);

            Messages = new Label { name = "menu-messages" };
            Messages.AddToClassList("um-messages");
            Messages.AddToClassList(HiddenClass);
            Root.Add(Messages);

            Bar = new VisualElement { name = "menu-bar" };
            Bar.AddToClassList("um-bar");
            ApplyButton = BarButton("menu-apply", "Apply", () => Apply());
            RevertButton = BarButton("menu-revert", "Revert", Revert);
            CancelButton = BarButton("menu-cancel", "Cancel", Cancel);
            Root.Add(Bar);

            Question = new VisualElement { name = "menu-question" };
            Question.AddToClassList("um-question");
            Question.AddToClassList(HiddenClass);
            var box = new VisualElement();
            box.AddToClassList("um-question__box");
            box.Add(new Label("Apply your changes?"));
            var buttons = new VisualElement();
            buttons.AddToClassList("um-question__buttons");
            QuestionApply = new Button(() => Decide(LeaveChoice.Apply)) { name = "menu-question-apply", text = "Apply" };
            QuestionDiscard = new Button(() => Decide(LeaveChoice.Discard)) { name = "menu-question-discard", text = "Discard" };
            QuestionStay = new Button(() => Decide(null)) { name = "menu-question-stay", text = "Stay" };
            buttons.Add(QuestionApply);
            buttons.Add(QuestionDiscard);
            buttons.Add(QuestionStay);
            box.Add(buttons);
            Question.Add(box);
            Root.Add(Question);

            // Esc at the top level (the navigator had no level to leave) reaches the menu as the
            // panel's cancel event: go back a page.
            Root.RegisterCallback<NavigationCancelEvent>(OnCancelEvent);

            _views.Register(Page, this);
            parent.Add(Root);
        }

        public VisualElement Root { get; }
        public Label TitleLabel { get; }
        /// <summary>The <c>.um-page</c> frame.</summary>
        public VisualElement Page { get; }
        /// <summary>The page's <c>.uf-cards</c>: the rows shown.</summary>
        public VisualElement RowsContainer { get; }
        public Label Messages { get; }
        public VisualElement Bar { get; }
        public Button ApplyButton { get; }
        public Button RevertButton { get; }
        public Button CancelButton { get; }
        public VisualElement Question { get; }
        public Button QuestionApply { get; }
        public Button QuestionDiscard { get; }
        public Button QuestionStay { get; }
        /// <summary>Select the first row whenever a page is shown (so keys work at once).</summary>
        public bool SelectOnShow { get; set; } = true;

        /// <summary>The row element of an entry (by name or path) on the current page, or null.</summary>
        public VisualElement RowElement(string nameOrPath)
        {
            var entry = Session?.Schema.Find(nameOrPath);
            return entry == null ? null : PageRows.FirstOrDefault(r => r.Entry == entry)?.Native as VisualElement;
        }

        // ---------------- MenuView ----------------

        protected override void ClearPage()
        {
            RowsContainer.Clear();
            _ui.Clear();
            _shown.Clear();
        }

        protected override object CreateRow(MenuEntry entry, string heading)
        {
            var row = new VisualElement { name = "row-" + entry.Path, tooltip = entry.Help };
            row.AddToClassList(RowClass);
            row.AddToClassList("um-row--" + entry.Kind.ToString().ToLowerInvariant());
            if (!string.IsNullOrEmpty(heading))
            {
                var h = new Label(heading);
                h.AddToClassList("um-row__heading");
                row.Add(h);
            }
            var line = new VisualElement();
            line.AddToClassList("um-row__line");
            row.Add(line);
            var control = new VisualElement();
            control.AddToClassList("um-row__control");

            var ui = new RowUi();
            var isButton = entry.Kind == MenuEntryKind.Button || entry.Kind == MenuEntryKind.Link;
            if (!isButton)
            {
                var label = new Label(entry.Label);
                label.AddToClassList("um-row__label");
                line.Add(label);
            }
            line.Add(control);
            Build(entry, row, control, ui);
            _ui[row] = ui;
            return row;
        }

        protected override void ShowRows(IReadOnlyList<object> rows)
        {
            var wanted = rows.Cast<VisualElement>().ToList();
            if (wanted.SequenceEqual(_shown)) return; // unchanged: keep focus and layout
            _shown.Clear();
            _shown.AddRange(wanted);
            RowsContainer.Clear();
            foreach (var r in wanted) RowsContainer.Add(r);
        }

        protected override void UpdateRow(MenuEntry entry, object row, object value, bool dirty)
        {
            var ve = (VisualElement)row;
            ve.EnableInClassList(DirtyClass, dirty);
            if (_ui.TryGetValue(ve, out var ui)) ui.Show?.Invoke(value);
        }

        protected override void UpdateChrome(string title, bool dirty)
        {
            TitleLabel.text = title;
            ApplyButton.SetEnabled(dirty);
            RevertButton.SetEnabled(dirty);
        }

        protected override void ShowMessages(IReadOnlyList<string> errors, IReadOnlyList<string> restartNeeded)
        {
            var lines = new List<string>(errors);
            if (restartNeeded.Count > 0) lines.Add("Restart to use the new " + string.Join(", ", restartNeeded) + ".");
            Messages.text = string.Join("\n", lines);
            Messages.EnableInClassList(HiddenClass, lines.Count == 0);
        }

        protected override void ShowQuestion(bool show)
        {
            Question.EnableInClassList(HiddenClass, !show);
            Root.EnableInClassList("um-asking", show);
            Page.SetEnabled(!show);
            Bar.SetEnabled(!show);
            if (_select == null) return;
            Question.UnregisterCallback<GeometryChangedEvent>(SelectQuestion);
            if (show) Question.RegisterCallback<GeometryChangedEvent>(SelectQuestion); // once it is laid out (shown)
            else _select(Page);
        }

        void SelectQuestion(GeometryChangedEvent evt)
        {
            Question.UnregisterCallback<GeometryChangedEvent>(SelectQuestion);
            if (IsAsking) _select?.Invoke(QuestionApply);
        }

        protected override void ShowConfirm(object row, string message, bool show)
        {
            var ve = (VisualElement)row;
            ve.EnableInClassList(ConfirmClass, show);
            if (_ui.TryGetValue(ve, out var ui) && ui.Confirmable != null)
                ui.Confirmable.text = show ? message : ui.ButtonText;
        }

        protected override void OnClosed()
        {
            _views.Unregister(Page);
            Root.RemoveFromHierarchy();
            _rescan?.Invoke();
        }

        protected override void OnPageShown()
        {
            _rescan?.Invoke();
            if (!SelectOnShow || _select == null) return;
            var first = _shown.FirstOrDefault();
            _select(first ?? Page);
        }

        public override void Dispose()
        {
            base.Dispose();
            Root.UnregisterCallback<NavigationCancelEvent>(OnCancelEvent);
            _views.Unregister(Page);
            if (Root.parent != null)
            {
                Root.RemoveFromHierarchy();
                _rescan?.Invoke();
            }
        }

        // ---------------- Controls ----------------

        void Build(MenuEntry entry, VisualElement row, VisualElement control, RowUi ui)
        {
            switch (entry.Kind)
            {
                case MenuEntryKind.Toggle:
                {
                    var t = PointerOnly(new Toggle());
                    t.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                    control.Add(t);
                    ui.Show = v => t.SetValueWithoutNotify(v is bool b && b);
                    break;
                }
                case MenuEntryKind.Slider:
                {
                    var value = ValueLabel();
                    if (entry.IsInteger)
                    {
                        var s = PointerOnly(new SliderInt((int)Math.Round(entry.Min ?? 0), (int)Math.Round(entry.Max ?? 10)));
                        s.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                        control.Add(s);
                        ui.Show = v => { s.SetValueWithoutNotify(ToInt(v)); value.text = Format(entry, v); };
                    }
                    else
                    {
                        var s = PointerOnly(new Slider((float)(entry.Min ?? 0), (float)(entry.Max ?? 1)));
                        s.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                        control.Add(s);
                        ui.Show = v => { s.SetValueWithoutNotify(ToFloat(v)); value.text = Format(entry, v); };
                    }
                    control.Add(value);
                    break;
                }
                case MenuEntryKind.Number:
                {
                    if (entry.IsInteger)
                    {
                        var f = new LongField { isDelayed = true };
                        f.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                        control.Add(f);
                        ui.Show = v => f.SetValueWithoutNotify(Convert.ToInt64(v ?? 0, CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        var f = new DoubleField { isDelayed = true };
                        f.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                        control.Add(f);
                        ui.Show = v => f.SetValueWithoutNotify(Convert.ToDouble(v ?? 0, CultureInfo.InvariantCulture));
                    }
                    if (!string.IsNullOrEmpty(entry.Unit)) control.Add(new Label(entry.Unit));
                    break;
                }
                case MenuEntryKind.Choice:
                {
                    var prev = PointerOnly(new Button(() => Adjust(row, -1)) { text = "‹", name = "choice-previous" });
                    var value = new Label();
                    value.AddToClassList("um-choice__value");
                    var next = PointerOnly(new Button(() => Adjust(row, 1)) { text = "›", name = "choice-next" });
                    control.Add(prev);
                    control.Add(value);
                    control.Add(next);
                    ui.Show = v => value.text = Session?.Choices(entry).FirstOrDefault(c => Equals(c.Value, v)).Label ?? Convert.ToString(v, CultureInfo.InvariantCulture);
                    break;
                }
                case MenuEntryKind.Flags:
                {
                    var toggles = new List<(Toggle Toggle, long Bit)>();
                    foreach (var (flag, label) in Session.Choices(entry))
                    {
                        var bit = Convert.ToInt64(flag, CultureInfo.InvariantCulture);
                        if (bit == 0) continue; // "None"
                        var t = new Toggle(label) { name = "flag-" + flag };
                        t.RegisterValueChangedCallback(e =>
                        {
                            var current = Convert.ToInt64(Session.Get(entry) ?? 0, CultureInfo.InvariantCulture);
                            var next = e.newValue ? current | bit : current & ~bit;
                            Edit(entry, Enum.ToObject(entry.ValueType, next));
                        });
                        toggles.Add((t, bit));
                        control.Add(t);
                    }
                    ui.Show = v =>
                    {
                        var current = Convert.ToInt64(v ?? 0, CultureInfo.InvariantCulture);
                        foreach (var (t, bit) in toggles) t.SetValueWithoutNotify((current & bit) == bit);
                    };
                    break;
                }
                case MenuEntryKind.Text:
                {
                    var f = new TextField { isDelayed = true, multiline = entry.Multiline };
                    f.RegisterValueChangedCallback(e => Edit(entry, e.newValue));
                    control.Add(f);
                    ui.Show = v => f.SetValueWithoutNotify(v as string ?? "");
                    break;
                }
                case MenuEntryKind.Button:
                case MenuEntryKind.Link:
                {
                    var text = entry.Kind == MenuEntryKind.Link ? entry.Label + "  ›" : entry.Label;
                    var b = PointerOnly(new Button(() => Activate(row)) { text = text });
                    control.Add(b);
                    ui.Confirmable = b;
                    ui.ButtonText = text;
                    break;
                }
                case MenuEntryKind.Info:
                {
                    var value = ValueLabel();
                    control.Add(value);
                    ui.Show = v => value.text = Format(entry, v);
                    break;
                }
            }
        }

        /// <summary>Mouse and touch only: keys reach the row instead (and clicks don't take focus).</summary>
        static T PointerOnly<T>(T control) where T : VisualElement
        {
            control.AddToClassList(FrameTags.PointerOnly);
            control.focusable = false;
            return control;
        }

        static Label ValueLabel()
        {
            var l = new Label();
            l.AddToClassList("um-row__value");
            return l;
        }

        static int ToInt(object v) => (int)Math.Round(Convert.ToDouble(v ?? 0, CultureInfo.InvariantCulture));
        static float ToFloat(object v) => (float)Convert.ToDouble(v ?? 0, CultureInfo.InvariantCulture);

        /// <summary>A value as text, with its unit. A "%" unit on a 0–1 range shows as a percentage.</summary>
        public static string Format(MenuEntry entry, object v)
        {
            if (v == null) return "";
            if (v is float || v is double || v is decimal)
            {
                var d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (entry.Unit == "%" && entry.Max.HasValue && entry.Max.Value <= 1d) d *= 100d;
                return d.ToString("0.##", CultureInfo.CurrentCulture) + (entry.Unit ?? "");
            }
            return Convert.ToString(v, CultureInfo.CurrentCulture) + (entry.Unit == null || v is string ? "" : entry.Unit);
        }

        Button BarButton(string name, string text, Action click)
        {
            var b = new Button(click) { name = name, text = text };
            Bar.Add(b);
            return b;
        }

        void OnCancelEvent(NavigationCancelEvent evt)
        {
            evt.StopPropagation();
            Back();
        }
    }
}
