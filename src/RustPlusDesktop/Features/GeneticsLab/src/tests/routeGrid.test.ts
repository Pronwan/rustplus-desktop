import { describe, expect, it } from 'vitest';
import { nextRoutePageSize } from '../components/workspace/Routes/RouteGrid.tsx';
import { analyzeMissingDonors } from '../domain/genetics/missingGenes.ts';
import { CloneUtils } from '../domain/genetics/Clone.ts';

describe('progressive route expansion', () => {
  it('adds one page without passing the result total', () => {
    expect(nextRoutePageSize(8, 500)).toBe(16);
    expect(nextRoutePageSize(495, 500)).toBe(500);
  });
});

describe('empty route calculation bottleneck detection', () => {
  it('identifies critical donor gaps when current clones cannot satisfy target', () => {
    // 20 plants similar to user scenario that lack donor genes for specific positions
    const clones = [
      CloneUtils.create('YHHWGX', 'hemp'),
      CloneUtils.create('WHHWWG', 'hemp'),
      CloneUtils.create('XGHWWX', 'hemp')
    ];
    const target = 'GGGYYY';
    const { slotAnalysis } = analyzeMissingDonors(clones, target);

    const criticalSlots = slotAnalysis.filter(s => s.weaknessLevel === 'critical');
    const moderateSlots = slotAnalysis.filter(s => s.weaknessLevel === 'moderate');

    expect(criticalSlots.length + moderateSlots.length).toBeGreaterThan(0);
    // Verifies that slot details include slot number and target gene for UI chips
    expect(criticalSlots[0].slotNumber).toBeGreaterThanOrEqual(1);
    expect(['G', 'Y']).toContain(criticalSlots[0].targetGene);
  });
});

describe('low route count grouping protection', () => {
  it('defines MIN_ROUTES_TO_GROUP to prevent grouping when route count is low', async () => {
    const { MIN_ROUTES_TO_GROUP } = await import('../context/CalculationContext.tsx');
    expect(MIN_ROUTES_TO_GROUP).toBe(25);
  });
});
