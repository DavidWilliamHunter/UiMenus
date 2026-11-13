using System;

namespace Threads.UiMenus
{
    /// <summary>A class shown as a menu page. Each object of it is edited in its own session
    /// (Apply, Revert and Cancel act on that object only).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
    public sealed class MenuPageAttribute : Attribute
    {
        public MenuPageAttribute(string title) { Title = title; }
        public string Title { get; }
        /// <summary>Position among sibling pages (tabs).</summary>
        public int Order { get; set; }
        /// <summary>Save key for this page's values; the type name by default.</summary>
        public string Key { get; set; }
    }

    /// <summary>Includes a member that would otherwise be left out (a non-public field or
    /// property, or a read-only property shown as information).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MenuEntryAttribute : Attribute { }

    /// <summary>Leaves a member out of the menu.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class MenuHiddenAttribute : Attribute { }

    /// <summary>The text shown for a member (by default its name split into words).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class LabelAttribute : Attribute
    {
        public LabelAttribute(string text) { Text = text; }
        public string Text { get; }
    }

    /// <summary>Position on the page. Members without it follow, fields first then properties,
    /// each in declaration order.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class OrderAttribute : Attribute
    {
        public OrderAttribute(int order) { Order = order; }
        public int Order { get; }
    }

    /// <summary>Changes reach the object at once while editing (volume, brightness), and are
    /// rolled back by Revert and Cancel.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class LivePreviewAttribute : Attribute { }

    /// <summary>A unit shown after a number (e.g. "%", "dB").</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class UnitAttribute : Attribute
    {
        public UnitAttribute(string text) { Text = text; }
        public string Text { get; }
    }

    /// <summary>The step for arrows and number fields (a slider moves by this).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class StepAttribute : Attribute
    {
        public StepAttribute(double step) { Step = step; }
        public double Step { get; }
    }

    /// <summary>Shown only while another member (in the values being edited) equals a value:
    /// <c>true</c> for a bool by default.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ShowIfAttribute : Attribute
    {
        public ShowIfAttribute(string member) { Member = member; Value = true; }
        public ShowIfAttribute(string member, object value) { Member = member; Value = value; }
        public string Member { get; }
        public object Value { get; }
    }

    /// <summary>The choices for a string: the name of a method, property or field on the same
    /// object returning <c>IEnumerable&lt;string&gt;</c>.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class OptionsAttribute : Attribute
    {
        public OptionsAttribute(string member) { Member = member; }
        public string Member { get; }
    }

    /// <summary>Changing this only takes full effect after a restart; Apply reports it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class RequiresRestartAttribute : Attribute { }

    /// <summary>
    /// A method checking the values being edited before Apply:
    /// <c>string Method(IMenuValues values)</c>, returning null when they are fine, else a message
    /// shown to the user. Apply is refused while any check fails.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuValidateAttribute : Attribute { }

    /// <summary>A method shown as a button; it runs at once (it is not part of Apply).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuButtonAttribute : Attribute
    {
        /// <summary>Asked before running, e.g. "Delete all saved games?".</summary>
        public string Confirm { get; set; }
    }

    /// <summary>A field or property holding another object, shown as a link that opens that
    /// object's own page (and its own session).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MenuLinkAttribute : Attribute { }

    /// <summary>The save key for a member (its name by default). Old keys can be listed with
    /// Unity's <c>[FormerlySerializedAs]</c>.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class SettingKeyAttribute : Attribute
    {
        public SettingKeyAttribute(string key) { Key = key; }
        public string Key { get; }
    }

    /// <summary>Not saved (e.g. a value only meant for this session).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class DontSaveAttribute : Attribute { }
}
