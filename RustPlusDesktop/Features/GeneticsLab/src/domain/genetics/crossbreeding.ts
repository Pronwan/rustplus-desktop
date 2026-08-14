import { Gene, GeneType, GREEN_GENE_WEIGHT, RED_GENE_WEIGHT } from './Gene.ts';
import { Sapling, GeneScores } from './Sapling.ts';
import { GeneticsMap } from './GeneticsMap.ts';

export interface CrossbreedingOptions {
  geneScores: GeneScores;
  minimumTrackedScore: number;
}

interface ColumnWeights {
  weightsByType: Map<GeneType, number>;
  maxWeight: number;
  winningTypes: GeneType[];
  isDefinitiveTie: boolean;
  contributingSaplingIndexesByType: Map<GeneType, number[]>;
}

/**
 * Calculates weights and winners for a single gene column across surrounding plants.
 */
function calculateColumnWeights(
  surroundingSaplings: Sapling[],
  columnIndex: number
): ColumnWeights {
  const weightsByType = new Map<GeneType, number>();
  const contributingSaplingIndexesByType = new Map<GeneType, number[]>();

  for (let sIdx = 0; sIdx < surroundingSaplings.length; sIdx++) {
    const gene = surroundingSaplings[sIdx].genes[columnIndex];
    const type = gene.type;
    const weight = gene.getCrossbreedingWeight();

    const currentWeight = weightsByType.get(type) ?? 0;
    weightsByType.set(type, Math.round((currentWeight + weight) * 100) / 100);

    const indexes = contributingSaplingIndexesByType.get(type) ?? [];
    indexes.push(sIdx);
    contributingSaplingIndexesByType.set(type, indexes);
  }

  let maxWeight = 0;
  for (const weight of weightsByType.values()) {
    if (weight > maxWeight) {
      maxWeight = weight;
    }
  }

  const winningTypes: GeneType[] = [];
  for (const [type, weight] of weightsByType.entries()) {
    if (Math.abs(weight - maxWeight) < 0.001) {
      winningTypes.push(type);
    }
  }

  // Definitive tie: > 1 type tied for max weight AND max weight > 1.0 (red gene weight)
  const isDefinitiveTie = winningTypes.length > 1 && maxWeight > RED_GENE_WEIGHT;

  return {
    weightsByType,
    maxWeight,
    winningTypes,
    isDefinitiveTie,
    contributingSaplingIndexesByType
  };
}

/**
 * Evaluates a combination of surrounding saplings and optional center sapling.
 * Produces valid GeneticsMap[] if combination passes all rules and score threshold.
 */
export function evaluateCombination(
  surroundingSaplings: Sapling[],
  allSourceSaplings: Sapling[],
  existingGenotypeStrings: Set<string>,
  options: CrossbreedingOptions,
  generationIndex: number
): GeneticsMap[] {
  const columnData: ColumnWeights[] = [];
  let definitiveTieCount = 0;
  let definitiveTieColumnIndex = -1;

  for (let col = 0; col < 6; col++) {
    const data = calculateColumnWeights(surroundingSaplings, col);
    columnData.push(data);

    if (data.isDefinitiveTie) {
      definitiveTieCount++;
      definitiveTieColumnIndex = col;
    }
  }

  // Rule A: reject if more than one definitive tie
  if (definitiveTieCount > 1) {
    return [];
  }

  // Rule B: every surrounding plant must contribute to at least one winning type
  const contributingPlantIndices = new Set<number>();
  for (let col = 0; col < 6; col++) {
    for (const winType of columnData[col].winningTypes) {
      const idxs = columnData[col].contributingSaplingIndexesByType.get(winType) ?? [];
      for (const idx of idxs) {
        contributingPlantIndices.add(idx);
      }
    }
  }

  if (contributingPlantIndices.size !== surroundingSaplings.length) {
    return [];
  }

  // Check if center plant needs evaluation
  // Condition: surrounding count <= 5 AND at least one winning total <= 1.0
  const needsCenterCheck =
    surroundingSaplings.length <= 5 &&
    columnData.some(c => c.maxWeight <= RED_GENE_WEIGHT);

  const results: GeneticsMap[] = [];

  if (!needsCenterCheck) {
    // No center plant needed
    const maps = buildMapsForOutcome(
      surroundingSaplings,
      undefined,
      columnData,
      definitiveTieColumnIndex,
      existingGenotypeStrings,
      options,
      generationIndex
    );
    results.push(...maps);
  } else {
    // Center plant is needed: evaluate each source plant NOT in the surrounding set
    const usedIndices = new Set<number>();
    for (const s of surroundingSaplings) {
      if (s.index !== undefined) {
        usedIndices.add(s.index);
      }
    }

    const candidateCenters = allSourceSaplings.filter(
      s => s.index === undefined || !usedIndices.has(s.index)
    );

    for (const center of candidateCenters) {
      const maps = buildMapsForOutcome(
        surroundingSaplings,
        center,
        columnData,
        definitiveTieColumnIndex,
        existingGenotypeStrings,
        options,
        generationIndex
      );
      results.push(...maps);
    }
  }

  return results;
}

/**
 * Builds GeneticsMap instances given surrounding weights and optional center plant.
 */
function buildMapsForOutcome(
  surroundingSaplings: Sapling[],
  centerSapling: Sapling | undefined,
  columnData: ColumnWeights[],
  definitiveTieColumnIndex: number,
  existingGenotypeStrings: Set<string>,
  options: CrossbreedingOptions,
  generationIndex: number
): GeneticsMap[] {
  // Determine winning gene type for each column (or candidate branches if tie)
  type Branch = {
    genes: GeneType[];
    tieWinningIndexes?: number[];
    tieLosingIndexes?: number[];
  };

  let branches: Branch[] = [{ genes: [] }];

  for (let col = 0; col < 6; col++) {
    const colInfo = columnData[col];
    let columnWinningTypes: GeneType[];

    if (centerSapling) {
      const centerGene = centerSapling.genes[col];
      const centerWeight = centerGene.getCrossbreedingWeight();

      // Rule: Center gene survives when centerGeneWeight >= winningSurroundingTotalWeight
      if (centerWeight >= colInfo.maxWeight) {
        columnWinningTypes = [centerGene.type];
      } else {
        columnWinningTypes = colInfo.winningTypes;
      }
    } else {
      columnWinningTypes = colInfo.winningTypes;
    }

    if (col === definitiveTieColumnIndex && columnWinningTypes.length > 1) {
      // Tie branching
      const newBranches: Branch[] = [];
      for (const branch of branches) {
        for (const tiedType of columnWinningTypes) {
          const winIndexes = colInfo.contributingSaplingIndexesByType.get(tiedType) ?? [];
          const loseIndexes: number[] = [];
          for (const otherType of columnWinningTypes) {
            if (otherType !== tiedType) {
              const otherIdxs = colInfo.contributingSaplingIndexesByType.get(otherType) ?? [];
              loseIndexes.push(...otherIdxs);
            }
          }

          newBranches.push({
            genes: [...branch.genes, tiedType],
            tieWinningIndexes: winIndexes,
            tieLosingIndexes: loseIndexes
          });
        }
      }
      branches = newBranches;
    } else {
      // Single winner (or non-definitive tie that resolved to first winner)
      const chosenType = columnWinningTypes[0];
      for (const branch of branches) {
        branch.genes.push(chosenType);
      }
    }
  }

  const localChance = 1 / branches.length;
  const validMaps: GeneticsMap[] = [];

  for (const branch of branches) {
    const geneString = branch.genes.join('');

    // Discard if genotype already owned in source set
    if (existingGenotypeStrings.has(geneString)) {
      continue;
    }

    const resultSapling = new Sapling(branch.genes, generationIndex);
    const score = resultSapling.getScore(options.geneScores);

    // Minimum tracked score filter
    if (score < options.minimumTrackedScore) {
      continue;
    }

    const map = new GeneticsMap(
      resultSapling,
      surroundingSaplings.map(s => s.clone()),
      centerSapling ? centerSapling.clone() : undefined,
      localChance,
      score,
      branch.tieWinningIndexes,
      branch.tieLosingIndexes
    );

    validMaps.push(map);
  }

  return validMaps;
}
