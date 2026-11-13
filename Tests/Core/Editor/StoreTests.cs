using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Threads.UiMenus.Tests
{
    public class StoreTests
    {
        string _dir;
        MenuSchemaCache _cache;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "UiMenusTests_" + Guid.NewGuid().ToString("N"));
            _cache = new MenuSchemaCache();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [Test]
        public void JsonFileStoreRoundTrip()
        {
            var store = new JsonFileStore(_dir);
            Assert.IsFalse(store.TryLoad("audio", out _));
            store.Save("audio", "{ \"version\": 1, \"values\": {} }");
            store.Save("audio", "second"); // replace an existing file
            Assert.IsTrue(store.TryLoad("audio", out var text));
            Assert.AreEqual("second", text);
            Assert.IsFalse(File.Exists(store.PathOf("audio") + ".tmp"), "no temporary file left");
            StringAssert.EndsWith("a_b.json", store.PathOf("a/b"), "unsafe characters replaced");
        }

        [Test]
        public void PlayerPrefsStoreRoundTrip()
        {
            var prefix = "uimenus.test." + Guid.NewGuid().ToString("N") + ".";
            var store = new PlayerPrefsStore(prefix);
            try
            {
                Assert.IsFalse(store.TryLoad("audio", out _));
                store.Save("audio", "text");
                Assert.IsTrue(store.TryLoad("audio", out var text));
                Assert.AreEqual("text", text);
            }
            finally { PlayerPrefs.DeleteKey(prefix + "audio"); }
        }

        [Test]
        public void SettingsLoadEditApplySave()
        {
            var store = new JsonFileStore(_dir);
            var first = new Settings<AudioSettings>(store, _cache);
            Assert.IsFalse(first.Load(), "nothing saved yet");
            var session = first.Edit();
            session.Set("MusicVolume", 1);
            session.Apply();

            var second = new Settings<AudioSettings>(store, new MenuSchemaCache());
            Assert.IsTrue(second.Load());
            Assert.AreEqual(1, second.Value.MusicVolume, "read back from the file");
        }

        [Test]
        public void ProblemsAreLoggedAndValuesKept()
        {
            var store = new InMemorySettingsStore();
            store.Save("audio", "{ \"version\": 1, \"values\": { \"MusicVolume\": \"loud\" } }");
            var settings = new Settings<AudioSettings>(store, _cache);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("loud"));
            Assert.IsTrue(settings.Load());
            Assert.AreEqual(5, settings.Value.MusicVolume);
        }
    }
}
