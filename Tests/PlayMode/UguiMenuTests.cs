using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Threads.UiFrames;
using Threads.UiFrames.Input;
using Threads.UiFrames.UGUI;
using Threads.UiMenus.UGUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Threads.UiMenus.Tests
{
    /// <summary>
    /// A uGUI menu driven as a player would: Move/Submit/Cancel through the EventSystem's input
    /// module, Tab through <see cref="UiFramesRunner"/>, the bar's buttons, links, the leave question
    /// and conditions. Uses <see cref="DemoSettings"/> from the UI Toolkit tests.
    /// </summary>
    public class UguiMenuTests : InputTestFixture
    {
        readonly List<Object> _created = new List<Object>();
        Keyboard _kb;
        UiFramesRunner _runner;
        UguiMenus _menus;
        UguiMenuView _view;
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
            _menus?.Dispose();
            _menus = null;
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
        GameObject Row(string name) => _view.RowElement(name);

        IEnumerator Open()
        {
            Keep(new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)))
                .GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var canvas = Keep(new GameObject("Canvas", typeof(Canvas), typeof(GraphicRaycaster)));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var root = (RectTransform)canvas.transform;

            var go = Keep(new GameObject("Runner"));
            go.SetActive(false);
            _runner = go.AddComponent<UiFramesRunner>();
            _runner.uguiRoots.Add(root);
            go.SetActive(true);
            _runner.Environment.PixelsPerDp = 1f;
            yield return null;

            _target = new DemoSettings();
            _menus = UguiMenus.For(_runner, new MenuSchemaCache());
            _view = _menus.Open(root, _target);
            yield return Wait();
        }

        [UnityTest]
        public IEnumerator OpensWithTheFirstRowSelected()
        {
            yield return Open();
            Assert.AreEqual("Demo", _view.TitleText.text);
            Assert.IsNotNull(_runner.Frames.Find(_view.Page.gameObject), "the page is a frame");
            Assert.AreSame(Row("Volume"), Selected);
            Assert.AreSame(Row("Volume"), EventSystem.current.currentSelectedGameObject, "uGUI selection follows");
            Assert.IsTrue(Row("Volume").GetComponent<UiFrameClasses>().HasClass(FrameTags.Selected));
            Assert.IsFalse(_view.ApplyButton.interactable, "nothing to apply yet");
            Assert.Greater(((RectTransform)Row("Volume").transform).rect.height, 20f, "rows are laid out");
        }

        [UnityTest]
        public IEnumerator ArrowsAndEnterEditRowsThenApplyCommits()
        {
            yield return Open();
            yield return Tap(_kb.rightArrowKey);
            Assert.AreEqual(6, _view.Session.Get("Volume"), "Right steps the slider row");
            Assert.AreEqual(5, _target.Volume, "edits a copy until Apply");
            Assert.AreEqual(6f, Row("Volume").GetComponentInChildren<Slider>().value, "the slider shows it");

            yield return Tap(_kb.downArrowKey);
            Assert.AreSame(Row("Subtitles"), Selected, "Down moves to the next row");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(false, _view.Session.Get("Subtitles"), "Enter flips a toggle");
            Assert.AreSame(Row("Subtitles"), Selected, "and stays on the row");

            yield return Tap(_kb.downArrowKey);
            yield return Tap(_kb.leftArrowKey);
            Assert.AreEqual(Difficulty.Easy, _view.Session.Get("Difficulty"), "Left steps a choice");
            Assert.IsTrue(_view.ApplyButton.interactable);

            yield return Tap(_kb.escapeKey);
            Assert.AreSame(_view.Page.gameObject, Selected);
            yield return Tap(_kb.tabKey);
            Assert.AreSame(_view.ApplyButton.gameObject, Selected, "hidden question buttons aside, Apply is next");
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(6, _target.Volume);
            Assert.IsFalse(_target.Subtitles);
            Assert.IsFalse(_view.Session.IsDirty);
        }

        [UnityTest]
        public IEnumerator HiddenRowsLeaveThePage()
        {
            yield return Open();
            Assert.AreSame(_view.RowsContainer, Row("Resolution").transform.parent, "shown while Fullscreen");
            _view.Session.Set("Fullscreen", false);
            yield return null;
            Assert.AreNotSame(_view.RowsContainer, Row("Resolution").transform.parent, "taken out, not just hidden");
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
            Assert.AreSame(Row("Name").GetComponentInChildren<InputField>().gameObject, Selected, "Enter goes into the row's field");
            yield return Tap(_kb.escapeKey);
            Assert.AreSame(Row("Name"), Selected);
        }

        [UnityTest]
        public IEnumerator ConfirmButtonsNeedTwoPresses()
        {
            yield return Open();
            var row = Row("ResetAll");
            SelectRow(row);
            yield return null;
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(0, _target.Resets);
            Assert.IsTrue(row.GetComponent<UiFrameClasses>().HasClass(UguiMenuView.ConfirmClass));
            Assert.AreEqual("Really reset?", row.GetComponentInChildren<Button>().GetComponentInChildren<Text>().text);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(1, _target.Resets);
        }

        [UnityTest]
        public IEnumerator LinksOpenAPageAndEscGoesBack()
        {
            yield return Open();
            SelectRow(Row("More"));
            yield return null;
            yield return Tap(_kb.enterKey);
            Assert.AreEqual("Demo › Sub Settings", _view.TitleText.text);
            yield return null;
            Assert.AreSame(_view.RowElement("Extra"), Selected, "the new page's first row");
            yield return Tap(_kb.escapeKey); // out of the page
            yield return Tap(_kb.escapeKey); // back
            Assert.AreEqual("Demo", _view.TitleText.text);
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
            Assert.IsTrue(_view.Question.gameObject.activeSelf);
            Assert.AreSame(_view.QuestionApply.gameObject, Selected);

            yield return Tap(_kb.escapeKey);
            Assert.IsFalse(_view.IsAsking, "Esc on the question: stay");
            Assert.IsFalse(closed);

            yield return Tap(_kb.escapeKey);
            Assert.IsTrue(_view.IsAsking);
            yield return Tap(_kb.enterKey); // Apply
            Assert.AreEqual(6, _target.Volume);
            Assert.IsTrue(closed);
            yield return null;
            Assert.IsFalse(_view.Root, "the menu destroyed itself");
            Assert.AreEqual(0, _menus.OpenMenus.Count);
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
            Assert.AreSame(_view.CancelButton.gameObject, Selected);
            yield return Tap(_kb.enterKey);
            Assert.AreEqual(5, _target.Volume);
            yield return null;
            Assert.IsFalse(_view.Root);
        }

        [UnityTest]
        public IEnumerator ClickingControlsEdits()
        {
            yield return Open();
            var toggle = Row("Subtitles").GetComponentInChildren<Toggle>();
            Assert.AreEqual(Navigation.Mode.None, toggle.navigation.mode, "a click does not select it");
            Assert.IsTrue(toggle.GetComponent<UiFrameClasses>().HasClass(FrameTags.PointerOnly));
            toggle.isOn = false; // what a click does
            Assert.AreEqual(false, _view.Session.Get("Subtitles"));

            var flags = Row("Hints").GetComponentsInChildren<Toggle>();
            Assert.AreEqual(2, flags.Length, "one toggle per flag, None left out");
            flags[1].isOn = true;
            Assert.AreEqual(Hints.Arrows | Hints.Text, _view.Session.Get("Hints"));
            var flagClasses = flags[0].GetComponent<UiFrameClasses>();
            Assert.IsFalse(flagClasses && flagClasses.HasClass(FrameTags.PointerOnly), "flags stay keyboard items");

            var next = Row("Difficulty").GetComponentsInChildren<Button>()[1];
            next.onClick.Invoke();
            Assert.AreEqual(Difficulty.Hard, _view.Session.Get("Difficulty"), "the › button steps the choice");
            yield return null;
        }

        void SelectRow(GameObject row)
        {
            IFrameElement found = null;
            void Walk(IFrameElement e)
            {
                if (found != null) return;
                if (ReferenceEquals(e.Native, row)) { found = e; return; }
                foreach (var c in e.Children) Walk(c);
            }
            Walk(Nav.Host.Root);
            Nav.SelectElement(found);
        }
    }
}
