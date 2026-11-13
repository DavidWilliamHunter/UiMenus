using System;
using System.Collections.Generic;
using Threads.UiFrames;
using Threads.UiFrames.Input;
using UnityEngine.UIElements;

namespace Threads.UiMenus.Toolkit
{
    /// <summary>
    /// Opens menus in UI Toolkit. One per app (or per UI), made by your bootstrap and passed where
    /// menus are opened:
    /// <code>
    /// var menus = ToolkitMenus.For(runner, schemas, menuStyleSheet);
    /// var view = menus.Open(document.rootVisualElement, audioSettings.Value, store);
    /// view.Closed += () => ...;
    /// </code>
    /// The menu's page is a UI Frames frame (<c>.um-page</c>), so the runner's keyboard, gamepad,
    /// mouse and touch handling all apply; this adds the kind to the registry once.
    /// </summary>
    public sealed class ToolkitMenus
    {
        readonly Action _rescan;
        readonly Action<VisualElement> _select;

        /// <param name="registry">The UI Frames registry the menu's UI is bound with.</param>
        /// <param name="styleSheet">Added to every menu (normally <c>UiMenus.uss</c>); null to style it yourself.</param>
        /// <param name="rescan">Tells the frame binders that UI was added or removed (e.g. <see cref="UiFramesRunner.Rescan"/>).</param>
        /// <param name="select">Makes an element the keyboard selection (see <see cref="SelectWith"/>); null to leave selection alone.</param>
        public ToolkitMenus(FrameRegistry registry, MenuSchemaCache schemas, StyleSheet styleSheet = null,
                            Action rescan = null, Action<VisualElement> select = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            StyleSheet = styleSheet;
            Views = MenuPageKind.Install(registry);
            _rescan = rescan;
            _select = select;
        }

        /// <summary>Menus for the UI Toolkit documents of a running <see cref="UiFramesRunner"/>.</summary>
        public static ToolkitMenus For(UiFramesRunner runner, MenuSchemaCache schemas, StyleSheet styleSheet = null)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            if (!runner.IsRunning) throw new InvalidOperationException("Start the UiFramesRunner before making menus for it.");
            return new ToolkitMenus(runner.Registry, schemas, styleSheet, runner.Rescan, SelectWith(runner.Navigators));
        }

        public MenuSchemaCache Schemas { get; }
        public StyleSheet StyleSheet { get; }
        public MenuViewRegistry Views { get; }

        /// <summary>Opens a menu on <paramref name="target"/> inside <paramref name="parent"/>. Apply saves
        /// to <paramref name="store"/> (null: not saved). The menu removes itself when its last page is left.</summary>
        public ToolkitMenuView Open(VisualElement parent, object target, ISettingsStore store = null)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var stack = new MenuStack();
            var view = new ToolkitMenuView(parent, stack, Schemas, store, Views, StyleSheet, _rescan, _select);
            view.Closed += view.Dispose;
            stack.Push(new MenuSession(target, Schemas, store));
            return view;
        }

        /// <summary>A select callback for <see cref="ToolkitMenus(FrameRegistry, MenuSchemaCache, StyleSheet, Action, Action{VisualElement})"/>:
        /// the navigator whose UI holds the element selects it (entering the frames around it).</summary>
        public static Action<VisualElement> SelectWith(IReadOnlyList<FrameNavigator> navigators) => ve =>
        {
            if (ve == null) return;
            foreach (var n in navigators)
            {
                var element = Find(n.Host.Root, ve);
                if (element == null) continue;
                n.SelectElement(element);
                return;
            }
        };

        /// <summary>The frame element for <paramref name="target"/> under <paramref name="root"/>, or null.</summary>
        static IFrameElement Find(IFrameElement root, VisualElement target)
        {
            if (root?.Native is not VisualElement rootVe) return null;
            if (target != rootVe && !rootVe.Contains(target)) return null;
            var path = new Stack<VisualElement>();
            for (var v = target; v != null && v != rootVe; v = v.parent) path.Push(v);
            var current = root;
            while (path.Count > 0)
            {
                var next = path.Pop();
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
