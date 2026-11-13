using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Threads.UiMenus.Tests
{
    public enum SpeakerMode
    {
        Stereo,
        [InspectorName("5.1 Surround")] Surround51,
        [Label("7.1")] Surround71,
        [MenuHidden] Debug,
    }

    [Flags]
    public enum Channels { None = 0, Voice = 1, Music = 2, Effects = 4 }

    [Serializable]
    public class Graphics
    {
        public bool Shadows = true;
        [Range(0, 4)] public int Quality = 2;
    }

    [Serializable]
    public struct Window
    {
        public int Width;
        public bool Borderless;
    }

    [MenuPage("Audio", Key = "audio", Order = 2)]
    public class AudioSettings
    {
        [Range(0, 1), LivePreview, Unit("%")] public float Master = 0.8f;
        [Range(0, 10)] public int MusicVolume = 5;
        [Tooltip("Show subtitles for speech"), Header("Speech")] public bool Subtitles = true;
        public SpeakerMode Speakers = SpeakerMode.Stereo;
        public Channels Mute = Channels.None;
        [Label("Profile name")] public string Profile = "Default";
        [Min(0)] public int Delay = 3;
        [Order(-1)] public bool Enabled = true;

        // Left out.
        [HideInInspector] public int HiddenByUnity;
        [MenuHidden] public int HiddenByUs;
        [NonSerialized] public int Runtime;
        public static int Static;
        public const int Constant = 1;
        int _private;
        public int ReadOnlyNotOpted => _private;
        public readonly int ReadOnlyField = 0;

        // Included by opting in.
        [SerializeField] int m_privateSerialized = 7;
        [MenuEntry] public string Version => "1.2";
        public int PrivateSerialized => m_privateSerialized;

        [MenuButton(Confirm = "Really?")] void ResetAll() => Resets++;
        [MenuHidden] public int Resets;
    }

    /// <summary>Records setter calls (side effects such as setting a mixer volume).</summary>
    public class Recorder
    {
        public readonly List<string> Calls = new List<string>();

        float _volume = 0.5f;
        [Range(0, 1), LivePreview] public float Volume { get => _volume; set { _volume = value; Calls.Add($"Volume={value}"); } }

        string _name = "a";
        public string Name { get => _name; set { _name = value; Calls.Add($"Name={value}"); } }

        int _size = 1;
        [RequiresRestart] public int Size { get => _size; set { _size = value; Calls.Add($"Size={value}"); } }
    }

    public class VideoSettings
    {
        public bool Fullscreen = true;
        [ShowIf(nameof(Fullscreen)), Options(nameof(Resolutions))] public string Resolution = "1920x1080";
        public SpeakerMode Mode;
        [ShowIf(nameof(Mode), SpeakerMode.Surround51)] public bool Centre;
        public Graphics Graphics = new Graphics();
        public Window Window = new Window { Width = 800 };
        [ShowIf(nameof(Fullscreen), false)] public Graphics Windowed = new Graphics();

        [SettingKey("res"), FormerlySerializedAs("oldRes")] public int RefreshRate = 60;
        [DontSave] public bool Preview;

        public AudioSettings Audio = new AudioSettings();
        [MenuLink] public AudioSettings AudioLink = new AudioSettings();

        IEnumerable<string> Resolutions() => new[] { "1280x720", "1920x1080", "2560x1440" };

        [MenuValidate]
        string RefreshMustBeSane(IMenuValues v) => v.Get<int>(nameof(RefreshRate)) > 240 ? "Refresh rate too high." : null;
    }
}
