// Planter and plant models for the 3D planting guide.
//
// Slot positions are in each planter model's own space (metres, Y up), measured from
// the soil mesh bounds of the Facepunch models in public/models/planters.

import type { PlanterType } from '../../domain/planner/farmPlanning.ts';

export interface PlanterSlot {
  /** 1-based number shown to the user, read left-to-right, back-to-front. */
  number: number;
  x: number;
  z: number;
  /** Short role in the classic breeding layout. */
  role: 'center' | 'edge' | 'corner' | 'end';
}

export interface PlanterModel {
  type: PlanterType;
  name: string;
  url: string;
  /** Height of the soil surface the plants stand on. */
  soilY: number;
  slots: PlanterSlot[];
  /** Index of the slot a breeding plan puts the target plant in. */
  centerSlot: number;
  /** Camera position that frames the whole planter. */
  camera: [number, number, number];
}

// Slots closer than this share genes during crossbreeding: orthogonal and diagonal
// neighbours in the 3x3 grid (0.93 m and 1.31 m apart), but not the far ends of a row.
export const NEIGHBOUR_DISTANCE = 1.45;

const LARGE_STEP = 0.93;

export const PLANTER_MODELS: Record<PlanterType, PlanterModel> = {
  large: {
    type: 'large',
    name: 'Large Planter',
    url: './models/planters/large.glb',
    soilY: 0.4,
    slots: [-1, 0, 1].flatMap((row, r) =>
      [-1, 0, 1].map((col, c) => ({
        number: r * 3 + c + 1,
        x: col * LARGE_STEP,
        z: row * LARGE_STEP,
        role: row === 0 && col === 0 ? 'center' : row === 0 || col === 0 ? 'edge' : 'corner'
      }))
    ),
    centerSlot: 4,
    camera: [2.6, 3.1, 3.2]
  },
  small: {
    type: 'small',
    name: 'Small Planter',
    url: './models/planters/small.glb',
    soilY: 0.4,
    slots: [-1, 0, 1].map((col, i) => ({
      number: i + 1,
      x: col * LARGE_STEP,
      z: 0,
      role: col === 0 ? 'center' : 'end'
    })),
    centerSlot: 1,
    camera: [2.1, 2.2, 2.6]
  },
  // Soil triangle: vertices (-0.52, ±1.17) and (1.48, 0); centroid at x ≈ 0.15.
  // One slot at the centroid, one toward each corner.
  triangle: {
    type: 'triangle',
    name: 'Triangle Planter',
    url: './models/planters/triangle.glb',
    soilY: 0.44,
    slots: [
      { number: 1, x: -0.22, z: -0.64, role: 'corner' },
      { number: 2, x: -0.22, z: 0.64, role: 'corner' },
      { number: 3, x: 0.15, z: 0, role: 'center' },
      { number: 4, x: 0.88, z: 0, role: 'corner' }
    ],
    centerSlot: 2,
    camera: [2.4, 2.6, 2.6]
  }
};

/** Slots that exchange genes with `slotIndex` during crossbreeding. */
export function neighboursOf(model: PlanterModel, slotIndex: number): number[] {
  const slot = model.slots[slotIndex];
  return model.slots
    .map((other, i) => ({ i, d: Math.hypot(other.x - slot.x, other.z - slot.z) }))
    .filter(({ i, d }) => i !== slotIndex && d <= NEIGHBOUR_DISTANCE)
    .map(({ i }) => i);
}

/** Which collectable model stands in for each Genetics Lab plant type. */
export const PLANT_MODEL_URLS: Record<string, string> = {
  hemp: './models/plants/hemp.glb',
  // Facepunch exports every berry bush with the same mesh and texture (the game colours
  // the berries in a shader), so one model serves all of them; BERRY_TINTS recolours it.
  'red-berry': './models/plants/berry.glb',
  'blue-berry': './models/plants/berry.glb',
  'yellow-berry': './models/plants/berry.glb',
  'green-berry': './models/plants/berry.glb',
  'white-berry': './models/plants/berry.glb',
  'mixed-berry': './models/plants/berry.glb',
  potato: './models/plants/potato.glb',
  pumpkin: './models/plants/pumpkin.glb',
  corn: './models/plants/corn.glb',
  wheat: './models/plants/wheat.glb',
  sunflower: './models/plants/sunflower.glb',
  rose: './models/plants/rose.glb',
  orchid: './models/plants/orchid.glb'
};

/** Berry colour per bush, as [r, g, b] at full brightness; 'mixed' mixes red, blue and yellow. */
export const BERRY_TINTS: Record<string, [number, number, number] | 'mixed'> = {
  'red-berry': [205, 35, 45],
  'blue-berry': [60, 95, 210],
  'yellow-berry': [238, 192, 40],
  'green-berry': [96, 178, 52],
  'white-berry': [240, 240, 234],
  'mixed-berry': 'mixed'
};

export type GrowthStage = 'seed' | 'seedling' | 'sapling' | 'mature' | 'fruiting' | 'dying';

/** Rust's growth stages in order (crossbreeding happens between sapling and mature). */
export const GROWTH_STAGES: { id: GrowthStage; label: string }[] = [
  { id: 'seed', label: 'Seed' },
  { id: 'seedling', label: 'Seedling' },
  { id: 'sapling', label: 'Sapling' },
  { id: 'mature', label: 'Mature' },
  { id: 'fruiting', label: 'Fruiting' },
  { id: 'dying', label: 'Dying' }
];

/**
 * Real per-stage models, where the library has them (only pumpkin). Plants without them
 * scale their fully grown model by STAGE_SCALE instead.
 */
export const STAGE_MODEL_URLS: Record<string, Record<GrowthStage, string>> = {
  pumpkin: {
    seed: './models/plants/pumpkin/seed.glb',
    seedling: './models/plants/pumpkin/seedling.glb',
    sapling: './models/plants/pumpkin/sapling.glb',
    mature: './models/plants/pumpkin/mature.glb',
    fruiting: './models/plants/pumpkin/fruiting.glb',
    dying: './models/plants/pumpkin/dying.glb'
  }
};

export const STAGE_SCALE: Record<GrowthStage, number> = {
  seed: 0.12,
  seedling: 0.28,
  sapling: 0.55,
  mature: 0.85,
  fruiting: 1,
  dying: 0.95
};

export const GENE_COLORS = {
  green: '#598518',
  red: '#94382A'
} as const;
