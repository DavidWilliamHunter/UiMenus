using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames;
using Threads.UiFrames.Input;
using Threads.UiMenus.Toolkit;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using Range = UnityEngine.RangeAttribute;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Threads.UiMenus.Tests
{
    public enum Difficulty { Easy, Normal, Hard }

    [Flags]
    public enum Hints { None = 0, Arrows = 1, Text = 2 }

    public class SubSettings
    {
        public bool Extra;
    }

    [MenuPage("Demo")]
    public class DemoSettings
    {
        [Range(0, 10)] public int Volume = 5;
        public bool Subtitles = true;
        public Difficulty Difficulty = Difficulty.Normal;
        public bool Fullscreen = true;
        [ShowIf(nameof(Fullscreen)), Options(nameof(Resolutions))] public string Resolution = "1920x1080";
        public string Name = "Player";
        public Hints Hints = Hints.Arrows;
        [MenuLink] public SubSettings More = new SubSettings();
        [MenuHidden] public int Resets;
        [MenuButton(Confirm = "Really reset?")] void ResetAll() => Resets++;

        IEnumerable<string> Resolutions() => new[] { "1280x720", "1920x1080" };
    }

    /// <summary>
    /// A UI Toolkit menu driven as a player would: keys through <see cref="UiFramesRunner"/> (no
    /// EventSystem), the bar's buttons, links, the leave question, and conditions.
    /// </summary>
    public class ToolkitMenuTests : InputTestFixture
    {
        readonly List<Object> _created = new List<Object>();
        Keyboard _kb;
        UiFramesRunner _runner;
        UIDocument _doc;
        ToolkitMenus _menus;
        ToolkitMenuView _view;
        DemoSettings _target;
        int _frameRate;

        public override void Setup()
        {
            base.Setup();
            _kb = InputSystem.AddDevice<Keyboard>();
            _frameRate = Application.targetFrameRate;
            Application.targetFrameRate = 60;
        }

        public override void TearDown()
        {
            Application.targetFrameRate = _frameRate;
            _view?.Dispose();
            _view = null;
            foreach (var o in _created) if (o) Object.Destroy(o);
            _created.Clear();
            base.TearDown();
        }

        T Keep<T>(T o) where T : Object { _created.Add(o); return o; }

        IEnumerator Tap(KeyControl key)
        {
            Press(key);
            yield return null;
            Release(key);
            yield return null;
            yield return null;
        }

        IEnumerator Wait(float seconds = 0.2f)
        {
            var end = Time.unscaledTime + seconds;
            while (Time.unscaledTime < end) yield return null;
        }

        FrameNavigator Nav => _runner.Navigators[0];
        object Selected => Nav.Selected?.Native;
        VisualElement Row(string name) => _view.RowElement(name);

        IEnumerator Open()
        {
#if UNITY_EDITOR
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Packages/com.threads.uiframes/Tests/PlayMode/TestAssets/TestTheme.tss");
            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.threads.uimenus/Runtime/Toolkit/UiMenus.uss");
#else
            ThemeStyleSheet theme = null;
            StyleSheet uss = null;
#endif
            if (uss == null) Assert.Ignore("The style sheet is only loadable in the Editor.");
            var settings = Keep(ScriptableObject.CreateInstance<PanelSettings>());
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.themeStyleSheet = theme;
            var go = Keep(new GameObject("Doc"));
            go.SetActive(false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = settings;
            _runner = go.AddComponent<UiFramesRunner>();
            _runner.documents.Add(_doc);
            go.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f;
            yield return null;

            _target = new DemoSettings();
            _menus = ToolkitMenus.For(_runner, new MenuSchemaCache(), uss);
            _view = _menus.Open(_doc.rootVisualElement, _target);
            yield return Wait();
        }

        [UnityTest]
        public IEnumerator OpensWithTheFirstRowSelected()
        {
            yield return Open();
            Assert.AreEqual("Demo", _view.TitleLabel.text);
            Assert.IsNotNull(_runner.Frames.Find(_view.Page), "the page is a frame");
            Assert.AreSame(Row("Volume"), Selected);
            Assert.IsTrue(Row("Volume").ClassListContains(FrameTags.Selected), "the selection is visible");
            Assert.IsFalse(_view.ApplyButton.enabledSelf, "nothing to apply yet");
        }

        [UnityTest]
        public IEnumerator ArrowsAndEnterEditRowsThenApplyCommits()
        {
            yield return Open();
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual(6, _view.Session.Get("Volume"), "Right steps the slider row");
            Assert.AreEqual(5, _target.Volume, "edits a copy until Apply");
            Assert.AreEqual(6f, _view.RowElement("Volume").Q<SliderInt>().value, "the slider shows it");
            Assert.IsTrue(Row("Volume").ClassListContains(ToolkitMenuView.DirtyClass));

            yield return Tap(_kb.downArrowKey);
            Assert.AreSame(Row("Subtitles"), Selected, "Down moves to the next row");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(false, _view.Session.Get("Subtitles"), "Enter flips a toggle");
            Assert.AreSame(Row("Subtitles"), Selected, "and stays on the row");

            yield return Tap(_kb.downArrowKey);
            yield return Tap(_kb.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, _view.Session.Get("Difficulty"), "Left steps a choice");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(Difficulty.Normal, _view.Session.Get("Difficulty"), "Enter steps it on");
            Assert.IsTrue(_view.ApplyButton.enabledSelf);

            // Out to the menu's level, Tab to Apply, press it.
            yield return Tap(_kb.escapeKey);
            Assert.AreSame(_view.Page, Selected);
            yield return Tap(_kb.tabKey);
            Assert.AreSame(_view.ApplyButton, Selected, "disabled Revert/hidden question buttons aside, Apply is next");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(6, _target.Volume);
            Assert.IsFalse(_target.Subtitles);
            Assert.IsFalse(_view.Session.IsDirty);
        }

        [UnityTest]
        public IEnumerator HiddenRowsLeaveThePage()
        {
            yield return Open();
            Assert.IsNotNull(Row("Resolution").parent, "shown while Fullscreen");
            _view.Session.Set("Fullscreen", false);
            yield return null;
            Assert.IsNull(Row("Resolution").parent, "taken out, not just hidden");
            for (var i = 0; i < 3; i++) yield return Tap(_kb.downArrowKey);
            Assert.AreSame(Row("Fullscreen"), Selected);
            yield return Tap(_kb.downArrowKey);
            Assert.AreSame(Row("Name"), Selected, "keys skip it");
        }

        [UnityTest]
        public IEnumerator TextRowsAreEnteredLikeCards()
        {
            yield return Open();
            for (var i = 0; i < 5; i++) yield return Tap(_kb.downArrowKey);
            Assert.AreSame(Row("Name"), Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreSame(Row("Name").Q<TextField>(), Selected, "Enter goes into the row's field");
            yield return Tap(_kb.escapeKey);
            Assert.AreSame(Row("Name"), Selected);
        }

        [UnityTest]
        public IEnumerator ConfirmButtonsNeedTwoPresses()
        {
            yield return Open();
            var button = Row("ResetAll");
            _runner.Navigators[0].SelectElement(Wrap(button));
            yield return null;
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(0, _target.Resets);
            Assert.IsTrue(button.ClassListContains(ToolkitMenuView.ConfirmClass));
            Assert.AreEqual("Really reset?", button.Q<Button>().text);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(1, _target.Resets);
            Assert.IsFalse(button.ClassListContains(ToolkitMenuView.ConfirmClass));
        }

        [UnityTest]
        public IEnumerator LinksOpenAPageAndEscGoesBack()
        {
            yield return Open();
            _runner.Navigators[0].SelectElement(Wrap(Row("More")));
            yield return null;
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Demo › Sub Settings", _view.TitleLabel.text);
            Assert.AreSame(_view.RowElement("Extra"), Selected, "the new page's first row");
            yield return Tap(_kb.escapeKey); // out of the page
            yield return Tap(_kb.escapeKey); // back
            Assert.AreEqual("Demo", _view.TitleLabel.text);
            Assert.AreEqual(1, _view.Stack.Pages.Count);
        }

        [UnityTest]
        public IEnumerator LeavingWithChangesAsks()
        {
            yield return Open();
            var closed = false;
            _view.Closed += () => closed = true;
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.escapeKey);
            yield return Tap(_kb.escapeKey);
            Assert.IsTrue(_view.IsAsking);
            Assert.IsFalse(_view.Question.ClassListContains(ToolkitMenuView.HiddenClass));
            Assert.AreSame(_view.QuestionApply, Selected);

            yield return Tap(_kb.escapeKey);
            Assert.IsFalse(_view.IsAsking, "Esc on the question: stay");
            Assert.IsFalse(closed);

            yield return Tap(_kb.escapeKey);
            Assert.IsTrue(_view.IsAsking);
            yield return Tap(_kb.enterKey); // Apply
            Assert.AreEqual(6, _target.Volume);
            Assert.IsTrue(closed);
            Assert.IsNull(_view.Root.parent, "the menu removed itself");
            Assert.IsNull(_runner.Frames.Find(_view.Page), "and its frame is gone");
        }

        [UnityTest]
        public IEnumerator CancelRevertsAndCloses()
        {
            yield return Open();
            yield return Tap(_kb.rightArrowKey);
            yield return Tap(_kb.escapeKey);
            yield return Tap(_kb.tabKey); // Apply
            yield return Tap(_kb.tabKey); // Revert
            yield return Tap(_kb.tabKey); // Cancel
            Assert.AreSame(_view.CancelButton, Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(5, _target.Volume);
            Assert.IsNull(_view.Root.parent);
        }

        [UnityTest]
        public IEnumerator ClickingControlsEdits()
        {
            yield return Open();
            var toggle = Row("Subtitles").Q<Toggle>();
            Assert.IsFalse(toggle.focusable, "row controls don't take keyboard focus");
            Assert.IsTrue(toggle.ClassListContains(FrameTags.PointerOnly));
            toggle.value = false; // what a click does
            Assert.AreEqual(false, _view.Session.Get("Subtitles"));

            var flags = Row("Hints").Query<Toggle>().ToList();
            Assert.AreEqual(2, flags.Count, "one toggle per flag, None left out");
            flags[1].value = true;
            Assert.AreEqual(Hints.Arrows | Hints.Text, _view.Session.Get("Hints"));
            Assert.IsFalse(flags[0].ClassListContains(FrameTags.PointerOnly), "flags stay keyboard items");
            yield return null;
            Assert.IsTrue(flags[1].value, "the view shows the edit");
        }

        IFrameElement Wrap(VisualElement ve)
        {
            // The same lookup the menus use to select.
            IFrameElement found = null;
            void Walk(IFrameElement e)
            {
                if (found != null) return;
                if (ReferenceEquals(e.Native, ve)) { found = e; return; }
                foreach (var c in e.Children) Walk(c);
            }
            Walk(Nav.Host.Root);
            return found;
        }
    }
}
