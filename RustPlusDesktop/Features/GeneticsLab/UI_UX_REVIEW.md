# Genetics Lab UI/UX Review

**Review date:** 2026-08-20  
**Re-verified:** 2026-08-20 against `master` @ `ae00a1a` (post solver rewrite), production build served via `vite preview`  
**Status:** Findings remediated in the current working tree; automated re-verification passes. Manual screen-reader and live Windows scanner checks remain.

Findings UX-01 through UX-06 and A11Y-01 through A11Y-04 were re-confirmed against the current build and still stand. This pass adds four findings the earlier review did not cover (A11Y-05, A11Y-06, UX-07, UX-08), all in areas the solver rewrite either touched or newly exposed.

## Remediation addendum — 2026-08-20

The findings below remain the audit trail for the pre-fix build. The current working tree now includes:

- Responsive desktop, tablet, and compact header/navigation layouts with no measured horizontal overflow.
- Consent dialog layering, durable choices, preservation of saved user data, and analytics loading only after affirmative consent.
- Programmatic form labels, names for icon-only actions, native Compare/group controls, page headings, corrected Guide list semantics, shared focus treatment, and larger action targets.
- Polite calculation and result-filter live regions plus an accessible progress name/value description.
- Passing dark/light contrast tokens for the audited screens.
- Progressive result expansion and explicit 500-result-cap wording.
- Compact recipe cards and sticky filters, one workspace scroll owner, a compact Guide section selector, and consistent secondary-page padding/headings.

Automated re-verification: production build passes; 102 tests pass; all four active destinations have one `h1` and no horizontal document overflow at 390×844, 768×1024, 1024×800, and 1440×1000 in both themes; axe reports zero serious/critical findings across all four active pages at compact dark and desktop light coverage. A production 124-plant run retained 500 routes and expanded them with a 128 ms maximum long task, below the 200 ms budget.

## Scope and method

Reviewed the active product surfaces and shared shell:

- Breeding Workspace: empty state, sample data, calculated routes, route cards, and inspector
- Farm Planner
- Tea Recipes list and grid controls
- Genetics Guide and planter visualizer
- Cookie banner and preference dialog
- Settings, keyboard shortcuts, projects, and about dialogs
- Scanner and breeding-assistant source flows (static review only)
- Dark and light themes
- Desktop, tablet, and compact layouts at 1440 x 1000, 1024 x 800, and 390 x 844

The review combined source inspection, rendered screenshots, pointer interaction, keyboard traversal, responsive overflow checks, and axe-core 4.10.2 checks using WCAG 2.0/2.1/2.2 A/AA tags. The AccessLint MCP was unavailable, so axe plus manual inspection was used as the fallback.

This is not a formal accessibility certification. A real screen-reader pass and live scanner capture could not be completed in the headless environment.

## Executive assessment

| Area | Assessment | Notes |
| --- | --- | --- |
| Desktop task flow | Strong | Inputs -> target -> calculate -> compare -> inspect is direct and understandable. |
| Visual hierarchy | Good | Route ranking, readiness, generation, and primary actions are easy to scan. |
| Feedback and system status | Good | Disabled states, progress, notifications, and selected-route state are visible. |
| Responsive behavior | Critical risk | The shared header overflows and hides navigation below desktop width. |
| Mobile content design | High risk | Workspace and Recipes retain desktop density and create long or clipped interactions. |
| Accessibility | High risk | Automated and source checks found missing names, labels, contrast, semantics, and small targets. |
| Theme consistency | Mixed | Both themes are coherent, but muted text fails contrast in common contexts. |
| Trust and consent | Critical risk | Consent layers overlap dialogs, and the analytics choice is not connected to script loading in this feature. |
| Perceived performance | Good, with one defect | Calculation no longer blocks the UI (65 ms total blocking on Fast, 640 ms on Thorough). "Show All" still freezes for about a second. |
| Status messaging | High risk | The application contains no live regions at all; progress, completion, and result counts are silent to assistive technology. |

## What works well

1. **The primary desktop workflow is efficient.** The left-to-right relationship between clone inventory, target definition, ranked routes, and inspector matches the user's mental model.
2. **Route results communicate useful decisions.** Best-route status, score, generation, chance, clone count, plant count, inventory readiness, inspect, compare, and breed actions are visible without opening another screen.
3. **The empty state is instructive.** It explains the minimum input needed and keeps the unavailable calculate action visibly disabled.
4. **The planner gives immediate feedback.** Inputs and four outcome cards update in a compact, readable layout.
5. **Themes share a consistent visual language.** Cyan primary actions, orange breeding actions, green success, card borders, and monospace genetics data are applied consistently.
6. **Motion is restrained and preference-aware.** Route reordering uses a FLIP pass on `transform`/`opacity` only, entry is staggered with a capped cascade, and reordering is suppressed while results stream so the grid does not churn. `prefers-reduced-motion` is honoured, animation is skipped while the page is hidden, and inline animation styles are cleaned up afterwards (verified: no leaked `transform`, `opacity`, or `will-change` after a run).
7. **The interface stays responsive during calculation.** Measured on a 124-plant input in the production build: Fast 0.62 s with 65 ms total main-thread blocking, Balanced 1.02 s with 6 ms, Thorough 23.7 s with 640 ms spread across 35 tasks. Long-running work is cancellable and skippable per generation.
8. **Secondary tools use dialogs instead of separate flows.** Settings, projects, shortcuts, and about information remain easy to exit and preserve workspace context.

## Prioritized findings

### UX-01 - Critical: shared header breaks compact and tablet navigation

**Evidence**

- The header uses one non-responsive flex row for brand/crop, four navigation tabs, scanner, and six utility actions: `src/components/layout/AppHeader.tsx:55-193`.
- At a 390 px client width, the rendered document was 581 px wide. Tea Recipes began at x=376 and Genetics Guide at x=487, outside the viewport.
- Pointer testing could not activate clipped navigation normally because other header elements intercepted the click.
- At 1024 px, the header already wraps the brand, crowds actions, and clips the last navigation item.
- `overflow-x: hidden` conceals the overflow rather than resolving it: `src/styles/main.css:24`.

**Impact**

Users cannot reliably discover or activate Farm Planner, Tea Recipes, or Genetics Guide on compact screens. Keyboard focus can move the page horizontally to off-screen controls, creating a disorienting experience.

**Recommendation**

Create explicit desktop, tablet, and compact header modes. Preserve the current desktop layout; use a two-row header or compact primary navigation plus an overflow menu below 1200 px. Keep the scanner prominent only if product priority justifies it. The acceptance condition is `scrollWidth === clientWidth` at 390, 768, 1024, and 1440 px.

### UX-02 - Critical: consent UI conflicts with the rest of the interface

**Evidence**

- The fixed banner uses `zIndex: 1400`: `src/components/modals/CookieConsentBanner.tsx:76-87`. MUI dialogs normally sit below that layer.
- The preference dialog opens while the original banner remains above the backdrop. The banner also overlaps Settings, Projects, Shortcuts, and About dialogs when consent is undecided.
- On the compact viewport, normal clicks on consent actions were intercepted by workspace content during automation.
- The Analytics & Telemetry preference is saved, but no code consumes that preference. The analytics script is loaded unconditionally in `index.html:11`.

**Impact**

Consent actions can be blocked or visually compete with modal actions. More importantly, the interface suggests that analytics is optional while the feature does not appear to gate the analytics script, which creates a trust and privacy risk.

**Recommendation**

Keep the banner below modal layers, hide it while its preference dialog is open, and make it safe-area aware with internal overflow on short screens. Load analytics only after affirmative analytics consent, or remove the preference if the host application governs analytics elsewhere and explain that ownership clearly.

### A11Y-01 - High: form controls and icon buttons lack accessible names

**Automated rules:** `label` (critical), `aria-input-field-name` (serious), `button-name` (critical).  
**WCAG:** 1.3.1, 3.3.2, 4.1.2.

**Verified examples**

- The primary clone textarea has no label or accessible name: `src/components/workspace/CloneBank/CloneBank.tsx:498`.
- Crop, match-mode, sort, and inventory selects use nearby text or context but no programmatic label: `src/components/layout/AppHeader.tsx:83`, `src/components/workspace/TargetDesigner/TargetDesigner.tsx:117`, `src/components/workspace/Routes/RouteToolbar.tsx:263`, and `:290`.
- Planner labels are Typography elements rather than associated labels: `src/components/planner/FarmOutputPlanner.tsx:60-105`.
- Settings sliders have visible headings but no `aria-label` or `aria-labelledby`: `src/components/modals/OptionsModal.tsx:119`, `:140`, and `:160`.
- Close, copy, delete, and similar icon buttons repeatedly depend on an icon alone. Examples: `src/components/modals/OptionsModal.tsx:89` and `src/components/workspace/Inspector/RouteInspector.tsx:167-171`.

**Recommendation**

Use MUI `label`/`InputLabel`/`FormControl` relationships where visible labels exist. Add concise `aria-label` values to icon-only buttons and `aria-labelledby` to sliders. Treat placeholders and hover tooltips as supplemental help, not labels.

### A11Y-02 - High: muted text fails minimum contrast in both themes

**Automated rule:** `color-contrast` (serious).  
**WCAG:** 1.4.3.

**Evidence**

- Dark `--gl-text-faint: #555555` on `#0E0E0E` is approximately **2.59:1**: `src/styles/tokens.css:99`.
- Light `--gl-text-muted: #94A3B8` on white is approximately **2.56:1**, and `--gl-text-faint: #A0AAB8` is approximately **2.35:1**: `src/styles/tokens.css:150-151`.
- The failing tokens are used for instructions, disabled-looking metadata, table controls, and labels. Some route metrics are only `0.58rem`: `src/components/workspace/Routes/RouteCard.tsx:193-236`.

**Impact**

Important instructions and metadata can look disabled or disappear for users with low vision, on low-quality displays, or in bright environments.

**Recommendation**

Fix semantic text tokens centrally, then reserve faint colors for decorative/nonessential content. Require 4.5:1 for normal text and 3:1 for large text and meaningful graphical controls. Do not solve contrast only by increasing font weight.

### A11Y-03 - High: custom clickable regions are not consistently keyboard-operable

**WCAG:** 2.1.1, 2.4.7, 4.1.2.

**Evidence**

- The route card is a clickable `Paper` without button/link semantics or keyboard handling: `src/components/workspace/Routes/RouteCard.tsx:65-66`.
- The Compare control is a clickable `Box` without role, tab stop, state, or keyboard handler: `src/components/workspace/Routes/RouteCard.tsx:157-179`.
- Keyboard traversal on the compact page moved focus to off-screen header controls and caused horizontal viewport movement.
- Gene slots are keyboard-operable, but the live focus inspection did not produce a reliable visible outline across the tested controls.
- The global shortcut handler calculates `isInput` but never uses it, so shortcuts can fire while users edit form controls: `src/context/AppContext.tsx:162-173`.

**Recommendation**

Use real `Button`/`IconButton` elements for actions. If the whole route card must be interactive, give it button semantics, Enter/Space handling, and a strong focus-visible style without creating nested-button conflicts. Expose Compare as a toggle with `aria-pressed`. Prevent non-editing shortcuts while focus is in an input unless the shortcut is deliberately documented for that input.

### A11Y-04 - High: several controls are too small for reliable touch use

**WCAG:** 2.5.8.

**Evidence**

- Clear, Sample, and Save render at about 18 px high: `src/components/workspace/CloneBank/CloneBank.tsx:287-328`.
- The compact Scan action rendered at 23 px high.
- Header icon controls are about 29 x 29 px: technically above the WCAG 2.2 AA 24 px minimum in isolation, but below the usual 44 px touch recommendation.
- Route result screens contained dozens of controls below 44 px.

**Recommendation**

Use at least 24 x 24 px for all targets with sufficient spacing, and target 44 x 44 px for primary compact/touch actions. The dense desktop mode may retain smaller visual icons inside a larger hit area.

### A11Y-05 - High: the application has no status messages

**WCAG:** 4.1.3 Status Messages (AA), 1.3.1.

**Evidence**

- Querying `[aria-live], [role="status"], [role="alert"]` returns an empty set both during and after a calculation. There are zero live regions in the running application.
- Progress text (`974,019 combinations - 60%`) updates continuously in `src/components/workspace/Routes/RouteToolbar.tsx:206-256` with no announcement.
- The result count (`133 routes of 483`) changes when a run completes, when the target changes, and when filters change, silently.
- Changing the sort reorders the whole grid. Sighted users get an animated transition; assistive-technology users get no signal that content moved or that ranking changed.
- Toast notifications ("Found N viable breeding routes") were not present as a live region at the moment of sampling and need separate confirmation.

**Impact**

A screen-reader user starting a Thorough run gets no feedback for roughly 24 seconds and no completion signal. They cannot tell whether the application is working, finished, or found nothing.

**Recommendation**

Add one polite live region for the calculation lifecycle (started, generation N of M, complete with result count, cancelled) and one for filter and sort outcomes ("Sorted by highest probability, 133 routes"). Throttle announcements to meaningful transitions rather than every progress tick. Confirm the notification system exposes `role="status"`.

### A11Y-06 - Medium: the progress bar announces a bare number

**WCAG:** 1.3.1, 4.1.2.

**Evidence**

- The bar exposes `role="progressbar"` and `aria-valuenow="60.4"` but no `aria-label`, `aria-labelledby`, or `aria-valuetext`: `src/components/workspace/Routes/RouteToolbar.tsx:216-256`.
- The adjacent stage text and combination count are visually associated with the bar but not programmatically linked to it.

**Recommendation**

Give the bar an accessible name ("Calculation progress") and an `aria-valuetext` carrying stage and generation, so the announcement is "Calculation progress, generation 2 of 3, 60 percent" rather than "60".

### IA-01 - Medium: heading and landmark structure does not represent the visual hierarchy

**Evidence**

- The four primary tabs are not inside a navigation landmark: `src/components/layout/AppHeader.tsx:106`.
- Most workspace titles and the brand render as `h6`; there is no page `h1` in the active workspace or Recipes screen.
- Recipes has no visible page heading; its only detected heading was the application brand.
- Guide jumps between h4, h6, and h5 based on visual variants rather than document order.
- axe reported `list` (serious) because `ListItemButton` elements are direct children of a `List` without list-item semantics: `src/components/guide/GuidePage.tsx:54-74`.

**Recommendation**

Add a labelled `nav` for primary navigation, one `h1` per page, and ordered `h2`/`h3` sections. Separate the rendered typography variant from the semantic `component`. Render guide entries as `li` children or use MUI `ListItem`.

### UX-03 - High: Recipes is not designed for compact consumption

**Evidence**

- The default list retains a three-column table at every breakpoint: `src/components/recipes/RecipesPage.tsx:214-344`.
- At 390 px, category tabs are clipped, the search/multiplier/view controls wrap awkwardly, recipe rows become tall multi-line blocks, and the full list produces an extremely long page.
- The component already has a responsive grid/card representation: `src/components/recipes/RecipesPage.tsx:381-465`, but list remains the default compact view.
- The page has no result count, grouping header, or persistent filter context once scrolled deep into the list.

**Recommendation**

Default to cards on compact screens or create a purpose-built two-column recipe row with an expandable breakdown. Keep search/category controls sticky, show result count, and preserve filters while changing view modes.

### UX-04 - Medium: compact workspace uses a nested, height-assumed scroll model

**Evidence**

- Compact and desktop workspace heights assume an 80 px header: `src/components/workspace/WorkspaceLayout.tsx:84` and `:122`.
- Compact content scrolls inside a viewport-height container while the clone bank also has bounded internal content: `src/components/workspace/WorkspaceLayout.tsx:80-99`.
- The header height changes when it wraps, so the 80 px assumption is not stable.
- Calculate and results can be several panels below the initial clone entry area.

**Recommendation**

Use natural page flow on compact screens, or derive the available height from a stable shell variable. Keep one main scroll owner. Consider a sticky Calculate action once the minimum input is valid.

### UX-05 - Medium: expert density sometimes hides meaning

**Evidence**

- Route cards abbreviate values as `unq`, `tot`, `GEN.1`, and use 9-11 px supporting text.
- Several explanations exist only in hover tooltips, which do not transfer well to touch.
- The empty state contains a second Calculate Routes action in addition to the toolbar action.

**Recommendation**

Keep the dense desktop default, but use full labels where space allows and reveal secondary explanations on focus/tap as well as hover. Retain only one obvious primary calculate action per visual state.

### UX-06 - Medium: page spacing and hierarchy vary between secondary pages

**Evidence**

- Planner has a clear title, intro, configuration card, and outcome cards.
- Recipes starts immediately with filters and a table, with no title or explanatory context.
- Guide uses no outer padding on compact screens, causing the title to touch the left edge while its cards use their own padding.

**Recommendation**

Adopt one secondary-page shell with consistent responsive padding, `h1`, optional description, toolbar, and content area. Reuse it for Planner, Recipes, and Guide.

### UX-07 - High: Show All blocks the main thread for about a second

**Evidence**

- The route grid pages 18 cards at a time and offers a Show All action that renders every filtered route in one pass: `src/components/workspace/Routes/RouteGrid.tsx:16` and `:169-176`.
- Measured on a 133-route result in the production build: Show All produced a single **1,047 ms** long task (997 ms total blocking) while rendering 133 cards.
- The result cap is 500, so a less-filtered run can push this materially higher.
- Each card is a heavy composition (Paper, chips, tooltips, a six-slot genetics sequence) and the reorder pass measures every card, so cost scales linearly with the number shown.

**Impact**

The single action a user takes when they want to survey all options is the one that freezes the interface, and it lands after the calculation, when the user believes the work is already finished.

**Recommendation**

Virtualize the grid, or render progressively across frames instead of one synchronous pass. Failing either, raise the page increment and remove Show All rather than offering an action that predictably janks. This is a rendering problem, not a solver problem: the solver returns this data in milliseconds.

### UX-08 - Medium: the result count presents an internal cap as a total

**Evidence**

- The toolbar renders "133 routes of 483". The second number is how many genotype groups the solver returned after truncating to `MAX_RETURNED_RESULTS = 500`: `src/services/orchestrator.ts:30`.
- It is not the number of routes that exist, and it is not stable: it varies with how many results survived the cap.
- A Thorough run may evaluate tens of millions of combinations and discover thousands of distinct genotypes, and still display "of 483".

**Impact**

The label reads as "483 routes were found, 133 match your filters". It actually means "we kept 483 and are showing 133". Users cannot distinguish a route that was never found from one that was silently truncated.

**Recommendation**

Either report the true discovered-group count alongside the shown count, or drop the second number and label the list "showing 133 matching routes". If the cap stays, say so explicitly when it is reached ("best 500 kept").

## Scanner and breeding-assistant notes

The scanner and breeding assistant show good intent: status language, calibration profiles, correction before commit, step-by-step instructions, history, and explicit exit confirmation. Source inspection also found the same repeated icon-button naming and small-text issues present elsewhere. A complete scanner UX verdict requires a live desktop capture session, permission-denial tests, multiple-monitor/DPI tests, and an OCR correction session.

## Questions for another reviewer

1. Is 390 px mobile a supported target, or should the product declare and enforce a desktop/tablet minimum width?
2. On compact screens, should primary navigation be a bottom bar, a second header row, or an overflow menu?
3. Is **Scan from Rust** important enough to remain a full-width primary header action on compact screens?
4. Should Recipes default to cards on compact screens, or must the table remain the primary representation?
5. Is WCAG 2.2 AA the release target? If yes, should CI block new critical/serious axe violations?
6. Who owns analytics consent: this feature or the desktop host? The current control and script loading need one clear owner.
7. Are the abbreviated route labels intentional for expert users, or should a comfortable density use full labels?
8. Should the results grid be virtualized, or is capping the visible list an acceptable answer to UX-07?
9. What should the result count mean to a user: routes matching filters, distinct genotypes discovered, or both (UX-08)?
10. Is there a main-thread blocking budget the UI should be held to? A measurable target (for example, no task over 200 ms during or after a calculation) would make UX-07 and any future regression testable.

## Bottom line

Do not redesign the successful desktop breeding workflow. Fix the shared responsive shell and consent layering first, then address labels, contrast, semantics, and targets through shared components and tokens. After that foundation, make Recipes and the compact workspace purpose-built for touch rather than compressed desktop layouts.

The solver rewrite removed calculation as a source of jank, which relocates the remaining performance work: it now sits entirely in rendering (UX-07), not computation. The other addition from this pass is that the application has no status messages at all (A11Y-05), a gap that grows more visible now that a Thorough run is a 24-second operation with a live progress bar that says nothing to assistive technology.
