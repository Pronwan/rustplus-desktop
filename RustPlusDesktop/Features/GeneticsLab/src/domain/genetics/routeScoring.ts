import { GeneticsMap } from './GeneticsMap.ts';
import { SavedClone } from './Clone.ts';
import {
  targetHasConstraint,
  isExactMatch,
  targetCloseness,
  positionMatches
} from '../../utils/targetMatch.ts';

export type RouteSortOption =
  | 'recommended'
  | 'target'
  | 'probability'
  | 'generations'
  | 'clones'
  | 'inventory';

export interface RouteCloneRequirement {
  genetics: string;
  requiredQuantity: number;
  availableQuantity: number;
  isAvailable: boolean;
  cloneId?: string;
  cloneName?: string;
}

export interface RouteAnalysis {
  recommendationScore: number; // 0 - 100
  probabilityPercent: number; // 0 - 100
  generationCount: number;
  uniqueCloneCount: number;
  totalPlacementsCount: number;
  intermediateCount: number;
  inventoryStatus: 'available' | 'partial' | 'missing';
  missingClonesCount: number;
  difficulty: 'Easy' | 'Medium' | 'Advanced';
  requirements: RouteCloneRequirement[];
}

/**
 * Recursively collects all required base/source clones for a breeding route.
 */
export function getRequiredSourceGenotypes(map: GeneticsMap): string[] {
  const sourceGenotypes: string[] = [];

  function traverse(currentMap: GeneticsMap) {
    if (currentMap.baseSapling) {
      if (currentMap.baseSapling.generationIndex > 0) {
        if (currentMap.baseSaplingVariants && currentMap.baseSaplingVariants.mapList.length > 0) {
          traverse(currentMap.baseSaplingVariants.mapList[0]);
        }
      } else {
        sourceGenotypes.push(currentMap.baseSapling.toString());
      }
    }

    currentMap.crossbreedingSaplings.forEach((parent, pIdx) => {
      if (parent.generationIndex > 0) {
        const variant = currentMap.crossbreedingSaplingsVariants?.[pIdx];
        if (variant && variant.mapList.length > 0) {
          traverse(variant.mapList[0]);
        }
      } else {
        sourceGenotypes.push(parent.toString());
      }
    });
  }

  traverse(map);
  return sourceGenotypes;
}

/**
 * Counts total intermediate plants involved in the multi-generation route.
 */
export function countIntermediatePlants(map: GeneticsMap): number {
  let count = 0;

  function traverse(currentMap: GeneticsMap) {
    if (currentMap.baseSapling && currentMap.baseSapling.generationIndex > 0) {
      count++;
      if (currentMap.baseSaplingVariants && currentMap.baseSaplingVariants.mapList.length > 0) {
        traverse(currentMap.baseSaplingVariants.mapList[0]);
      }
    }

    currentMap.crossbreedingSaplings.forEach((parent, pIdx) => {
      if (parent.generationIndex > 0) {
        count++;
        const variant = currentMap.crossbreedingSaplingsVariants?.[pIdx];
        if (variant && variant.mapList.length > 0) {
          traverse(variant.mapList[0]);
        }
      }
    });
  }

  traverse(map);
  return count;
}

/**
 * Comprehensive analysis of a breeding route against the user's clone inventory.
 */
export function analyzeRoute(
  map: GeneticsMap,
  ownedClones: SavedClone[],
  targetGeneString?: string
): RouteAnalysis {
  const rawSources = getRequiredSourceGenotypes(map);
  const totalPlacementsCount = rawSources.length;
  const intermediateCount = countIntermediatePlants(map);

  // Tally required quantities per unique gene string
  const requiredMap = new Map<string, number>();
  for (const g of rawSources) {
    requiredMap.set(g, (requiredMap.get(g) || 0) + 1);
  }

  // Tally available quantities in inventory
  const availableMap = new Map<string, { quantity: number; id?: string; name?: string }>();
  for (const clone of ownedClones) {
    const prev = availableMap.get(clone.genetics);
    if (prev) {
      prev.quantity += clone.quantity;
      if (!prev.name && clone.name) prev.name = clone.name;
    } else {
      availableMap.set(clone.genetics, {
        quantity: clone.quantity,
        id: clone.id,
        name: clone.name
      });
    }
  }

  const requirements: RouteCloneRequirement[] = [];
  let missingClonesCount = 0;
  let allAvailable = true;
  let someAvailable = false;

  for (const [genetics, reqQty] of requiredMap.entries()) {
    const avail = availableMap.get(genetics);
    const availQty = avail ? avail.quantity : 0;
    const isAvail = availQty >= reqQty;

    if (!isAvail) {
      allAvailable = false;
      missingClonesCount += Math.max(0, reqQty - availQty);
    }
    if (availQty > 0) {
      someAvailable = true;
    }

    requirements.push({
      genetics,
      requiredQuantity: reqQty,
      availableQuantity: availQty,
      isAvailable: isAvail,
      cloneId: avail?.id,
      cloneName: avail?.name
    });
  }

  const inventoryStatus: 'available' | 'partial' | 'missing' =
    allAvailable
      ? 'available'
      : someAvailable
      ? 'partial'
      : 'missing';

  const chanceProd = map.getChanceProduct();
  const probabilityPercent = Math.round(chanceProd * 100);
  const genCount = Math.max(1, map.resultSapling.generationIndex || 1);
  const uniqueCloneCount = requiredMap.size;

  // Composite Recommendation Score (0-100)
  // 1. Probability weight (up to 40 pts)
  const probScore = chanceProd * 40;

  // 2. Generation efficiency (up to 25 pts)
  // Gen 1 = 25 pts, Gen 2 = 18 pts, Gen 3 = 10 pts
  const genScore = genCount === 1 ? 25 : genCount === 2 ? 18 : 10;

  // 3. Inventory match (up to 20 pts)
  const invScore = allAvailable ? 20 : someAvailable ? 10 : 2;

  // 4. Clone simplicity (up to 15 pts) - fewer unique clones & fewer placements
  const simplicityScore = Math.max(0, 15 - (uniqueCloneCount - 2) * 2 - Math.max(0, totalPlacementsCount - 4));

  const rawTotal = probScore + genScore + invScore + simplicityScore;
  const recommendationScore = Math.min(100, Math.max(1, Math.round(rawTotal)));

  // Difficulty categorization
  const difficulty: 'Easy' | 'Medium' | 'Advanced' =
    genCount === 1 && chanceProd >= 0.95
      ? 'Easy'
      : genCount <= 2 && chanceProd >= 0.5
      ? 'Medium'
      : 'Advanced';

  return {
    recommendationScore,
    probabilityPercent,
    generationCount: genCount,
    uniqueCloneCount,
    totalPlacementsCount,
    intermediateCount,
    inventoryStatus,
    missingClonesCount,
    difficulty,
    requirements
  };
}

export interface SortableRouteLike {
  group: { resultSaplingGeneString: string };
  bestMap: GeneticsMap;
  analysis: RouteAnalysis;
}

/**
 * Deterministic comparison of routes according to the 2-level Rust Breeder specification:
 * 1. Higher genotype score first (score DESC)
 * 2. Higher recursive chance product first (recursiveChanceProduct DESC)
 * 3. Lower generationIndex first (generationIndex ASC)
 * 4. Lower sumOfComposingSaplingsGenerations first (sumOfComposingSaplingsGenerations ASC)
 * 5. Genotype string alphabetically (genotype ASC) as final deterministic tie-breaker
 */
export function compareScoredRoutes(
  a: SortableRouteLike,
  b: SortableRouteLike,
  sortBy: RouteSortOption,
  target?: string
): number {
  const ra = a.group.resultSaplingGeneString;
  const rb = b.group.resultSaplingGeneString;
  const hasTarget = !!target && targetHasConstraint(target);

  const mapA = a.bestMap;
  const mapB = b.bestMap;

  const scoreA = mapA?.score ?? 0;
  const scoreB = mapB?.score ?? 0;

  const chanceProdA = mapA ? mapA.getChanceProduct() : (a.analysis.probabilityPercent / 100);
  const chanceProdB = mapB ? mapB.getChanceProduct() : (b.analysis.probabilityPercent / 100);

  const genA = mapA ? mapA.resultSapling.generationIndex : a.analysis.generationCount;
  const genB = mapB ? mapB.resultSapling.generationIndex : b.analysis.generationCount;

  const sumA = mapA?.sumOfComposingSaplingsGenerations ?? 0;
  const sumB = mapB?.sumOfComposingSaplingsGenerations ?? 0;

  if (sortBy === 'target' && hasTarget) {
    const ea = isExactMatch(ra, target) ? 1 : 0;
    const eb = isExactMatch(rb, target) ? 1 : 0;
    if (ea !== eb) return eb - ea;

    const ca = targetCloseness(ra, target);
    const cb = targetCloseness(rb, target);
    if (ca !== cb) return cb - ca;

    const pa = positionMatches(ra, target);
    const pb = positionMatches(rb, target);
    if (pa !== pb) return pb - pa;

    if (Math.abs(scoreB - scoreA) > 0.001) {
      return scoreB - scoreA;
    }
    if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
      return chanceProdB - chanceProdA;
    }
    if (genA !== genB) {
      return genA - genB;
    }
    if (sumA !== sumB) {
      return sumA - sumB;
    }
    return ra.localeCompare(rb);
  }

  if (sortBy === 'probability') {
    if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
      return chanceProdB - chanceProdA;
    }
    if (Math.abs(scoreB - scoreA) > 0.001) {
      return scoreB - scoreA;
    }
    if (genA !== genB) {
      return genA - genB;
    }
    if (sumA !== sumB) {
      return sumA - sumB;
    }
    return ra.localeCompare(rb);
  }

  if (sortBy === 'generations') {
    if (genA !== genB) {
      return genA - genB;
    }
    if (Math.abs(scoreB - scoreA) > 0.001) {
      return scoreB - scoreA;
    }
    if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
      return chanceProdB - chanceProdA;
    }
    if (sumA !== sumB) {
      return sumA - sumB;
    }
    return ra.localeCompare(rb);
  }

  if (sortBy === 'clones') {
    if (a.analysis.uniqueCloneCount !== b.analysis.uniqueCloneCount) {
      return a.analysis.uniqueCloneCount - b.analysis.uniqueCloneCount;
    }
    if (a.analysis.totalPlacementsCount !== b.analysis.totalPlacementsCount) {
      return a.analysis.totalPlacementsCount - b.analysis.totalPlacementsCount;
    }
    if (Math.abs(scoreB - scoreA) > 0.001) {
      return scoreB - scoreA;
    }
    if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
      return chanceProdB - chanceProdA;
    }
    if (genA !== genB) {
      return genA - genB;
    }
    return ra.localeCompare(rb);
  }

  if (sortBy === 'inventory') {
    const invOrder: Record<RouteAnalysis['inventoryStatus'], number> = { available: 3, partial: 2, missing: 1 };
    const diff = invOrder[b.analysis.inventoryStatus] - invOrder[a.analysis.inventoryStatus];
    if (diff !== 0) return diff;
    if (a.analysis.missingClonesCount !== b.analysis.missingClonesCount) {
      return a.analysis.missingClonesCount - b.analysis.missingClonesCount;
    }
    if (Math.abs(scoreB - scoreA) > 0.001) {
      return scoreB - scoreA;
    }
    if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
      return chanceProdB - chanceProdA;
    }
    if (genA !== genB) {
      return genA - genB;
    }
    return ra.localeCompare(rb);
  }

  // 'recommended' (default): 2-level Rust Breeder ranking:
  // 1. Higher genotype score first
  if (Math.abs(scoreB - scoreA) > 0.001) {
    return scoreB - scoreA;
  }
  // 2. Higher recursive chance product first
  if (Math.abs(chanceProdB - chanceProdA) > 0.0001) {
    return chanceProdB - chanceProdA;
  }
  // 3. Lower generationIndex first
  if (genA !== genB) {
    return genA - genB;
  }
  // 4. Lower sumOfComposingSaplingsGenerations first
  if (sumA !== sumB) {
    return sumA - sumB;
  }
  // Target closeness tiebreaker if target specified
  if (hasTarget) {
    const ca = targetCloseness(ra, target);
    const cb = targetCloseness(rb, target);
    if (ca !== cb) return cb - ca;
  }
  // 5. Genotype string alphabetically/lexicographically as deterministic tie breaker
  return ra.localeCompare(rb);
}

