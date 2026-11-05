import { describe, expect, it } from 'vitest';
import { buildBreedingPlan } from '../domain/genetics/breedingPlan.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { Sapling } from '../domain/genetics/Sapling.ts';

describe('buildBreedingPlan', () => {
  it('returns multi-generation dependencies before the final breeding run', () => {
    const sourceA = new Sapling('GGYYHH', 0, 0);
    const sourceB = new Sapling('YYGGHH', 0, 1);
    const sourceC = new Sapling('HHGGYY', 0, 2);
    const intermediate = new Sapling('GGGGYY', 1);
    const dependency = new GeneticsMap(intermediate, [sourceA, sourceB], sourceC, 0.5);

    const sourceD = new Sapling('WYGGYX', 0, 3);
    const result = new Sapling('GGGYYY', 2);
    const route = new GeneticsMap(result, [intermediate, sourceD], sourceA, 1);
    route.crossbreedingSaplingsVariants = [
      new GeneticsMapGroup(intermediate.toString(), [dependency]),
      new GeneticsMapGroup(sourceD.toString())
    ];

    const steps = buildBreedingPlan(route);

    expect(steps.map(step => step.targetGeneString)).toEqual(['GGGGYY', 'GGGYYY']);
    expect(steps.map(step => step.chance)).toEqual([0.5, 1]);
    expect(steps[0].surroundingSaplingsStrings).toEqual(['GGYYHH', 'YYGGHH']);
    expect(steps[0].surroundingSourceIndexes).toEqual([0, 1]);
    expect(steps[1].centerSaplingString).toBe('GGYYHH');
  });
});
