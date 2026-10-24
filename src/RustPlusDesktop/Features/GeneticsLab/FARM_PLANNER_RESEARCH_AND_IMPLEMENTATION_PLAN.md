# Farm Planner Research and Implementation Plan

**Date:** 2026-08-21  
**Status:** Core implementation delivered  
**Scope:** Replace the current passive estimator with an operational farm-planning workflow.

## Delivered implementation

Implemented on 2026-08-21:

- Goal-first sizing for supported raw crops and farm-backed tea/food recipes
- Per-harvest, hourly, daily, and session/deadline-window planning
- Clone reserve, first-harvest, crop allocation, water, power, layout, component, and material calculations
- Existing-farm audit with water, power, conditions, capacity, and clone bottlenecks
- Measured yield and cycle-time calibration for server-specific behavior
- Large, triangle, and small planter support with explicit unsupported-data behavior
- Local autosave, owned-item checklist state, text copy, SVG export, and native printing
- Direct handoffs to Breeding Workspace and Tea Recipes
- Responsive one-column/two-column UI verified without horizontal overflow at 390, 768, and 1440 px
- Focused planner tests plus the full project test suite

Deferred intentionally: named multi-plan storage, a calendar date picker, ranked multi-change optimization, and a drag-and-drop base or circuit designer. The current session-hours input covers deadline sizing without adding a date-time subsystem.

## Executive recommendation

The existing page should become a **Farm Operations Planner** with three connected capabilities:

1. **Plan by Goal**: work backward from a desired output. This is the default workflow.
2. **Audit My Setup**: enter an existing farm and identify its bottlenecks and realistic production.
3. **Build Requirements**: generate the layout modules, components, power, water, clones, and material checklist required by either workflow.

These are not three separate top-level tools. They should share one calculation engine and one results model. A user can begin with a goal or an existing setup, then receive the same production, infrastructure, schedule, and warning sections.

The first useful release should answer this complete question:

> “I want this amount of cloth, berries, tea, or food within this timeframe. What genetics, planters, water, power, clones, and harvest schedule do I need?”

## Why the current planner feels useless

The current implementation in `src/components/planner/FarmOutputPlanner.tsx` accepts four inputs and returns four approximate totals. It has several product and accuracy problems:

- It starts from planter count instead of the player's actual objective.
- It reports output but never recommends an action.
- It treats environment quality as a single optimal/not-optimal switch.
- It does not identify the limiting condition.
- It does not size lights, pumps, purifiers, barrels, batteries, or other infrastructure.
- It does not account for the clones consumed to replant the farm.
- It does not connect berry output to the existing tea and cooking recipes.
- It does not compare output per cycle, per active hour, or over a wipe.
- It uses undocumented constants for gene effects, environment bonuses, and water use.
- It silently falls back to hemp when the selected crop is not in `CROP_GROWTH_DATABASE`.
- It has no tests, source metadata, patch version, or user calibration.
- Its 960 px centered layout leaves most of a large display empty while presenting only four summary cards.

This is an estimator-shaped page. A useful planner must convert an objective into a feasible setup and a short list of next actions.

## Research findings

### What the game requires players to decide

Rust farming is an operating system, not only a yield formula. The practical decisions are:

- What useful item is needed: cloth, a particular berry, tea, food, or a recipe ingredient?
- How much is needed per harvest, per hour, per session, or by a deadline?
- Which genetics maximize the desired outcome while producing enough clones to replant?
- How many planter slots and harvest cycles are required?
- Can the available water source sustain the farm?
- Can the electrical system sustain lighting, pumps, and purification?
- Which condition is limiting plant performance?
- When should the player clone, plant, and harvest?
- What must be crafted or collected before building?

The official farming guide recommends arranging planters in groups of three or six because sprinkler coverage and ceiling-light placement are physical constraints. It also distinguishes fresh-water pumps, salt-water purification, barrels, and sprinkler limits. The planner therefore needs infrastructure and layout reasoning, not only multiplication.

### Current mechanics that the data model must cover

- Large planters have nine slots.
- The 2025 Crafting Update added four-slot triangle planters and wheat.
- Genetics affect growth, yield, hardiness, and water demand.
- Clone yield is part of the production loop, not a secondary statistic.
- Ceiling lights consume electrical power; water pumps and powered purifiers also require power.
- Water barrels have a finite output rate, and sprinkler coverage depends on placement and line of sight.
- The August 2026 Power Trip update introduced roadside water outlets controlled through Water Treatment Plant activity. The planner should support this as a water-source option with an editable measured flow until a stable official flow value is available.

### Patterns used by comparable tools

| Tool | Useful product pattern | What Genetics Lab should take from it |
| --- | --- | --- |
| [rustgenes.gg Farm Calculator](https://rustgenes.gg/farm-calculator) | Starts with target output and sizes planters, lights, sprinklers, pumps, solar, and power. Allows an output-rate override. | Work backward from the goal and let users calibrate uncertain production rates. |
| [Frozen Rust Farm Planner](https://frozen-rust.com/farm-planner.html) | Combines genetics, conditions, planter count, output per hour, power, water, and clone replenishment. | Treat clones and operating resources as first-class results. |
| [Frozen Rust Farm Simulator](https://frozen-rust.com/farm-simulator.html) | Shows per-cycle, per-hour, per-day, and recipe-related production over time. | Provide time horizons and connect berry farming to tea output. |
| [RUST RU Farmer Calculator](https://rust-ru.com/en/tools/farm) | Models planter type and separate light, water, soil, and temperature conditions. Shows the bottleneck. | Replace the binary environment switch with condition inputs and an explicit bottleneck. |
| [Rustrician](https://www.rustrician.io/) | Models electrical and fluid components as an operating network. | Produce compatible component counts and power budgets, but do not build another circuit editor. |

The competitive opportunity is integration. Other calculators handle one slice. Genetics Lab already contains the genetics inventory, route solver, recipe engine, crop selection, scanner, project storage, and desktop host. The planner can connect those existing capabilities into one continuous workflow.

## Capability 1: Plan by Goal

### User job

The player knows the outcome they need but does not know how large the farm should be.

Examples:

- 5,000 cloth per hour
- 20,000 cloth before the team logs off in four hours
- 4 Pure Ore Teas per day
- Enough pumpkins and wheat for 12 Pumpkin Pies
- 200 yellow berries per harvest

### Inputs to implement

#### Primary inputs

- **Output goal**: item or recipe selected from searchable groups such as Cloth, Berries, Teas, Food, and Pies.
- **Quantity**: positive numeric amount.
- **Time basis**: Per harvest, Per hour, Per session, Per day, or By deadline.
- **Genetics**: Use current breeding target, choose an owned clone, enter six genes, or select a preset.

#### Planning assumptions

- **Server rate**: Vanilla 1x by default, with an editable multiplier for modded servers.
- **Planter type**: Large, Triangle, Small, Ground, or Let planner choose.
- **Expected condition**: simple overall percentage by default; advanced Light, Water, Temperature, and Ground values.
- **Player availability**: Always available at harvest, or a normal session window such as every 90 minutes or every four hours.
- **Measured production override**: optional observed yield and grow time from the user's server.

### Calculations to implement

1. Resolve a raw crop target.
   - Cloth maps directly to hemp.
   - A raw berry maps directly to that berry crop.
   - A tea or food goal is expanded into raw ingredients with the existing `RecipeEngine.expandItem()` logic.
2. Calculate production per plant and cycle for the selected genetics and conditions.
3. Calculate usable cycles within the selected time horizon.
4. Reserve clone-taking cycles or plants needed to keep the farm replanted.
5. Calculate required plant slots and round them to valid planter combinations.
6. For multi-crop recipes, allocate planter slots by ingredient demand and identify the limiting ingredient.
7. Calculate expected first-harvest time and steady-state output separately.
8. Generate infrastructure requirements and warnings through the shared Build Requirements engine.

### Results to implement

The primary result should be a recommendation sentence, not four unrelated cards:

> **Recommended: 6 large planters using GGGYYY hemp. Expect approximately 5,200 cloth/hour after clone reserve, with a first harvest in 104 minutes.**

Then show:

- Required planters and occupied slots
- Recommended genetics and whether the clone exists in the current inventory
- First harvest and steady-state production
- Harvests required and completion time
- Clones needed at startup and per replant
- Expected surplus or shortfall
- Water demand and available margin
- Electrical draw and generation/storage requirement
- Component and material checklist
- Confidence label and the assumptions that materially affect the result

### Actions to implement

- **Use owned clone**: select a compatible clone from the existing inventory.
- **Breed recommended genetics**: open Breeding Workspace with the calculated target.
- **Open recipe**: open Tea Recipes with the selected output and quantity.
- **Audit this plan**: switch to Audit My Setup with the recommendation prefilled.
- **Copy or export plan**: create a compact text/image checklist for a second monitor or teammate.

### Why this should be the default

It begins with player intent and produces an actionable answer. It also creates the strongest connection between genetics, farming, and recipes, which is the product's unique advantage over a generic farming calculator.

## Capability 2: Audit My Setup

### User job

The player already has a farm or a planned room and wants to know what it can produce, why it underperforms, and which single change gives the largest improvement.

Examples:

- “Can two river pumps sustain 18 planters?”
- “Why does this farm produce less than the calculator predicted?”
- “Will my battery run the lights overnight?”
- “Should I improve genetics or add planters?”

### Inputs to implement

#### Farm inventory

- Planter type and count
- Filled slots per planter
- Crop and genetics for each crop group
- Dedicated clone planters or shared production planters

#### Conditions

- Light percentage
- Water percentage or observed saturation range
- Temperature percentage
- Ground percentage and fertilizer use
- Optional observed grow time and harvest per plant

The default condition interface should be one “Observed overall condition” field. An Advanced disclosure reveals the four individual conditions. This keeps first use simple while supporting diagnosis.

#### Water system

- Source: River/Fresh Water Pump, Salt Water + Purifier, Catchers + Barrels, Roadside Water Outlet, Tanker/Manual Refill, or Custom
- Pump, purifier, barrel, and sprinkler counts
- Sprinkler coverage: planters reached by each sprinkler group
- Measured source flow override
- Desired buffer duration when the source is unavailable

#### Electrical system

- Power source: Solar, Wind, Powerline/Grid, Generator, Existing Circuit, or Custom
- Available sustained power
- Battery type and count
- Farm-specific loads: lights, pumps, purifiers, and optional control circuit overhead

### Calculations to implement

1. Calculate theoretical production from genetics and crop data.
2. Apply the observed or modeled limiting condition.
3. Compare water consumption with water-source throughput and stored buffer.
4. Compare electrical demand with sustained generation and battery output/capacity.
5. Calculate clone replenishment sustainability.
6. Calculate utilization: occupied slots versus total slots.
7. Compare modeled production with an optional observed harvest.
8. Rank improvements by output gained per component or planter added.

### Results to implement

#### Overall status

- **Sustainable**: all systems have positive margin.
- **At risk**: one system has less than a configurable safety margin.
- **Unsustainable**: water, power, clones, or timing cannot maintain the plan.

#### Bottleneck panel

Show one primary bottleneck with evidence:

> **Water is limiting this setup. Demand is approximately 12% above sustained supply, leaving a 48-minute buffer. Add one pump, reduce coverage waste, or run two fewer planters.**

Secondary findings should be collapsed below it.

#### Before/after recommendation

Show the smallest useful intervention and its effect:

- Current output
- Recommended change
- New output
- Added components/materials
- Change in water and power margin

### Actions to implement

- **Apply recommended fix**: update the modeled setup for comparison without changing saved data.
- **Save as farm plan**: persist the setup locally.
- **Generate build additions**: show only the missing components.
- **Improve genetics**: send the recommended profile to Breeding Workspace.

### Why this capability matters

Player discussions consistently identify water balancing, sprinkler coverage, and offline timing as the difficult parts of farming. A calculator that only reports ideal yield does not help when the actual farm is constrained. Auditing turns the planner into a troubleshooting tool that remains useful after construction.

## Capability 3: Build Requirements

### User job

The player has a production recommendation or an existing setup and needs a buildable checklist.

This should be generated from Plan by Goal or Audit My Setup. It should not require users to enter the same farm a third time.

### Outputs to implement

#### Layout modules

- Recommend modules of three or six planters where possible.
- Show the number of full modules and any remainder.
- Show planter type, slot count, crop assignment, and sprinkler coverage assumption.
- Provide a small schematic for each repeated module rather than a freeform base-building canvas.

Example:

```text
2 modules × 3 large planters

[P][P][P]   1 light row
   [S]      1 sprinkler group
[P][P][P]   54 growing slots total
```

The schematic is a planning aid, not a guarantee of in-game line of sight. The result must tell the player to verify coverage with Rust's range visualization.

#### Crop and clone allocation

- Production planters per crop
- Clone-reserve slots or dedicated clone planter
- Startup clones required
- Clones taken per cycle
- Expected spare clones after replanting

#### Water checklist

- Water source and assumed flow
- Pumps
- Powered purifiers when salt water is selected
- Water barrels required for the selected buffer duration
- Sprinklers and coverage groups
- Fluid splitters/combiners where required by the selected topology
- Fluid Switch & Pump only when elevation or automatic switching requires it
- Safety margin and any manually entered uncertainty

#### Power checklist

- Ceiling lights
- Water pumps
- Powered purifiers
- Optional switching/control overhead
- Sustained rW demand
- Battery output/capacity requirement
- Suggested solar/wind/grid capacity with a configurable safety margin

The planner must keep electrical rW separate from water flow. A sprinkler uses water throughput and should not be presented as an electrical load unless the actual design powers a separate fluid-switch component.

#### Crafting materials

- Total wood, metal fragments, HQM, tarp, gears, tech trash, and other required components
- Workbench requirements where verified data exists
- Owned quantity fields with a remaining-to-collect total

#### Operating schedule

- Plant time
- Clone-taking time or cycle
- Expected ripe window
- Harvest and replant window
- Number of cycles during the selected session or deadline
- Warning when the user's availability misses the useful harvest window

### Actions to implement

- **Copy checklist**: plain text suitable for Discord or notes.
- **Export plan image**: compact visual with goal, layout modules, totals, and schedule.
- **Print**: use browser-native print styling instead of a PDF dependency.
- **Mark owned**: local checklist state for components and materials.
- **Open Breeding Workspace**: calculate the genetics that the build assumes.

### Explicit non-goal

Do not implement a drag-and-drop base designer or an electrical circuit simulator. Rustrician already handles circuits well. A repeated-module schematic and accurate checklist solve the planning need with far less complexity.

## Shared calculation and data design

### Replace undocumented estimates with a confidence-aware model

Every value should be classified as one of:

- **Verified**: supported by an official current item page or recipe data.
- **Community estimate**: commonly measured but not published as an official formula.
- **User calibrated**: entered from an observed server result.
- **Derived**: arithmetic based on values from the other categories.

Each mechanics record should include:

```ts
interface SourcedValue {
  value: number;
  unit: string;
  confidence: 'verified' | 'community' | 'user';
  sourceUrl?: string;
  verifiedOn: string;
  note?: string;
}
```

The UI should show confidence at the section level and expose source details through one Assumptions disclosure. It should not place warning chips beside every number.

### Domain functions

Keep calculations as pure TypeScript functions:

- `estimateCropCycle(input)`
- `expandProductionGoal(goal)`
- `sizeFarmFromGoal(goal, assumptions)`
- `auditFarmSetup(setup, assumptions)`
- `deriveInfrastructure(plan, constraints)`
- `deriveBuildMaterials(infrastructure)`
- `deriveOperatingSchedule(plan, availability)`

Do not put formulas in React components. Do not add a state library or calculation dependency.

### Reuse existing application capabilities

- `WorkspaceContext`: selected crop, owned clones, and target genetics
- `RecipeEngine`: recursive recipe expansion and ingredient consolidation
- `StorageService`: local persistence and project patterns
- Existing crop/item images
- Existing gene input and gene-sequence components
- Existing notification and responsive MUI patterns
- Existing SVG export approach used by the planter export utility

### Unsupported-data behavior

Never silently substitute hemp. If a selected crop has no validated production model:

1. State that the crop is not modeled yet.
2. Offer a measured yield/time override.
3. Disable unsupported automatic sizing until the minimum calibration is provided.

## Proposed interface

### Desktop

```text
┌─────────────────────────────────────────────────────────────────────────────┐
│ FARM OPERATIONS PLANNER                         [Saved plans] [Assumptions] │
│ [Plan by goal] [Audit my setup]                                           │
├──────────────────────────┬──────────────────────────────────────────────────┤
│ Objective / setup        │ Recommended plan                                 │
│                          │ 6 large planters · 5.2k cloth/hr · ready in 104m │
│ Output, quantity, time   │                                                  │
│ Crop / recipe            │ [Summary] [Production] [Water & power] [Build]  │
│ Genetics                 │                                                  │
│ Conditions               │ Bottleneck / confidence / schedule               │
│ Advanced assumptions     │ Actionable checklist and cross-tool actions      │
└──────────────────────────┴──────────────────────────────────────────────────┘
```

Use approximately one-third of the width for inputs and two-thirds for results. Increase the useful content width from 960 px to roughly 1280-1440 px while retaining readable line lengths. The result area should fill the current empty space with decisions, not decorative cards.

### Compact and tablet

- Use one natural page scroll.
- Keep the mode switch and goal summary near the top.
- Present the recommendation before detailed assumptions.
- Use collapsible sections for Production, Water & Power, Build List, and Schedule.
- Keep one sticky primary action only when it is useful, such as Breed Genetics or Copy Checklist.
- Preserve all labels and never depend on hover.

### Empty state

Replace the current prefilled but unexplained calculator with three task examples:

- Plan 5,000 cloth/hour
- Plan 4 Pure Ore Teas/day
- Audit an existing 6-planter farm

Selecting an example prefills the form and demonstrates the tool immediately.

## Implementation phases

### Phase 0: Correctness foundation

1. Move planner math out of `cropGrowthData.ts` into tested pure domain modules.
2. Remove the silent hemp fallback.
3. Inventory every supported crop and planter type.
4. Add source, patch, confidence, and verification-date metadata.
5. Add measured yield/time overrides.
6. Add unit tests for gene parsing, cycle estimation, yield, water, and unsupported crops.

**Exit condition:** Every displayed number has a defined unit, source class, and deterministic test.

### Phase 1: Useful MVP, Plan by Goal

1. Add output item/recipe, quantity, and time-basis inputs.
2. Reuse current target genetics and owned clones.
3. Expand recipe goals through `RecipeEngine`.
4. Calculate planters, slots, cycles, first harvest, steady-state output, and clone reserve.
5. Display one recommendation plus assumptions and shortfalls.
6. Add Breed Recommended Genetics and Open Recipe actions.
7. Persist the last plan locally.

**Exit condition:** A player can enter a cloth or tea target and receive a complete planter and timing recommendation.

### Phase 2: Infrastructure and build requirements

1. Add planter types and three/six-planter modules.
2. Add water-source selection and capacity model.
3. Add power-source, battery, and electrical-load model.
4. Generate component counts and crafting materials.
5. Add buffer and safety-margin assumptions.
6. Add copy, print, and image export.

**Exit condition:** The recommendation can be built from its generated checklist without repeating inputs in another tool.

### Phase 3: Audit My Setup

1. Add existing planter, water, power, genetics, and condition inputs.
2. Calculate utilization and system margins.
3. Identify the primary bottleneck.
4. Rank small improvements by production gained.
5. Add before/after comparison and missing-component list.

**Exit condition:** Deliberately undersupplying water or power produces a clear warning and a minimal corrective recommendation.

### Phase 4: Operations and calibration

1. Add session/deadline scheduling.
2. Add clone-taking and replant cadence.
3. Add observed harvest calibration.
4. Compare modeled and observed output.
5. Add saved named farm plans and duplicate/edit flows using existing local project patterns.

**Exit condition:** A returning player can update an observed harvest and receive a more accurate plan for that server.

## Acceptance criteria

### Goal planning

- A player can plan raw crops and existing tea/food recipes.
- Per-harvest, per-hour, session, daily, and deadline goals produce consistent units.
- Recipe goals correctly expand into raw crop requirements.
- Clone reserve is included rather than hidden in gross yield.
- Unsupported crops never use hemp values.

### Audit

- Water, power, conditions, clone supply, and empty slots can each become the primary bottleneck.
- The result explains the bottleneck with demand, capacity, and margin.
- At least one minimal improvement is generated when the setup is constrained.
- User-observed yield/time overrides community estimates without modifying global defaults.

### Build requirements

- Component counts derive from the same setup used by the production calculation.
- Electrical rW and water flow use separate units and summaries.
- Planter layout assumptions are visible.
- Material totals update deterministically when component counts change.
- Exported text and image contain the goal, planters, crops, infrastructure, and schedule.

### Quality

- Recalculation completes in under 100 ms for up to 200 planters.
- Core calculations have unit tests, including boundary and unsupported-data cases.
- No new framework or runtime dependency is introduced.
- Plans persist locally and remain usable without an account.
- The page has no horizontal overflow at 390, 768, 1024, and 1440 px.
- All fields have programmatic labels, numeric results use tabular figures, and keyboard operation does not depend on hover.

## Success metrics

The redesign is useful if users can complete these tasks without consulting another calculator:

1. Determine the farm size for a target amount of cloth or tea.
2. Identify the limiting system in an existing farm.
3. Produce a build and material checklist.
4. Determine when to clone, harvest, and replant.
5. Move directly from a missing genetics requirement into Breeding Workspace.

Measure:

- Time to first valid farm plan
- Percentage of plans that use a cross-tool action
- Percentage of users who save or export a plan
- Number of changed assumptions before accepting a plan
- Whether test users can explain the primary bottleneck and next action

## Do not implement initially

- Drag-and-drop base construction
- Full electrical/fluid circuit simulation
- Live server telemetry or Rust+ farm entity control
- Accounts or cloud synchronization
- Marketplace prices or profit calculations that depend on server economy
- Chicken, bee, horse, or food-spoilage simulation
- Optimization across multiple farm buildings
- Automatic claims of “exact” output when the underlying value is estimated

## Decision log

| Decision | Alternatives considered | Reason |
| --- | --- | --- |
| Make Plan by Goal the default | Existing-setup estimator; construction-first tool | It starts with player intent and produces the clearest actionable outcome. |
| Keep Audit as a second mode | Separate audit page; omit audit | It reuses the same engine and keeps the tool useful after the farm is built. |
| Generate Build Requirements from both modes | Third independent form | Re-entering the setup would create inconsistency and unnecessary work. |
| Use repeated layout modules | Full drag-and-drop builder; no layout | Modules address real 3/6-planter constraints without building a second Rustrician. |
| Label confidence and allow calibration | Present all results as exact; omit uncertain values | Rust formulas and server settings vary, so trust requires visible provenance and overrides. |
| Reuse RecipeEngine and WorkspaceContext | Duplicate recipe/genetics data | Existing data already connects the product's main workflows. |
| Keep all processing local | Add backend/account storage | The calculations are small and local-first behavior matches the existing product. |

## Research sources

### Official Rust sources

- [Farming Basics](https://wiki.facepunch.com/rust/Farming_Basics)
- [Farming 2.0 Update](https://rust.facepunch.com/news/farming-update)
- [Crafting Update, March 2025](https://rust.facepunch.com/news/crafting-update)
- [Power Trip, August 2026](https://rust.facepunch.com/news/power-trip)
- [Large Planter Box](https://wiki.facepunch.com/rust/item/planter.large)
- [Ceiling Light](https://wiki.facepunch.com/rust/item/ceilinglight)
- [Water Pump](https://wiki.facepunch.com/rust/item/waterpump)
- [Powered Water Purifier](https://wiki.facepunch.com/rust/item/powered.water.purifier)
- [Water Barrel](https://wiki.facepunch.com/rust/item/water.barrel)
- [Fluid Switch & Pump](https://wiki.facepunch.com/rust/item/fluid.switch)
- [Large Solar Panel](https://wiki.facepunch.com/rust/item/electric.solarpanel.large)
- [Large Rechargeable Battery](https://wiki.facepunch.com/rust/item/electric.battery.rechargable.large)

### Comparable tools

- [rustgenes.gg Farm Calculator](https://rustgenes.gg/farm-calculator)
- [Frozen Rust Farm Planner](https://frozen-rust.com/farm-planner.html)
- [Frozen Rust Farm Simulator](https://frozen-rust.com/farm-simulator.html)
- [RUST RU Farmer Calculator](https://rust-ru.com/en/tools/farm)
- [Rustrician](https://www.rustrician.io/)

### First-hand player discussions

- [Farming quality-of-life and water-management discussion](https://www.reddit.com/r/playrust/comments/1cy5agz)
- [Casual farming and offline-harvest concerns](https://www.reddit.com/r/playrust/comments/1g0ig3l)
- [Farming questions about water, conditions, and efficiency](https://www.reddit.com/r/playrust/comments/g8mid3)

## Research limitations

- Facepunch does not publish every plant growth and yield formula in its public wiki.
- Community calculators sometimes disagree about gene and condition multipliers.
- Server modifiers can invalidate vanilla assumptions.
- Infrastructure performance depends on real placement, line of sight, elevation, and player timing.
- Official and community mechanics can change in monthly updates.

For those reasons, the implementation must separate verified facts from estimates, include patch/source metadata, and support observed-value calibration.
