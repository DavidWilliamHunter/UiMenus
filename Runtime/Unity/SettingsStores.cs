using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Threads.UiMenus
{
    /// <summary>The one place UI Menus reads Unity's globals.</summary>
    public static class UiMenusHost
    {
        /// <summary>Where <see cref="JsonFileStore"/> keeps settings by default:
        /// <c>Application.persistentDataPath/Settings</c>.</summary>
        public static string DefaultSettingsDirectory => Path.Combine(Application.persistentDataPath, "Settings");
    }

    /// <summary>
    /// One JSON file per settings object in a folder (<c>&lt;key&gt;.json</c>). Writes go to a
    /// temporary file that then replaces the old one, so a crash mid-save cannot leave half a file.
    /// </summary>
    public sealed class JsonFileStore : ISettingsStore
    {
        public JsonFileStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A folder is needed.", nameof(directory));
            Directory = directory;
        }

        public string Directory { get; }

        public string PathOf(string key)
        {
            var safe = new StringBuilder(key.Length);
            foreach (var c in key) safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return Path.Combine(Directory, safe + ".json");
        }

        public bool TryLoad(string key, out string text)
        {
            text = null;
            var path = PathOf(key);
            if (!File.Exists(path)) return false;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return true;
        }

        public void Save(string key, string text)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathOf(key);
            var temp = path + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }

    /// <summary>Keeps each settings object in PlayerPrefs under <c>&lt;prefix&gt;&lt;key&gt;</c> and saves PlayerPrefs at once.</summary>
    public sealed class PlayerPrefsStore : ISettingsStore
    {
        public PlayerPrefsStore(string prefix = "settings.")
        {
            Prefix = prefix ?? "";
        }

        public string Prefix { get; }

        public bool TryLoad(string key, out string text)
        {
            text = null;
            if (!PlayerPrefs.HasKey(Prefix + key)) return false;
            text = PlayerPrefs.GetString(Prefix + key);
            return true;
        }

        public void Save(string key, string text)
        {
            PlayerPrefs.SetString(Prefix + key, text);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// A settings object with its store: <see cref="Load"/> at startup (problems are logged and
    /// the object's own values kept), <see cref="Edit"/> to open a menu session whose Apply saves.
    /// Owned by your bootstrap and passed where needed, like the rest of the package.
    /// </summary>
    public sealed class Settings<T> where T : class, new()
    {
        public Settings(ISettingsStore store, MenuSchemaCache schemas, T value = null)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Schemas = schemas ?? throw new ArgumentNullException(nameof(schemas));
            Value = value ?? new T();
        }

        public ISettingsStore Store { get; }
        public MenuSchemaCache Schemas { get; }
        /// <summary>The settings in use (the game reads these).</summary>
        public T Value { get; }

        /// <summary>Loads saved values into <see cref="Value"/>. Returns false if nothing was saved yet.</summary>
        public bool Load()
        {
            var schema = Schemas.Get<T>();
            if (!Store.TryLoad(schema.Key, out var text)) return false;
            foreach (var problem in SettingsDocument.Read(text, schema, Value, Schemas.Converters))
                Debug.LogWarning("UI Menus: " + problem);
            return true;
        }

        /// <summary>Saves <see cref="Value"/> now (Apply in a session does this by itself).</summary>
        public void Save() => Store.Save(Schemas.Get<T>().Key, SettingsDocument.Write(Schemas.Get<T>(), Value, Schemas.Converters));

        /// <summary>A menu session on <see cref="Value"/>; its Apply saves to the store.</summary>
        public MenuSession Edit() => new MenuSession(Value, Schemas, Store);
    }
}
