using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;

namespace Threads.UiMenus
{
    /// <summary>What a menu row is.</summary>
    public enum MenuEntryKind
    {
        /// <summary>bool.</summary>
        Toggle,
        /// <summary>A number with a range (<c>[Range]</c>).</summary>
        Slider,
        /// <summary>A number without a full range.</summary>
        Number,
        /// <summary>One of a set: an enum, or a string with <c>[Options]</c>.</summary>
        Choice,
        /// <summary>A <c>[Flags]</c> enum: several on/off choices.</summary>
        Flags,
        /// <summary>A string (or a type with a custom converter).</summary>
        Text,
        /// <summary>A <c>[MenuButton]</c> method.</summary>
        Button,
        /// <summary>A <c>[MenuLink]</c> to another object's page.</summary>
        Link,
        /// <summary>A nested <c>[Serializable]</c> object: its members are rows of this page, in a group.</summary>
        Group,
        /// <summary>A value shown but not edited (a read-only property with <c>[MenuEntry]</c>).</summary>
        Info,
    }

    /// <summary>One row of a menu page: what it edits and how, read from a member and its attributes.</summary>
    public sealed class MenuEntry
    {
        internal MenuEntry(MemberInfo member, MenuEntry parent)
        {
            Member = member;
            Parent = parent;
        }

        public MemberInfo Member { get; }
        /// <summary>The group this entry is inside, or null.</summary>
        public MenuEntry Parent { get; }
        /// <summary>The member's name.</summary>
        public string Name => Member.Name;
        /// <summary>Dotted path from the page's object, e.g. <c>Graphics.Shadows</c>.</summary>
        public string Path => Parent == null ? Name : Parent.Path + "." + Name;
        public MenuEntryKind Kind { get; internal set; }
        public Type ValueType { get; internal set; }
        public string Label { get; internal set; }
        /// <summary>From <c>[Tooltip]</c>.</summary>
        public string Help { get; internal set; }
        /// <summary>From <c>[Header]</c>: a heading shown before this row.</summary>
        public string Header { get; internal set; }
        public string Unit { get; internal set; }
        public double? Min { get; internal set; }
        public double? Max { get; internal set; }
        public double? Step { get; internal set; }
        public bool IsInteger => ValueType != null && MenuConverters.IsInteger(ValueType);
        public bool LivePreview { get; internal set; }
        public bool RequiresRestart { get; internal set; }
        public bool Multiline { get; internal set; }
        public int Order { get; internal set; }
        /// <summary>Save key (path-based; <c>[SettingKey]</c> renames the last part).</summary>
        public string Key { get; internal set; }
        /// <summary>Earlier keys from <c>[FormerlySerializedAs]</c>, read when the current key is missing.</summary>
        public IReadOnlyList<string> OldKeys { get; internal set; } = Array.Empty<string>();
        public bool Saved { get; internal set; }
        public string OptionsMember { get; internal set; }
        public string Confirm { get; internal set; }
        public IReadOnlyList<ShowIfAttribute> ShowIf { get; internal set; } = Array.Empty<ShowIfAttribute>();
        /// <summary>A group's rows.</summary>
        public IReadOnlyList<MenuEntry> Children { get; internal set; } = Array.Empty<MenuEntry>();

        /// <summary>An edited value (not a button, link, group or info row).</summary>
        public bool IsValue => Kind != MenuEntryKind.Button && Kind != MenuEntryKind.Link &&
                               Kind != MenuEntryKind.Group && Kind != MenuEntryKind.Info;

        /// <summary>Reads this member from its owning object (the page object, or a group's object).</summary>
        public object GetFrom(object owner)
        {
            switch (Member)
            {
                case FieldInfo f: return f.GetValue(owner);
                case PropertyInfo p: return p.GetValue(owner);
                default: return null;
            }
        }

        /// <summary>Writes this member on its owning object (a boxed struct is changed in place).</summary>
        public void SetOn(object owner, object value)
        {
            switch (Member)
            {
                case FieldInfo f: f.SetValue(owner, value); break;
                case PropertyInfo p: p.SetValue(owner, value); break;
            }
        }

        /// <summary>Runs a button's method on the page object.</summary>
        public void Invoke(object owner) => (Member as MethodInfo)?.Invoke(owner, null);

        public override string ToString() => $"{Path} ({Kind})";
    }

    /// <summary>
    /// The rows of a page, read once from a type: public fields and properties with a getter and
    /// setter, non-public ones with <c>[SerializeField]</c> or <c>[MenuEntry]</c>, and
    /// <c>[MenuButton]</c> methods. Unity's <c>[Range]</c>, <c>[Min]</c>, <c>[Tooltip]</c>,
    /// <c>[Header]</c>, <c>[Multiline]</c>/<c>[TextArea]</c>, <c>[HideInInspector]</c>,
    /// <c>[NonSerialized]</c> and <c>[FormerlySerializedAs]</c> are honoured.
    /// Values are read and written by reflection, which works under IL2CPP.
    /// </summary>
    public sealed class MenuSchema
    {
        internal MenuSchema(Type type) { Type = type; }

        public Type Type { get; }
        public string Title { get; internal set; }
        public int Order { get; internal set; }
        /// <summary>Save key for objects of this type.</summary>
        public string Key { get; internal set; }
        /// <summary>The page's rows in order (groups hold their own rows).</summary>
        public IReadOnlyList<MenuEntry> Entries { get; internal set; }
        /// <summary>Every edited value on the page, groups included, in order.</summary>
        public IReadOnlyList<MenuEntry> Values { get; internal set; }
        internal IReadOnlyList<MethodInfo> Validators { get; set; }

        /// <summary>The entry for a member name or dotted path, or null.</summary>
        public MenuEntry Find(string nameOrPath)
        {
            foreach (var e in All(Entries))
                if (e.Path == nameOrPath) return e;
            foreach (var e in All(Entries))
                if (e.Name == nameOrPath) return e;
            return null;
        }

        static IEnumerable<MenuEntry> All(IEnumerable<MenuEntry> entries)
        {
            foreach (var e in entries)
            {
                yield return e;
                foreach (var c in All(e.Children)) yield return c;
            }
        }
    }

    /// <summary>
    /// Builds and keeps <see cref="MenuSchema"/>s, one per type. Create one per app (or per menu
    /// system) and pass it where needed; nothing here is global.
    /// </summary>
    public sealed class MenuSchemaCache
    {
        const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        readonly Dictionary<Type, MenuSchema> _schemas = new Dictionary<Type, MenuSchema>();

        public MenuSchemaCache(MenuConverters converters = null)
        {
            Converters = converters ?? new MenuConverters();
        }

        public MenuConverters Converters { get; }

        public MenuSchema Get(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (_schemas.TryGetValue(type, out var s)) return s;
            s = Build(type);
            _schemas[type] = s;
            return s;
        }

        public MenuSchema Get<T>() => Get(typeof(T));

        MenuSchema Build(Type type)
        {
            var page = type.GetCustomAttribute<MenuPageAttribute>(true);
            var schema = new MenuSchema(type)
            {
                Title = page?.Title ?? Nicify(type.Name),
                Order = page?.Order ?? 0,
                Key = string.IsNullOrEmpty(page?.Key) ? type.Name : page.Key,
            };
            schema.Entries = Members(type, null, new HashSet<Type> { type });
            var values = new List<MenuEntry>();
            Collect(schema.Entries, values);
            schema.Values = values;
            schema.Validators = Hierarchy(type)
                .SelectMany(t => t.GetMethods(Instance))
                .Where(m => m.IsDefined(typeof(MenuValidateAttribute), true))
                .Where(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType == typeof(IMenuValues))
                .ToList();
            return schema;
        }

        static void Collect(IEnumerable<MenuEntry> entries, List<MenuEntry> into)
        {
            foreach (var e in entries)
            {
                if (e.IsValue) into.Add(e);
                Collect(e.Children, into);
            }
        }

        /// <summary>Base classes first, so inherited members come before the type's own.</summary>
        static IEnumerable<Type> Hierarchy(Type type)
        {
            var chain = new List<Type>();
            for (var t = type; t != null && t != typeof(object) && t != typeof(ValueType) && t != typeof(MonoBehaviour) &&
                               t != typeof(ScriptableObject) && t != typeof(Behaviour) && t != typeof(Component) &&
                               t != typeof(UnityEngine.Object); t = t.BaseType)
                chain.Add(t);
            chain.Reverse();
            return chain;
        }

        List<MenuEntry> Members(Type type, MenuEntry parent, HashSet<Type> visiting)
        {
            var list = new List<(MenuEntry entry, int sequence)>();
            var sequence = 0;
            foreach (var t in Hierarchy(type))
            {
                foreach (var f in t.GetFields(Instance).OrderBy(f => f.MetadataToken))
                {
                    var e = FromField(f, parent, visiting);
                    if (e != null) list.Add((e, sequence++));
                }
                foreach (var p in t.GetProperties(Instance).OrderBy(p => p.MetadataToken))
                {
                    var e = FromProperty(p, parent, visiting);
                    if (e != null) list.Add((e, sequence++));
                }
                foreach (var m in t.GetMethods(Instance).OrderBy(m => m.MetadataToken))
                {
                    var e = FromMethod(m, parent);
                    if (e != null) list.Add((e, sequence++));
                }
            }
            return list.OrderBy(x => x.entry.Order).ThenBy(x => x.sequence).Select(x => x.entry).ToList();
        }

        MenuEntry FromField(FieldInfo f, MenuEntry parent, HashSet<Type> visiting)
        {
            if (f.IsStatic || f.IsLiteral || f.Name.Contains("<")) return null; // consts, auto-property backing fields
            if (Hidden(f) || f.IsDefined(typeof(NonSerializedAttribute), false)) return null;
            var opted = f.IsDefined(typeof(MenuEntryAttribute), true) || f.IsDefined(typeof(SerializeField), true) || f.IsDefined(typeof(MenuLinkAttribute), true);
            if (!f.IsPublic && !opted) return null;
            if (f.IsInitOnly && !opted) return null; // readonly: shown as information only when asked for
            return Describe(f, f.FieldType, parent, readOnly: f.IsInitOnly, visiting);
        }

        MenuEntry FromProperty(PropertyInfo p, MenuEntry parent, HashSet<Type> visiting)
        {
            if (p.GetIndexParameters().Length > 0 || p.GetMethod == null || p.GetMethod.IsStatic || Hidden(p)) return null;
            var opted = p.IsDefined(typeof(MenuEntryAttribute), true) || p.IsDefined(typeof(MenuLinkAttribute), true);
            var publicGetSet = p.GetMethod.IsPublic && p.SetMethod != null && p.SetMethod.IsPublic;
            if (!publicGetSet && !opted) return null;
            return Describe(p, p.PropertyType, parent, readOnly: p.SetMethod == null, visiting);
        }

        MenuEntry FromMethod(MethodInfo m, MenuEntry parent)
        {
            var button = m.GetCustomAttribute<MenuButtonAttribute>(true);
            if (button == null || m.IsStatic || m.GetParameters().Length > 0 || Hidden(m)) return null;
            var e = new MenuEntry(m, parent) { Kind = MenuEntryKind.Button, Confirm = button.Confirm };
            Common(e, m);
            return e;
        }

        static bool Hidden(MemberInfo m) =>
            m.IsDefined(typeof(MenuHiddenAttribute), true) || m.IsDefined(typeof(HideInInspector), true);

        MenuEntry Describe(MemberInfo m, Type type, MenuEntry parent, bool readOnly, HashSet<Type> visiting)
        {
            var e = new MenuEntry(m, parent) { ValueType = type };
            Common(e, m);
            // Keys first: a group's rows build theirs from it.
            var keyName = m.GetCustomAttribute<SettingKeyAttribute>(true)?.Key ?? m.Name;
            e.Key = parent == null ? keyName : parent.Key + "." + keyName;
            e.OldKeys = m.GetCustomAttributes<FormerlySerializedAsAttribute>(true)
                .Select(a => parent == null ? a.oldName : parent.Key + "." + a.oldName).ToList();

            if (m.IsDefined(typeof(MenuLinkAttribute), true))
            {
                e.Kind = MenuEntryKind.Link;
                return e;
            }

            if (Converters.CanConvert(type))
            {
                e.Kind = Kind(type, m, e);
                if (readOnly) e.Kind = MenuEntryKind.Info;
            }
            else if ((type.IsClass || (type.IsValueType && !type.IsPrimitive)) && type.IsDefined(typeof(SerializableAttribute), false) && !visiting.Contains(type))
            {
                e.Kind = MenuEntryKind.Group;
                visiting.Add(type);
                e.Children = Members(type, e, visiting);
                visiting.Remove(type);
            }
            else return null; // a type the menu cannot edit

            e.LivePreview = m.IsDefined(typeof(LivePreviewAttribute), true);
            e.RequiresRestart = m.IsDefined(typeof(RequiresRestartAttribute), true);
            e.Saved = e.IsValue && !m.IsDefined(typeof(DontSaveAttribute), true);
            return e;
        }

        MenuEntryKind Kind(Type type, MemberInfo m, MenuEntry e)
        {
            var range = m.GetCustomAttribute<RangeAttribute>(true);
            var min = m.GetCustomAttribute<MinAttribute>(true);
            var step = m.GetCustomAttribute<StepAttribute>(true);
            if (range != null) { e.Min = range.min; e.Max = range.max; }
            else if (min != null) e.Min = min.min;
            if (step != null) e.Step = step.Step;
            e.Unit = m.GetCustomAttribute<UnitAttribute>(true)?.Text;
            e.OptionsMember = m.GetCustomAttribute<OptionsAttribute>(true)?.Member;
            e.Multiline = m.IsDefined(typeof(MultilineAttribute), true) || m.IsDefined(typeof(TextAreaAttribute), true);

            if (type == typeof(bool)) return MenuEntryKind.Toggle;
            if (type.IsEnum) return MenuConverters.IsFlags(type) ? MenuEntryKind.Flags : MenuEntryKind.Choice;
            if (MenuConverters.IsNumber(type)) return e.Min.HasValue && e.Max.HasValue ? MenuEntryKind.Slider : MenuEntryKind.Number;
            if (type == typeof(string) && e.OptionsMember != null) return MenuEntryKind.Choice;
            return MenuEntryKind.Text;
        }

        static void Common(MenuEntry e, MemberInfo m)
        {
            e.Label = m.GetCustomAttribute<LabelAttribute>(true)?.Text ?? Nicify(m.Name);
            e.Help = m.GetCustomAttribute<TooltipAttribute>(true)?.tooltip;
            e.Header = m.GetCustomAttribute<HeaderAttribute>(true)?.header;
            e.Order = m.GetCustomAttribute<OrderAttribute>(true)?.Order ?? 0;
            e.ShowIf = m.GetCustomAttributes<ShowIfAttribute>(true).ToList();
        }

        /// <summary>"masterVolume", "m_MasterVolume", "_masterVolume" → "Master Volume".</summary>
        public static string Nicify(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.StartsWith("m_", StringComparison.Ordinal)) name = name.Substring(2);
            name = name.TrimStart('_');
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (c == '_') { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
                if (i > 0 && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    var prev = name[i - 1];
                    var next = i + 1 < name.Length ? name[i + 1] : '\0';
                    var boundary = (char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && char.IsLower(next)))) ||
                                   (char.IsDigit(c) && char.IsLetter(prev));
                    if (boundary) sb.Append(' ');
                }
                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }
    }
}
