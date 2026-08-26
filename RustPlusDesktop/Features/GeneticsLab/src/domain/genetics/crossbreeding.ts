import { Gene, GeneType, GREEN_GENE_WEIGHT, RED_GENE_WEIGHT } from './Gene.ts';
import { Sapling, GeneScores } from './Sapling.ts';
import { GeneticsMap } from './GeneticsMap.ts';

export interface CrossbreedingOptions {
  geneScores: GeneScores;
  minimumTrackedScore: number;
}

interface ColumnWeights {
  maxWeight: number;
  winningTypes: GeneType[];
  isDefinitiveTie: boolean;
  contributingSaplingIndexesByType: Map<GeneType, number[]>;
}

// Fixed index for each gene type so the hot loop uses plain array slots instead of
// allocating Maps and hashing string keys millions of times per calculation.
const TYPE_TO_INDEX: Record<GeneType, number> = { G: 0, H: 1, Y: 2, W: 3, X: 4 };
const INDEX_TO_TYPE: GeneType[] = ['G', 'H', 'Y', 'W', 'X'];

/**
 * Calculates weights and winners for a single gene column across surrounding plants.
 *
 * Behaviour is identical to a Map-based implementation: types are considered in the order
 * they are first seen across the surrounding plants (so the "first winner" chosen for a
 * non-branching column is unchanged), weights are accumulated with the same rounding, and
 * a definitive tie is still "more than one type tied for max, with max weight above the
 * red-gene weight". Only the allocation strategy changed.
 */
function calculateColumnWeights(
  surroundingSaplings: Sapling[],
  columnIndex: number
): ColumnWeights {
  const weights = [0, 0, 0, 0, 0];
  const contributors: (number[] | null)[] = [null, null, null, null, null];
  const seenOrder: number[] = [];

  for (let sIdx = 0; sIdx < surroundingSaplings.length; sIdx++) {
    const gene = surroundingSaplings[sIdx].genes[columnIndex];
    const ti = TYPE_TO_INDEX[gene.type];

    let list = contributors[ti];
    if (list === null) {
      list = [];
      contributors[ti] = list;
      seenOrder.push(ti);
    }
    weights[ti] = Math.round((weights[ti] + gene.weight) * 100) / 100;
    list.push(sIdx);
  }

  let maxWeight = 0;
  for (const ti of seenOrder) {
    if (weights[ti] > maxWeight) {
      maxWeight = weights[ti];
    }
  }

  const winningTypes: GeneType[] = [];
  const contributingSaplingIndexesByType = new Map<GeneType, number[]>();
  for (const ti of seenOrder) {
    contributingSaplingIndexesByType.set(INDEX_TO_TYPE[ti], contributors[ti]!);
    if (Math.abs(weights[ti] - maxWeight) < 0.001) {
      winningTypes.push(INDEX_TO_TYPE[ti]);
    }
  }

  // Definitive tie: > 1 type tied for max weight AND max weight > 1.0 (red gene weight)
  const isDefinitiveTie = winningTypes.length > 1 && maxWeight > RED_GENE_WEIGHT;

  return {
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

  // The surrounding plants are identical for every map this combination produces (across
  // all candidate centers and tie branches) and are never mutated after a map is built, so
  // clone them ONCE here and share the clones. Previously they were deep-cloned inside every
  // map for every center, multiplying allocation/GC cost by (centers x maps) in the hot loop.
  const surroundingSaplingsClone = surroundingSaplings.map(s => s.clone());

  if (!needsCenterCheck) {
    // No center plant needed
    const maps = buildMapsForOutcome(
      surroundingSaplingsClone,
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
        surroundingSaplingsClone,
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
 * `surroundingSaplingsClone` is an already-cloned, read-only set shared across every map
 * produced for the combination (see evaluateCombination) to avoid redundant deep clones.
 */
function buildMapsForOutcome(
  surroundingSaplingsClone: Sapling[],
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
      surroundingSaplingsClone,
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
