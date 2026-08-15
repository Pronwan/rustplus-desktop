export interface CropGrowthInfo {
  id: string;
  name: string;
  baseYieldPerPlant: number;
  baseCycleMinutes: number;
  waterPerMinutePerPlantMl: number;
  fertilizerBenefit: string;
}

export const CROP_GROWTH_DATABASE: Record<string, CropGrowthInfo> = {
  'hemp': {
    id: 'hemp',
    name: 'Hemp (Cloth)',
    baseYieldPerPlant: 35,
    baseCycleMinutes: 90,
    waterPerMinutePerPlantMl: 5.5,
    fertilizerBenefit: '+20% growth speed'
  },
  'red-berry': {
    id: 'red-berry',
    name: 'Red Berry',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'blue-berry': {
    id: 'blue-berry',
    name: 'Blue Berry',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'yellow-berry': {
    id: 'yellow-berry',
    name: 'Yellow Berry',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'green-berry': {
    id: 'green-berry',
    name: 'Green Berry',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'white-berry': {
    id: 'white-berry',
    name: 'White Berry',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'mixed-berry': {
    id: 'mixed-berry',
    name: 'Mixed Berries',
    baseYieldPerPlant: 4,
    baseCycleMinutes: 80,
    waterPerMinutePerPlantMl: 4.8,
    fertilizerBenefit: '+15% yield'
  },
  'potato': {
    id: 'potato',
    name: 'Potato',
    baseYieldPerPlant: 3,
    baseCycleMinutes: 85,
    waterPerMinutePerPlantMl: 6.0,
    fertilizerBenefit: '+25% yield'
  },
  'pumpkin': {
    id: 'pumpkin',
    name: 'Pumpkin',
    baseYieldPerPlant: 1,
    baseCycleMinutes: 100,
    waterPerMinutePerPlantMl: 7.0,
    fertilizerBenefit: '+15% growth speed'
  },
  'corn': {
    id: 'corn',
    name: 'Corn',
    baseYieldPerPlant: 2,
    baseCycleMinutes: 95,
    waterPerMinutePerPlantMl: 6.5,
    fertilizerBenefit: '+20% yield'
  }
};

export interface FarmOutputEstimation {
  cropName: string;
  totalPlanters: number;
  plantsPerPlanter: number;
  totalPlants: number;
  gCount: number;
  yCount: number;
  hCount: number;
  estimatedCycleDurationMinutes: number;
  estimatedYieldPerPlant: number;
  estimatedHarvestPerCycle: number;
  estimatedTotalHarvest: number;
  estimatedWaterNeededPerCycleLiters: number;
  isEstimate: true;
}

export function estimateFarmOutput(params: {
  cropType: string;
  genetics: string;
  planterCount: number;
  plantsPerPlanter?: number; // default 9 for large planter
  cycles?: number;
  optimalConditions?: boolean;
}): FarmOutputEstimation {
  const crop = CROP_GROWTH_DATABASE[params.cropType] || CROP_GROWTH_DATABASE['hemp'];
  const plantsPerPlanter = params.plantsPerPlanter || 9;
  const totalPlants = params.planterCount * plantsPerPlanter;
  const cycles = params.cycles || 1;

  const genes = params.genetics.toUpperCase();
  let gCount = 0;
  let yCount = 0;
  let hCount = 0;

  for (const char of genes) {
    if (char === 'G') gCount++;
    if (char === 'Y') yCount++;
    if (char === 'H') hCount++;
  }

  // Growth speed formula: each G reduces cycle time by ~9%, optimal environment reduces by up to 15%
  const growthMultiplier = Math.max(0.4, 1 - gCount * 0.09 - (params.optimalConditions ? 0.15 : 0));
  const estimatedCycleDurationMinutes = Math.round(crop.baseCycleMinutes * growthMultiplier);

  // Yield formula: each Y increases yield by ~25%
  const yieldMultiplier = 1 + yCount * 0.25 + (params.optimalConditions ? 0.2 : 0);
  const estimatedYieldPerPlant = Math.round(crop.baseYieldPerPlant * yieldMultiplier * 10) / 10;

  const estimatedHarvestPerCycle = Math.round(totalPlants * estimatedYieldPerPlant);
  const estimatedTotalHarvest = estimatedHarvestPerCycle * cycles;

  // Water requirement: ml per min * cycle duration / 1000 to get liters
  // H genes reduce water consumption by ~5% per H
  const waterEfficiency = Math.max(0.65, 1 - hCount * 0.05);
  const totalWaterMl = totalPlants * crop.waterPerMinutePerPlantMl * estimatedCycleDurationMinutes * waterEfficiency;
  const estimatedWaterNeededPerCycleLiters = Math.round(totalWaterMl / 1000);

  return {
    cropName: crop.name,
    totalPlanters: params.planterCount,
    plantsPerPlanter,
    totalPlants,
    gCount,
    yCount,
    hCount,
    estimatedCycleDurationMinutes,
    estimatedYieldPerPlant,
    estimatedHarvestPerCycle,
    estimatedTotalHarvest,
    estimatedWaterNeededPerCycleLiters,
    isEstimate: true
  };
}
