import { describe, it, expect } from 'vitest';
import { Gene, GREEN_GENE_WEIGHT, RED_GENE_WEIGHT } from '../domain/genetics/Gene.ts';
import { Sapling, DEFAULT_GENE_SCORES } from '../domain/genetics/Sapling.ts';
import { evaluateCombination } from '../domain/genetics/crossbreeding.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import {
  resultMapsSortingFunction,
  resultMapGroupsSortingFunction,
  appendAndOrganizeResults
} from '../domain/genetics/sorting.ts';
import {
  binomialCoefficient,
  multisetCoefficient,
  getNumberOfCrossbreedingCombinations
} from '../domain/genetics/combinations.ts';

describe('Gene Model', () => {
  it('assigns correct crossbreeding weights for green and red genes', () => {
    expect(new Gene('G').getCrossbreedingWeight()).toBe(0.6);
    expect(new Gene('Y').getCrossbreedingWeight()).toBe(0.6);
    expect(new Gene('H').getCrossbreedingWeight()).toBe(0.6);
    expect(new Gene('W').getCrossbreedingWeight()).toBe(1.0);
    expect(new Gene('X').getCrossbreedingWeight()).toBe(1.0);
  });

  it('rejects invalid gene characters', () => {
    expect(() => new Gene('Z')).toThrow();
  });
});

describe('Sapling Model', () => {
  it('counts green gene types accurately', () => {
    const s = new Sapling('GGYYHH');
    expect(s.numberOfGs()).toBe(2);
    expect(s.numberOfYs()).toBe(2);
    expect(s.numberOfHs()).toBe(2);
    expect(s.numberOfWs()).toBe(0);
    expect(s.numberOfXs()).toBe(0);
  });

  it('calculates score based on configured weights', () => {
    const s = new Sapling('GGYYHH');
    // G=1, Y=1, H=0.5 => 1+1+1+1+0.5+0.5 = 5.0
    expect(s.getScore(DEFAULT_GENE_SCORES)).toBe(5.0);

    const sBad = new Sapling('XXWWGG');
    // X=0, W=0, G=1 => 0+0+0+0+1+1 = 2.0
    expect(sBad.getScore(DEFAULT_GENE_SCORES)).toBe(2.0);
  });

  it('validates 6-gene strings', () => {
    expect(Sapling.isValidGeneString('GYXWHH')).toBe(true);
    expect(Sapling.isValidGeneString('gyxwhh')).toBe(true);
    expect(Sapling.isValidGeneString('GYXW')).toBe(false);
    expect(Sapling.isValidGeneString('GYXWHHA')).toBe(false);
    expect(Sapling.isValidGeneString('GYXWHZ')).toBe(false);
  });
});

describe('Crossbreeding Rules', () => {
  const options = {
    geneScores: DEFAULT_GENE_SCORES,
    minimumTrackedScore: 0
  };

  it('produces expected genotype when two green genes beat one red gene', () => {
    // 2 surrounding plants with G at slot 0 (0.6 + 0.6 = 1.2) beats 1 plant with W (1.0)
    // Result genotype will be GGGGGG
    const p1 = new Sapling('GGGGGG', 0, 0);
    const p2 = new Sapling('GGGGGG', 0, 1);
    const p3 = new Sapling('WGGGGG', 0, 2);

    const surrounding = [p1, p2, p3];
    const allSource = [p1, p2, p3];
    // Do not include GGGGGG in existing to verify generation
    const existing = new Set(['WGGGGG']);

    const maps = evaluateCombination(surrounding, allSource, existing, options, 1);
    expect(maps.length).toBe(1);
    expect(maps[0].resultSapling.toString()).toBe('GGGGGG');
  });

  it('preserves center gene when center weight >= surrounding total', () => {
    // Surrounding: 2 plants contributing H (0.6 each = 1.2) in cols 1-5, but in col 0 each contributes 0.6
    // Center has W in col 0. W (1.0) >= 0.6 => Center W survives in col 0!
    // Result genotype: WHHHHH (Score: 2.5)
    const s1 = new Sapling('GHHHHH', 0, 0);
    const s2 = new Sapling('HHHHHG', 0, 1);
    const center = new Sapling('WHHHHH', 0, 2);

    const surrounding = [s1, s2];
    const allSource = [s1, s2, center];
    const existing = new Set(['GHHHHH', 'HHHHHG']); // WHHHHH is new!

    const maps = evaluateCombination(surrounding, allSource, existing, options, 1);
    const centerMaps = maps.filter(m => m.baseSapling?.toString() === 'WHHHHH');
    expect(centerMaps.length).toBeGreaterThan(0);
    expect(centerMaps[0].resultSapling.genes[0].type).toBe('W');
    expect(centerMaps[0].resultSapling.toString()).toBe('WHHHHH');
  });

  it('branches into 0.5 chance each when single definitive tie occurs', () => {
    // Col 0: G (0.6 + 0.6 = 1.2) vs Y (0.6 + 0.6 = 1.2). Both > 1.0 and equal => Definitive tie!
    // Cols 1-5: 2x H (1.2) -> H
    // Branches: GHHHHH and YHHHHH
    const p1 = new Sapling('GHHHHH', 0, 0);
    const p2 = new Sapling('GHHHHH', 0, 1);
    const p3 = new Sapling('YHHHHH', 0, 2);
    const p4 = new Sapling('YHHHHH', 0, 3);

    const surrounding = [p1, p2, p3, p4];
    const allSource = [p1, p2, p3, p4];
    const existing = new Set(['XXXXXX']); // GHHHHH and YHHHHH are new!

    const maps = evaluateCombination(surrounding, allSource, existing, options, 1);
    expect(maps.length).toBe(2);
    expect(maps[0].chance).toBe(0.5);
    expect(maps[1].chance).toBe(0.5);
    const resultStrings = maps.map(m => m.resultSapling.toString());
    expect(resultStrings).toContain('GHHHHH');
    expect(resultStrings).toContain('YHHHHH');
  });

  it('rejects combination with more than one definitive tie (Rule A)', () => {
    // Col 0 tied (1.2 G vs 1.2 Y) AND Col 1 tied (1.2 G vs 1.2 Y) => > 1 tie => Rejected!
    const p1 = new Sapling('GGHHHH', 0, 0);
    const p2 = new Sapling('GGHHHH', 0, 1);
    const p3 = new Sapling('YYHHHH', 0, 2);
    const p4 = new Sapling('YYHHHH', 0, 3);

    const surrounding = [p1, p2, p3, p4];
    const allSource = [p1, p2, p3, p4];
    const existing = new Set(['XXXXXX']);

    const maps = evaluateCombination(surrounding, allSource, existing, options, 1);
    expect(maps.length).toBe(0);
  });

  it('rejects combination if any surrounding plant fails to contribute (Rule B)', () => {
    // p3 has W in all columns while p1 and p2 have 2x G (1.2) everywhere => p3 contributes nowhere!
    const p1 = new Sapling('GGGGGG', 0, 0);
    const p2 = new Sapling('GGGGGG', 0, 1);
    const p3 = new Sapling('WWWWWW', 0, 2);

    const surrounding = [p1, p2, p3];
    const allSource = [p1, p2, p3];
    const existing = new Set(['XXXXXX']);

    const maps = evaluateCombination(surrounding, allSource, existing, options, 1);
    expect(maps.length).toBe(0);
  });
});

describe('Sorting Priorities', () => {
  it('sorts maps in group by generation, chance, composing gens, surrounding count', () => {
    const r = new Sapling('GGYYYY', 1);
    const mapHighChance = new GeneticsMap(r, [new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0)], undefined, 1.0, 6.0);
    const mapLowChance = new GeneticsMap(r, [new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0)], undefined, 0.5, 6.0);

    const sorted = [mapLowChance, mapHighChance].sort(resultMapsSortingFunction);
    expect(sorted[0].chance).toBe(1.0);
  });

  it('sorts groups by score, chance product, generation, and alphabetical genotype', () => {
    const group1 = new GeneticsMapGroup('GGYYYY', [
      new GeneticsMap(new Sapling('GGYYYY', 1), [], undefined, 1.0, 6.0)
    ]);
    const group2 = new GeneticsMapGroup('GGYYHH', [
      new GeneticsMap(new Sapling('GGYYHH', 1), [], undefined, 1.0, 5.0)
    ]);

    const sorted = [group2, group1].sort(resultMapGroupsSortingFunction);
    expect(sorted[0].resultSaplingGeneString).toBe('GGYYYY');
  });

  it('limits groups to at most 3 maps', () => {
    const groupMap = new Map<string, GeneticsMapGroup>();
    const r = new Sapling('GGYYYY', 1);

    const map1 = new GeneticsMap(r, [new Sapling('GGGGGG', 0)], undefined, 1.0, 6.0);
    const map2 = new GeneticsMap(r, [new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0)], undefined, 0.9, 6.0);
    const map3 = new GeneticsMap(r, [new Sapling('GGGGGG', 0), new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0)], undefined, 0.8, 6.0);
    const map4 = new GeneticsMap(r, [new Sapling('GGGGGG', 0), new Sapling('YYYYYY', 0), new Sapling('YYYYYY', 0)], undefined, 0.5, 6.0);

    appendAndOrganizeResults(groupMap, [map1, map2, map3, map4]);
    const group = groupMap.get('GGYYYY')!;
    expect(group.mapList.length).toBe(3);
  });
});

describe('Combinatorics', () => {
  it('computes correct binomial coefficients', () => {
    expect(binomialCoefficient(5, 2)).toBe(10);
    expect(binomialCoefficient(6, 3)).toBe(20);
  });

  it('computes correct multiset coefficients', () => {
    // C(n + k - 1, k) for n=3, k=2 => C(4, 2) = 6
    expect(multisetCoefficient(3, 2)).toBe(6);
  });
});
