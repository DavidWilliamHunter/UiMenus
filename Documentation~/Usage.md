# UI Menus: usage

UI Menus turns your own C# classes into menus and settings screens. Fields, properties and methods, described with attributes (Unity's own `[Range]`, `[Tooltip]`, `[Header]` and others work too), become pages of rows. Each object is edited as a copy:

- **Apply** checks the values, writes them to the object and saves them.
- **Revert** returns to the last applied values.
- **Cancel** reverts and closes the page.

Views exist for **UI Toolkit** and **uGUI**. Both are built on **UI Frames**, so mouse, touch, keyboard and gamepad all work.

**Requirements:**
- Unity 6000.3 or later.
- The New Input System.
- `com.threads.uiframes` 0.1.4 or later.

## 1. Install

Add the packages to `Packages/manifest.json`. Unity only reads Git URLs from the project's manifest, so UI Frames must be listed as well:

```json
"com.threads.uiframes": "https://github.com/DavidWilliamHunter/UiFrames.git#v0.1.4",
"com.threads.uimenus": "https://github.com/DavidWilliamHunter/UiMenus.git#v0.0.3",
```

Set *Project Settings > Player > Active Input Handling* to **Input System Package (New)** or **Both**.

## 2. Quick start

Write the settings as an ordinary class:

```csharp
[MenuPage("Audio", Key = "audio")]
public class AudioSettings
{
    [Range(0, 1), LivePreview, Unit("%")] public float Master = 0.8f;
    [Range(0, 10)] public int Music = 7;
    [Header("Speech"), Tooltip("Show subtitles")] public bool Subtitles = true;
    [ShowIf(nameof(Subtitles)), Range(12, 32)] public int SubtitleSize = 18;
    public SpeakerMode Speakers;
}
```

Then make the pieces in your bootstrap and pass them where they are needed. Nothing in the package is global.

```csharp
var schemas = new MenuSchemaCache();
var store = new JsonFileStore(UiMenusHost.DefaultSettingsDirectory);   // or PlayerPrefsStore
var audio = new Settings<AudioSettings>(store, schemas);
audio.Load();                                                          // at startup

// UI Toolkit: the document must be one of the runner's documents.
var menus = ToolkitMenus.For(runner, schemas, uiMenusUss);             // UiMenus.uss from the package
var view = menus.Open(document.rootVisualElement, audio.Value, store);

// uGUI: the parent must be under one of the runner's uGUI roots, and the scene needs an EventSystem.
var menus = UguiMenus.For(runner, schemas);
var view = menus.Open(canvasRect, audio.Value, store);

view.Closed += () => { /* the menu removed itself */ };
```

The game reads `audio.Value` as usual. Apply writes to it and saves it through the store.

## 3. Which members become rows

Members are shown in declaration order, base classes first. `[Order(n)]` moves a member.

| Member | Shown? |
|---|---|
| public field, or public property with a getter and a setter | yes |
| non-public field with `[SerializeField]` or `[MenuEntry]` | yes |
| read-only property, or `readonly` field, with `[MenuEntry]` | yes, as information |
| method with `[MenuButton]` (no parameters) | as a button |
| `[HideInInspector]`, `[MenuHidden]`, `[NonSerialized]`, static, const | no |

### Controls by type

| Type | Control | Keys on the row |
|---|---|---|
| `bool` | toggle | Enter or Left/Right flips it |
| number with `[Range]` | slider and value | Left/Right step it (`[Step]`; 1 for integers, otherwise a twentieth of the range) |
| number without a range | number field | Enter goes into the field |
| `enum` | ‹ choice › | Left/Right, or Enter for the next value |
| `[Flags] enum` | one toggle per flag | Enter goes in; Enter toggles |
| `string` | text field | Enter goes in and types |
| `string` with `[Options(nameof(Method))]` | ‹ choice › | as for an enum |
| `[Serializable]` class or struct | its own rows, under its name as a heading | — |
| `[MenuLink]` field or property | opens that object as a page of its own | Enter |
| `[MenuButton]` method | button | Enter |

### Attributes

| Attribute | Meaning |
|---|---|
| `[MenuPage("Title", Key = "audio", Order = 1)]` | the page title, the save key (by default the type name), and the order |
| `[Label("Text")]` | the row label (by default the member name split into words) |
| `[Tooltip]`, `[Header]` (Unity) | help text, and a heading above the row |
| `[Range]`, `[Min]` (Unity), `[Step(0.5)]`, `[Unit("%")]` | limits, the step for Left/Right, and the unit shown (`%` on a 0–1 range shows as a percentage) |
| `[Multiline]`, `[TextArea]` (Unity) | a multi-line text field |
| `[LivePreview]` | also writes the object while the value is edited (volume, brightness). Revert and Cancel put it back. |
| `[ShowIf(nameof(Other))]`, `[ShowIf(nameof(Other), value)]` | the row is only shown while the condition holds (with the edited values). Hidden rows are taken out of the page, so keys skip them. |
| `[Options(nameof(Method))]` | the choices for a string (a method, property or field returning strings) |
| `[RequiresRestart]` | after Apply, the menu says a restart is needed |
| `[MenuValidate] string Check(IMenuValues v)` | runs at Apply; a non-null message stops the Apply and is shown |
| `[MenuButton(Confirm = "Press again to …")]` | a button; with `Confirm`, the first press asks |
| `[MenuLink]` | opens the member's object as its own page (its own Apply, Revert and Cancel) |
| `[SettingKey("name")]`, `[FormerlySerializedAs("old")]` (Unity) | rename the saved key, and read old saves |
| `[DontSave]` | edited but not saved |
| `[MenuEntry]`, `[MenuHidden]`, `[Order(n)]` | include, exclude, reorder |

## 4. Apply, Revert, Cancel

- **One session per object.** Each page edits one object in its own `MenuSession`, so Apply, Revert and Cancel act on that object only.
  - Nested `[Serializable]` members belong to their parent's session.
  - A `[MenuLink]` opens the linked object's own session.
- **Apply:**
  1. Runs the `[MenuValidate]` methods; any message stops the Apply.
  2. Writes the changed values to the object, through setters in page order.
  3. Saves through the store and raises `Applied`.
  - Apply is enabled only when something changed.
- **Revert** returns to the last applied values, including `[LivePreview]` values already written to the object.
- **Cancel** reverts and leaves the page.
- **Leaving a page with changes** asks *Apply your changes?* with Apply, Discard and Stay. Esc on the question means Stay.
- **From code:** use `MenuSession` (`Set`, `Step`, `Apply`, `Revert`, `Cancel`) and `MenuStack` (pages, `Back`).

## 5. Keys, mouse and touch

A page is a UI Frames frame (`um-page`, a vertical strip whose cards are the rows), so it follows the UI Frames levels:

- **Up/Down** move between rows; the page scrolls to show them.
- **Left/Right** change the row: a slider steps, a choice moves, a toggle flips.
- **Enter:**
  - flips a toggle, steps a choice, presses a button or opens a link;
  - goes into a text, number or flags row (Esc comes back out).
- **Esc:** the first Esc leaves the rows. A second Esc at the menu level goes back a page, asking about unapplied changes; on the last page it closes the menu.
- **Tab** at the menu level reaches Apply, Revert and Cancel.
- **Mouse and touch** use the controls directly. Dragging the page scrolls it.
- **Gamepad:** the d-pad and stick act as the arrows. Each controller's own confirm and back buttons act as Enter and Esc (A and B on Xbox, Cross and Circle on PlayStation, A on the right and B at the bottom on Nintendo). UI Frames binds them by the Input System's `{Submit}` and `{Cancel}` usages, as Unity's own UI actions do.

Row controls that the arrows already drive (sliders, toggles, choice arrows, buttons) are tagged `uf-pointer-only`. That makes each row a single keyboard stop.

## 6. Saving

- **Stores:** `ISettingsStore` has two methods, `TryLoad(key, out text)` and `Save(key, text)`. The package ships:
  - `JsonFileStore(folder)`: one `<key>.json` per object, written atomically. `UiMenusHost.DefaultSettingsDirectory` is `persistentDataPath/Settings`.
  - `PlayerPrefsStore(prefix)`.
  - `InMemorySettingsStore`.
- **`Settings<T>`** keeps an object together with its store: `Load()` at startup, `Save()`, and `Edit()` to make a session.
- **Format:** `{ "version": 1, "values": { "Music": "7", "Graphics.Shadows": "true" } }`. Values are invariant-culture text, keyed by member path.
- **Forgiving reads:**
  - A missing key keeps the object's own value.
  - An unreadable value is kept as it is and reported as a warning.
  - Unknown keys are ignored.
  - Old names given with `[FormerlySerializedAs]` are still found.
- **Other value types:** add your own with `schemas.Converters.Add<T>(write, read)`.

## 7. Styling

- **UI Toolkit:**
  - `UiMenus.uss` holds the defaults. Override them in your own sheets.
  - Classes: `um-menu`, `um-title`, `um-page`, `um-row`, `um-row--<kind>` (for example `um-row--slider`), `um-row--dirty`, `um-row__label`, `um-row__control`, `um-row__value`, `um-confirm`, `um-messages`, `um-bar`, `um-question`.
  - Rows also get the UI Frames state classes: `uf-selected`, `uf-entered`.
- **uGUI:**
  - Menus are built in code with uGUI's default controls and the built-in font, so no TextMeshPro setup is needed.
  - `UguiMenuStyle` sets the font, sizes and colours.
  - Rows carry the same classes on their `UiFrameClasses`, and get the UI Frames state visuals (or your own `UiFrameStateVisual`).

## 8. Extending

- **`MenuView`** is the shared view logic. A new UI system implements its abstract members.

## 9. The demo

1. In Package Manager, select UI Menus › Samples › **Demo** › Import.
2. Open `UiMenusToolkitDemo` or `UiMenusUguiDemo`. Rebuild them with *Threads > UI Menus > Build Demo Scenes* if needed.

The demo has a main menu (Play, Audio, Video, Controls, Gameplay, Quit), generated from `DemoSettings.cs`:
- **Audio:** live master volume, and a subtitle size shown only while subtitles are on.
- **Video:** properties that set Unity's own screen, quality and v-sync settings.
- **Controls:** look inversion and sensitivity (plain settings).
- **Gameplay:** a choice, flags, text, a number, information, a confirm button and validation.

Settings are saved in `persistentDataPath/Settings/UiMenusDemo`. With the menu closed, the status line shows the Jump binding and reacts to it. The game's *Pause Menu* action (Esc or Start by default, an ordinary Input System action) opens the menu again.

## 10. Input and standards

**Input comes only through Input System actions.** UI Menus never reads a device itself:
- UI Toolkit keys and gamepad come through UI Frames' navigation actions (`UiFramesNavigation.inputactions`, or your own asset on the runner).
- uGUI keys and gamepad come through the EventSystem's `InputSystemUIInputModule` actions.
- Mouse and touch come through the UI systems' own pointer events.

Rebinding is therefore the Input System's job (binding overrides), and UI Menus does not do it. Put "open the pause menu" in your game's actions, as the demo's *Pause Menu* is, so it can be rebound like any other control. The menu's own navigation actions are separate, so rebinding the game cannot lock a player out of the menu.

UI Menus follows these accepted standards for menus:

- **[Xbox Accessibility Guideline 107: Input](https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/107):**
  - **Menus usable with single, non-simultaneous presses (sliders included, without holding):** a row is one stop, and Left/Right adjust it.
  - **Pointer actions on the up event, with a way to back out:** clicks act on release (uGUI and UI Toolkit buttons).
  - **Keyboard alone can start, change settings, play and quit:** yes.
  - **Per-stick inversion and sensitivity (±50 % or more):** these belong in your settings class, as the demo's *Invert Look* and *Look Sensitivity* do.
- **Platform UI conventions:** confirm and back follow each controller (Nintendo's A and B are swapped against Xbox), through the `{Submit}` and `{Cancel}` usages.

## 11. Limits

- `[ShowIf]` compares with `Equals`, so there are no ranges or expressions.
- Labels are not localised yet.
