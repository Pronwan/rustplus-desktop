# Genetics Lab UI/UX Remediation Plan

This plan implements the findings in [UI_UX_REVIEW.md](./UI_UX_REVIEW.md) while preserving the current desktop breeding workflow and Material UI stack.

**Revised:** 2026-08-20 against `master` @ `ae00a1a`. The solver rewrite landed between the original review and this revision, which changes two things for this plan: calculation is no longer a source of UI jank (so the remaining performance work is purely rendering), and four new findings were added (A11Y-05, A11Y-06, UX-07, UX-08). Items already satisfied by that work are marked **[done]** so nobody redoes them.

## Goal

Make every active screen usable at 390, 768, 1024, and 1440 px in dark and light themes, and reach WCAG 2.2 AA for the reviewed flows.

## Execution status — 2026-08-20

Implemented in the current working tree:

- **Phase 0:** responsive shell, compact More menu, stable viewport sizing, consent/dialog isolation, saved-data preservation, and a disclosed, durable analytics opt-out.
- **Phase 1:** form/control names, icon-action names, native toggle semantics, page/section headings, Guide list semantics, visible focus, editable-control shortcut guard, calculation/result live regions, progress value text, contrast tokens, and shared minimum icon targets.
- **Phase 2:** progressive full-result rendering, honest cap wording, one compact workspace scroll owner, compact recipe cards with sticky filters/result count, responsive secondary-page padding, and compact Guide section selection with focus movement.
- **Phase 3 foundation:** consistent active-page titles/descriptions, focus/tap-compatible MUI tooltips, and reviewed empty/calculating/no-result states.

Automated verification completed:

| Check | Result |
| --- | --- |
| Production TypeScript/Vite build | Pass |
| Vitest | 103 passed, 16 benchmark cases intentionally skipped |
| 390×844, 768×1024, 1024×800, 1440×1000; dark and light; all four pages | No horizontal document overflow; exactly one page `h1` |
| axe-core serious/critical checks | 0 on all four pages at compact dark and desktop light coverage |
| Consent | Anonymous Umami analytics defaults on; opt-out removes it and persists; banner hides during Manage; unrelated data is preserved |
| 124-plant production result expansion | 500 retained routes; maximum long task 128 ms (budget: 200 ms) |

Still requires hardware/manual validation before formal sign-off: NVDA keyboard narration, 200%/400% zoom and forced-colors inspection, and the Windows scanner matrix (permission denial, monitor/DPI selection, OCR correction, calibration recovery, stop/restart). These cannot be certified by the headless test environment.

## Already satisfied

These were open in the original review and are now closed. Verify rather than reimplement.

- **[done]** Motion respects `prefers-reduced-motion`, is skipped while the page is hidden, and cleans up its inline styles: `src/utils/useFlipGrid.ts`.
- **[done]** Route reordering animates on `transform`/`opacity` only, and is suppressed while results stream so the grid does not churn.
- **[done]** Calculation no longer blocks the UI. Measured on 124 plants: Fast 0.62 s / 65 ms blocking, Balanced 1.02 s / 6 ms, Thorough 23.7 s / 640 ms.
- **[done]** Long-running calculations remain cancellable and skippable per generation, and progress reaches 100% rather than stalling short.

## Non-goals

- No genetics, scoring, scanner-recognition, or planner-calculation changes
- No new UI framework
- No wholesale desktop redesign
- No speculative component system beyond shared fixes already repeated across the codebase

## Phase 0 - Release blockers

### P0.1 Responsive application shell

**Work**

- Preserve the current one-row header at desktop width.
- Define a tablet layout that does not crowd brand, navigation, scanner, and utilities into one row.
- Define a compact layout with visible primary navigation and a secondary actions menu.
- Remove horizontal overflow instead of masking it with `overflow-x: hidden`.
- Give the main content a stable header-height contract; do not hard-code 80 px against a wrapping header.

**Acceptance criteria**

- `scrollWidth === clientWidth` at 390, 768, 1024, and 1440 px.
- All four primary destinations are visible or exposed through one clearly labelled control.
- Every destination works by pointer, touch, keyboard, and screen reader.
- Focusing any header control never scrolls the page sideways.
- Scanner and utility actions remain reachable without covering primary navigation.

### P0.2 Consent layering and behavior

**Work**

- Put the banner below dialog layers.
- Hide or transform the banner while Manage Preferences is open.
- Add safe-area spacing, short-height overflow, and reliable pointer hit testing.
- Connect Analytics & Telemetry consent to analytics script loading, or remove the in-feature control if the host owns consent.

**Acceptance criteria**

- Accept, Decline, and Manage work at every target viewport without force clicking.
- The banner never overlaps another dialog's actions.
- Only one consent surface is interactive at a time.
- Default-on anonymous analytics is disclosed, and opting out stops subsequent analytics requests immediately and durably.
- Decline remains durable after reload and does not erase unrelated user data.

## Phase 1 - Accessibility foundation

### P1.1 Names and labels

- Label the clone textarea, target input, crop/match/sort/filter selects, planner inputs, settings selects, and settings sliders.
- Add accessible names to every icon-only close, copy, delete, move, filter, and view button.
- Add accessible descriptions only where a label alone cannot explain a control.

### P1.2 Semantic structure

- Wrap primary tabs in a labelled navigation landmark.
- Add one `h1` to each active page and ordered section headings below it.
- Correct Guide list-item semantics.
- Make route-card and Compare interactions native keyboard controls; expose selected/toggled state.

### P1.3 Focus and keyboard

- Establish one visible focus style for buttons, links, tabs, gene slots, chips, cards, and form controls.
- Verify Tab/Shift+Tab order, arrow-key tab behavior, modal focus trapping, Escape behavior, and focus return.
- Stop global shortcuts from firing inside editable controls unless explicitly intended.

### P1.4 Status messages

**Work**

- Add one polite live region for the calculation lifecycle: started, generation N of M, complete with result count, cancelled.
- Add one live region for filter and sort outcomes, announcing the new ordering and the resulting count.
- Throttle announcements to meaningful transitions, not every progress tick.
- Give the progress bar an accessible name and an `aria-valuetext` carrying stage and generation.
- Confirm the notification system exposes `role="status"` so toasts are announced.

**Acceptance criteria**

- Starting, finishing, and cancelling a calculation each produce exactly one announcement.
- Changing sort or filter announces the new ordering and result count.
- The progress bar announces as "Calculation progress, generation 2 of 3, 60 percent", not "60".
- No announcement storm: a Thorough run produces a small, bounded number of messages, not one per tick.

### P1.5 Contrast and target sizing

- Raise dark/light muted and faint text tokens to passing values before component-level exceptions.
- Remove sub-10 px meaningful labels; keep decorative microtext nonessential.
- Ensure 24 x 24 px minimum targets and spacing under WCAG 2.2; use 44 x 44 px hit areas for primary compact actions.

**Phase 1 acceptance criteria**

- Zero critical or serious axe violations on the reviewed screens and dialogs.
- Calculation start, completion, and cancellation are announced to assistive technology.
- Normal text meets 4.5:1; large text and meaningful graphical controls meet 3:1.
- Every interactive element has a name, role, state, and keyboard path.
- At 200% zoom, content remains available without two-dimensional scrolling except genuinely two-dimensional content.
- Modal close returns focus to the control that opened it.

## Phase 2 - Compact task flows

### P2.0 Results grid rendering (UX-07, UX-08)

**Work**

- Stop rendering the full result set in one synchronous pass. Virtualize the grid, or render progressively across frames.
- If neither is adopted, raise the page increment and remove Show All rather than shipping an action that predictably freezes.
- Replace "133 routes of 483" with an honest label. Either show the true discovered-group count alongside the shown count, or say "showing 133 matching routes" and state explicitly when the 500-group cap was reached.

**Acceptance criteria**

- Showing every route in a 500-route result produces no main-thread task over 200 ms.
- Scroll stays smooth with the full result set expanded.
- The result count cannot be misread as the number of routes the solver found.
- Reordering the expanded list does not regress the blocking budget.

### P2.1 Breeding Workspace

- Use one compact scroll owner.
- Keep clone entry, target, calculate, routes, and inspector in a clear task order.
- Consider a sticky Calculate action only after inputs make it valid.
- Keep route inspector as a full-width drawer/sheet and test close/focus behavior.
- Use full route metric labels in comfortable density; keep abbreviations only in compact density if still needed.

### P2.2 Tea Recipes

- Default to the existing card/grid representation on compact screens, or create a dedicated compact row.
- Keep category, search, multiplier, and result count visible while browsing.
- Make raw-material expansion a labelled toggle with expanded state.
- Avoid rendering the full desktop table as one extremely long compact page.

### P2.3 Genetics Guide and Planner

- Apply the shared secondary-page shell and responsive padding.
- Replace the large compact Guide side navigation with a select, accordion, or horizontally scrollable section control.
- Keep Planner labels programmatic and preserve the current one-column compact result cards.

**Phase 2 acceptance criteria**

- A new user can complete each primary compact journey without horizontal scrolling.
- Calculate remains discoverable after data entry.
- Recipe filters stay understandable after scrolling deep into results.
- Guide section changes announce the new section and move focus appropriately when needed.

## Phase 3 - Consistency and polish

- Create one secondary-page layout pattern for title, description, toolbar, and content.
- Use full metric names in comfortable density and reserve abbreviations for compact density.
- Remove duplicated primary actions in the same visual state.
- Ensure hover help is also available on focus and tap.
- Review loading, empty, error, permission-denied, no-results, and success states for every active flow.
- Run the scanner-specific UX matrix on Windows: permission denied, capture stopped, multiple monitors, DPI scaling, OCR ambiguity, correction, and calibration recovery.

## Verification matrix

| Coverage | Required checks |
| --- | --- |
| Viewports | 390 x 844, 768 x 1024, 1024 x 800, 1440 x 1000 |
| Themes | Dark and light |
| Input | Mouse, touch emulation, keyboard only |
| Zoom | 100%, 200%, 400% text where practical |
| Preferences | Reduced motion, high contrast/forced colors where supported |
| Assistive tech | NVDA + Chromium or the product's supported Windows screen-reader/browser pair |
| Journeys | Add/paste clones, set target, calculate, inspect, compare, breed, plan farm, search recipe, change guide section, configure settings, save/import project, consent choices |
| Main-thread budget | No task over 200 ms during calculation, on completion, on sort/filter change, or when expanding the full result list. Measure with a `longtask` PerformanceObserver on the production build, not the dev server |
| Motion | Reduced-motion honoured, no animation while hidden, no leaked inline `transform`/`opacity`/`will-change` after a run |
| Status messages | Calculation start/progress/completion/cancel and sort/filter changes announced, without announcement storms |
| Scanner | Permission, monitor selection, calibration, recognition, correction, stop/restart, failure recovery |

## Suggested implementation order

1. Header overflow and compact navigation
2. Consent stacking and analytics gating
3. Shared labels, icon-button names, and focus treatment
4. Live regions for calculation lifecycle and sort/filter results, plus progress-bar naming
5. Shared contrast tokens and target sizing
6. Route-card/Compare semantics and Guide list structure
7. Results-grid rendering (virtualize or progressive) and honest result count
8. Compact Recipes representation
9. Compact workspace scroll/action treatment
10. Secondary-page consistency and scanner-specific QA

Items 4 and 7 are new in this revision. Item 7 is the only remaining performance work; it is a rendering concern and does not touch the solver.

## Definition of done

- Phase acceptance criteria pass in automated and manual checks.
- No app-code behavior regression in clone entry, calculation, route selection, project persistence, or scanner control.
- Screenshots for the four target viewports and both themes are attached to the change review.
- The reviewer can trace every code change back to a finding in `UI_UX_REVIEW.md`.
- The main-thread blocking budget holds on the production build, verified with a `longtask` observer rather than by feel.
- No regression in solver throughput; the measured preset timings above remain the baseline.
