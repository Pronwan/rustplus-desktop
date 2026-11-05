import { describe, expect, it } from 'vitest';
import { PLANTER_MODELS, neighboursOf } from '../components/planter3d/planterModels.ts';
import { demoBreedingPlan, donorSlotsFor } from '../components/planter3d/breedingLayout.ts';

describe('planter slot layout', () => {
  it('large planter centre touches all eight other slots', () => {
    const large = PLANTER_MODELS.large;
    expect(large.slots).toHaveLength(9);
    expect(neighboursOf(large, large.centerSlot)).toHaveLength(8);
  });

  it('large planter corners touch three slots, edges five', () => {
    const large = PLANTER_MODELS.large;
    expect(neighboursOf(large, 0).map((i) => large.slots[i].number)).toEqual([2, 4, 5]);
    expect(neighboursOf(large, 1)).toHaveLength(5);
  });

  it('small planter ends only touch the middle slot', () => {
    const small = PLANTER_MODELS.small;
    expect(neighboursOf(small, 0)).toEqual([1]);
    expect(neighboursOf(small, 1)).toEqual([0, 2]);
  });

  it('triangle planter centre touches the three corner slots', () => {
    const tri = PLANTER_MODELS.triangle;
    expect(tri.slots).toHaveLength(4);
    expect(neighboursOf(tri, tri.centerSlot)).toHaveLength(3);
  });

  it('puts donors on the edges before the diagonals', () => {
    const large = PLANTER_MODELS.large;
    expect(donorSlotsFor(large, 4).map((i) => large.slots[i].number).sort()).toEqual([2, 4, 6, 8]);
    expect(donorSlotsFor(large, 6)).toHaveLength(6);
  });
});

describe('worked example', () => {
  it('breeds a result from the demo donors', () => {
    const plan = demoBreedingPlan();
    expect(plan).not.toBeNull();
    expect(plan!.donors).toHaveLength(4);
    expect(plan!.result).toMatch(/^[GHYWX]{6}$/);
  });
});
