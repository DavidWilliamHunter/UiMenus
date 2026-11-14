using System.IO;
using Threads.UiFrames.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace Threads.UiMenus.Demo.Editor
{
    /// <summary>Threads > UI Menus > Build Demo Scenes: (re)creates the UI Toolkit and uGUI demo scenes.</summary>
    public static class MenusDemoBuilder
    {
        const string MenuUss = "Packages/com.threads.uimenus/Runtime/Toolkit/UiMenus.uss";
        const string FramesUss = "Packages/com.threads.uiframes/Runtime/Toolkit/UiFrames.uss";

        [MenuItem("Threads/UI Menus/Build Demo Scenes")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var toolkit = Build();
            if (toolkit != null) EditorSceneManager.OpenScene(toolkit);
        }

        /// <summary>Builds both scenes without asking (batch mode); returns the UI Toolkit scene's path.</summary>
        public static string Build()
        {
            var dir = DemoFolder();
            if (dir == null)
            {
                Debug.LogError("UI Menus: import the Demo sample first (MenusDemo.cs not found).");
                return null;
            }
            Directory.CreateDirectory(Path.Combine(dir, "Scenes"));
            Directory.CreateDirectory(Path.Combine(dir, "Toolkit"));
            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>($"{dir}/DemoControls.inputactions");
            var toolkit = BuildToolkitScene(dir, controls);
            var ugui = BuildUguiScene(dir, controls);
            AssetDatabase.SaveAssets();
            Debug.Log($"UI Menus: built {toolkit} and {ugui}.");
            return toolkit;
        }

        static string DemoFolder()
        {
            foreach (var guid in AssetDatabase.FindAssets("MenusDemo t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/MenusDemo.cs")) return Path.GetDirectoryName(path).Replace('\\', '/');
            }
            return null;
        }

        static string BuildToolkitScene(string dir, InputActionAsset controls)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddCamera();

            var settingsPath = $"{dir}/Toolkit/DemoPanelSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(settingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>($"{dir}/Toolkit/DemoTheme.tss");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 720);
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);

            // Touch reaches UI Toolkit only through an EventSystem (keys come through the runner either way).
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var go = new GameObject("UI Toolkit Menus");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            var runner = go.AddComponent<UiFramesRunner>();
            runner.documents.Add(doc);
            runner.toolkitDefaults = AssetDatabase.LoadAssetAtPath<StyleSheet>(FramesUss);
            var demo = go.AddComponent<MenusDemo>();
            demo.runner = runner;
            demo.document = doc;
            demo.menuStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>(MenuUss);
            demo.controls = controls;

            var path = $"{dir}/Scenes/UiMenusToolkitDemo.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static string BuildUguiScene(string dir, InputActionAsset controls)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddCamera();
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            var go = new GameObject("uGUI Menus");
            var runner = go.AddComponent<UiFramesRunner>();
            runner.uguiRoots.Add((RectTransform)canvasGo.transform);
            runner.eventSystem = es.GetComponent<EventSystem>();
            var demo = go.AddComponent<MenusDemo>();
            demo.runner = runner;
            demo.canvasRoot = (RectTransform)canvasGo.transform;
            demo.controls = controls;

            var path = $"{dir}/Scenes/UiMenusUguiDemo.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        static void AddCamera()
        {
            var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(22, 26, 34, 255);
        }
    }
}
