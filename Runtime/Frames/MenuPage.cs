using System;
using System.Collections.Generic;
using Threads.UiFrames;

namespace Threads.UiMenus
{
    /// <summary>A menu view as its page frame sees it (keyboard/gamepad on a row).</summary>
    public interface IMenuView
    {
        /// <summary>Left/Right on a row (±1).</summary>
        void Adjust(object row, int direction);
        /// <summary>Enter on a row.</summary>
        void Activate(object row);
    }

    /// <summary>Which view owns which page element (a VisualElement or GameObject). One per app,
    /// shared by the page frame kind and the views.</summary>
    public sealed class MenuViewRegistry
    {
        readonly Dictionary<object, IMenuView> _views = new Dictionary<object, IMenuView>();

        public void Register(object page, IMenuView view) => _views[page] = view;
        public void Unregister(object page) => _views.Remove(page);
        public IMenuView Find(object page) => page != null && _views.TryGetValue(page, out var v) ? v : null;
    }

    /// <summary>
    /// <c>.um-page</c>: a menu page, a vertical strip of rows (cards). Up/Down move between rows
    /// (the page scrolls to show them); Left/Right change the selected row's value; Enter acts on
    /// it (toggle, press, open). Register with <see cref="FrameRegistry.Add"/>; the menu views do this.
    /// </summary>
    public sealed class MenuPageKind : IFrameKind
    {
        public const string Marker = "um-page";

        static readonly FrameProperty[] s_properties =
        {
            FrameProperties.Axis, FrameProperties.Deceleration, FrameProperties.Duration, FrameProperties.Easing,
            FrameProperties.Input, FrameProperties.Navigation,
        };

        public MenuPageKind(MenuViewRegistry views)
        {
            Views = views ?? throw new ArgumentNullException(nameof(views));
        }

        public MenuViewRegistry Views { get; }
        public string MarkerClass => Marker;
        public IReadOnlyList<FrameProperty> Properties => s_properties;
        public IFrameBehaviour Create(FrameContext context) => new MenuPageFrame(context, Views);

        /// <summary>Adds the kind to <paramref name="registry"/> unless a menu page kind is there already;
        /// returns the registry's menu views.</summary>
        public static MenuViewRegistry Install(FrameRegistry registry)
        {
            if (registry.FindKind(Marker) is MenuPageKind existing) return existing.Views;
            var kind = new MenuPageKind(new MenuViewRegistry());
            registry.Add(kind);
            return kind.Views;
        }
    }

    /// <summary>The behaviour of a <c>.um-page</c> element: a vertical strip that hands Adjust and
    /// Activate on a row to its menu view.</summary>
    public sealed class MenuPageFrame : CardFrameBase
    {
        readonly MenuViewRegistry _views;

        public MenuPageFrame(FrameContext context, MenuViewRegistry views) : base(context)
        {
            _views = views;
            Model = new StripModel(context.Clock, new StripOptions
            {
                FlingThresholdDps = Environment.FlingThresholdDps,
                RubberBandLimitDp = Environment.RubberBandLimitDp,
            });
            Model.OffsetChanged += OnOffset;
            Model.DraggingChanged += OnDraggingChanged;
            OnLayoutChanged();
        }

        public StripModel Model { get; }
        protected override FrameAxis DefaultAxis => FrameAxis.Vertical;
        protected override float OffsetDp => Model.OffsetDp;
        public override bool IsMoving => Model.IsMoving;
        public override bool CanStep(int direction) => Model.CanStep(direction);
        public override void Update() => Model.Update();

        protected override bool ExecuteModel(in FrameCommand command)
        {
            var view = _views.Find(Element.Native);
            if (command.Id == FrameCommandId.Adjust && command.Payload is IFrameElement adjusted)
            {
                view?.Adjust(adjusted.Native, command.Index);
                return view != null;
            }
            if (command.Id == FrameCommandId.Activate && command.Payload is IFrameElement activated)
            {
                view?.Activate(activated.Native);
                return view != null;
            }
            return Model.Execute(command);
        }

        public override void Dispose()
        {
            Model.OffsetChanged -= OnOffset;
            Model.DraggingChanged -= OnDraggingChanged;
            base.Dispose();
        }

        protected override void ApplyModelStyle(FrameStyle style) => Model.ApplyStyle(style);
        protected override void SetLayout(CardLayout layout) => Model.SetLayout(layout);
        protected override void Reveal(FrameRect r, int card) => Model.Reveal(r.Start(Axis), r.End(Axis));
        protected override bool IsCardRangeShown(FrameRect r, int card) => Model.IsRangeShown(r.Start(Axis), r.End(Axis));
        void OnOffset(float _) => Place();
    }
}
