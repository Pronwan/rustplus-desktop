import { describe, it, expect } from 'vitest';
import { analyzeRoute, getRequiredSourceGenotypes, getRequiredSourceIndexes, compareScoredRoutes } from '../domain/genetics/routeScoring.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
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

  it('should extract original list positions through intermediate generations', () => {
    const intermediateResult = new Sapling('GGGGYY', 1);
    const intermediateMap = new GeneticsMap(
      intermediateResult,
      [new Sapling('GGGGGG', 0, 8), new Sapling('YYYYYY', 0, 21)]
    );
    const rootMap = new GeneticsMap(
      new Sapling('GGGYYY', 2),
      [intermediateResult],
      new Sapling('HHHHHH', 0, 50)
    );
    rootMap.crossbreedingSaplingsVariants = [new GeneticsMapGroup('GGGGYY', [intermediateMap])];

    expect(getRequiredSourceIndexes(rootMap)).toEqual([50, 8, 21]);
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

describe('compareScoredRoutes', () => {
  const makeRoute = (
    genotype: string,
    options: {
      score?: number;
      chance?: number;
      generationIndex?: number;
      sumOfComposingSaplingsGenerations?: number;
      analysisOverrides?: Partial<import('../domain/genetics/routeScoring.ts').RouteAnalysis>;
    } = {}
  ) => {
    const sapling = new Sapling(genotype, options.generationIndex ?? 1);
    const map = new GeneticsMap(
      sapling,
      [new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0)],
      undefined,
      options.chance ?? 1.0,
      options.score ?? sapling.getScore()
    );
    map.sumOfComposingSaplingsGenerations = options.sumOfComposingSaplingsGenerations ?? 0;

    return {
      group: { resultSaplingGeneString: genotype },
      bestMap: map,
      analysis: {
        recommendationScore: 80,
        probabilityPercent: Math.round((options.chance ?? 1.0) * 100),
        generationCount: options.generationIndex ?? 1,
        uniqueCloneCount: 2,
        totalPlacementsCount: 3,
        intermediateCount: 0,
        inventoryStatus: 'available' as const,
        missingClonesCount: 0,
        difficulty: 'Easy' as const,
        requirements: [],
        ...(options.analysisOverrides || {})
      }
    };
  };

  it('Level 2 #1: Prioritizes higher genotype score first (score DESC)', () => {
    const route6 = makeRoute('GGYYYY', { score: 6.0, chance: 1.0, generationIndex: 1 });
    const route4 = makeRoute('GGYXGY', { score: 4.0, chance: 1.0, generationIndex: 1 });

    const sorted = [route4, route6].sort((a, b) =>
      compareScoredRoutes(a, b, 'recommended')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGYYYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYXGY');
  });

  it('Level 2 #2: Prioritizes higher recursive chance product when score is tied (chance DESC)', () => {
    const route100 = makeRoute('GGYYYY', { score: 6.0, chance: 1.0, generationIndex: 1 });
    const route50 = makeRoute('YYYYGG', { score: 6.0, chance: 0.5, generationIndex: 1 });

    const sorted = [route50, route100].sort((a, b) =>
      compareScoredRoutes(a, b, 'recommended')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGYYYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('YYYYGG');
  });

  it('Level 2 #3: Prioritizes lower generationIndex when score & chance are tied (genIndex ASC)', () => {
    const gen1 = makeRoute('GGYYYY', { score: 6.0, chance: 1.0, generationIndex: 1 });
    const gen2 = makeRoute('YYYYGG', { score: 6.0, chance: 1.0, generationIndex: 2 });

    const sorted = [gen2, gen1].sort((a, b) =>
      compareScoredRoutes(a, b, 'recommended')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGYYYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('YYYYGG');
  });

  it('Level 2 #4: Prioritizes lower sumOfComposingSaplingsGenerations when tied on score, chance, gen (sum ASC)', () => {
    const direct = makeRoute('GGYYYY', { score: 6.0, chance: 1.0, generationIndex: 2, sumOfComposingSaplingsGenerations: 1 });
    const complex = makeRoute('YYYYGG', { score: 6.0, chance: 1.0, generationIndex: 2, sumOfComposingSaplingsGenerations: 3 });

    const sorted = [complex, direct].sort((a, b) =>
      compareScoredRoutes(a, b, 'recommended')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGYYYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('YYYYGG');
  });

  it('Level 2 #5: Uses genotype string alphabetically as deterministic tie breaker', () => {
    const routeA = makeRoute('GGGGYY', { score: 6.0, chance: 1.0, generationIndex: 1 });
    const routeB = makeRoute('GGYYYY', { score: 6.0, chance: 1.0, generationIndex: 1 });

    const sorted = [routeB, routeA].sort((a, b) =>
      compareScoredRoutes(a, b, 'recommended')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGGGYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYYYY');
  });

  it('sorts by Closest Target Match when sortBy is target', () => {
    const routeExact = makeRoute('GGYXGY', { score: 4.0 });
    const routeOther = makeRoute('GGYYYY', { score: 6.0 });

    const sorted = [routeOther, routeExact].sort((a, b) =>
      compareScoredRoutes(a, b, 'target', 'GGYXGY')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGYXGY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYYYY');
  });

  it('sorts by Highest Probability descending when sortBy is probability', () => {
    const route50 = makeRoute('GGYYYY', { score: 6.0, chance: 0.5 });
    const route100 = makeRoute('GGGGYY', { score: 5.0, chance: 1.0 });

    const sorted = [route50, route100].sort((a, b) =>
      compareScoredRoutes(a, b, 'probability')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGGGYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYYYY');
  });

  it('sorts by Fewest Generations ascending when sortBy is generations', () => {
    const gen2 = makeRoute('GGYYYY', { score: 6.0, generationIndex: 2 });
    const gen1 = makeRoute('GGGGYY', { score: 5.0, generationIndex: 1 });

    const sorted = [gen2, gen1].sort((a, b) =>
      compareScoredRoutes(a, b, 'generations')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGGGYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYYYY');
  });

  it('sorts by Fewest Clones ascending when sortBy is clones', () => {
    const route4Clones = makeRoute('GGYYYY', { analysisOverrides: { uniqueCloneCount: 4, totalPlacementsCount: 5 } });
    const route2Clones = makeRoute('GGGGYY', { analysisOverrides: { uniqueCloneCount: 2, totalPlacementsCount: 3 } });

    const sorted = [route4Clones, route2Clones].sort((a, b) =>
      compareScoredRoutes(a, b, 'clones')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('GGGGYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGYYYY');
  });

  it('sorts by Best Inventory Match (available > partial > missing) when sortBy is inventory', () => {
    const missing = makeRoute('GGYYYY', { analysisOverrides: { inventoryStatus: 'missing', missingClonesCount: 3 } });
    const partial = makeRoute('GGGGYY', { analysisOverrides: { inventoryStatus: 'partial', missingClonesCount: 1 } });
    const available = makeRoute('YYYYYY', { analysisOverrides: { inventoryStatus: 'available', missingClonesCount: 0 } });

    const sorted = [missing, available, partial].sort((a, b) =>
      compareScoredRoutes(a, b, 'inventory')
    );

    expect(sorted[0].group.resultSaplingGeneString).toBe('YYYYYY');
    expect(sorted[1].group.resultSaplingGeneString).toBe('GGGGYY');
    expect(sorted[2].group.resultSaplingGeneString).toBe('GGYYYY');
  });
});

