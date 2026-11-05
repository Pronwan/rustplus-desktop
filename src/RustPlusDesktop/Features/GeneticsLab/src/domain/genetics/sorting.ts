import { GeneticsMap } from './GeneticsMap.ts';
import { GeneticsMapGroup } from './GeneticsMapGroup.ts';

/**
 * Sorts GeneticsMaps that produce the same result genotype.
 * Preferred order:
 * 1. Lower result generationIndex
 * 2. Higher local chance
 * 3. Lower sumOfComposingSaplingsGenerations
 * 4. Fewer surrounding plants
 */
export function resultMapsSortingFunction(a: GeneticsMap, b: GeneticsMap): number {
  if (a.resultSapling.generationIndex !== b.resultSapling.generationIndex) {
    return a.resultSapling.generationIndex - b.resultSapling.generationIndex;
  }
  if (Math.abs(b.chance - a.chance) > 0.0001) {
    return b.chance - a.chance;
  }
  if (a.sumOfComposingSaplingsGenerations !== b.sumOfComposingSaplingsGenerations) {
    return a.sumOfComposingSaplingsGenerations - b.sumOfComposingSaplingsGenerations;
  }
  return a.crossbreedingSaplings.length - b.crossbreedingSaplings.length;
}

/**
 * Sorts GeneticsMapGroups by their best (first) map.
 * Preferred order:
 * 1. Higher score
 * 2. Higher recursive chance product
 * 3. Lower generation index
 * 4. Lower sum of composing generations
 * 5. Lexicographically smaller genotype string as deterministic tie breaker
 */
export function resultMapGroupsSortingFunction(a: GeneticsMapGroup, b: GeneticsMapGroup): number {
  const mapA = a.mapList[0];
  const mapB = b.mapList[0];

  if (!mapA && !mapB) return 0;
  if (!mapA) return 1;
  if (!mapB) return -1;

  if (Math.abs(mapB.score - mapA.score) > 0.001) {
    return mapB.score - mapA.score;
  }

  const chanceProdA = mapA.getChanceProduct();
  const chanceProdB = mapB.getChanceProduct();
  if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
    return chanceProdB - chanceProdA;
  }

  if (mapA.resultSapling.generationIndex !== mapB.resultSapling.generationIndex) {
    return mapA.resultSapling.generationIndex - mapB.resultSapling.generationIndex;
  }

  if (mapA.sumOfComposingSaplingsGenerations !== mapB.sumOfComposingSaplingsGenerations) {
    return mapA.sumOfComposingSaplingsGenerations - mapB.sumOfComposingSaplingsGenerations;
  }

  return a.resultSaplingGeneString.localeCompare(b.resultSaplingGeneString);
}

/**
 * Appends new maps to a map group map, sorts maps within groups, and limits each group to top 3 maps.
 */
export function appendAndOrganizeResults(
  groupMap: Map<string, GeneticsMapGroup>,
  newMaps: GeneticsMap[]
): void {
  for (const map of newMaps) {
    const geneStr = map.resultSapling.toString();
    let group = groupMap.get(geneStr);

    if (!group) {
      group = new GeneticsMapGroup(geneStr, [map]);
      groupMap.set(geneStr, group);
    } else {
      // Check if identical map already exists to avoid exact duplicates
      const isDuplicate = group.mapList.some(existing => {
        if (existing.chance !== map.chance) return false;
        if (existing.crossbreedingSaplings.length !== map.crossbreedingSaplings.length) return false;
        const sameCenter = (!existing.baseSapling && !map.baseSapling) ||
          (existing.baseSapling?.toString() === map.baseSapling?.toString());
        if (!sameCenter) return false;
        const sameSurrounding = existing.crossbreedingSaplings.every((s, i) =>
          s.toString() === map.crossbreedingSaplings[i].toString()
        );
        return sameSurrounding;
      });

      if (!isDuplicate) {
        group.mapList.push(map);
        group.mapList.sort(resultMapsSortingFunction);
        if (group.mapList.length > 3) {
          group.mapList.length = 3;
        }
      }
    }
  }
}
