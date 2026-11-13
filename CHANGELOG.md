# Changelog

## [0.0.1] - preview (UI Toolkit views; uGUI views to come)

### Added
- Core model: attributes, schema reflection (including Unity's attributes), value converters, `MenuSession` (Apply, Revert, Cancel, live preview, validation, conditions, dirty state), `MenuStack`, `SettingsDocument` and `ISettingsStore`.
- Stores: `JsonFileStore`, `PlayerPrefsStore`, `InMemorySettingsStore`; `Settings<T>` (load, save, edit).
- `.um-page` frame kind on UI Frames (`MenuPageKind`, `MenuPageFrame`): rows are cards of a vertical strip; Left/Right change a row, Enter acts on it.
- `MenuView`: what the UI views share (rows kept in step with the session, conditions removing rows, confirm buttons, links as pages, the "Apply, discard or stay?" question).
- UI Toolkit views (`Threads.UiMenus.Toolkit`): `ToolkitMenus` (`For(runner, …)`, `Open`) and `ToolkitMenuView`, with `UiMenus.uss`.
