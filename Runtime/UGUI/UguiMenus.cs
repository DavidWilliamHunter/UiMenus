using System;
using System.Collections.Generic;
using Threads.UiFrames;
using Threads.UiFrames.Input;
using UnityEngine;

namespace Threads.UiMenus.UGUI
{
    /// <summary>
    /// Opens menus in uGUI. One per app (or per UI), made by your bootstrap and passed where menus
    /// are opened:
    /// <code>
    /// var menus = UguiMenus.For(runner, schemas);
    /// var view = menus.Open(canvasRoot, audioSettings.Value, store);
    /// view.Closed += () => ...;
    /// </code>
    /// The parent must be under one of the runner's uGUI roots. The menu's page is a UI Frames frame
    /// (<c>um-page</c>), so the runner's keyboard, gamepad, mouse and touch handling all apply.
    /// Needs an EventSystem (uGUI's keys come from its input module). Dispose to let go of the navigators.
    /// </summary>
    public sealed class UguiMenus : IDisposable
    {
        readonly Action _rescan;
        readonly Action<GameObject> _select;
        readonly List<UguiMenuView> _open = new List<UguiMenuView>();
        readonly List<FrameNavigator> _navigators = new List<FrameNavigator>();

        /// <param name="registry">The UI Frames registry the canvas is bound with.</param>
        /// <param name="style">Font, sizes and colours; null for the defaults.</param>
        /// <param name="rescan">Tells the frame binders that UI was added or removed (e.g. <see cref="UiFramesRunner.Rescan"/>).</param>
        /// <param name="navigators">Keyboard/gamepad navigators: they select rows and give the menu Esc at the top level.</param>
        public UguiMenus(FrameRegistry registry, MenuSchemaCache schemas, UguiMenuStyle style = null,
                         Action rescan = null, IReadOnlyList<FrameNavigator> navigators = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Style = style ?? new UguiMenuStyle();
            Views = MenuPageKind.Install(registry);
            _rescan = rescan;
            if (navigators != null)
            {
                _navigators.AddRange(navigators);
                foreach (var n in _navigators) n.TopLevelCancel += OnTopLevelCancel;
                _select = go => Select(go);
            }
        }

        /// <summary>Menus for the uGUI roots of a running <see cref="UiFramesRunner"/>.</summary>
        public static UguiMenus For(UiFramesRunner runner, MenuSchemaCache schemas, UguiMenuStyle style = null)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (!runner.IsRunning) throw new InvalidOperationException("Start the UiFramesRunner before making menus for it.");
            return new UguiMenus(runner.Registry, schemas, style, runner.Rescan, runner.Navigators);
        }

        public MenuSchemaCache Schemas { get; }
        public UguiMenuStyle Style { get; }
        public MenuViewRegistry Views { get; }
        public IReadOnlyList<UguiMenuView> OpenMenus => _open;

        /// <summary>Opens a menu on <paramref name="target"/>, filling <paramref name="parent"/>. Apply saves
        /// to <paramref name="store"/> (null: not saved). The menu destroys itself when its last page is left.</summary>
        public UguiMenuView Open(RectTransform parent, object target, ISettingsStore store = null)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var stack = new MenuStack();
            var view = new UguiMenuView(parent, stack, Schemas, store, Views, Style, _rescan, _select);
            _open.Add(view);
            view.Closed += () =>
            {
                _open.Remove(view);
                view.Dispose();
            };
            stack.Push(new MenuSession(target, Schemas, store));
            return view;
        }

        public void Dispose()
        {
            foreach (var n in _navigators) n.TopLevelCancel -= OnTopLevelCancel;
            _navigators.Clear();
        }

        /// <summary>Esc with nothing left to leave: the menu holding the selection goes back a page.</summary>
        bool OnTopLevelCancel(IFrameElement selected)
        {
            if (!(selected?.Native is GameObject go) || !go) return false;
            for (var i = _open.Count - 1; i >= 0; i--)
            {
                var view = _open[i];
                if (!view.Root || !go.transform.IsChildOf(view.Root)) continue;
                view.Back();
                return true;
            }
            return false;
        }

        void Select(GameObject target)
        {
            if (!target) return;
            foreach (var n in _navigators)
            {
                var element = Find(n.Host.Root, target.transform);
                if (element == null) continue;
                n.SelectElement(element);
                return;
            }
        }

        /// <summary>The frame element for <paramref name="target"/> under <paramref name="root"/>, or null.</summary>
        static IFrameElement Find(IFrameElement root, Transform target)
        {
            if (!(root?.Native is GameObject rootGo) || !rootGo || !target.IsChildOf(rootGo.transform)) return null;
            var path = new Stack<Transform>();
            for (var t = target; t && t != rootGo.transform; t = t.parent) path.Push(t);
            var current = root;
            while (path.Count > 0)
            {
                var next = path.Pop().gameObject;
                IFrameElement found = null;
                foreach (var c in current.Children)
                    if (ReferenceEquals(c.Native, next)) { found = c; break; }
                if (found == null) return null;
                current = found;
            }
            return current;
        }
    }
}
