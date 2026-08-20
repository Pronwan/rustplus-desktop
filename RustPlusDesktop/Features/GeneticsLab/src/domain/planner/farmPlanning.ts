import { RecipeEngine } from '../recipes/recipeEngine.ts';
import { RUST_RECIPES } from '../recipes/recipesData.ts';

export type FarmConfidence = 'verified' | 'community' | 'user';
export type PlanterType = 'large' | 'triangle' | 'small';
export type FarmTimeBasis = 'harvest' | 'hour' | 'session' | 'day';
export type WaterSource = 'fresh-pump' | 'salt-purifier' | 'catcher-barrel' | 'road-outlet' | 'custom';
export type PowerSource = 'solar-battery' | 'wind-battery' | 'grid' | 'existing';

export interface FarmConditions {
  light: number;
  water: number;
  temperature: number;
  ground: number;
}

export interface CropProfile {
  id: string;
  name: string;
  outputItem: string;
  baseYieldPerPlant: number;
  baseCycleMinutes: number;
  waterPerMinutePerPlantMl: number;
  confidence: FarmConfidence;
  reviewedOn: string;
  note: string;
}

export const FARM_MODEL_REVIEWED_ON = '2026-08-21';

export interface FarmGoalOption {
  item: string;
  category: 'crop' | 'tea' | 'food' | 'pie';
}

export interface GoalPlannerInput {
  outputItem: string;
  quantity: number;
  timeBasis: FarmTimeBasis;
  horizonHours: number;
  genetics: string;
  planterType: PlanterType;
  conditions: FarmConditions;
  serverRate: number;
  reserveClones: boolean;
  waterSource: WaterSource;
  powerSource: PowerSource;
  bufferHours: number;
  safetyMarginPercent: number;
  measuredYieldPerPlant?: number;
  measuredCycleMinutes?: number;
}

export interface AuditPlannerInput {
  cropId: string;
  genetics: string;
  planterType: PlanterType;
  planterCount: number;
  filledSlotsPerPlanter: number;
  conditions: FarmConditions;
  serverRate: number;
  reserveClones: boolean;
  waterSource: WaterSource;
  powerSource: PowerSource;
  bufferHours: number;
  safetyMarginPercent: number;
  availableWaterMlPerMinute: number;
  availablePowerRw: number;
  measuredYieldPerPlant?: number;
  measuredCycleMinutes?: number;
}

export interface CropCycleEstimate {
  cropId: string;
  cropName: string;
  outputItem: string;
  genetics: string;
  gCount: number;
  yCount: number;
  hCount: number;
  wCount: number;
  effectiveConditionPercent: number;
  limitingCondition: keyof FarmConditions;
  cycleMinutes: number;
  yieldPerPlant: number;
  clonesPerPlant: number;
  waterPerPlantPerMinuteMl: number;
  confidence: FarmConfidence;
}

export interface CropPlan extends CropCycleEstimate {
  requiredPerGoalUnit: number;
  planterCount: number;
  totalSlots: number;
  cloneReservePlants: number;
  harvestPlants: number;
  harvestPerCycle: number;
  productionForPeriod: number;
  productionPerHour: number;
  cyclesForPeriod: number;
  goalUnitsForPeriod: number;
  waterDemandMlPerMinute: number;
  waterPerCycleLiters: number;
}

export interface BuildMaterial {
  item: string;
  quantity: number;
}

export interface InfrastructurePlan {
  layout: {
    sixPlanterModules: number;
    threePlanterModules: number;
    remainderPlanters: number;
  };
  components: Array<{ item: string; quantity: number; note?: string }>;
  materials: BuildMaterial[];
  lights: number;
  sprinklers: number;
  waterPumps: number;
  poweredPurifiers: number;
  waterBarrels: number;
  powerDrawRw: number;
  powerWithSafetyMarginRw: number;
  solarPanelsPeak: number;
  solarPanelsSuggested: number;
  largeBatteries: number;
  waterDemandMlPerMinute: number;
  recommendedWaterFlowMlPerMinute: number;
  storedWaterLiters: number;
}

export interface FarmPlan {
  supported: boolean;
  goal: GoalPlannerInput;
  crops: CropPlan[];
  nonFarmIngredients: Array<{ item: string; quantity: number }>;
  totalPlanters: number;
  totalSlots: number;
  totalCloneReservePlants: number;
  firstHarvestMinutes: number;
  goalOutputForPeriod: number;
  infrastructure: InfrastructurePlan;
  confidence: FarmConfidence;
  warnings: string[];
}

export interface FarmAudit {
  supported: boolean;
  input: AuditPlannerInput;
  crop?: CropPlan;
  infrastructure?: InfrastructurePlan;
  status: 'sustainable' | 'at-risk' | 'unsustainable';
  bottleneck: 'water' | 'power' | 'conditions' | 'capacity' | 'clones' | 'none';
  headline: string;
  recommendation: string;
  waterMarginMlPerMinute: number;
  powerMarginRw: number;
  productionPerHour: number;
  potentialProductionPerHour: number;
  warnings: string[];
}

export const PLANTER_TYPES: Record<PlanterType, { name: string; slots: number }> = {
  large: { name: 'Large planter', slots: 9 },
  triangle: { name: 'Triangle planter', slots: 4 },
  small: { name: 'Small planter', slots: 3 }
};

// Crop rates are community estimates. The UI exposes measured overrides because
// server settings and Rust patches can change them independently of this app.
export const FARM_CROPS: Record<string, CropProfile> = {
  hemp: crop('hemp', 'Hemp', 'Cloth', 35, 90, 5.5),
  'red-berry': crop('red-berry', 'Red Berry', 'Red Berry', 4, 80, 4.8),
  'blue-berry': crop('blue-berry', 'Blue Berry', 'Blue Berry', 4, 80, 4.8),
  'yellow-berry': crop('yellow-berry', 'Yellow Berry', 'Yellow Berry', 4, 80, 4.8),
  'green-berry': crop('green-berry', 'Green Berry', 'Green Berry', 4, 80, 4.8),
  'white-berry': crop('white-berry', 'White Berry', 'White Berry', 4, 80, 4.8),
  'mixed-berry': crop('mixed-berry', 'Mixed Berry', 'Mixed Berry', 4, 80, 4.8),
  potato: crop('potato', 'Potato', 'Potato', 3, 85, 6),
  pumpkin: crop('pumpkin', 'Pumpkin', 'Pumpkin', 1, 100, 7),
  corn: crop('corn', 'Corn', 'Corn', 2, 95, 6.5),
  wheat: crop('wheat', 'Wheat', 'Wheat', 3, 90, 5.5),
  sunflower: crop('sunflower', 'Sunflower', 'Sunflower', 1, 90, 5.5),
  rose: crop('rose', 'Rose', 'Rose', 1, 90, 5.5),
  orchid: crop('orchid', 'Orchid', 'Orchid', 1, 90, 5.5)
};

const FARM_ITEM_TO_CROP = new Map(
  Object.values(FARM_CROPS).map((profile) => [profile.outputItem.toLowerCase(), profile.id])
);
const recipeEngine = new RecipeEngine(RUST_RECIPES);

export const DEFAULT_CONDITIONS: FarmConditions = {
  light: 100,
  water: 100,
  temperature: 100,
  ground: 100
};

export const DEFAULT_GOAL_INPUT: GoalPlannerInput = {
  outputItem: 'Cloth',
  quantity: 5000,
  timeBasis: 'hour',
  horizonHours: 4,
  genetics: 'GGGYYY',
  planterType: 'large',
  conditions: { ...DEFAULT_CONDITIONS },
  serverRate: 1,
  reserveClones: true,
  waterSource: 'fresh-pump',
  powerSource: 'solar-battery',
  bufferHours: 2,
  safetyMarginPercent: 20
};

export const DEFAULT_AUDIT_INPUT: AuditPlannerInput = {
  cropId: 'hemp',
  genetics: 'GGGYYY',
  planterType: 'large',
  planterCount: 6,
  filledSlotsPerPlanter: 9,
  conditions: { ...DEFAULT_CONDITIONS },
  serverRate: 1,
  reserveClones: true,
  waterSource: 'fresh-pump',
  powerSource: 'existing',
  bufferHours: 2,
  safetyMarginPercent: 20,
  availableWaterMlPerMinute: 350,
  availablePowerRw: 50
};

export const FARM_GOAL_OPTIONS: FarmGoalOption[] = buildGoalOptions();

function crop(
  id: string,
  name: string,
  outputItem: string,
  baseYieldPerPlant: number,
  baseCycleMinutes: number,
  waterPerMinutePerPlantMl: number
): CropProfile {
  return {
    id,
    name,
    outputItem,
    baseYieldPerPlant,
    baseCycleMinutes,
    waterPerMinutePerPlantMl,
    confidence: 'community',
    reviewedOn: FARM_MODEL_REVIEWED_ON,
    note: 'Community baseline. Calibrate with an observed harvest for this server.'
  };
}

function buildGoalOptions(): FarmGoalOption[] {
  const raw = Object.values(FARM_CROPS).map((profile) => ({
    item: profile.outputItem,
    category: 'crop' as const
  }));
  const recipes = RUST_RECIPES
    .filter((recipe) => ['tea', 'food', 'pie'].includes(recipe.category))
    .filter((recipe) => resolveFarmRequirements(recipe.output.item, 1).farmable.length > 0)
    .map((recipe) => ({
      item: recipe.output.item,
      category: recipe.category as 'tea' | 'food' | 'pie'
    }));

  return [...raw, ...recipes].filter(
    (option, index, list) => list.findIndex((candidate) => candidate.item === option.item) === index
  );
}

export function normalizeGenetics(value: string): string {
  return value.toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);
}

export function isValidGenetics(value: string): boolean {
  return /^[GHYWX]{6}$/.test(value);
}

export function effectiveCondition(conditions: FarmConditions): {
  value: number;
  limiting: keyof FarmConditions;
} {
  const entries = Object.entries(conditions) as Array<[keyof FarmConditions, number]>;
  const [limiting, value] = entries.reduce((lowest, current) =>
    clampPercent(current[1]) < clampPercent(lowest[1]) ? current : lowest
  );
  return { value: clampPercent(value), limiting };
}

export function estimateCropCycle(input: {
  cropId: string;
  genetics: string;
  conditions: FarmConditions;
  serverRate?: number;
  measuredYieldPerPlant?: number;
  measuredCycleMinutes?: number;
}): CropCycleEstimate | null {
  const profile = FARM_CROPS[input.cropId];
  const genetics = normalizeGenetics(input.genetics);
  if (!profile || !isValidGenetics(genetics)) return null;

  const gCount = countGene(genetics, 'G');
  const yCount = countGene(genetics, 'Y');
  const hCount = countGene(genetics, 'H');
  const wCount = countGene(genetics, 'W');
  const condition = effectiveCondition(input.conditions);
  const conditionFactor = Math.max(0.25, condition.value / 100);
  const growthSpeed = 1 + gCount * 0.25;
  const yieldMultiplier = 1 + yCount * 0.25;
  const serverRate = Math.max(0.01, input.serverRate || 1);
  const hasYieldCalibration = isPositive(input.measuredYieldPerPlant);
  const hasCycleCalibration = isPositive(input.measuredCycleMinutes);

  return {
    cropId: profile.id,
    cropName: profile.name,
    outputItem: profile.outputItem,
    genetics,
    gCount,
    yCount,
    hCount,
    wCount,
    effectiveConditionPercent: condition.value,
    limitingCondition: condition.limiting,
    cycleMinutes: round1(
      hasCycleCalibration
        ? input.measuredCycleMinutes!
        : profile.baseCycleMinutes / growthSpeed / conditionFactor
    ),
    yieldPerPlant: round1(
      hasYieldCalibration
        ? input.measuredYieldPerPlant! * serverRate
        : profile.baseYieldPerPlant * yieldMultiplier * serverRate
    ),
    clonesPerPlant: Math.max(1, yCount),
    waterPerPlantPerMinuteMl: round1(profile.waterPerMinutePerPlantMl * (1 + wCount * 0.1)),
    confidence: hasYieldCalibration || hasCycleCalibration ? 'user' : profile.confidence
  };
}

export function planFarmFromGoal(input: GoalPlannerInput): FarmPlan {
  const safeInput = sanitizeGoalInput(input);
  const requirements = resolveFarmRequirements(safeInput.outputItem, safeInput.quantity);
  const warnings: string[] = [];

  if (!isValidGenetics(safeInput.genetics)) {
    return emptyPlan(safeInput, 'Enter a complete six-gene sequence.');
  }
  if (requirements.farmable.length === 0) {
    return emptyPlan(safeInput, `${safeInput.outputItem} has no supported farm-grown ingredients.`);
  }

  const crops = requirements.farmable.map((requirement) =>
    sizeCropForGoal(safeInput, requirement.cropId, requirement.quantity)
  );
  const totalPlanters = sum(crops.map((cropPlan) => cropPlan.planterCount));
  const totalSlots = sum(crops.map((cropPlan) => cropPlan.totalSlots));
  const totalCloneReservePlants = sum(crops.map((cropPlan) => cropPlan.cloneReservePlants));
  const goalOutputForPeriod =
    Math.min(...crops.map((cropPlan) => cropPlan.goalUnitsForPeriod)) * safeInput.quantity;
  const firstHarvestMinutes = Math.max(...crops.map((cropPlan) => cropPlan.cycleMinutes));
  const infrastructure = deriveInfrastructure({
    planterCount: totalPlanters,
    planterType: safeInput.planterType,
    waterDemandMlPerMinute: sum(crops.map((cropPlan) => cropPlan.waterDemandMlPerMinute)),
    waterSource: safeInput.waterSource,
    powerSource: safeInput.powerSource,
    bufferHours: safeInput.bufferHours,
    safetyMarginPercent: safeInput.safetyMarginPercent
  });

  crops.forEach((cropPlan) => {
    if (cropPlan.harvestPlants === 0) {
      warnings.push(`${cropPlan.cropName} has no harvest plants after reserving clones.`);
    }
    if (cropPlan.planterCount >= 200 && cropPlan.goalUnitsForPeriod < 1) {
      warnings.push(`${cropPlan.cropName} exceeds the 200-planter planning limit.`);
    }
  });
  if (effectiveCondition(safeInput.conditions).value < 90) {
    warnings.push('Low conditions materially slow the plan. Audit the limiting condition before building more planters.');
  }
  if (safeInput.waterSource === 'road-outlet' || safeInput.waterSource === 'custom') {
    warnings.push('Verify the selected water source flow in game; the planner sizes demand but does not assume a fixed supply.');
  }
  if (requirements.nonFarm.length > 0) {
    warnings.push('The recipe also needs non-farm ingredients listed in Production details.');
  }

  return {
    supported: true,
    goal: safeInput,
    crops,
    nonFarmIngredients: requirements.nonFarm,
    totalPlanters,
    totalSlots,
    totalCloneReservePlants,
    firstHarvestMinutes,
    goalOutputForPeriod: round1(goalOutputForPeriod),
    infrastructure,
    confidence: crops.some((cropPlan) => cropPlan.confidence === 'user') ? 'user' : 'community',
    warnings
  };
}

export function auditFarmSetup(input: AuditPlannerInput): FarmAudit {
  const safeInput = sanitizeAuditInput(input);
  const estimate = estimateCropCycle(safeInput);
  if (!estimate) {
    return {
      supported: false,
      input: safeInput,
      status: 'unsustainable',
      bottleneck: 'none',
      headline: 'This crop or genetics sequence is not modeled.',
      recommendation: 'Choose a supported crop and enter six valid genes.',
      waterMarginMlPerMinute: 0,
      powerMarginRw: 0,
      productionPerHour: 0,
      potentialProductionPerHour: 0,
      warnings: []
    };
  }

  const planterSlots = PLANTER_TYPES[safeInput.planterType].slots;
  const occupiedSlots = safeInput.planterCount * Math.min(planterSlots, safeInput.filledSlotsPerPlanter);
  const cropPlan = buildCropPlan(
    estimate,
    occupiedSlots,
    safeInput.planterCount,
    safeInput.reserveClones,
    'hour',
    1,
    1
  );
  const infrastructure = deriveInfrastructure({
    planterCount: safeInput.planterCount,
    planterType: safeInput.planterType,
    waterDemandMlPerMinute: cropPlan.waterDemandMlPerMinute,
    waterSource: safeInput.waterSource,
    powerSource: safeInput.powerSource,
    bufferHours: safeInput.bufferHours,
    safetyMarginPercent: safeInput.safetyMarginPercent
  });
  const waterMargin = round1(safeInput.availableWaterMlPerMinute - infrastructure.recommendedWaterFlowMlPerMinute);
  const powerMargin = round1(safeInput.availablePowerRw - infrastructure.powerWithSafetyMarginRw);
  const utilization = occupiedSlots / Math.max(1, safeInput.planterCount * planterSlots);
  const condition = effectiveCondition(safeInput.conditions);
  const fullConditionEstimate = estimateCropCycle({
    ...safeInput,
    conditions: { ...DEFAULT_CONDITIONS }
  });
  const fullConditionCrop = fullConditionEstimate
    ? buildCropPlan(
        fullConditionEstimate,
        occupiedSlots,
        safeInput.planterCount,
        safeInput.reserveClones,
        'hour',
        1,
        1
      )
    : cropPlan;

  let bottleneck: FarmAudit['bottleneck'] = 'none';
  let recommendation = 'The modeled setup has enough water and power. Keep the measured values updated after each patch.';
  if (cropPlan.harvestPlants === 0) {
    bottleneck = 'clones';
    recommendation = 'Use genetics with more Y genes or dedicate additional plants to clone production.';
  } else if (waterMargin < 0) {
    bottleneck = 'water';
    const supportedPlants = Math.max(
      0,
      Math.floor(safeInput.availableWaterMlPerMinute / Math.max(0.1, estimate.waterPerPlantPerMinuteMl))
    );
    recommendation = `Add ${Math.ceil(Math.abs(waterMargin))} ml/min of water capacity or reduce the farm to about ${supportedPlants} occupied slots.`;
  } else if (powerMargin < 0) {
    bottleneck = 'power';
    recommendation = `Add at least ${Math.ceil(Math.abs(powerMargin))} rW of sustained farm power.`;
  } else if (condition.value < 90) {
    bottleneck = 'conditions';
    recommendation = `Raise ${condition.limiting} from ${condition.value}% before adding more planters.`;
  } else if (utilization < 1) {
    bottleneck = 'capacity';
    const emptySlots = safeInput.planterCount * planterSlots - occupiedSlots;
    recommendation = `Fill the ${emptySlots} unused planter slot${emptySlots === 1 ? '' : 's'} before expanding the room.`;
  }

  const unsustainable = cropPlan.harvestPlants === 0 || waterMargin < 0 || powerMargin < 0;
  const atRisk =
    !unsustainable &&
    (waterMargin < infrastructure.recommendedWaterFlowMlPerMinute * 0.15 ||
      powerMargin < infrastructure.powerWithSafetyMarginRw * 0.15 ||
      condition.value < 90 ||
      utilization < 1);
  const status: FarmAudit['status'] = unsustainable ? 'unsustainable' : atRisk ? 'at-risk' : 'sustainable';
  const headline =
    status === 'sustainable'
      ? 'This setup is sustainable with the entered margins.'
      : status === 'at-risk'
        ? 'This setup works, but has little margin or unused capacity.'
        : 'This setup cannot sustain the modeled production.';

  return {
    supported: true,
    input: safeInput,
    crop: cropPlan,
    infrastructure,
    status,
    bottleneck,
    headline,
    recommendation,
    waterMarginMlPerMinute: waterMargin,
    powerMarginRw: powerMargin,
    productionPerHour: cropPlan.productionPerHour,
    potentialProductionPerHour: fullConditionCrop.productionPerHour,
    warnings: safeInput.waterSource === 'road-outlet' || safeInput.waterSource === 'custom'
      ? ['Available water must be measured in game for this source.']
      : []
  };
}

export function buildFarmPlanText(plan: FarmPlan): string {
  const period = timeBasisLabel(plan.goal.timeBasis, plan.goal.horizonHours);
  const cropLines = plan.crops.map((cropPlan) =>
    `- ${cropPlan.cropName}: ${cropPlan.planterCount} ${PLANTER_TYPES[plan.goal.planterType].name.toLowerCase()}${cropPlan.planterCount === 1 ? '' : 's'}, ${cropPlan.productionForPeriod.toLocaleString()} ${cropPlan.outputItem} ${period}`
  );
  const components = plan.infrastructure.components
    .filter((component) => component.quantity > 0)
    .map((component) => `- ${component.item}: ${component.quantity}`);
  const materials = plan.infrastructure.materials.map((material) => `- ${material.item}: ${material.quantity.toLocaleString()}`);

  return [
    `RUST FARM PLAN: ${plan.goal.quantity.toLocaleString()} ${plan.goal.outputItem} ${period}`,
    `Genetics: ${plan.goal.genetics}`,
    `Planters: ${plan.totalPlanters} (${plan.totalSlots} slots, ${plan.totalCloneReservePlants} clone reserve)`,
    `First harvest: ~${formatDuration(plan.firstHarvestMinutes)}`,
    '',
    'Production',
    ...cropLines,
    '',
    `Water: ${plan.infrastructure.recommendedWaterFlowMlPerMinute.toLocaleString()} ml/min recommended`,
    `Power: ${plan.infrastructure.powerWithSafetyMarginRw.toLocaleString()} rW with safety margin`,
    '',
    'Components',
    ...components,
    '',
    'Materials',
    ...materials,
    '',
    `Confidence: ${plan.confidence === 'user' ? 'user calibrated' : 'community estimate'}`
  ].join('\n');
}

export function formatDuration(minutes: number): string {
  if (!Number.isFinite(minutes) || minutes <= 0) return '0 min';
  if (minutes < 60) return `${Math.round(minutes)} min`;
  const hours = Math.floor(minutes / 60);
  const remaining = Math.round(minutes % 60);
  return remaining ? `${hours}h ${remaining}m` : `${hours}h`;
}

export function timeBasisLabel(timeBasis: FarmTimeBasis, horizonHours: number): string {
  if (timeBasis === 'harvest') return 'per harvest';
  if (timeBasis === 'hour') return 'per hour';
  if (timeBasis === 'day') return 'per day';
  return `within ${round1(horizonHours)} hours`;
}

function resolveFarmRequirements(outputItem: string, quantity: number): {
  farmable: Array<{ cropId: string; quantity: number }>;
  nonFarm: Array<{ item: string; quantity: number }>;
} {
  const directCrop = FARM_ITEM_TO_CROP.get(outputItem.toLowerCase());
  if (directCrop) {
    return { farmable: [{ cropId: directCrop, quantity }], nonFarm: [] };
  }

  const expanded = recipeEngine.expandItem(outputItem, quantity);
  const farmableMap = new Map<string, number>();
  const nonFarmMap = new Map<string, number>();
  expanded.forEach((ingredient) => {
    const cropId = FARM_ITEM_TO_CROP.get(ingredient.item.toLowerCase());
    const target = cropId ? farmableMap : nonFarmMap;
    const key = cropId || ingredient.item;
    target.set(key, (target.get(key) || 0) + ingredient.quantity);
  });

  return {
    farmable: Array.from(farmableMap, ([cropId, required]) => ({ cropId, quantity: round1(required) })),
    nonFarm: Array.from(nonFarmMap, ([item, required]) => ({ item, quantity: round1(required) }))
  };
}

function sizeCropForGoal(input: GoalPlannerInput, cropId: string, requiredRawOutput: number): CropPlan {
  const slots = PLANTER_TYPES[input.planterType].slots;
  const estimate = estimateCropCycle({
    cropId,
    genetics: input.genetics,
    conditions: input.conditions,
    serverRate: input.serverRate,
    measuredYieldPerPlant: input.measuredYieldPerPlant,
    measuredCycleMinutes: input.measuredCycleMinutes
  });
  if (!estimate) {
    throw new Error(`Unsupported farm crop: ${cropId}`);
  }

  let selected = buildCropPlan(estimate, slots, 1, input.reserveClones, input.timeBasis, input.horizonHours, requiredRawOutput);
  for (let planterCount = 1; planterCount <= 200; planterCount += 1) {
    const candidate = buildCropPlan(
      estimate,
      planterCount * slots,
      planterCount,
      input.reserveClones,
      input.timeBasis,
      input.horizonHours,
      requiredRawOutput
    );
    selected = candidate;
    if (candidate.productionForPeriod >= requiredRawOutput) break;
  }
  return selected;
}

function buildCropPlan(
  estimate: CropCycleEstimate,
  totalSlots: number,
  planterCount: number,
  reserveClones: boolean,
  timeBasis: FarmTimeBasis,
  horizonHours: number,
  requiredPerGoalUnit: number
): CropPlan {
  const cloneReservePlants = reserveClones ? Math.ceil(totalSlots / estimate.clonesPerPlant) : 0;
  const harvestPlants = Math.max(0, totalSlots - cloneReservePlants);
  const harvestPerCycle = round1(harvestPlants * estimate.yieldPerPlant);
  const cyclesForPeriod = cyclesForTimeBasis(timeBasis, estimate.cycleMinutes, horizonHours);
  const productionForPeriod = round1(harvestPerCycle * cyclesForPeriod);
  const productionPerHour = round1(harvestPerCycle * (60 / estimate.cycleMinutes));
  const waterDemandMlPerMinute = round1(totalSlots * estimate.waterPerPlantPerMinuteMl);

  return {
    ...estimate,
    requiredPerGoalUnit,
    planterCount,
    totalSlots,
    cloneReservePlants,
    harvestPlants,
    harvestPerCycle,
    productionForPeriod,
    productionPerHour,
    cyclesForPeriod: round1(cyclesForPeriod),
    goalUnitsForPeriod: requiredPerGoalUnit > 0 ? round1(productionForPeriod / requiredPerGoalUnit) : 0,
    waterDemandMlPerMinute,
    waterPerCycleLiters: round1((waterDemandMlPerMinute * estimate.cycleMinutes) / 1000)
  };
}

function cyclesForTimeBasis(timeBasis: FarmTimeBasis, cycleMinutes: number, horizonHours: number): number {
  if (timeBasis === 'harvest') return 1;
  if (timeBasis === 'hour') return 60 / cycleMinutes;
  if (timeBasis === 'day') return (24 * 60) / cycleMinutes;
  return Math.floor(Math.max(0, horizonHours) * 60 / cycleMinutes);
}

function deriveInfrastructure(input: {
  planterCount: number;
  planterType: PlanterType;
  waterDemandMlPerMinute: number;
  waterSource: WaterSource;
  powerSource: PowerSource;
  bufferHours: number;
  safetyMarginPercent: number;
}): InfrastructurePlan {
  const planterCount = Math.max(0, Math.ceil(input.planterCount));
  const sprinklers = planterCount ? Math.ceil(planterCount / 4) : 0;
  const lights = planterCount;
  let waterPumps = 0;
  let poweredPurifiers = 0;
  let waterBarrels = 0;

  if (input.waterSource === 'fresh-pump') waterPumps = Math.ceil(sprinklers / 4);
  if (input.waterSource === 'salt-purifier') {
    poweredPurifiers = Math.ceil(sprinklers / 4);
    waterPumps = poweredPurifiers * 2;
  }
  const storedWaterLiters = round1(input.waterDemandMlPerMinute * Math.max(0, input.bufferHours) * 60 / 1000);
  if (input.waterSource === 'catcher-barrel') {
    // Community-observed storage size. Kept explicit and easy to update.
    waterBarrels = Math.max(1, Math.ceil(storedWaterLiters / 20));
  }

  const powerDrawRw = lights * 2 + waterPumps * 5 + poweredPurifiers * 5;
  const powerWithSafetyMarginRw = Math.ceil(powerDrawRw * (1 + Math.max(0, input.safetyMarginPercent) / 100));
  const solarPanelsPeak = input.powerSource === 'solar-battery' ? Math.ceil(powerWithSafetyMarginRw / 20) : 0;
  const solarPanelsSuggested = input.powerSource === 'solar-battery' ? Math.ceil(powerWithSafetyMarginRw / 10) : 0;
  const largeBatteries = ['solar-battery', 'wind-battery'].includes(input.powerSource)
    ? Math.max(
        powerWithSafetyMarginRw > 0 ? Math.ceil(powerWithSafetyMarginRw / 100) : 0,
        powerWithSafetyMarginRw > 0 ? Math.ceil(powerWithSafetyMarginRw * 12 * 60 / 24000) : 0
      )
    : 0;
  const recommendedWaterFlowMlPerMinute = Math.ceil(
    input.waterDemandMlPerMinute * (1 + Math.max(0, input.safetyMarginPercent) / 100)
  );

  const components = [
    { item: PLANTER_TYPES[input.planterType].name, quantity: planterCount },
    { item: 'Ceiling Light', quantity: lights },
    { item: 'Sprinkler', quantity: sprinklers, note: 'Assumes coverage of four tightly arranged planters.' },
    { item: 'Water Pump', quantity: waterPumps },
    { item: 'Powered Water Purifier', quantity: poweredPurifiers },
    { item: 'Water Barrel', quantity: waterBarrels },
    { item: 'Large Solar Panel', quantity: solarPanelsSuggested },
    { item: 'Large Rechargeable Battery', quantity: largeBatteries }
  ].filter((component) => component.quantity > 0);

  return {
    layout: layoutModules(planterCount),
    components,
    materials: deriveMaterials(input.planterType, {
      planters: planterCount,
      lights,
      sprinklers,
      waterPumps,
      poweredPurifiers,
      waterBarrels,
      solarPanels: solarPanelsSuggested,
      batteries: largeBatteries
    }),
    lights,
    sprinklers,
    waterPumps,
    poweredPurifiers,
    waterBarrels,
    powerDrawRw,
    powerWithSafetyMarginRw,
    solarPanelsPeak,
    solarPanelsSuggested,
    largeBatteries,
    waterDemandMlPerMinute: round1(input.waterDemandMlPerMinute),
    recommendedWaterFlowMlPerMinute,
    storedWaterLiters
  };
}

function deriveMaterials(
  planterType: PlanterType,
  counts: {
    planters: number;
    lights: number;
    sprinklers: number;
    waterPumps: number;
    poweredPurifiers: number;
    waterBarrels: number;
    solarPanels: number;
    batteries: number;
  }
): BuildMaterial[] {
  const totals = new Map<string, number>();
  const add = (item: string, quantity: number) => totals.set(item, (totals.get(item) || 0) + quantity);
  const planterCost = planterType === 'large'
    ? { wood: 200, tarp: 2 }
    : planterType === 'triangle'
      ? { wood: 150, tarp: 1 }
      : { wood: 100, tarp: 1 };

  add('Wood', counts.planters * planterCost.wood);
  add('Tarp', counts.planters * planterCost.tarp);
  add('Metal Fragments', counts.lights * 50 + counts.sprinklers * 75);
  add('Wood', counts.waterPumps * 250);
  add('Metal Fragments', counts.waterPumps * 200);
  add('Gears', counts.waterPumps);
  add('Wood', counts.poweredPurifiers * 100);
  add('Metal Fragments', counts.poweredPurifiers * 300);
  add('Cloth', counts.poweredPurifiers * 20);
  add('Wood', counts.waterBarrels * 250);
  add('Tarp', counts.waterBarrels);
  add('High Quality Metal', counts.solarPanels * 5 + counts.batteries * 10);
  add('Tech Trash', counts.solarPanels + counts.batteries * 2);

  return Array.from(totals, ([item, quantity]) => ({ item, quantity }))
    .filter((material) => material.quantity > 0)
    .sort((a, b) => b.quantity - a.quantity || a.item.localeCompare(b.item));
}

function layoutModules(planterCount: number): InfrastructurePlan['layout'] {
  const sixPlanterModules = Math.floor(planterCount / 6);
  const afterSix = planterCount % 6;
  const threePlanterModules = afterSix >= 3 ? 1 : 0;
  return {
    sixPlanterModules,
    threePlanterModules,
    remainderPlanters: afterSix - threePlanterModules * 3
  };
}

function sanitizeGoalInput(input: GoalPlannerInput): GoalPlannerInput {
  return {
    ...input,
    quantity: Math.max(0.01, Number(input.quantity) || 0.01),
    horizonHours: Math.max(0.1, Number(input.horizonHours) || 0.1),
    genetics: normalizeGenetics(input.genetics),
    conditions: sanitizeConditions(input.conditions),
    serverRate: Math.max(0.01, Number(input.serverRate) || 1),
    bufferHours: Math.max(0, Number(input.bufferHours) || 0),
    safetyMarginPercent: Math.max(0, Number(input.safetyMarginPercent) || 0),
    measuredYieldPerPlant: optionalPositive(input.measuredYieldPerPlant),
    measuredCycleMinutes: optionalPositive(input.measuredCycleMinutes)
  };
}

function sanitizeAuditInput(input: AuditPlannerInput): AuditPlannerInput {
  const planterSlots = PLANTER_TYPES[input.planterType]?.slots || 9;
  return {
    ...input,
    genetics: normalizeGenetics(input.genetics),
    planterCount: Math.max(1, Math.floor(Number(input.planterCount) || 1)),
    filledSlotsPerPlanter: Math.min(planterSlots, Math.max(1, Math.floor(Number(input.filledSlotsPerPlanter) || 1))),
    conditions: sanitizeConditions(input.conditions),
    serverRate: Math.max(0.01, Number(input.serverRate) || 1),
    bufferHours: Math.max(0, Number(input.bufferHours) || 0),
    safetyMarginPercent: Math.max(0, Number(input.safetyMarginPercent) || 0),
    availableWaterMlPerMinute: Math.max(0, Number(input.availableWaterMlPerMinute) || 0),
    availablePowerRw: Math.max(0, Number(input.availablePowerRw) || 0),
    measuredYieldPerPlant: optionalPositive(input.measuredYieldPerPlant),
    measuredCycleMinutes: optionalPositive(input.measuredCycleMinutes)
  };
}

function sanitizeConditions(conditions: FarmConditions): FarmConditions {
  return {
    light: clampPercent(conditions.light),
    water: clampPercent(conditions.water),
    temperature: clampPercent(conditions.temperature),
    ground: clampPercent(conditions.ground)
  };
}

function emptyPlan(goal: GoalPlannerInput, warning: string): FarmPlan {
  return {
    supported: false,
    goal,
    crops: [],
    nonFarmIngredients: [],
    totalPlanters: 0,
    totalSlots: 0,
    totalCloneReservePlants: 0,
    firstHarvestMinutes: 0,
    goalOutputForPeriod: 0,
    infrastructure: deriveInfrastructure({
      planterCount: 0,
      planterType: goal.planterType,
      waterDemandMlPerMinute: 0,
      waterSource: goal.waterSource,
      powerSource: goal.powerSource,
      bufferHours: goal.bufferHours,
      safetyMarginPercent: goal.safetyMarginPercent
    }),
    confidence: 'community',
    warnings: [warning]
  };
}

function countGene(genetics: string, gene: string): number {
  return genetics.split('').filter((value) => value === gene).length;
}

function clampPercent(value: number): number {
  return Math.min(100, Math.max(0, Number(value) || 0));
}

function optionalPositive(value: number | undefined): number | undefined {
  return isPositive(value) ? Number(value) : undefined;
}

function isPositive(value: number | undefined): boolean {
  return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function round1(value: number): number {
  return Math.round(value * 10) / 10;
}

function sum(values: number[]): number {
  return values.reduce((total, value) => total + value, 0);
}
