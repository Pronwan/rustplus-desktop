import { describe, it, expect } from 'vitest';
import { analyzeRoute, getRequiredSourceGenotypes } from '../domain/genetics/routeScoring.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { CloneUtils } from '../domain/genetics/Clone.ts';

describe('Route Scoring & Inventory Analysis', () => {
  it('should extract required leaf source genotypes for a single-gen route', () => {
    const parent1 = new Sapling('GGYYHH', 0, 0);
    const parent2 = new Sapling('YYGGHH', 0, 1);
    const center = new Sapling('WWWWWW', 0, 2);
    const result = new Sapling('GGYYHH', 1);

    const map = new GeneticsMap(result, [parent1, parent2], center, 1.0);
    const sources = getRequiredSourceGenotypes(map);

    expect(sources).toContain('GGYYHH');
    expect(sources).toContain('YYGGHH');
    expect(sources).toContain('WWWWWW');
    expect(sources.length).toBe(3);
  });

  it('should analyze route recommendation score and inventory status when all clones are available', () => {
    const parent1 = new Sapling('GGYYHH', 0, 0);
    const parent2 = new Sapling('YYGGHH', 0, 1);
    const center = new Sapling('WWWWWW', 0, 2);
    const result = new Sapling('GGYYHH', 1);

    const map = new GeneticsMap(result, [parent1, parent2], center, 1.0);

    const ownedClones = [
      CloneUtils.create('GGYYHH', 'hemp', { quantity: 2 }),
      CloneUtils.create('YYGGHH', 'hemp', { quantity: 2 }),
      CloneUtils.create('WWWWWW', 'hemp', { quantity: 2 })
    ];

    const analysis = analyzeRoute(map, ownedClones, 'GGYYHH');

    expect(analysis.probabilityPercent).toBe(100);
    expect(analysis.generationCount).toBe(1);
    expect(analysis.inventoryStatus).toBe('available');
    expect(analysis.missingClonesCount).toBe(0);
    expect(analysis.recommendationScore).toBeGreaterThanOrEqual(80);
    expect(analysis.difficulty).toBe('Easy');
  });

  it('should detect missing clones and partial inventory', () => {
    const parent1 = new Sapling('GGYYHH', 0, 0);
    const parent2 = new Sapling('YYGGHH', 0, 1);
    const result = new Sapling('GGYYHH', 1);

    const map = new GeneticsMap(result, [parent1, parent2], undefined, 1.0);

    // Only have parent1 in inventory, missing parent2
    const ownedClones = [
      CloneUtils.create('GGYYHH', 'hemp', { quantity: 1 })
    ];

    const analysis = analyzeRoute(map, ownedClones, 'GGYYHH');

    expect(analysis.inventoryStatus).toBe('partial');
    expect(analysis.missingClonesCount).toBe(1);
    expect(analysis.requirements.find(r => r.genetics === 'YYGGHH')?.isAvailable).toBe(false);
  });
});
