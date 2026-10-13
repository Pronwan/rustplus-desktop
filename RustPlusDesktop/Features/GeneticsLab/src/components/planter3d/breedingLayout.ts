import type { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import type { Sapling } from '../../domain/genetics/Sapling.ts';
import { Sapling as SaplingClass, DEFAULT_GENE_SCORES } from '../../domain/genetics/Sapling.ts';
import { evaluateCombination } from '../../domain/genetics/crossbreeding.ts';
import { neighboursOf, type PlanterModel } from './planterModels.ts';

export interface BreedingPlan {
  /** Genes of the plant to put in the centre first; null when any plant will do. */
  target: string | null;
  donors: string[];
  result: string;
}

const geneString = (sapling: Sapling) => sapling.genes.map((g) => g.type).join('');

export function breedingPlanFromMap(map: GeneticsMap): BreedingPlan {
  return {
    target: map.baseSapling ? geneString(map.baseSapling) : null,
    donors: map.crossbreedingSaplings.map(geneString),
    result: geneString(map.resultSapling)
  };
}

/**
 * Slots for `count` donors around the planter's centre: the closest (edge) slots first,
 * then the diagonals, matching how the plan is usually planted by hand.
 */
export function donorSlotsFor(model: PlanterModel, count: number): number[] {
  const centre = model.slots[model.centerSlot];
  return neighboursOf(model, model.centerSlot)
    .map((i) => ({ i, d: Math.hypot(model.slots[i].x - centre.x, model.slots[i].z - centre.z) }))
    .sort((a, b) => a.d - b.d || a.i - b.i)
    .slice(0, count)
    .map(({ i }) => i);
}

// Four donors that breed GGGYYY: green G and Y outvote the lone red gene in each column.
export const DEMO_DONORS = ['GGXYYH', 'GGGYHY', 'XGGYYY', 'GHGWYY'];

let demo: BreedingPlan | null | undefined;

/** A worked example for the guide, computed by the same code as real plans. */
export function demoBreedingPlan(): BreedingPlan | null {
  if (demo !== undefined) return demo;
  const donors = DEMO_DONORS.map((genes, i) => new SaplingClass(genes, 0, i));
  const maps = evaluateCombination(
    donors,
    donors,
    new Set<string>(),
    { geneScores: DEFAULT_GENE_SCORES, minimumTrackedScore: Number.NEGATIVE_INFINITY },
    1
  );
  demo = maps[0] ? breedingPlanFromMap(maps[0]) : null;
  return demo;
}
