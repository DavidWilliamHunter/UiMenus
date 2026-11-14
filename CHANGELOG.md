# Changelog

## [0.0.3] - preview

### Added
- Demo sample: a main menu with Audio, Video, Controls and Gameplay settings in UI Toolkit and uGUI (Threads > UI Menus > Build Demo Scenes). The menu opens with the game's *Pause Menu* action (Esc / Start by default), an ordinary Input System action.
- `Documentation~/Usage.md`, including the input routes and the menu standards followed.

### Changed
- UI Toolkit: readable dark buttons, fields and toggles in `UiMenus.uss`. uGUI: inset toggle marks.
- Requires UI Frames 0.1.4 (gamepad confirm/back by `{Submit}`/`{Cancel}`, uGUI double-action fix).

### Not included
- Key rebinding: it belongs to the Input System side, not to menus. A separate package is being planned (`com.threads.inputrebinding`).

## [0.0.2] - preview

### Added
- uGUI views (`Threads.UiMenus.UGUI`): `UguiMenus` (`For(runner, …)`, `Open`) and `UguiMenuView`, built in code with uGUI's default controls and the built-in font; `UguiMenuStyle` for font, sizes and colours. Esc at the top level reaches the menu through `FrameNavigator.TopLevelCancel`.
- `MenuText.Format` (shared value formatting).

## [0.0.1] - preview (UI Toolkit views; uGUI views to come)

### Added
- Core model: attributes, schema reflection (including Unity's attributes), value converters, `MenuSession` (Apply, Revert, Cancel, live preview, validation, conditions, dirty state), `MenuStack`, `SettingsDocument` and `ISettingsStore`.
- Stores: `JsonFileStore`, `PlayerPrefsStore`, `InMemorySettingsStore`; `Settings<T>` (load, save, edit).
- `.um-page` frame kind on UI Frames (`MenuPageKind`, `MenuPageFrame`): rows are cards of a vertical strip; Left/Right change a row, Enter acts on it.
- `MenuView`: what the UI views share (rows kept in step with the session, conditions removing rows, confirm buttons, links as pages, the "Apply, discard or stay?" question).
- UI Toolkit views (`Threads.UiMenus.Toolkit`): `ToolkitMenus` (`For(runner, …)`, `Open`) and `ToolkitMenuView`, with `UiMenus.uss`.
