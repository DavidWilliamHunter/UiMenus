using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Threads.UiMenus.Demo
{
    // The settings of a small game, written as ordinary classes. Every menu in the demo is
    // generated from these: the attributes say how each member is shown.

    public enum SpeakerMode { Stereo, Headphones, [Label("5.1 Surround")] Surround51 }

    public enum Difficulty { Story, Normal, Hard, [Label("Very hard")] VeryHard }

    [Flags]
    public enum Hints { None = 0, Arrows = 1, Markers = 2, Tutorials = 4 }

    [MenuPage("Audio", Key = "audio")]
    public class AudioSettings
    {
        float _master = 0.8f;

        // A property with a side effect, heard while the slider moves ([LivePreview]); Cancel puts it back.
        [Order(-1), Range(0, 1), LivePreview, Unit("%")]
        public float Master
        {
            get => _master;
            set { _master = value; AudioListener.volume = value; }
        }

        [Range(0, 1), Unit("%")] public float Music = 0.7f;
        [Range(0, 1), Unit("%")] public float Effects = 0.8f;
        [Label("Speaker setup")] public SpeakerMode Speakers = SpeakerMode.Stereo;
        [Header("Speech"), Tooltip("Show subtitles for speech")] public bool Subtitles = true;
        [ShowIf(nameof(Subtitles)), Range(12, 32)] public int SubtitleSize = 18;
    }

    [MenuPage("Video", Key = "video")]
    public class VideoSettings
    {
        // Properties that read and write Unity's own settings.
        public bool Fullscreen
        {
            get => Screen.fullScreen;
            set => Screen.fullScreen = value;
        }

        [ShowIf(nameof(Fullscreen)), Options(nameof(Resolutions))]
        public string Resolution
        {
            get => $"{Screen.width}x{Screen.height}";
            set
            {
                var parts = value?.Split('x');
                if (parts?.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    Screen.SetResolution(w, h, Screen.fullScreenMode);
            }
        }

        [Options(nameof(QualityLevels)), RequiresRestart]
        public string Quality
        {
            get => QualitySettings.names.Length == 0 ? "" : QualitySettings.names[QualitySettings.GetQualityLevel()];
            set
            {
                var i = Array.IndexOf(QualitySettings.names, value);
                if (i >= 0) QualitySettings.SetQualityLevel(i, true);
            }
        }

        [Label("V-Sync")]
        public bool VSync
        {
            get => QualitySettings.vSyncCount > 0;
            set => QualitySettings.vSyncCount = value ? 1 : 0;
        }

        [ShowIf(nameof(VSync), false), Range(30, 240), Step(30), Unit(" fps")]
        public int FrameRateLimit
        {
            get => Application.targetFrameRate <= 0 ? 120 : Application.targetFrameRate;
            set => Application.targetFrameRate = value;
        }

        IEnumerable<string> Resolutions()
        {
            var list = Screen.resolutions.Select(r => $"{r.width}x{r.height}").Distinct().ToList();
            var current = $"{Screen.width}x{Screen.height}";
            if (!list.Contains(current)) list.Add(current); // the Editor's Game view, a window
            return list;
        }

        IEnumerable<string> QualityLevels() => QualitySettings.names;
    }

    [MenuPage("Controls", Key = "controls")]
    public class ControlsSettings
    {
        public bool InvertLook;
        [Range(0.1f, 3f), Step(0.1)] public float LookSensitivity = 1f;
    }

    [MenuPage("Gameplay", Key = "gameplay")]
    public class GameplaySettings
    {
        public Difficulty Difficulty = Difficulty.Normal;
        public Hints Hints = Hints.Arrows | Hints.Markers;
        [Label("Player name")] public string PlayerName = "Player";
        [Tooltip("Shown in the status line")] public bool ShowFps;
        [Min(0), Unit(" s")] public int AutosaveInterval = 300;

        [MenuEntry] public string Version => Application.version;

        [MenuButton(Confirm = "Press again to erase all progress")]
        void ResetProgress() => Debug.Log("UI Menus demo: progress erased (not really).");

        [MenuValidate]
        string NameNeeded(IMenuValues values) =>
            string.IsNullOrWhiteSpace(values.Get<string>(nameof(PlayerName))) ? "Enter a player name." : null;
    }

    /// <summary>The main menu: buttons and links to the settings pages (each applied on its own).</summary>
    [MenuPage("Main Menu")]
    public class MainMenu
    {
        readonly MenusDemo _demo;

        public MainMenu(MenusDemo demo, AudioSettings audio, VideoSettings video, ControlsSettings controls, GameplaySettings gameplay)
        {
            _demo = demo;
            Audio = audio;
            Video = video;
            Controls = controls;
            Gameplay = gameplay;
        }

        [MenuLink] public AudioSettings Audio;
        [MenuLink] public VideoSettings Video;
        [MenuLink] public ControlsSettings Controls;
        [MenuLink] public GameplaySettings Gameplay;

        [MenuButton, Order(-1)] void Play() => _demo.Play();

        [MenuButton(Confirm = "Press again to quit"), Order(1)] void Quit() => _demo.Quit();
    }
}
