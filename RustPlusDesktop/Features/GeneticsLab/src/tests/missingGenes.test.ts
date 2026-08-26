import { describe, it, expect } from 'vitest';
import { analyzeMissingDonors, analyzeCloneUtilities } from '../domain/genetics/missingGenes.ts';
import { CloneUtils } from '../domain/genetics/Clone.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { Sapling } from '../domain/genetics/Sapling.ts';

describe('Missing Genes & Clone Utilities Analyzer', () => {
  it('should identify missing donor slots and generate ranked patterns', () => {
    // Inventory only has donors for slot 1 and 2
    const ownedClones = [
      CloneUtils.create('GGWWWW', 'hemp', { quantity: 2 })
    ];

    const target = 'GGGYYY';
    const { slotAnalysis, recommendedPatterns } = analyzeMissingDonors(ownedClones, target);

    expect(slotAnalysis.length).toBe(6);
    expect(slotAnalysis[0].weaknessLevel).toBe('sufficient'); // Slot 1 'G' has donors
    expect(slotAnalysis[1].weaknessLevel).toBe('sufficient'); // Slot 2 'G' has donors
    expect(slotAnalysis[2].weaknessLevel).toBe('critical'); // Slot 3 'G' missing
    expect(slotAnalysis[3].weaknessLevel).toBe('critical'); // Slot 4 'Y' missing

    expect(recommendedPatterns.length).toBeGreaterThan(0);
    expect(recommendedPatterns[0].pattern).toContain('?');
  });

  it('should rate clone utilities based on route inclusion', () => {
    const cloneA = CloneUtils.create('GGYYHH', 'hemp');
    const cloneB = CloneUtils.create('WWWWWW', 'hemp');

    const sA = new Sapling('GGYYHH', 0, 0);
    const sB = new Sapling('YYGGHH', 0, 1);
    const res = new Sapling('GGYYHH', 1);

    const map = new GeneticsMap(res, [sA, sB], undefined, 1.0);
    const group = new GeneticsMapGroup(res.toString(), [map]);

    const ratings = analyzeCloneUtilities([cloneA, cloneB], [group], 'GGYYHH');

    expect(ratings.get(cloneA.id)?.rating).toBe('MEDIUM'); // Used in 1 route
    expect(ratings.get(cloneB.id)?.rating).toBe('REDUNDANT'); // Not used in top routes
  });
});
