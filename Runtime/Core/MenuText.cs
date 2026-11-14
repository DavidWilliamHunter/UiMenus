using System;
using System.Globalization;

namespace Threads.UiMenus
{
    /// <summary>How the menu views show values.</summary>
    public static class MenuText
    {
        /// <summary>A value as text, with its unit. A "%" unit on a 0–1 range shows as a percentage.</summary>
        public static string Format(MenuEntry entry, object v)
        {
            if (v == null) return "";
            var unit = entry?.Unit;
            if (v is float || v is double || v is decimal)
            {
                var d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (unit == "%" && entry.Max.HasValue && entry.Max.Value <= 1d) d *= 100d;
                return d.ToString("0.##", CultureInfo.CurrentCulture) + (unit ?? "");
            }
            return Convert.ToString(v, CultureInfo.CurrentCulture) + (unit == null || v is string ? "" : unit);
        }
    }
}
