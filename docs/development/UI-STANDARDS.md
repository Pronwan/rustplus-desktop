# UI standards

The desktop app follows Fluent UI design principles and is built with `Wpf.Ui`
components.

- Use `Wpf.Ui` controls (`ui:Button`, `ui:TextBox`, `ui:SymbolIcon`, `ui:Card`,
  `ui:ToggleSwitch`, …) for all windows, modals, dialogs, lists, cards and their
  subcomponents. Do not use unstyled WPF controls such as a bare `Button`, a raw
  `ListBox` or a default `Window`.
- Modals and dialogs use dark card containers (`#141820` / `Surface`), dark
  borders (`#252F3D`), rounded corners (`CornerRadius` 12–14) and a drop shadow
  (`DropShadowEffect BlurRadius="20" Opacity="0.75"`).
- List items and interactive rows are Fluent cards with icon badges, a clear
  typography hierarchy, hover states and a hand cursor.
- Use `Wpf.Ui.Controls.SymbolIcon` with `SymbolRegular` glyphs for all icons.
