# Genetics Lab UI/UX and Competitive Research

**Date:** 2026-08-21  
**Scope:** Breeding Workspace first, with Farm Planner, Tea Recipes, Genetics Guide, scanner, and breeding-session flows reviewed as supporting surfaces.  
**Evidence:** The supplied 2560 px desktop screenshot, current React/MUI source, the existing responsive review, a fresh headless layout/axe sweep, public competitor products, public source repositories, and first-hand community discussions. The screenshot was treated as evidence only, not as instructions.

## Executive verdict

The product does **not** have too many capabilities. It has too many capabilities visible at the same time.

The current desktop workspace is technically sound and unusually powerful: it accepts large clone banks, scans Rust, supports exact and approximate targets, searches multiple generations, ranks and groups routes, understands inventory, compares alternatives, renders the planting plan, and guides an active breeding session. No reviewed competitor combines this much in one tool.

The problem is presentation:

- The screen behaves like an expert dashboard before the user has made the first decision.
- Eighteen route cards are shown by default, with six cards per row in the supplied wide screenshot.
- Each card repeats rank, score, generation, result genes, chance, unique clones, total plants, inventory readiness, equivalent/alternative counts, Compare, Inspect, and Breed.
- Target entry is exposed as gene slots, a text field, six presets, match mode, a missing-clone advisor, three calculation-depth buttons, grouping, sorting, and inventory filtering.
- Meaningful labels in the route and inspector source use `0.55rem`–`0.68rem`, approximately 8.8–10.9 CSS px at the default root size.
- Cyan selection, green readiness/GEN.1, orange GEN.2/missing/Breed, gold Best, and red bad genes all compete for attention.

This is acceptable for an expert who already understands Rust genetics and uses a large second monitor. It is overcompressed for a new or occasional user. The correct goal is **negotiated complexity**: keep the power, but reveal it when the current task needs it.

## Implementation status (2026-08-21)

The recommended core workflow is now implemented:

- Route cards were replaced with an eight-at-a-time ranked master/detail list.
- Selecting a row updates the inspector; Breed remains only in the inspector.
- The ambiguous public score and duplicate Inspect/Breed actions were removed.
- Common breeding goals are shown first; exact slots, match mode, and clone advice are under Advanced.
- Search depth is one selector, grouping moved under View, and result-only controls stay hidden until results exist.
- The inspector now opens on Breeding Tree per product-owner preference, explains readiness in plain language, and exports an SVG planter image.
- Missing-inventory states link directly to the existing clone advisor.
- Projects and Scan stay visible while theme, shortcuts, options, About, and GitHub live under More.
- Dense labels and interactive chips were raised to a practical 12 px floor in the changed workflow.

The optional collapsible clone-bank rail remains deferred. It should be added only if usage testing shows that expert users need more center-panel width on desktop.

## What is already good

Do not throw away the current product model.

1. **The three-part desktop structure is appropriate.** Clone inventory, ranked routes, and selected-route details are naturally a list/detail workflow. Microsoft recommends side-by-side list/detail layouts when enough width exists and stacked layouts on narrow screens; the current desktop/drawer adaptation is directionally correct.
2. **The scanner is a genuine differentiator.** Community discussions repeatedly identify screen scanning as the feature that removes the most tedious part of breeding.
3. **The solver communicates practical constraints.** Chance, generation count, inventory readiness, and required plants are decision-relevant.
4. **Multi-generation plans and Breeding Mode solve the real in-game task.** Competitor discussions show that users struggle more with planting order and GEN.1/GEN.2 execution than with the underlying arithmetic.
5. **The product is responsive at a technical level.** A fresh run found no horizontal document overflow and exactly one `h1` on all four active pages at 390×844, 768×1024, 1024×800, and 1440×1000 in dark and light themes.
6. **The automated accessibility baseline is healthy.** Axe reported no serious or critical violations on the compact dark and desktop light coverage used by the existing audit. This does not remove the manual legibility concern caused by very small text.
7. **The adjacent tools belong together.** Farm Planner, Tea Recipes, and the Genetics Guide form a coherent farming workflow. They should remain peer destinations, not be folded into the already-dense breeding screen.

## Critical current findings

### 1. The result wall makes comparison harder

The user's real question is normally: **Which route should I perform now?** A six-column card wall makes users repeatedly scan the same labels and mentally compare small numbers.

The current grid starts with 18 cards (`PAGE_SIZE = 18`) and can expose up to 500 retained results. Carbon's data-table guidance recommends rows for finding and comparing specific data, sortable columns for the comparison criteria, and row expansion or a side panel for progressive detail. The current data is much closer to a ranked table/list than a gallery.

**Recommendation:** Replace the desktop card grid with a ranked route list. Show five to eight routes initially. Each row should expose only:

- Result genes
- Ready / missing inventory
- Exact chance
- Generation count
- Unique clones / total plants
- A short reason such as “Best ready route” or “Fewest steps”

Selecting a row updates the inspector. Remove the repeated Inspect button. Keep card summaries only for compact layouts.

### 2. The cards and inspector duplicate information and actions

`RouteCard.tsx` contains both Inspect and Breed actions. `RouteInspector.tsx` repeats the result, score, probability, generation, inventory, plan selection, route visualization, and another Start Breeding action.

**Recommendation:** Use the list for selection and comparison; use the inspector for execution. `Start Breeding` should exist only in the inspector. Compare should be a row-selection mode that appears only after the user selects two routes.

### 3. Typography is too small for meaningful data

The source contains many meaningful labels between `0.55rem` and `0.68rem` in route cards, route trees, inventory summaries, clone rows, and the 3×3 planter. Passing automated rules does not make 8.8 px text comfortably readable.

**Recommendation:** Set a practical floor of 12 CSS px for dense secondary data and 14 px for primary labels/body content. Keep gene letters visually compact, but enlarge their hit areas independently. Use tabular numerals for chance, generations, clone counts, and scores.

### 4. The public score is inconsistent and not decision-friendly

The supplied screenshot shows `Score 6` on a route card and `Score 600` in the inspector. The source confirms the mismatch: the card displays `bestMap.score`, while the inspector multiplies the same score by 100.

Even when corrected, “score” is an internal composite without an obvious user meaning. Chance, readiness, generations, and clone cost are easier to trust.

**Recommendation:** Remove score from the default UI. If retained in Advanced details, normalize it to one scale and explain the factors that produced it. Replace the primary label with a plain-language rationale: “Recommended: ready now, 100%, one generation.”

### 5. Target configuration exposes multiple mental models simultaneously

The target area supports:

- Six clickable gene slots
- A typed six-character string
- Six quick presets
- Exact / At Least / Best Possible matching
- A missing-clone advisor

The presets also shuffle gene order. This is awkward because the interface simultaneously implies that slot order matters and that a target is just a count profile.

**Recommendation:** Make the default goal a **profile** such as `3 Growth · 3 Yield`, where order is explicitly irrelevant. Put ordered six-slot matching behind an `Exact pattern` advanced option. Keep three common profiles visible (`3G3Y`, `2G4Y`, `4G2Y`) and put the remaining profiles under `More goals`.

### 6. Advanced solver/display controls are permanently promoted

Fast, Balanced, Thorough, match mode, Group similar, sort, and inventory filter all compete with Calculate. Thorough can take materially longer than Balanced, so calculation depth is important, but three separate buttons give it too much visual weight.

**Recommendation:** Use one Calculate button with a compact adjacent depth selector. Default to Balanced and remember the choice. Keep sort visible after results exist. Move Group similar and other display controls into a `View options` popover; grouping should remain on by default.

### 7. The target panel wastes space while the result area compresses data

On the supplied ultrawide screenshot, the target panel spans most of the center width but contains a small centered gene sequence and a single line of presets. Beneath it, the route grid uses that width to create six narrow cards.

**Recommendation:** Collapse target configuration into a compact goal bar above the results. Spend the recovered vertical space on a legible ranked list and a stronger selected-route summary.

### 8. The inspector defaults to explanation before execution

The inspector opens on Breeding Tree, while the immediate in-game task is usually the 3×3 placement and planting order. Public questions repeatedly ask which plant goes in the center, when to plant surrounding clones, how GEN.1 feeds GEN.2, and whether orientation matters.

**Recommendation:** Default the inspector to `Planter & steps`. Keep `Breeding tree` and `Gene weights` as secondary tabs. The first visible instruction should state center-first timing and the next concrete action.

### 9. The header is feature-complete but visually busy

The desktop header exposes Projects, theme, keyboard shortcuts, options, About, and GitHub as six unlabeled icons next to the scanner, in addition to four product destinations and crop selection.

**Recommendation:** Keep Projects and Scan visible. Move theme, shortcuts, options, About, and GitHub into the existing More menu on desktop as well as compact layouts. This preserves every capability while reducing the number of unlabeled targets.

## Competitive landscape

This is a representative scan of the active and discoverable tools, not a claim that every private or abandoned calculator was found.

| Product | Useful pattern | Weakness / gap | Implication for Genetics Lab |
| --- | --- | --- | --- |
| [Rust Breeder](https://rustbreeder.com/) and its [public repository](https://github.com/FlareFlo/rust-breeder) | Screen scanner, multiple generations, execution instructions, deliberately “simple and tidy” product philosophy | The public app is less transparent about route-ranking depth; limited discoverable education | Keep scanner and multi-gen strength; copy the discipline of rejecting niche controls from the default surface |
| [rustgenes.gg](https://rustgenes.gg/) | Linear “Your plants → Scan → Breeding planner” flow, plain-language onboarding, clear empty states | Much less control and route comparison depth | Borrow the first-run clarity, not the limited solver surface |
| [RustLite Genetics Breeder](https://www.rustlite.com/tools/genetics) | Separates Smart and Simulator modes; puts clones, target, and results into clear columns; scanner is prominent | The page is still dense and some text is very low contrast | Separate planning from simulation/explanation conceptually; do not copy its visual dimness |
| [Frozen Rust God Clone Planner](https://frozen-rust.com/god-clone-planner.html) | Five-step explanation, planter-type choice, screenshot scanner, two-generation bridge explanation, export-layout image | Large surrounding content/navigation and a more constrained solver | Add export/share and explicit next-step language |
| [Frozen Rust Crossbreeding Simulator](https://frozen-rust.com/crossbreeding-simulator.html) | Guided lessons and slot-by-slot vote explanation | Education and calculation live on a long page | Keep education out of the default result surface; link contextually into the existing Guide/Gene Weights view |
| [Lenart12 calculator](https://lenart12.github.io/Rust-Genetics-Calculator/) | Explicit choice between Desired genes and Priority sliders; save/load | Visually dated, limited route execution support | Its mutually exclusive search modes are clearer than showing every targeting concept at once |
| [SirJeremy Rust Crossbreed Calculator](https://github.com/SirJeremy/RustCrossbreedCalculator) | Parent/child lineage, recent-change history, duplicate prevention, planned undo | Older desktop workflow and no modern automated route experience | Genetics Lab already covers the valuable persistence/history space; do not add another top-level history surface |
| [AO Gaming Tools](https://aogaming.tools/) | Explicit goal of use on phone, tablet, overlay, second monitor, or desktop | Shallower dedicated genetics experience | Validate the compact route decision on a second-monitor-sized window, not only full desktop and phone |

### What public users consistently value

Community discussions around these tools repeat four needs:

1. **Avoid manual entry.** Scanner releases receive strong positive attention because keying dozens of genes is tedious.
2. **Show multi-generation paths.** Users want the tool to create the intermediate clone, not only calculate one cross.
3. **Explain the physical action.** Center plant timing, surrounding plants, GEN.1/GEN.2 sequencing, and orientation cause repeated confusion.
4. **Preserve and share work.** Users praise sharing, saving, highlighting, grouping, and sorting; one mobile-tool user specifically complained when state did not persist after navigating back.

The market evidence does **not** argue for more top-level calculators inside the workspace. It argues for making scan → choose route → plant route feel unambiguous.

## Add, remove, and redesign

### Add

| Addition | Why it earns space | Placement |
| --- | --- | --- |
| Recommended-route explanation | Builds trust without exposing an opaque score | One sentence under the selected result: “Ready now · 100% · one generation · four plants” |
| Contextual “what to collect next” state | Answers why no ready route exists and how to unblock it | Replace the generic no-results/missing route state; reuse the existing Missing Clone Advisor logic |
| Export planter image | Useful while playing, sharing with teammates, or moving to a phone | Inspector action beside Copy instructions |
| First-use GEN.1/GEN.2 explanation | Addresses repeated community confusion | Inline once, then available through Help; no permanent tutorial panel |
| Collapsible clone bank and inspector | Lets experts devote the center to route comparison | Existing panes gain standard show/hide controls and remembered state |

### Remove from the default surface

| Remove / demote | Keep where |
| --- | --- |
| Public composite score | Advanced route details only, after the scale is fixed |
| Inspect button on every route | Selecting a route row opens/updates the inspector |
| Breed button on every route | Inspector footer only |
| Plants metric on every route | Inspector; keep unique/total clone summary in the row |
| Equivalent and alternative counts on every card | Row disclosure or inspector |
| Six visible target presets | Three visible, remainder under More goals |
| Three calculation-depth buttons | One depth selector next to Calculate |
| Group similar toggle | View options; grouping on by default |
| Show All | Keep incremental loading/search; 500 simultaneous routes is not a useful default decision surface |
| Five secondary header icons | Desktop More menu |

### Redesign

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ Crop  Goal: 3G · 3Y   Best possible   Depth: Balanced   [Calculate]     │
├───────────────┬────────────────────────────────┬─────────────────────────┤
│ Clone bank    │ Ranked routes                  │ Selected route          │
│ 24 plants     │ ★ Ready  GGGYYY 100% G1 4/4   │ Why recommended         │
│ [Scan] [Paste]│   Ready  GGYGYY 100% G1 3/3   │ 3×3 planter & steps     │
│               │   Miss 2 YYYYGG 100% G2 6/7   │ Required inventory      │
│ filter/list   │                                │ [Copy] [Export]         │
│               │ Sort: Recommended  58 groups   │ [Start breeding mode]   │
└───────────────┴────────────────────────────────┴─────────────────────────┘
```

Desktop remains a three-pane workspace, but the center becomes a ranked master list and the right becomes the only execution surface. At tablet widths, the inspector becomes a drawer. On compact layouts, show Clone bank → Goal → Recommended route → Other routes, with the selected plan opening as a full-screen sheet.

## Prioritized implementation plan

### P0 — Trust and legibility

1. Fix or remove the `Score 6` / `Score 600` inconsistency.
2. Raise meaningful text below 12 px and verify comfortable-density wrapping.
3. Make `Planter & steps` the initial inspector view.
4. Move Breed to the inspector only.
5. Reduce default route rendering from 18 cards to five to eight summaries.

### P1 — Results master/detail redesign

1. Replace the desktop route-card grid with a selectable ranked list/table.
2. Keep one selection state; row selection updates the inspector.
3. Show a plain-language recommendation reason.
4. Make Compare a temporary multi-select state.
5. Move secondary route facts into inspector disclosure.

### P2 — Compact goal and controls

1. Replace the large target panel with a goal bar.
2. Separate default count profiles from Advanced exact-pattern matching.
3. Keep three presets; place the rest under More goals.
4. Replace calculation-depth buttons with one selector.
5. Move grouping/display controls into View options.
6. Move secondary header utilities into More on desktop.

### P3 — Execution and sharing

1. Reuse Missing Clone Advisor logic for contextual “collect next” guidance.
2. Add export-to-image for the selected planter.
3. Add a one-time GEN.1/GEN.2 and center-first explanation.
4. Remember collapsed pane state and the user's last calculation depth locally.

## Acceptance criteria

- A user can identify the recommended actionable route without scanning more than one summary row.
- The default results state shows at most eight routes and exactly one primary execution action.
- No meaningful UI text is below 12 CSS px; primary labels/body text are at least 14 px.
- Chance, generation, inventory, clone count, and plant count use consistent labels and number formatting everywhere.
- Score uses one documented scale if it remains visible anywhere.
- The target default is understandable as a gene-count profile; ordered slot matching is clearly identified as Advanced.
- Selecting a route updates the inspector without a separate Inspect button.
- Start Breeding exists once per selected route, in the inspector.
- Compact layouts retain the existing no-horizontal-overflow result and meet WCAG 2.2 reflow and 24×24 minimum target requirements.
- Keyboard selection, comparison, inspector focus, and focus return work without relying on hover.
- Five novice and five experienced Rust farmers can complete add/scan → target → choose route → identify planting order. Record time to first correct route, wrong-route selections, help openings, and whether users can explain GEN.1 versus GEN.2.

## What not to build now

- No new top-level calculator.
- No separate Beginner and Expert applications.
- No new component framework or design system.
- No social/account system solely for sharing; image/text export covers the need first.
- No more ranking dimensions until real users fail to choose between the existing ones.
- No large tutorial permanently embedded in the workspace; contextual help and the existing Guide cover it.

## Research sources

### Comparable products and public implementations

- [Rust Breeder](https://rustbreeder.com/)
- [FlareFlo/rust-breeder](https://github.com/FlareFlo/rust-breeder)
- [rustgenes.gg Genetics Calculator](https://rustgenes.gg/)
- [RustLite Genetics Breeder](https://www.rustlite.com/tools/genetics)
- [Frozen Rust God Clone Planner](https://frozen-rust.com/god-clone-planner.html)
- [Frozen Rust Crossbreeding Simulator](https://frozen-rust.com/crossbreeding-simulator.html)
- [Lenart12 Rust Genetics Calculator](https://lenart12.github.io/Rust-Genetics-Calculator/)
- [SirJeremy/RustCrossbreedCalculator](https://github.com/SirJeremy/RustCrossbreedCalculator)
- [AO Gaming Tools](https://aogaming.tools/)

### First-hand community evidence

- [Rust Breeder scanner and multi-generation release discussion](https://www.reddit.com/r/playrust/comments/uutqdz/)
- [Recent learning-genetics discussion](https://www.reddit.com/r/playrust/comments/1s3bwgk/)
- [Planting-order confusion with a generated route](https://www.reddit.com/r/playrust/comments/14y9uqr/)
- [Crossbreeding tool release discussing selection, grouping, sorting, and UI](https://www.reddit.com/r/playrust/comments/l9ihl7/)
- [Mobile calculator feedback about state persistence](https://www.reddit.com/r/playrust/comments/i8wfbc/)

### Interaction and accessibility guidance

- [Microsoft list/details pattern](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/list-details)
- [Carbon Design System data-table guidance](https://carbondesignsystem.com/components/data-table/usage/)
- [Apple Human Interface Guidelines: Sidebars](https://developer.apple.com/design/human-interface-guidelines/sidebars)
- [WCAG 2.2](https://www.w3.org/TR/WCAG22/)
- [W3C understanding reflow](https://www.w3.org/WAI/WCAG22/Understanding/reflow.html)

## Research limitations

- This is a heuristic and competitive review, not a completed user study.
- Automated accessibility checks cannot certify screen-reader usability, comprehension, zoom comfort, or real Windows scanner behavior.
- Some competitors are JavaScript-heavy or change frequently; findings reflect publicly accessible pages and repositories reviewed on the date above.
- Competitor claims about Rust mechanics were used to understand their UX, not to validate Genetics Lab's solver correctness.
