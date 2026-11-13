using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Threads.UiMenus
{
    /// <summary>The values being edited, by member name or dotted path (for validators and conditions).</summary>
    public interface IMenuValues
    {
        object Get(string nameOrPath);
        T Get<T>(string nameOrPath);
    }

    /// <summary>What an Apply did.</summary>
    public sealed class ApplyResult
    {
        internal ApplyResult(bool applied, IReadOnlyList<string> errors, IReadOnlyList<MenuEntry> changed)
        {
            Applied = applied;
            Errors = errors;
            Changed = changed;
        }

        /// <summary>False when a check failed (see <see cref="Errors"/>); nothing was written.</summary>
        public bool Applied { get; }
        public IReadOnlyList<string> Errors { get; }
        /// <summary>The entries whose values changed.</summary>
        public IReadOnlyList<MenuEntry> Changed { get; }
        /// <summary>Changed entries marked <c>[RequiresRestart]</c>.</summary>
        public IEnumerable<MenuEntry> NeedRestart => Changed.Where(e => e.RequiresRestart);
    }

    /// <summary>
    /// Editing one object through its menu page. The session works on a copy of the values:
    /// <list type="bullet">
    /// <item><see cref="Set"/> changes the copy (and, for <c>[LivePreview]</c> entries, the object at once).</item>
    /// <item><see cref="Apply"/> checks the copy, writes the changes to the object, saves it, and makes
    /// this the new "last applied" state.</item>
    /// <item><see cref="Revert"/> returns to the last applied state (live-preview values on the object too).</item>
    /// <item><see cref="Cancel"/> reverts and ends the session.</item>
    /// </list>
    /// </summary>
    public sealed class MenuSession : IMenuValues
    {
        readonly Dictionary<MenuEntry, object> _buffer = new Dictionary<MenuEntry, object>();
        readonly Dictionary<MenuEntry, object> _applied = new Dictionary<MenuEntry, object>();
        readonly MenuConverters _converters;

        /// <param name="target">The object edited (its type's schema comes from <paramref name="schemas"/>).</param>
        /// <param name="store">Where Apply saves the object's values; null to not save.</param>
        public MenuSession(object target, MenuSchemaCache schemas, ISettingsStore store = null)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            if (schemas == null) throw new ArgumentNullException(nameof(schemas));
            Schema = schemas.Get(target.GetType());
            Schemas = schemas;
            _converters = schemas.Converters;
            Store = store;
            ReadFromTarget();
        }

        public object Target { get; }
        public MenuSchema Schema { get; }
        public MenuSchemaCache Schemas { get; }
        public ISettingsStore Store { get; }
        public bool IsClosed { get; private set; }

        /// <summary>An entry's edited value changed (by editing, Revert or Reload).</summary>
        public event Action<MenuEntry> ValueChanged;
        public event Action<ApplyResult> Applied;
        public event Action Reverted;
        /// <summary>The session ended (Cancel, or <see cref="Close"/>).</summary>
        public event Action Closed;

        // ---------------- Values ----------------

        /// <summary>The edited value of an entry.</summary>
        public object Get(MenuEntry entry) => _buffer.TryGetValue(entry, out var v) ? v : null;

        public object Get(string nameOrPath)
        {
            var e = Schema.Find(nameOrPath) ?? throw new ArgumentException($"'{Schema.Type.Name}' has no menu entry '{nameOrPath}'.", nameof(nameOrPath));
            return Get(e);
        }

        public T Get<T>(string nameOrPath)
        {
            var v = Get(nameOrPath);
            return v is T t ? t : default;
        }

        /// <summary>The last applied value of an entry.</summary>
        public object AppliedValue(MenuEntry entry) => _applied.TryGetValue(entry, out var v) ? v : null;

        /// <summary>Changes an entry's edited value. Numbers are rounded to integers where needed and
        /// clamped to <c>[Range]</c>/<c>[Min]</c>. Returns false if the value cannot be converted.</summary>
        public bool Set(MenuEntry entry, object value)
        {
            if (entry == null || !entry.IsValue || IsClosed) return false;
            if (!_converters.TryCoerce(entry.ValueType, value, out var v)) return false;
            v = Clamp(entry, v);
            if (Equals(Get(entry), v)) return true;
            _buffer[entry] = v;
            if (entry.LivePreview) WriteToTarget(entry, v);
            ValueChanged?.Invoke(entry);
            return true;
        }

        public bool Set(string nameOrPath, object value) => Set(Schema.Find(nameOrPath), value);

        /// <summary>
        /// One step (Left/Right in a menu): a toggle flips; a number moves by its <c>[Step]</c>
        /// (1 for integers, a twentieth of a range otherwise); a choice moves to the next or previous
        /// option, wrapping. Returns false for entries that cannot step.
        /// </summary>
        public bool Step(MenuEntry entry, int direction)
        {
            if (entry == null || direction == 0 || IsClosed) return false;
            var current = Get(entry);
            switch (entry.Kind)
            {
                case MenuEntryKind.Toggle:
                    return Set(entry, !(current is bool b && b));
                case MenuEntryKind.Slider:
                case MenuEntryKind.Number:
                    var step = entry.Step ?? (entry.IsInteger ? 1d : entry.Min.HasValue && entry.Max.HasValue ? (entry.Max.Value - entry.Min.Value) / 20d : 1d);
                    var d = Convert.ToDouble(current ?? 0, System.Globalization.CultureInfo.InvariantCulture);
                    return Set(entry, d + Math.Sign(direction) * step);
                case MenuEntryKind.Choice:
                    var choices = Choices(entry);
                    if (choices.Count == 0) return false;
                    var i = choices.FindIndex(c => Equals(c.Value, current));
                    var j = i < 0 ? 0 : ((i + Math.Sign(direction)) % choices.Count + choices.Count) % choices.Count;
                    return Set(entry, choices[j].Value);
                default:
                    return false;
            }
        }

        /// <summary>The options of a Choice entry: an enum's values (labels from <c>[Label]</c>,
        /// <c>[InspectorName]</c> or the name), or a string's <c>[Options]</c>.</summary>
        public List<(object Value, string Label)> Choices(MenuEntry entry)
        {
            var list = new List<(object, string)>();
            if (entry?.ValueType == null) return list;
            if (entry.ValueType.IsEnum)
            {
                foreach (var name in Enum.GetNames(entry.ValueType))
                {
                    var field = entry.ValueType.GetField(name);
                    if (field.IsDefined(typeof(MenuHiddenAttribute), false) || field.IsDefined(typeof(UnityEngine.HideInInspector), false)) continue;
                    var label = field.GetCustomAttribute<LabelAttribute>()?.Text
                                ?? field.GetCustomAttribute<UnityEngine.InspectorNameAttribute>()?.displayName
                                ?? MenuSchemaCache.Nicify(name);
                    list.Add((Enum.Parse(entry.ValueType, name), label));
                }
                return list;
            }
            if (entry.OptionsMember != null)
                foreach (var s in OptionsFrom(OwnerOf(entry), entry.OptionsMember))
                    list.Add((s, s));
            return list;
        }

        /// <summary>Whether an entry is shown: all its <c>[ShowIf]</c> conditions hold for the
        /// values being edited (and its group is shown).</summary>
        public bool IsVisible(MenuEntry entry)
        {
            if (entry == null) return false;
            if (entry.Parent != null && !IsVisible(entry.Parent)) return false;
            foreach (var c in entry.ShowIf)
            {
                var other = Schema.Find(entry.Parent == null ? c.Member : entry.Parent.Path + "." + c.Member) ?? Schema.Find(c.Member);
                if (other == null) continue;
                if (!Equals(Get(other), Coerce(other, c.Value))) return false;
            }
            return true;
        }

        // ---------------- State ----------------

        /// <summary>Whether any edited value differs from the last applied one.</summary>
        public bool IsDirty => Schema.Values.Any(IsDirtyEntry);

        public bool IsDirtyEntry(MenuEntry entry) =>
            entry != null && entry.IsValue && !Equals(Get(entry), AppliedValue(entry));

        /// <summary>The <c>[MenuValidate]</c> messages for the values being edited (empty when fine).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (var m in Schema.Validators)
            {
                var message = m.Invoke(Target, new object[] { this }) as string;
                if (!string.IsNullOrEmpty(message)) errors.Add(message);
            }
            return errors;
        }

        /// <summary>Checks, then writes every changed value to the object (in page order), saves it, and
        /// makes the edited values the new last-applied state.</summary>
        public ApplyResult Apply()
        {
            if (IsClosed) return new ApplyResult(false, new[] { "The menu is closed." }, Array.Empty<MenuEntry>());
            var errors = Validate();
            if (errors.Count > 0)
            {
                var refused = new ApplyResult(false, errors, Array.Empty<MenuEntry>());
                Applied?.Invoke(refused);
                return refused;
            }
            var changed = Schema.Values.Where(IsDirtyEntry).ToList();
            foreach (var e in changed)
                if (!e.LivePreview) WriteToTarget(e, Get(e)); // live-preview values are already there
            foreach (var e in changed) _applied[e] = Get(e);
            Save();
            var result = new ApplyResult(true, Array.Empty<string>(), changed);
            Applied?.Invoke(result);
            return result;
        }

        /// <summary>Back to the last applied values, including live-preview values on the object.</summary>
        public void Revert()
        {
            if (IsClosed) return;
            foreach (var e in Schema.Values)
            {
                if (!IsDirtyEntry(e)) continue;
                var v = AppliedValue(e);
                _buffer[e] = v;
                if (e.LivePreview) WriteToTarget(e, v);
                ValueChanged?.Invoke(e);
            }
            Reverted?.Invoke();
        }

        /// <summary>Reverts and ends the session.</summary>
        public void Cancel()
        {
            Revert();
            Close();
        }

        /// <summary>Ends the session without reverting (call Apply or Revert first).</summary>
        public void Close()
        {
            if (IsClosed) return;
            IsClosed = true;
            Closed?.Invoke();
        }

        /// <summary>Reads the object again (it was changed by other code). Edits in progress are lost.</summary>
        public void Reload()
        {
            ReadFromTarget();
            foreach (var e in Schema.Values) ValueChanged?.Invoke(e);
        }

        /// <summary>Runs a button entry's method. (Ask the user first if <see cref="MenuEntry.Confirm"/> is set.)</summary>
        public void Press(MenuEntry button)
        {
            if (button?.Kind == MenuEntryKind.Button && !IsClosed) button.Invoke(OwnerOf(button));
        }

        /// <summary>The object a link entry points to (to open its own session), or null.</summary>
        public object LinkTarget(MenuEntry link) =>
            link?.Kind == MenuEntryKind.Link ? link.GetFrom(OwnerOf(link)) : null;

        /// <summary>An Info entry's value, read from the object.</summary>
        public object InfoValue(MenuEntry info) => info?.Kind == MenuEntryKind.Info ? info.GetFrom(OwnerOf(info)) : null;

        // ---------------- Object access ----------------

        void ReadFromTarget()
        {
            _buffer.Clear();
            _applied.Clear();
            foreach (var e in Schema.Values)
            {
                var v = e.GetFrom(OwnerOf(e));
                _buffer[e] = v;
                _applied[e] = v;
            }
        }

        void Save()
        {
            if (Store == null) return;
            Store.Save(Schema.Key, SettingsDocument.Write(Schema, Target, _converters));
        }

        /// <summary>The object holding an entry's member (the target, or a group's object inside it).</summary>
        object OwnerOf(MenuEntry entry)
        {
            if (entry.Parent == null) return Target;
            return entry.Parent.GetFrom(OwnerOf(entry.Parent));
        }

        /// <summary>Writes a value into the target, through any groups; groups that are structs are
        /// written back to their owners.</summary>
        void WriteToTarget(MenuEntry entry, object value)
        {
            var chain = new List<MenuEntry>();
            for (var e = entry; e != null; e = e.Parent) chain.Insert(0, e);
            WritePath(Target, chain, 0, value);
        }

        internal static object WritePath(object owner, List<MenuEntry> chain, int index, object value)
        {
            var e = chain[index];
            if (index == chain.Count - 1)
            {
                e.SetOn(owner, value);
                return owner;
            }
            var child = e.GetFrom(owner);
            if (child == null) return owner;
            child = WritePath(child, chain, index + 1, value);
            if (e.ValueType.IsValueType) e.SetOn(owner, child); // a struct was changed in a copy: put it back
            return owner;
        }

        object Clamp(MenuEntry e, object v)
        {
            if (!MenuConverters.IsNumber(e.ValueType) || (!e.Min.HasValue && !e.Max.HasValue)) return v;
            var d = Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
            var c = d;
            if (e.Min.HasValue && c < e.Min.Value) c = e.Min.Value;
            if (e.Max.HasValue && c > e.Max.Value) c = e.Max.Value;
            if (c == d) return v;
            return _converters.TryCoerce(e.ValueType, c, out var r) ? r : v;
        }

        object Coerce(MenuEntry e, object value) => _converters.TryCoerce(e.ValueType, value, out var v) ? v : value;

        static IEnumerable<string> OptionsFrom(object owner, string member)
        {
            if (owner == null) yield break;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = owner.GetType();
            object result = null;
            var method = type.GetMethod(member, flags, null, Type.EmptyTypes, null);
            if (method != null) result = method.Invoke(method.IsStatic ? null : owner, null);
            else if (type.GetProperty(member, flags) is PropertyInfo p) result = p.GetValue(owner);
            else if (type.GetField(member, flags) is FieldInfo f) result = f.GetValue(owner);
            if (!(result is IEnumerable items)) yield break;
            foreach (var item in items)
                if (item != null) yield return item.ToString();
        }
    }
}
