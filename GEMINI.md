## graphify

This project has a knowledge graph at .cache/graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "[question]" --graph .cache/graphify-out/graph.json` when the cached graph exists. Use `graphify path "[nodeA]" "[nodeB]" --graph .cache/graphify-out/graph.json` for relationships and `graphify explain "[concept]" --graph .cache/graphify-out/graph.json` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If .cache/graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read .cache/graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- The cached graph predates the source-folder reorganization. Refresh it before relying on file paths; keep generated analysis output under `.cache/`.

## Fluent UI & Component Standards

- Always use **Fluent UI design principles and `Wpf.Ui` components** (`ui:Button`, `ui:TextBox`, `ui:SymbolIcon`, `ui:Card`, `ui:ToggleSwitch`, etc.) for all windows, modals, dialogs, lists, cards, and subcomponents.
- Do NOT use unstyled WPF controls (like bare `Button`, raw `ListBox`, default `Window`).
- Modals and dialogs must feature dark card containers (`#141820` / `Surface`), dark borders (`#252F3D`), rounded corners (`CornerRadius="12"` to `"14"`), and drop shadows (`DropShadowEffect BlurRadius="20" Opacity="0.75"`).
- List items and interactive rows must be designed as sleek Fluent cards with icon badges, typography hierarchy, smooth hover states, and hand cursors.
- Use `Wpf.Ui.Controls.SymbolIcon` with `SymbolRegular` glyphs for all iconography.

