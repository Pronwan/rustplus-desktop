# Accepted layout direction — 2026-10-16

Jawad supplied `reference/layout-reference.png` as the visual layout to preserve in the new Avalonia
theme. This supersedes the initial preview's large header and Devices/Recycler tab arrangement.

- Keep the narrow navigation rail at the far left, then the server/connection/device panel.
- Keep the main map workspace to the right, with its tools across the top and activity/logs below it.
- Keep a compact application header and a thin status bar along the bottom.
- Preserve the original navigation order and placement as each shipped tool becomes available.
- Use FluentAvalonia Windows 11 controls on Windows and platform conventions elsewhere; the screenshot
  specifies the arrangement, not a replacement set of hand-drawn control templates.
- Respond to window size without overlapping panels, stretching controls excessively or hiding actions
  beyond the minimum supported width. Keep lists independently scrollable and toolbars wrapping.

## Implemented foundation

The shell now composes `NavigationRail`, `ServerDevicePane` and `MapWorkspace` with the existing
feature views. Minimum size is 1000×700 logical pixels; default size is 1380×950. There is no maximum
window width. The rail is 60 pixels; the side panel scales with the window between 300 and 480 pixels.
Its divider supports pointer dragging and focused Left/Right keys. The rail toggle collapses/reopens
the panel. Devices reopens it; Recycler keeps it available. The map fills the remaining width.
The activity divider resizes its panel between 90 and 400 pixels, preserving the map's minimum area.
Server selection uses a compact dropdown, device actions wrap, and device names wrap inside the tree.
Native window decorations and system light/dark mode remain enabled.

The activity panel displays the existing safe pairing feed and health status. Connection and pairing
commands are real. Live map rendering, map toolbars/layers, full log filtering/expansion, team/shop
docks, remaining navigation entries, device search/filter and legacy footer actions are still pending.
Do not add inert buttons merely to make a screenshot look complete. This foundation does not establish
full visual or shipped-feature parity.

## Verification

Headless rendered checks cover 1000×700, 1380×1000 and 2560×1440; collapse/reopen, keyboard divider
resizing, changing to minimum size, long names in light mode, and Recycler navigation with the device
panel present. Existing connected-device commands, account overlays and Recycler interaction checks
remain required. These are Windows-host renders, not native Linux/macOS or physical display/DPI acceptance.
Generated `ui-layout-*.png` files are under the test output folder and contain synthetic server data.

## Next layout work

Port the real map and its tools into the reserved map area; integrate full logs, team/shop docks and
the remaining original navigation/actions without changing this panel arrangement. Run native visual
acceptance at the minimum size, maximized, and at common desktop scaling factors on each target OS.
