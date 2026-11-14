# UI Menus

Menus and settings screens generated from your own C# classes: fields, properties and methods, described with attributes (and Unity's own `[Range]`, `[Tooltip]`, `[Header]`…), become pages of rows. Each object is edited as a copy; **Apply** writes it back and saves it, **Revert** returns to the last applied values, **Cancel** reverts and closes. Views for UI Toolkit and uGUI are built on UI Frames.

Also: JSON or PlayerPrefs saving, live preview, validation, conditions and links between pages. Input comes only through Input System actions (rebinding is the Input System's job).

- Usage: [`Documentation~/Usage.md`](Documentation~/Usage.md)
- Demo: Package Manager › UI Menus › Samples › Demo
- Status: preview. Plan: `Documentation~/plan.md`.
