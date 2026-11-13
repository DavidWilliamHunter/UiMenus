# Plan: menus and settings generated from annotated C# classes

## Context

The user needs a package for common menus and settings screens. The simplest route, as the user framed it: read a class's fields and properties, plus annotations, and turn them into a menu. The menu must support **Apply**, **Revert** and **Cancel**.

Decisions made with the user (2026-11-13):
- **Revert:** goes back to the last applied values. There is no reset-to-defaults and no undo history.
- **Editing:** the menu edits a copy, and Apply commits it. Fields marked `[LivePreview]` (volume, brightness) also update the real object while you edit; Cancel rolls them back.
- **UI:** UI Toolkit and uGUI, both built on **UI Frames**. Pages are frames, so mouse, touch, keyboard and gamepad work as they do elsewhere.
- **Saving:** pluggable stores (JSON file and PlayerPrefs). Apply saves.

The house rules apply:
- no singletons; explicit Startup/Shutdown;
- New Input System only; dp sizes;
- everything unit tested, with tests run on a scratch copy;
- commit or push only when asked; the no-reply email for commits.

**Name (confirmed):** `com.threads.uimenus`, namespace `Threads.UiMenus`, display name "UI Menus". It will be a new public repo, added as a submodule like the other packages.

## What a developer writes

```csharp
[MenuPage("Audio", Order = 1)]
public class AudioSettings
{
    [Range(0, 1), LivePreview, Unit("%")] public float Master = 0.8f;   // slider, heard as you move it
    [Range(0, 1), LivePreview] public float Music = 0.6f;
    public bool Subtitles = true;                                        // toggle
    [Label("Speaker setup")] public SpeakerMode Speakers;                // enum: a cycler (← →)
}

[MenuPage("Video")]
public class VideoSettings
{
    public bool Fullscreen { get; set; }                                 // properties work too
    [ShowIf(nameof(Fullscreen)), Options(nameof(Resolutions))] public string Resolution;
    [RequiresRestart] public QualityLevel Quality;
    string[] Resolutions() => ...;
    [Validate] string Check() => ...;                                    // null = valid, else a message
}

[MenuPage("Main")]
public class MainMenu
{
    [MenuButton] void Continue() { ... }                                 // methods become buttons
    [MenuLink(typeof(AudioSettings))] Settings;                          // opens another page
}
```

Unity's own attributes are read as well, so existing classes work unchanged: `[Range]`, `[Min]`, `[Tooltip]` (the help line), `[Header]` (groups), `[Multiline]`, `[HideInInspector]` (hidden), `[FormerlySerializedAs]` (renamed save keys).

## Architecture

| Assembly | Depends on | Contents |
|---|---|---|
| `Threads.UiMenus` (core, plain C#) | none | Attributes; `MenuSchema` (types reflected once into item definitions with compiled getters and setters, owned by a `MenuSchemaCache` instance, not a static); value converters (invariant culture); `MenuSession` (edit buffer, Apply, Revert, Cancel, dirty tracking, live preview, validation, `ShowIf` conditions); `MenuStack` (pages, Back); `SettingsDocument` (versioned key/value serialisation, renames, unknown keys ignored); the `ISettingsStore` interface |
| `Threads.UiMenus.Unity` | core | `JsonFileStore` (persistent data path, read through a host), `PlayerPrefsStore`, `Settings<T>` (owns an instance and a store; Load at startup, Save on Apply) |
| `Threads.UiMenus.Toolkit` | core, UI Frames | View generator: pages, rows and controls as VisualElements from overridable UXML templates; default USS |
| `Threads.UiMenus.UGUI` | core, UI Frames | View generator from a `MenuTheme` asset of row and control prefabs; defaults built with `UguiFrameFactory` |
| `Threads.UiMenus.Input` | core, Input System | Rebinding items: `InputActionReference` fields or `[Rebind]`, using `PerformInteractiveRebinding`. Binding overrides take part in Apply/Revert/Cancel and are saved through the store |

### Session semantics

- **Open:** read every item's value from the target into an edit buffer. Snapshot the last-applied values, which are the same at this point.
- **Edit:** write to the buffer and mark the row `um-dirty`. A `[LivePreview]` item also calls its setter on the target right away.
- **Apply:**
  1. Validate; on an error, report it on the page and stop.
  2. Write dirty items to the target in declared order.
  3. Save through the store.
  4. Raise `Applied` (with the `[RequiresRestart]` items that changed).
  5. Take a new snapshot.
- **Revert:** set the buffer back to the snapshot, restore live-preview values on the target, and stay on the page.
- **Cancel:** revert, then close. If something is dirty, an optional confirm step comes first: "Discard changes?"
- **Scope (confirmed): per object.** Each annotated target object has its own `MenuSession`, so Apply, Revert and Cancel act on that object only.
  - Nested `[Serializable]` members (groups, sub-pages) belong to their parent object's session.
  - A `[MenuLink]` to another object opens that object's own session.
  - A tabbed settings screen with one object per tab therefore applies each tab separately. Leaving a tab with unapplied changes asks "Apply, discard or stay?".
- **Back:** a sub-page goes back to its parent.

### Views

**Page layout**
- A page is a vertical UI Frames strip (`uf-card-strip`, `uf-vertical`), and each row is a card.
- The page selector is a tab strip.
- Apply, Revert and Cancel form a button bar; Apply is enabled only when something is dirty.

**Controls by type**

| Type | Control |
|---|---|
| bool | toggle |
| number with `[Range]` | slider |
| number without | field with steps |
| enum | cycler (← →) |
| `[Flags]` enum | toggles |
| string | text field, or a cycler with `[Options]` |
| `[MenuButton]` | button |
| `[MenuLink]` | opens a page |
| nested `[Serializable]` class | group or sub-page |
| binding | rebind row |

**Navigation follows game-menu conventions, using the UI Frames levels:**
- **Up/Down** moves between rows.
- **Left/Right** changes the row's value: it steps a slider, and moves a cycler or toggle.
- **Enter** toggles a toggle, presses a button, follows a link, or starts editing a text field.
- **Esc** means Back or Cancel.

This needs one small, backward-compatible addition to UI Frames. Today a cross-axis arrow at the cards level is used and does nothing. Instead, it will send a new `adjust` command (±1, with the selected card) to the frame. The menu page frame kind handles it; other frames ignore it, which is today's behaviour.

**Styling:** state classes `um-dirty`, `um-invalid` and `um-restart` sit alongside the UI Frames ones. Every template can be overridden: UXML/USS for UI Toolkit, the `MenuTheme` prefabs for uGUI.

**Labels:** `[Label]`, or by default the field name split into words. An `ILabelSource` hook allows localisation (Unity Localization optional, through `versionDefines`).

### Persistence

`SettingsDocument` holds `{ "version": n, "values": { "Audio.Master": "0.8", … } }`.
- Keys are `Page.Member`, and `[FormerlySerializedAs]` adds an old key.
- A missing value keeps the object's own initial value; an unparsable one keeps it too and logs a warning.
- Stores implement `ISettingsStore { bool TryLoad(string key, out string text); void Save(string key, string text); }`.
- It uses Unity's own JSON handling, so there is no Newtonsoft dependency.

## Phases

0. **Scaffold:** repo, submodule, asmdefs, no-reply email. Confirm the package name. Ask before creating the GitHub repo.
1. **Core model:** attributes, schema reflection (including Unity attributes), converters, `MenuSession` (Apply/Revert/Cancel, live preview, validation, ShowIf, dirty state), `MenuStack`, `SettingsDocument`. EditMode tests with no Unity UI.
2. **Stores and `Settings<T>`:** JSON file and PlayerPrefs stores through a host. Tests use a scratch path.
3. **UI Frames 0.1.2:** the cross-axis `adjust` command, with tests in UI Frames.
4. **UI Toolkit views:** the generator, default templates and USS. PlayMode tests with keyboard, mouse and touch through the existing UI Frames controllers.
5. **uGUI views:** the generator, the `MenuTheme` asset with default prefabs, and tests.
6. **Rebinding rows:** Input System rebinding, with overrides that take part in Apply/Revert/Cancel and are saved.
7. **Samples and docs:** a main menu plus Audio/Video/Controls/Gameplay settings in both UI systems, `Usage.md`, and a scratch demo walkthrough (as for UI Frames).

## Verification

- EditMode tests on the session semantics:
  - Apply writes and saves only dirty items.
  - Revert restores the last-applied values.
  - Cancel rolls back live preview.
  - Validation blocks Apply.
  - ShowIf follows the buffer.
  - Renamed keys load.
- PlayMode tests in both UI systems: generated rows for each type; Up/Down, Left/Right, Enter and Esc; Apply enabled only when dirty; mouse and touch on sliders and toggles; rebinding with Cancel.
- The demo scenes driven by a simulated keyboard and gamepad on the scratch copy.

## Confirmed (2026-11-13)

- **Name:** `com.threads.uimenus`, namespace `Threads.UiMenus`.
- **Apply/Revert/Cancel scope:** per object.

## Status (2026-11-13)

**Phase 0 (local only):** the scaffold, with local git set to the no-reply email. The GitHub repo is not created yet: that waits for the user's go-ahead.

**Phase 1, the core:** done, 39 tests.
- Attributes and schema rules, including Unity's attributes.
- Converters (invariant culture).
- `MenuSession`: Apply, Revert, Cancel, live preview, validation, ShowIf, Step, and structs inside groups.
- `MenuStack`, with Apply/discard/stay when leaving a page.
- `SettingsDocument`, with old keys and forgiving reads.
- Values are read and written by reflection, not compiled expressions, so it works under IL2CPP.

**Phase 2, saving:** done, 43 tests in total. `JsonFileStore` (atomic writes), `PlayerPrefsStore`, `Settings<T>`.

**Phase 3, UI Frames 0.1.2 (not committed):** done. `FrameCommandId.Adjust` carries cross-axis arrows to the frame. UI Frames: 166 EditMode and 58 PlayMode tests pass.

**Phase 4 design: two more small UI Frames additions, both backward compatible:**
- **`FrameCommandId.Activate`:** Enter on a card with nothing inside also sends this to the card's frame, as well as raising the navigator's `Activated` event. A menu row can then toggle, press a button or open a link with one Enter.
- **The `uf-pointer-only` class:** a control that the mouse and touch use but keyboard navigation skips. With it, a row's slider, toggle or choice arrows are not separate keyboard stops: the row is the stop, Left/Right adjusts it and Enter activates it. Text and number fields stay normal keyboard items (Enter goes in, then edits).

**Phase 4 view layout:** a `um-menu` holds a title, a `um-page` frame and a bar.
- **The page:** a UI Menus frame kind, a vertical strip whose cards are rows. It handles Adjust (step the row) and Activate (toggle, press or link).
- **Hidden rows:** rows hidden by ShowIf are removed from the content rather than set to `display: none`, so navigation never stops on them.
- **The bar:** Apply, Revert and Cancel, plus the error and restart lines.
