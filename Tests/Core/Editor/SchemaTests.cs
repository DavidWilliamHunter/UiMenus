using System.Linq;
using NUnit.Framework;

namespace Threads.UiMenus.Tests
{
    public class SchemaTests
    {
        MenuSchemaCache _cache;

        [SetUp]
        public void SetUp() => _cache = new MenuSchemaCache();

        string[] Names(MenuSchema s) => s.Entries.Select(e => e.Name).ToArray();

        [Test]
        public void PageTitleKeyAndOrder()
        {
            var s = _cache.Get<AudioSettings>();
            Assert.AreEqual("Audio", s.Title);
            Assert.AreEqual("audio", s.Key);
            Assert.AreEqual(2, s.Order);
            var v = _cache.Get<VideoSettings>();
            Assert.AreEqual("Video Settings", v.Title, "no [MenuPage]: the type name");
            Assert.AreEqual("VideoSettings", v.Key);
        }

        [Test]
        public void MembersFollowTheRulesAndOrder()
        {
            CollectionAssert.AreEqual(new[]
            {
                "Enabled", // [Order(-1)]
                "Master", "MusicVolume", "Subtitles", "Speakers", "Mute", "Profile", "Delay",
                "m_privateSerialized", // [SerializeField]
                "Version",             // read-only property with [MenuEntry]
                "ResetAll",            // [MenuButton]
            }, Names(_cache.Get<AudioSettings>()));
        }

        [Test]
        public void KindsFromTypesAndAttributes()
        {
            var s = _cache.Get<AudioSettings>();
            MenuEntryKind K(string n) => s.Find(n).Kind;
            Assert.AreEqual(MenuEntryKind.Slider, K("Master"));
            Assert.AreEqual(MenuEntryKind.Slider, K("MusicVolume"));
            Assert.AreEqual(MenuEntryKind.Toggle, K("Subtitles"));
            Assert.AreEqual(MenuEntryKind.Choice, K("Speakers"));
            Assert.AreEqual(MenuEntryKind.Flags, K("Mute"));
            Assert.AreEqual(MenuEntryKind.Text, K("Profile"));
            Assert.AreEqual(MenuEntryKind.Number, K("Delay"));
            Assert.AreEqual(MenuEntryKind.Info, K("Version"));
            Assert.AreEqual(MenuEntryKind.Button, K("ResetAll"));
            var v = _cache.Get<VideoSettings>();
            Assert.AreEqual(MenuEntryKind.Choice, v.Find("Resolution").Kind, "a string with [Options]");
            Assert.AreEqual(MenuEntryKind.Group, v.Find("Graphics").Kind);
            Assert.AreEqual(MenuEntryKind.Link, v.Find("AudioLink").Kind);
            Assert.IsNull(v.Find("Audio"), "a non-[Serializable] object without [MenuLink] is left out");
        }

        [Test]
        public void DetailsFromAttributes()
        {
            var s = _cache.Get<AudioSettings>();
            var master = s.Find("Master");
            Assert.AreEqual(0d, master.Min);
            Assert.AreEqual(1d, master.Max);
            Assert.AreEqual("%", master.Unit);
            Assert.IsTrue(master.LivePreview);
            Assert.AreEqual("Show subtitles for speech", s.Find("Subtitles").Help);
            Assert.AreEqual("Speech", s.Find("Subtitles").Header);
            Assert.AreEqual("Profile name", s.Find("Profile").Label);
            Assert.AreEqual(0d, s.Find("Delay").Min);
            Assert.IsNull(s.Find("Delay").Max);
            Assert.AreEqual("Really?", s.Find("ResetAll").Confirm);
            Assert.AreEqual("Private Serialized", s.Find("m_privateSerialized").Label);
        }

        [TestCase("masterVolume", "Master Volume")]
        [TestCase("m_MasterVolume", "Master Volume")]
        [TestCase("_fieldOfView", "Field Of View")]
        [TestCase("HDRMode", "HDR Mode")]
        [TestCase("player2Name", "Player 2 Name")]
        [TestCase("snake_case_name", "Snake case name")]
        public void Nicify(string name, string label) => Assert.AreEqual(label, MenuSchemaCache.Nicify(name));

        [Test]
        public void GroupsHaveRowsAndPathKeys()
        {
            var v = _cache.Get<VideoSettings>();
            var g = v.Find("Graphics");
            CollectionAssert.AreEqual(new[] { "Shadows", "Quality" }, g.Children.Select(c => c.Name).ToArray());
            Assert.AreEqual("Graphics.Shadows", g.Children[0].Path);
            Assert.AreEqual("Graphics.Shadows", g.Children[0].Key);
            Assert.AreSame(g.Children[1], v.Find("Graphics.Quality"));
            CollectionAssert.Contains(v.Values.Select(e => e.Path).ToArray(), "Window.Width");
        }

        [Test]
        public void SaveKeys()
        {
            var v = _cache.Get<VideoSettings>();
            var rate = v.Find("RefreshRate");
            Assert.AreEqual("res", rate.Key);
            CollectionAssert.AreEqual(new[] { "oldRes" }, rate.OldKeys);
            Assert.IsFalse(v.Find("Preview").Saved);
        }

        [Test]
        public void SchemasAreBuiltOnce()
        {
            Assert.AreSame(_cache.Get<AudioSettings>(), _cache.Get(typeof(AudioSettings)));
            Assert.AreNotSame(_cache.Get<AudioSettings>(), new MenuSchemaCache().Get<AudioSettings>(), "per cache, not global");
        }
    }
}
