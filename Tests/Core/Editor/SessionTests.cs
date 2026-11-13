using System.Globalization;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Threads.UiMenus.Tests
{
    public class SessionTests
    {
        MenuSchemaCache _cache;
        InMemorySettingsStore _store;

        [SetUp]
        public void SetUp()
        {
            _cache = new MenuSchemaCache();
            _store = new InMemorySettingsStore();
        }

        MenuSession Open(object target) => new MenuSession(target, _cache, _store);

        // ---- Edit a copy, Apply commits ----

        [Test]
        public void EditsStayInTheCopyUntilApply()
        {
            var audio = new AudioSettings();
            var s = Open(audio);
            Assert.IsFalse(s.IsDirty);
            s.Set("Subtitles", false);
            Assert.IsTrue(audio.Subtitles, "the object is untouched");
            Assert.IsTrue(s.IsDirty);
            Assert.IsTrue(s.IsDirtyEntry(s.Schema.Find("Subtitles")));
            var result = s.Apply();
            Assert.IsTrue(result.Applied);
            Assert.IsFalse(audio.Subtitles, "written by Apply");
            Assert.IsFalse(s.IsDirty);
            CollectionAssert.AreEqual(new[] { "Subtitles" }, result.Changed.Select(e => e.Name));
        }

        [Test]
        public void SettersRunOnApplyInPageOrderAndOnlyForChanges()
        {
            var r = new Recorder();
            var s = Open(r);
            s.Set("Size", 4);
            s.Set("Name", "b");
            CollectionAssert.IsEmpty(r.Calls, "non-live setters wait for Apply");
            s.Apply();
            CollectionAssert.AreEqual(new[] { "Name=b", "Size=4" }, r.Calls, "page order (declaration), changed only");
            r.Calls.Clear();
            s.Apply();
            CollectionAssert.IsEmpty(r.Calls, "nothing changed: nothing written");
        }

        [Test]
        public void LivePreviewWritesAtOnceAndRollsBack()
        {
            var r = new Recorder();
            var s = Open(r);
            s.Set("Volume", 0.9f);
            Assert.AreEqual(0.9f, r.Volume, "heard while editing");
            s.Revert();
            Assert.AreEqual(0.5f, r.Volume, "Revert rolls the object back");
            s.Set("Volume", 0.7f);
            s.Cancel();
            Assert.AreEqual(0.5f, r.Volume, "Cancel too");
            Assert.IsTrue(s.IsClosed);
        }

        [Test]
        public void LivePreviewIsNotWrittenTwiceOnApply()
        {
            var r = new Recorder();
            var s = Open(r);
            s.Set("Volume", 0.9f);
            r.Calls.Clear();
            s.Apply();
            CollectionAssert.IsEmpty(r.Calls);
            Assert.IsFalse(s.IsDirty);
        }

        [Test]
        public void RevertGoesToTheLastAppliedValues()
        {
            var audio = new AudioSettings();
            var s = Open(audio);
            s.Set("MusicVolume", 8);
            s.Apply();
            s.Set("MusicVolume", 2);
            s.Revert();
            Assert.AreEqual(8, s.Get<int>("MusicVolume"), "the applied value, not the original");
            Assert.AreEqual(8, audio.MusicVolume);
            Assert.IsFalse(s.IsDirty);
        }

        [Test]
        public void CancelRevertsAndCloses()
        {
            var audio = new AudioSettings();
            var s = Open(audio);
            var closed = 0;
            s.Closed += () => closed++;
            s.Set("Profile", "Pro");
            s.Cancel();
            Assert.AreEqual("Default", audio.Profile);
            Assert.AreEqual(1, closed);
            Assert.IsFalse(s.Set("Profile", "X"), "a closed session takes no edits");
        }

        // ---- Values ----

        [Test]
        public void NumbersAreClampedAndRounded()
        {
            var s = Open(new AudioSettings());
            s.Set("Master", 3.0);
            Assert.AreEqual(1f, s.Get<float>("Master"), "[Range] max, and converted to float");
            s.Set("MusicVolume", 4.6);
            Assert.AreEqual(5, s.Get<int>("MusicVolume"), "rounded to the int member");
            s.Set("Delay", -4);
            Assert.AreEqual(0, s.Get<int>("Delay"), "[Min]");
            Assert.IsFalse(s.Set("Delay", "lots"), "not convertible");
        }

        [Test]
        public void StepFollowsTheKind()
        {
            var s = Open(new AudioSettings());
            Assert.IsTrue(s.Step(s.Schema.Find("Subtitles"), 1));
            Assert.IsFalse(s.Get<bool>("Subtitles"), "a toggle flips");
            s.Step(s.Schema.Find("Master"), -1);
            Assert.AreEqual(0.75f, s.Get<float>("Master"), 1e-5f, "a twentieth of the range");
            s.Step(s.Schema.Find("MusicVolume"), 1);
            Assert.AreEqual(6, s.Get<int>("MusicVolume"), "integers step by 1");
            s.Step(s.Schema.Find("Speakers"), -1);
            Assert.AreEqual(SpeakerMode.Surround71, s.Get<SpeakerMode>("Speakers"), "choices wrap (hidden values skipped)");
            Assert.IsFalse(s.Step(s.Schema.Find("Profile"), 1), "text does not step");
        }

        [Test]
        public void ChoicesHaveLabels()
        {
            var s = Open(new AudioSettings());
            var choices = s.Choices(s.Schema.Find("Speakers"));
            CollectionAssert.AreEqual(new[] { "Stereo", "5.1 Surround", "7.1" }, choices.Select(c => c.Label));
            var v = Open(new VideoSettings());
            CollectionAssert.AreEqual(new[] { "1280x720", "1920x1080", "2560x1440" }, v.Choices(v.Schema.Find("Resolution")).Select(c => c.Label));
            v.Step(v.Schema.Find("Resolution"), 1);
            Assert.AreEqual("2560x1440", v.Get<string>("Resolution"));
        }

        [Test]
        public void ShowIfFollowsTheEditedValues()
        {
            var v = Open(new VideoSettings());
            var resolution = v.Schema.Find("Resolution");
            var centre = v.Schema.Find("Centre");
            var windowed = v.Schema.Find("Windowed.Shadows");
            Assert.IsTrue(v.IsVisible(resolution));
            Assert.IsFalse(v.IsVisible(centre));
            Assert.IsFalse(v.IsVisible(windowed), "a group's rows follow the group");
            v.Set("Fullscreen", false);
            Assert.IsFalse(v.IsVisible(resolution), "before Apply: the copy decides");
            Assert.IsTrue(v.IsVisible(windowed));
            v.Set("Mode", SpeakerMode.Surround51);
            Assert.IsTrue(v.IsVisible(centre), "enum condition");
        }

        [Test]
        public void ValidationBlocksApply()
        {
            var video = new VideoSettings();
            var v = Open(video);
            v.Set("RefreshRate", 300);
            var result = v.Apply();
            Assert.IsFalse(result.Applied);
            CollectionAssert.AreEqual(new[] { "Refresh rate too high." }, result.Errors);
            Assert.AreEqual(60, video.RefreshRate, "nothing written");
            Assert.IsTrue(v.IsDirty);
            Assert.AreEqual(0, _store.Saves, "nothing saved");
            v.Set("RefreshRate", 144);
            Assert.IsTrue(v.Apply().Applied);
        }

        [Test]
        public void GroupsInsideTheObjectAreEditedToo()
        {
            var video = new VideoSettings();
            var v = Open(video);
            v.Set("Graphics.Quality", 4);
            v.Set("Window.Width", 1024);
            v.Apply();
            Assert.AreEqual(4, video.Graphics.Quality);
            Assert.AreEqual(1024, video.Window.Width, "a struct is written back to its owner");
        }

        [Test]
        public void RestartsAreReported()
        {
            var r = new Recorder();
            var s = Open(r);
            s.Set("Size", 9);
            s.Set("Name", "z");
            var result = s.Apply();
            CollectionAssert.AreEqual(new[] { "Size" }, result.NeedRestart.Select(e => e.Name));
        }

        [Test]
        public void ButtonsLinksAndInfo()
        {
            var audio = new AudioSettings();
            var s = Open(audio);
            s.Press(s.Schema.Find("ResetAll"));
            Assert.AreEqual(1, audio.Resets);
            Assert.AreEqual("1.2", s.InfoValue(s.Schema.Find("Version")));
            var video = new VideoSettings();
            var v = Open(video);
            Assert.AreSame(video.AudioLink, v.LinkTarget(v.Schema.Find("AudioLink")));
            var linked = new MenuSession(v.LinkTarget(v.Schema.Find("AudioLink")), _cache);
            Assert.AreEqual("Audio", linked.Schema.Title, "a link opens the other object's own session");
        }

        [Test]
        public void ApplySavesThroughTheStore()
        {
            var audio = new AudioSettings();
            var s = Open(audio);
            s.Set("MusicVolume", 9);
            s.Apply();
            Assert.AreEqual(1, _store.Saves);
            StringAssert.Contains("\"MusicVolume\": \"9\"", _store.Texts["audio"]);
        }

        [Test]
        public void ValueChangedIsRaised()
        {
            var s = Open(new AudioSettings());
            var changes = 0;
            s.ValueChanged += _ => changes++;
            s.Set("Subtitles", false);
            s.Set("Subtitles", false); // same value
            s.Revert();
            Assert.AreEqual(2, changes, "edit and revert, not the repeat");
        }
    }

    public class StackTests
    {
        MenuSchemaCache _cache;

        [SetUp]
        public void SetUp() => _cache = new MenuSchemaCache();

        [Test]
        public void BackNeedsADecisionWhenDirty()
        {
            var stack = new MenuStack();
            var audio = new AudioSettings();
            var page = new MenuSession(audio, _cache);
            stack.Push(page);
            page.Set("Subtitles", false);
            Assert.IsTrue(stack.NeedsDecision);
            Assert.IsFalse(stack.Back(), "ask first");
            Assert.AreSame(page, stack.Current);
            Assert.IsTrue(stack.Back(LeaveChoice.Apply));
            Assert.IsFalse(audio.Subtitles);
            Assert.IsTrue(page.IsClosed);
            Assert.IsTrue(stack.IsEmpty);
        }

        [Test]
        public void DiscardReverts()
        {
            var stack = new MenuStack();
            var r = new Recorder();
            var page = new MenuSession(r, _cache);
            stack.Push(page);
            page.Set("Volume", 1f);
            stack.Back(LeaveChoice.Discard);
            Assert.AreEqual(0.5f, r.Volume, "live preview rolled back");
        }

        [Test]
        public void FailedApplyStays()
        {
            var stack = new MenuStack();
            var page = new MenuSession(new VideoSettings(), _cache);
            stack.Push(page);
            page.Set("RefreshRate", 999);
            Assert.IsFalse(stack.Back(LeaveChoice.Apply));
            Assert.AreSame(page, stack.Current);
            Assert.IsFalse(page.IsClosed);
        }

        [Test]
        public void SwitchReplacesTheTab()
        {
            var stack = new MenuStack();
            var audio = new MenuSession(new AudioSettings(), _cache);
            var video = new MenuSession(new VideoSettings(), _cache);
            stack.Push(audio);
            audio.Set("Profile", "Pro");
            Assert.IsTrue(stack.Switch(video, LeaveChoice.Discard));
            Assert.AreSame(video, stack.Current);
            Assert.AreEqual(1, stack.Pages.Count);
            Assert.AreEqual("Default", ((AudioSettings)audio.Target).Profile);
        }

        [Test]
        public void CancelAllRevertsEveryPage()
        {
            var stack = new MenuStack();
            var a = new Recorder();
            var b = new Recorder();
            var pa = new MenuSession(a, _cache);
            var pb = new MenuSession(b, _cache);
            stack.Push(pa);
            stack.Push(pb);
            pa.Set("Volume", 0.1f);
            pb.Set("Volume", 0.2f);
            stack.CancelAll();
            Assert.AreEqual(0.5f, a.Volume);
            Assert.AreEqual(0.5f, b.Volume);
            Assert.IsTrue(stack.IsEmpty);
        }
    }

    public class DocumentTests
    {
        MenuSchemaCache _cache;

        [SetUp]
        public void SetUp() => _cache = new MenuSchemaCache();

        [Test]
        public void RoundTripInAnyCulture()
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // decimal comma
            try
            {
                var a = new AudioSettings { Master = 0.25f, MusicVolume = 7, Subtitles = false, Speakers = SpeakerMode.Surround51, Mute = Channels.Voice | Channels.Effects, Profile = "Say \"hi\"\n" };
                var text = SettingsDocument.Write(_cache.Get<AudioSettings>(), a, _cache.Converters);
                StringAssert.Contains("\"Master\": \"0.25\"", text, "invariant culture");
                var b = new AudioSettings();
                CollectionAssert.IsEmpty(SettingsDocument.Read(text, _cache.Get<AudioSettings>(), b, _cache.Converters));
                Assert.AreEqual(0.25f, b.Master);
                Assert.AreEqual(7, b.MusicVolume);
                Assert.IsFalse(b.Subtitles);
                Assert.AreEqual(SpeakerMode.Surround51, b.Speakers);
                Assert.AreEqual(Channels.Voice | Channels.Effects, b.Mute);
                Assert.AreEqual("Say \"hi\"\n", b.Profile);
            }
            finally { Thread.CurrentThread.CurrentCulture = culture; }
        }

        [Test]
        public void MissingUnknownOldAndBadKeys()
        {
            var text = "{ \"version\": 1, \"values\": { \"oldRes\": \"120\", \"Fullscreen\": \"maybe\", \"Gone\": \"x\", \"Graphics.Quality\": \"3\" } }";
            var v = new VideoSettings();
            var problems = SettingsDocument.Read(text, _cache.Get<VideoSettings>(), v, _cache.Converters);
            Assert.AreEqual(120, v.RefreshRate, "found under its old name");
            Assert.AreEqual(3, v.Graphics.Quality, "group keys");
            Assert.IsTrue(v.Fullscreen, "a bad value keeps the object's own");
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("maybe", problems[0]);
            Assert.AreEqual("1920x1080", v.Resolution, "missing keys keep the object's own");
        }

        [Test]
        public void DontSaveIsNotWritten()
        {
            var text = SettingsDocument.Write(_cache.Get<VideoSettings>(), new VideoSettings { Preview = true }, _cache.Converters);
            StringAssert.DoesNotContain("Preview", text);
            StringAssert.Contains("\"res\": \"60\"", text, "[SettingKey]");
        }

        [Test]
        public void NotADocument()
        {
            var problems = SettingsDocument.Read("{ nope", _cache.Get<VideoSettings>(), new VideoSettings(), _cache.Converters);
            Assert.AreEqual(1, problems.Count);
        }

        [Test]
        public void LoadFromStore()
        {
            var store = new InMemorySettingsStore();
            var a = new AudioSettings();
            var s = new MenuSession(a, _cache, store);
            s.Set("MusicVolume", 2);
            s.Apply();
            var fresh = new AudioSettings();
            SettingsDocument.Load(store, _cache, fresh);
            Assert.AreEqual(2, fresh.MusicVolume);
        }
    }
}
