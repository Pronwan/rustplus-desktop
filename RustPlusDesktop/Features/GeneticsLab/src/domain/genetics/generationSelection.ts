import { Sapling, GeneScores } from './Sapling.ts';
import { GeneticsMap } from './GeneticsMap.ts';
import { GeneticsMapGroup } from './GeneticsMapGroup.ts';
import { resultMapGroupsSortingFunction } from './sorting.ts';

/**
 * Evaluates the maximum score available at each of the 6 columns in the current source population.
 */
function getColumnMaxScores(sourceSaplings: Sapling[], geneScores: GeneScores): number[] {
  const colMaxes = [-Infinity, -Infinity, -Infinity, -Infinity, -Infinity, -Infinity];

  for (const sapling of sourceSaplings) {
    for (let col = 0; col < 6; col++) {
      const type = sapling.genes[col].type;
      const score = geneScores[type] ?? 0;
      if (score > colMaxes[col]) {
        colMaxes[col] = score;
      }
    }
  }

  return colMaxes;
}

/**
 * Selects up to `maxToAdd` best result saplings from the completed generation to use in the next generation.
 */
export function getBestSaplingsForNextGeneration(
  groups: GeneticsMapGroup[],
  sourceSaplings: Sapling[],
  geneScores: GeneScores,
  maxToAdd: number,
  completedGenerationIndex: number
): Sapling[] {
  // Filter groups whose best map is from the completed generation
  const candidateGroups = groups.filter(g => {
    const bestMap = g.mapList[0];
    return bestMap && bestMap.resultSapling.generationIndex === completedGenerationIndex;
  });

  if (candidateGroups.length === 0) {
    return [];
  }

  // Sort candidate groups using standard group sorting
  const sortedCandidates = [...candidateGroups].sort(resultMapGroupsSortingFunction);

  const colMaxes = getColumnMaxScores(sourceSaplings, geneScores);
  const selected: Sapling[] = [];
  const selectedGenotypes = new Set<string>();

  // Pass 1: Select plants that improve weaker columns
  for (const group of sortedCandidates) {
    if (selected.length >= maxToAdd) break;
    const sapling = group.mapList[0].resultSapling;
    const geneStr = sapling.toString();
    if (selectedGenotypes.has(geneStr)) continue;

    let improvesWeakColumn = false;
    for (let col = 0; col < 6; col++) {
      const score = geneScores[sapling.genes[col].type] ?? 0;
      if (score > colMaxes[col]) {
        improvesWeakColumn = true;
        colMaxes[col] = score; // Update max
      }
    }

    if (improvesWeakColumn) {
      selected.push(sapling.clone());
      selectedGenotypes.add(geneStr);
    }
  }

  // Pass 2: Fill remaining slots with best ranked candidates
  for (const group of sortedCandidates) {
    if (selected.length >= maxToAdd) break;
    const sapling = group.mapList[0].resultSapling;
    const geneStr = sapling.toString();
    if (!selectedGenotypes.has(geneStr)) {
      selected.push(sapling.clone());
      selectedGenotypes.add(geneStr);
    }
  }

  return selected;
}

/**
 * Recursively links generated saplings in breeding maps to their parent GeneticsMapGroups.
 */
export function linkGenerationTree(
  groups: GeneticsMapGroup[],
  allGroupsMap: Map<string, GeneticsMapGroup>
): void {
  for (const group of groups) {
    for (const map of group.mapList) {
      if (map.baseSapling && map.baseSapling.generationIndex > 0) {
        const parentGroup = allGroupsMap.get(map.baseSapling.toString());
        if (parentGroup) {
          map.baseSaplingVariants = parentGroup;
        }
      }

      if (map.crossbreedingSaplings) {
        map.crossbreedingSaplingsVariants = map.crossbreedingSaplings.map(s => {
          if (s.generationIndex > 0) {
            return allGroupsMap.get(s.toString()) ?? new GeneticsMapGroup(s.toString());
          }
          return new GeneticsMapGroup(s.toString());
        });
      }
    }
  }
}
