using System;
using UnityEngine;

namespace Threads.UiMenus.UGUI
{
    /// <summary>
    /// How uGUI menus look: font, sizes and colours (uGUI has no style sheets). Keep one on a
    /// component or asset of your own and pass it to <see cref="UguiMenus"/>; the defaults match
    /// the UI Toolkit menus. Rows also take UI Frames' state visuals (<c>uf-selected</c> etc.).
    /// </summary>
    [Serializable]
    public sealed class UguiMenuStyle
    {
        [Tooltip("Null: Unity's built-in LegacyRuntime font.")]
        public Font font;
        public int fontSize = 16;
        public int titleSize = 22;
        [Tooltip("Height of a row's line, in canvas units.")]
        public float rowHeight = 36f;
        public float padding = 12f;
        public Color background = new Color32(24, 26, 32, 240);
        public Color text = new Color32(230, 232, 238, 255);
        public Color muted = new Color32(160, 170, 190, 255);
        public Color warning = new Color32(255, 170, 120, 255);
        public Color row = new Color(1f, 1f, 1f, 0.02f);
        public Color control = new Color32(52, 58, 72, 255);
        [Tooltip("A toggle's box.")]
        public Color toggle = new Color32(96, 104, 124, 255);
        public Color accent = new Color32(110, 165, 255, 255);
        public Color danger = new Color32(170, 60, 50, 255);
        public Color dim = new Color(0f, 0f, 0f, 0.6f);
        public Color box = new Color32(40, 44, 54, 255);

        [NonSerialized] Font _builtIn;

        /// <summary>The font in use (<see cref="font"/>, or the built-in one).</summary>
        public Font Font => font ? font : _builtIn ? _builtIn : _builtIn = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
