using System.Collections;
using System.IO;
using Threads.UiFrames.Input;
using Threads.UiMenus.Toolkit;
using Threads.UiMenus.UGUI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Threads.UiMenus.Demo
{
    /// <summary>
    /// The demo's bootstrap: loads the settings, opens the main menu in UI Toolkit (when
    /// <see cref="document"/> is set) or uGUI (<see cref="canvasRoot"/>), and shows a status line.
    /// The game's Pause Menu action (Esc or Start by default; an ordinary Input System action, so it can
    /// be rebound like any other) opens the menu again once it is closed. Settings are saved as JSON
    /// in <c>persistentDataPath/Settings/UiMenusDemo</c>.
    /// </summary>
    public sealed class MenusDemo : MonoBehaviour
    {
        public UiFramesRunner runner;
        [Header("UI Toolkit")]
        public UIDocument document;
        [Tooltip("UiMenus.uss from the package.")]
        public StyleSheet menuStyle;
        [Header("uGUI")]
        public RectTransform canvasRoot;
        [Header("Input")]
        [Tooltip("The game's actions: Jump, and Pause Menu, which opens the menu.")]
        public InputActionAsset controls;

        MenuSchemaCache _schemas;
        ISettingsStore _store;
        Settings<AudioSettings> _audio;
        Settings<VideoSettings> _video;
        Settings<ControlsSettings> _controls;
        Settings<GameplaySettings> _gameplay;
        MainMenu _main;
        ToolkitMenus _toolkit;
        UguiMenus _ugui;
        MenuView _open;
        int _closedFrame = -10;
        InputAction _jump;
        InputAction _pause;
        string _event = "";
        Label _toolkitStatus;
        Text _uguiStatus;

        /// <summary>The menu showing, or null.</summary>
        public MenuView OpenMenu => _open;
        public string Status { get; private set; } = "";

        IEnumerator Start()
        {
            // Everything the menus need is made here and passed on: no singletons.
            _schemas = new MenuSchemaCache();
            _store = new JsonFileStore(Path.Combine(UiMenusHost.DefaultSettingsDirectory, "UiMenusDemo"));
            _audio = new Settings<AudioSettings>(_store, _schemas);
            _video = new Settings<VideoSettings>(_store, _schemas);
            _gameplay = new Settings<GameplaySettings>(_store, _schemas);
            _controls = new Settings<ControlsSettings>(_store, _schemas);
            _audio.Load();
            _video.Load();
            _gameplay.Load();
            _controls.Load();
            _main = new MainMenu(this, _audio.Value, _video.Value, _controls.Value, _gameplay.Value);

            if (controls)
            {
                controls.Enable();
                _jump = controls.FindAction("Gameplay/Jump");
                _pause = controls.FindAction("Gameplay/Pause Menu");
                if (_jump != null) _jump.performed += OnJump;
            }

            yield return null; // the runner binds UI Toolkit documents once their panels exist
            if (document)
            {
                _toolkit = ToolkitMenus.For(runner, _schemas, menuStyle);
                _toolkitStatus = new Label { name = "demo-status" };
                _toolkitStatus.style.position = Position.Absolute;
                _toolkitStatus.style.left = 12;
                _toolkitStatus.style.bottom = 8;
                _toolkitStatus.style.color = (Color)new Color32(255, 196, 64, 255);
                document.rootVisualElement.Add(_toolkitStatus);
            }
            else if (canvasRoot)
            {
                _ugui = UguiMenus.For(runner, _schemas);
                var go = new GameObject("Status", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(canvasRoot, false);
                rt.anchorMin = new Vector2(0, 0);
                rt.anchorMax = new Vector2(1, 0);
                rt.pivot = new Vector2(0, 0);
                rt.anchoredPosition = new Vector2(12, 8);
                rt.sizeDelta = new Vector2(-24, 24);
                _uguiStatus = go.AddComponent<Text>();
                _uguiStatus.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _uguiStatus.fontSize = 15;
                _uguiStatus.color = new Color32(255, 196, 64, 255);
                _uguiStatus.raycastTarget = false;
            }
            Open();
        }

        void OnDestroy()
        {
            if (_jump != null) _jump.performed -= OnJump;
            _open?.Dispose();
            _ugui?.Dispose();
        }

        /// <summary>Opens the main menu (if none is open).</summary>
        public void Open()
        {
            if (_open != null) return;
            if (_toolkit != null) _open = _toolkit.Open(document.rootVisualElement, _main, _store);
            else if (_ugui != null)
            {
                _open = _ugui.Open(canvasRoot, _main, _store);
                if (_uguiStatus) _uguiStatus.transform.SetAsLastSibling();
            }
            if (_open == null) return;
            _open.Closed += () =>
            {
                _open = null;
                _closedFrame = Time.frameCount;
            };
        }

        public void Play()
        {
            _event = $"Play pressed — hello, {_gameplay.Value.PlayerName}.";
            _open?.Cancel(); // close the menu
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnJump(InputAction.CallbackContext _)
        {
            if (_open == null) _event = $"Jump! ({Time.frameCount})";
        }

        void Update()
        {
            // The press that closed the menu (Esc) must not open it again.
            var reopen = _pause != null && _pause.WasPerformedThisFrame();
            if (_open == null && reopen && Time.frameCount > _closedFrame + 1) Open();

            var jump = _jump == null ? "" : _jump.GetBindingDisplayString(0);
            var pause = _pause == null ? "" : _pause.GetBindingDisplayString(0);
            var fps = _gameplay != null && _gameplay.Value.ShowFps ? $"   {1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f):0} fps" : "";
            Status = _open != null
                ? $"Menu open: arrows, Enter, Esc (or mouse, touch, gamepad).{fps}"
                : $"Menu closed: {pause} (Pause Menu) opens it. Jump is {jump}. {_event}{fps}";
            if (_toolkitStatus != null) _toolkitStatus.text = Status;
            if (_uguiStatus) _uguiStatus.text = Status;
        }
    }
}
