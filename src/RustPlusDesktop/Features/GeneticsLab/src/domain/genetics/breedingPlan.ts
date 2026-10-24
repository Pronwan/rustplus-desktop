import { GeneticsMap } from './GeneticsMap.ts';

export interface BreedingPlanStep {
  generationIndex: number;
  targetGeneString: string;
  centerSaplingString?: string;
  centerSourceIndex?: number;
  surroundingSaplingsStrings: string[];
  surroundingSourceIndexes: Array<number | undefined>;
  priorityWinningIndices?: number[];
  priorityLosingIndices?: number[];
  chance: number;
}

/** Returns every breeding run in dependency-first execution order. */
export function buildBreedingPlan(routeMap: GeneticsMap): BreedingPlanStep[] {
  const steps: BreedingPlanStep[] = [];

  function visit(map: GeneticsMap): void {
    if (map.baseSapling?.generationIndex && map.baseSaplingVariants?.mapList[0]) {
      visit(map.baseSaplingVariants.mapList[0]);
    }

    map.crossbreedingSaplings.forEach((parent, index) => {
      if (parent.generationIndex > 0 && map.crossbreedingSaplingsVariants?.[index]?.mapList[0]) {
        visit(map.crossbreedingSaplingsVariants[index].mapList[0]);
      }
    });

    steps.push({
      generationIndex: map.resultSapling.generationIndex || 1,
      targetGeneString: map.resultSapling.toString(),
      centerSaplingString: map.baseSapling?.toString(),
      centerSourceIndex: map.baseSapling?.index,
      surroundingSaplingsStrings: map.crossbreedingSaplings.map(sapling => sapling.toString()),
      surroundingSourceIndexes: map.crossbreedingSaplings.map(sapling => sapling.index),
      priorityWinningIndices: map.tieWinningCrossbreedingSaplingIndexes,
      priorityLosingIndices: map.tieLosingCrossbreedingSaplingIndexes,
      chance: map.chance
    });
  }

  visit(routeMap);
  return steps;
}
