# Recycler port evidence — 2026-10-16

Reference: `RustPlusDesktop/Views/MainWindow/Map/RecyclerOverlay.xaml(.cs)`,
`MainWindow.xaml`'s `RecyclerCalculatorTab`, and `MainWindow.Map.Recycler.cs`.
The portable implementation separates the calculation, bundled catalog and feature presentation.

- All 776 recyclable inputs remain available. Detailed wild/safe-zone yields and stack sizes take
  precedence; legacy guaranteed/percentage outputs remain the parser fallback.
- Expected, guaranteed, maximum and weighted-chance values follow the reference calculation.
  Each tick processes `ceil(stackSize × 10%)` units, at five seconds wild/eight seconds outpost.
- Search matches names/short names; category filtering retains hidden inventory. The initial 30-item
  page expands on scrolling or More items. Fill stacks affects rendered items; Clear all resets all inputs.
- Left/right click, wheel and Shift×10 shortcuts coexist with validated numeric input. Closing returns
  to devices and reopening retains the calculator state. The server sidebar remains available.
- The 796 required input/output icons are copied unchanged from the existing `Assets/icons/items`
  pack and renamed by short name. Selection uses the reference SHA-1 key for its optimized/original
  RustHelp URL. Every required icon has been decoded by the portable UI test; no downloader is needed.
- Quantity arithmetic avoids integer wraparound. Processing-time hours do not wrap after 24 hours.
  Bulk clear/fill recalculates once instead of once per item.

Unchanged source data SHA-256:

| Asset | Hash |
| --- | --- |
| recycler-items.json | B8B43C908228D727F0A4AEBE85D183506C9EC46854CDF50ED44BEC101A6DC078 |
| Recycling-Data.json | 42668C49CE8F83640CBCFB6CDE78A266CF5F65D9B163535CEC7AED49FD2BEC28 |

Golden checks cover wolf-headdress guaranteed/probabilistic yields, mixed-input accumulation, tick
rounding, negative/zero and large counts. UI checks exercise real typed bindings, numeric entry,
click/wheel shortcuts, Escape, reopen state and minimum window bounds without network access.

The remaining original navigation, tutorial targets, localization and native OS visual/accessibility
acceptance remain required. This port does not establish full application feature or visual parity.
