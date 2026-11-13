using System;
using System.Collections.Generic;
using System.Linq;

namespace Threads.UiMenus
{
    /// <summary>
    /// What both UI systems' menu views share: a <see cref="MenuStack"/> of pages shown one at a
    /// time, rows kept in step with the session, and the menu's actions.
    /// <list type="bullet">
    /// <item><b>Rows:</b> one per value, button, link and info entry (groups add their rows, with
    /// the group's name as a heading). Rows hidden by <c>[ShowIf]</c> are taken out of the page,
    /// so keyboard navigation never stops on them.</item>
    /// <item><b>Keys on a row</b> (from the page frame): Left/Right step the value; Enter flips a
    /// toggle, steps a choice, presses a button (twice when it asks for confirmation) or opens a link.</item>
    /// <item><b>Apply / Revert / Cancel</b> act on the current page's object. <b>Back</b> (Esc)
    /// leaves the page, asking Apply, discard or stay when it has changes.</item>
    /// </list>
    /// A UI system implements the abstract members (building and updating its elements).
    /// </summary>
    public abstract class MenuView : IMenuView, IDisposable
    {
        protected sealed class Row
        {
            public MenuEntry Entry;
            public object Native;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly Dictionary<object, Row> _byNative = new Dictionary<object, Row>();
        MenuSession _session;
        Row _confirming;

        protected MenuView(MenuStack stack, MenuSchemaCache schemas, ISettingsStore store)
        {
            Stack = stack ?? throw new ArgumentNullException(nameof(stack));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Store = store;
            Stack.Opened += OnStackChanged;
            Stack.Left += OnStackChanged;
        }

        public MenuStack Stack { get; }
        public MenuSchemaCache Schemas { get; }
        public ISettingsStore Store { get; }
        public MenuSession Session => _session;
        /// <summary>Whether the "Apply, discard or stay?" question is showing.</summary>
        public bool IsAsking { get; private set; }
        /// <summary>The last Apply's problems (empty when it worked).</summary>
        public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();
        /// <summary>Changed settings that need a restart, from the last Apply.</summary>
        public IReadOnlyList<string> RestartNeeded { get; private set; } = Array.Empty<string>();

        /// <summary>The last page was left: the menu is closed.</summary>
        public event Action Closed;
        /// <summary>A page was shown (the first, a link, or going back).</summary>
        public event Action<MenuSession> PageShown;

        /// <summary>The page title trail, e.g. "Settings › Audio".</summary>
        public string Title => string.Join(" › ", Stack.Pages.Select(p => p.Schema.Title));

        // ---------------- Actions ----------------

        public ApplyResult Apply()
        {
            if (_session == null) return null;
            var result = _session.Apply();
            Errors = result.Errors;
            RestartNeeded = result.NeedRestart.Select(e => e.Label).ToList();
            ShowMessages(Errors, RestartNeeded);
            UpdateBar();
            return result;
        }

        public void Revert()
        {
            _session?.Revert();
            Errors = Array.Empty<string>();
            ShowMessages(Errors, RestartNeeded);
        }

        /// <summary>Reverts the page and leaves it.</summary>
        public void Cancel()
        {
            if (_session == null) return;
            SetAsking(false);
            Stack.Back(LeaveChoice.Discard);
        }

        /// <summary>Leaves the page; asks first when it has changes (answer with <see cref="Decide"/>).</summary>
        public void Back()
        {
            if (_session == null) return;
            if (IsAsking) { SetAsking(false); return; } // Esc on the question: stay
            if (Stack.NeedsDecision) { SetAsking(true); return; }
            Stack.Back();
        }

        /// <summary>The answer to "Apply, discard or stay?": Apply, Discard, or null to stay.</summary>
        public void Decide(LeaveChoice? choice)
        {
            SetAsking(false);
            if (choice == null || _session == null) return;
            if (choice == LeaveChoice.Apply)
            {
                var result = _session.Apply();
                Errors = result.Errors;
                ShowMessages(Errors, RestartNeeded);
                if (!result.Applied) return; // stay to fix it
            }
            Stack.Back(LeaveChoice.Discard);
        }

        // ---------------- Keys on rows (IMenuView) ----------------

        public void Adjust(object row, int direction)
        {
            var r = RowOf(row);
            if (r == null || _session == null) return;
            ClearConfirm();
            if (r.Entry.IsValue) _session.Step(r.Entry, direction);
        }

        public void Activate(object row)
        {
            var r = RowOf(row);
            if (r == null || _session == null) return;
            switch (r.Entry.Kind)
            {
                case MenuEntryKind.Toggle:
                case MenuEntryKind.Choice:
                    ClearConfirm();
                    _session.Step(r.Entry, 1);
                    break;
                case MenuEntryKind.Button:
                    if (!string.IsNullOrEmpty(r.Entry.Confirm) && _confirming != r)
                    {
                        ClearConfirm();
                        _confirming = r;
                        ShowConfirm(r, true);
                        return;
                    }
                    ClearConfirm();
                    _session.Press(r.Entry);
                    break;
                case MenuEntryKind.Link:
                    ClearConfirm();
                    OpenLink(r.Entry);
                    break;
            }
        }

        /// <summary>Opens a <c>[MenuLink]</c>'s object as a page of its own (its own session).</summary>
        public void OpenLink(MenuEntry link)
        {
            var target = _session?.LinkTarget(link);
            if (target != null) Stack.Push(new MenuSession(target, Schemas, Store));
        }

        // ---------------- For the UI system ----------------

        /// <summary>Shows the stack's current page (call once the view's chrome exists).</summary>
        protected void ShowCurrent() => OnStackChanged(Stack.Current);

        /// <summary>A control on a row changed (a click, a drag): edits the value.</summary>
        protected void Edit(MenuEntry entry, object value)
        {
            if (_session == null) return;
            ClearConfirm();
            _session.Set(entry, value);
        }

        protected IReadOnlyList<Row> PageRows => _rows;
        protected Row RowOf(object native) => native != null && _byNative.TryGetValue(native, out var r) ? r : null;

        /// <summary>Removes the page's rows and anything else of the previous page.</summary>
        protected abstract void ClearPage();
        /// <summary>Creates a row for an entry (not yet shown); <paramref name="heading"/> is a heading
        /// to show above it, or null. Returns the row's element.</summary>
        protected abstract object CreateRow(MenuEntry entry, string heading);
        /// <summary>Shows exactly these rows, in this order (rows hidden by conditions are left out).</summary>
        protected abstract void ShowRows(IReadOnlyList<object> rows);
        /// <summary>Shows an entry's current value and whether it differs from the applied one.</summary>
        protected abstract void UpdateRow(MenuEntry entry, object row, object value, bool dirty);
        /// <summary>Title and the Apply/Revert buttons (enabled only with changes).</summary>
        protected abstract void UpdateChrome(string title, bool dirty);
        protected abstract void ShowMessages(IReadOnlyList<string> errors, IReadOnlyList<string> restartNeeded);
        /// <summary>Shows or hides the "Apply, discard or stay?" question.</summary>
        protected abstract void ShowQuestion(bool show);
        /// <summary>Marks a button row as waiting for a second press ("press again to …").</summary>
        protected abstract void ShowConfirm(object row, string message, bool show);
        /// <summary>The last page was left.</summary>
        protected abstract void OnClosed();
        /// <summary>A page's rows are built and shown.</summary>
        protected virtual void OnPageShown() { }

        public virtual void Dispose()
        {
            Stack.Opened -= OnStackChanged;
            Stack.Left -= OnStackChanged;
            Unsubscribe();
        }

        // ---------------- Internals ----------------

        void OnStackChanged(MenuSession _)
        {
            Unsubscribe();
            SetAsking(false);
            _confirming = null;
            _session = Stack.Current;
            _rows.Clear();
            _byNative.Clear();
            ClearPage();
            if (_session == null)
            {
                OnClosed();
                Closed?.Invoke();
                return;
            }
            _session.ValueChanged += OnValueChanged;
            _session.Applied += OnApplied;
            Errors = Array.Empty<string>();
            RestartNeeded = Array.Empty<string>();
            Build(_session.Schema.Entries, null);
            ShowMessages(Errors, RestartNeeded);
            Refresh();
            OnPageShown();
            PageShown?.Invoke(_session);
        }

        void Build(IEnumerable<MenuEntry> entries, string groupHeading)
        {
            foreach (var e in entries)
            {
                if (e.Kind == MenuEntryKind.Group)
                {
                    Build(e.Children, e.Header ?? e.Label);
                    continue;
                }
                var heading = e.Header ?? groupHeading;
                groupHeading = null; // a group's name heads its first row only
                var native = CreateRow(e, heading);
                var row = new Row { Entry = e, Native = native };
                _rows.Add(row);
                _byNative[native] = row;
            }
        }

        void Refresh()
        {
            foreach (var r in _rows) UpdateRow(r.Entry, r.Native, ValueOf(r.Entry), _session.IsDirtyEntry(r.Entry));
            ShowRows(_rows.Where(r => _session.IsVisible(r.Entry)).Select(r => r.Native).ToList());
            UpdateBar();
        }

        object ValueOf(MenuEntry e) => e.Kind == MenuEntryKind.Info ? _session.InfoValue(e) : e.IsValue ? _session.Get(e) : null;

        void OnValueChanged(MenuEntry entry) => Refresh();
        void OnApplied(ApplyResult _) => Refresh();
        void UpdateBar() => UpdateChrome(Title, _session != null && _session.IsDirty);

        void Unsubscribe()
        {
            if (_session == null) return;
            _session.ValueChanged -= OnValueChanged;
            _session.Applied -= OnApplied;
        }

        void SetAsking(bool asking)
        {
            if (IsAsking == asking) return;
            IsAsking = asking;
            ShowQuestion(asking);
        }

        void ClearConfirm()
        {
            if (_confirming == null) return;
            var r = _confirming;
            _confirming = null;
            ShowConfirm(r, false);
        }

        void ShowConfirm(Row r, bool show) => ShowConfirm(r.Native, r.Entry.Confirm, show);
    }
}
