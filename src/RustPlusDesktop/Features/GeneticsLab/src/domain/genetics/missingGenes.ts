import { SavedClone } from './Clone.ts';
import { GeneticsMapGroup } from './GeneticsMapGroup.ts';

export interface MissingGeneSlotInfo {
  slotIndex: number; // 0 - 5
  slotNumber: number; // 1 - 6
  targetGene: string; // e.g. 'G' or 'Y'
  currentDonorCount: number;
  weaknessLevel: 'critical' | 'moderate' | 'sufficient';
}

export interface MissingPatternRecommendation {
  pattern: string; // e.g. 'G??YG?'
  reason: string;
  priority: 'high' | 'medium' | 'low';
}

export interface CloneUtilityRating {
  cloneId: string;
  rating: 'CORE' | 'HIGH' | 'MEDIUM' | 'LOW' | 'REDUNDANT';
  usageCountInTopRoutes: number;
  strongPositions: number[]; // 1-based
  description: string;
}

/**
 * Evaluates current clone inventory against a target gene string to find weak donor slots.
 */
export function analyzeMissingDonors(
  ownedClones: SavedClone[],
  targetGeneString: string
): {
  slotAnalysis: MissingGeneSlotInfo[];
  recommendedPatterns: MissingPatternRecommendation[];
} {
  const cleanTarget = targetGeneString.toUpperCase();
  const targetChars = cleanTarget.split('').slice(0, 6);

  const slotAnalysis: MissingGeneSlotInfo[] = [];
  const weakSlots: { index: number; target: string }[] = [];

  for (let slot = 0; slot < 6; slot++) {
    const targetChar = targetChars[slot] || '*';
    if (targetChar === '*' || targetChar === '?') {
      slotAnalysis.push({
        slotIndex: slot,
        slotNumber: slot + 1,
        targetGene: targetChar,
        currentDonorCount: 0,
        weaknessLevel: 'sufficient'
      });
      continue;
    }

    let donorCount = 0;
    for (const clone of ownedClones) {
      if (clone.genetics[slot] === targetChar) {
        donorCount += clone.quantity;
      }
    }

    const weaknessLevel: 'critical' | 'moderate' | 'sufficient' =
      donorCount === 0 ? 'critical' : donorCount <= 1 ? 'moderate' : 'sufficient';

    slotAnalysis.push({
      slotIndex: slot,
      slotNumber: slot + 1,
      targetGene: targetChar,
      currentDonorCount: donorCount,
      weaknessLevel
    });

    if (weaknessLevel !== 'sufficient') {
      weakSlots.push({ index: slot, target: targetChar });
    }
  }

  // Generate ranked patterns to look for
  const recommendedPatterns: MissingPatternRecommendation[] = [];

  if (weakSlots.length > 0) {
    // Pattern 1: Combined weak slots
    const patternChars = ['?', '?', '?', '?', '?', '?'];
    const weakSlotNumbers: number[] = [];
    for (const ws of weakSlots) {
      patternChars[ws.index] = ws.target;
      weakSlotNumbers.push(ws.index + 1);
    }

    recommendedPatterns.push({
      pattern: patternChars.join(''),
      reason: `Provides missing target genes at slots ${weakSlotNumbers.join(', ')}`,
      priority: 'high'
    });

    // Pattern 2 & 3: Individual critical slots with good complementary green genes
    for (const ws of weakSlots) {
      const single = ['?', '?', '?', '?', '?', '?'];
      single[ws.index] = ws.target;
      recommendedPatterns.push({
        pattern: single.join(''),
        reason: `Donates required ${ws.target} at slot ${ws.index + 1}`,
        priority: ws.target === 'G' || ws.target === 'Y' ? 'medium' : 'low'
      });
    }
  }

  return { slotAnalysis, recommendedPatterns };
}

/**
 * Calculates utility rating for each clone in inventory based on its participation in top routes.
 */
export function analyzeCloneUtilities(
  ownedClones: SavedClone[],
  topRouteGroups: GeneticsMapGroup[],
  targetGeneString: string
): Map<string, CloneUtilityRating> {
  const ratings = new Map<string, CloneUtilityRating>();
  const topMaps = topRouteGroups.slice(0, 10).map(g => g.mapList[0]).filter(Boolean);

  // Count occurrences of each genotype in top routes
  const usageCountByGenetics = new Map<string, number>();

  for (const map of topMaps) {
    const used = new Set<string>();
    if (map.baseSapling) used.add(map.baseSapling.toString());
    map.crossbreedingSaplings.forEach(s => used.add(s.toString()));

    for (const g of used) {
      usageCountByGenetics.set(g, (usageCountByGenetics.get(g) || 0) + 1);
    }
  }

  for (const clone of ownedClones) {
    const uses = usageCountByGenetics.get(clone.genetics) || 0;
    const strongPositions: number[] = [];

    for (let i = 0; i < 6; i++) {
      const g = clone.genetics[i];
      if (targetGeneString[i] && targetGeneString[i] === g) {
        strongPositions.push(i + 1);
      }
    }

    let rating: 'CORE' | 'HIGH' | 'MEDIUM' | 'LOW' | 'REDUNDANT';
    let description = '';

    if (uses >= 5) {
      rating = 'CORE';
      description = `Key donor used in ${uses} top breeding routes.`;
    } else if (uses >= 2) {
      rating = 'HIGH';
      description = `Valuable contributor used in ${uses} top routes.`;
    } else if (uses === 1) {
      rating = 'MEDIUM';
      description = 'Used in 1 active route.';
    } else if (strongPositions.length > 0) {
      rating = 'LOW';
      description = `Matches target at position ${strongPositions.join(', ')}, but not currently in top routes.`;
    } else {
      rating = 'REDUNDANT';
      description = 'Not utilized by any current top routes.';
    }

    ratings.set(clone.id, {
      cloneId: clone.id,
      rating,
      usageCountInTopRoutes: uses,
      strongPositions,
      description
    });
  }

  return ratings;
}
