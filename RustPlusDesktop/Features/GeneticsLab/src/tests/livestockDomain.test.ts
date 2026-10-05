import { describe, it, expect } from 'vitest';
import {
  createAnimal,
  decodeGeneRow,
  findScannedAnimal,
  encodeAnimalGenes,
  encodeGeneRow,
  LivestockGeneRow,
  suggestAnimalName
} from '../domain/livestock/animal.ts';
import { GeneLevel, cycleLevel } from '../domain/livestock/livestockGenes.ts';
import { ageFactor, estimateSalePrice, healthFactor } from '../domain/livestock/pricing.ts';
import { animalStats } from '../domain/livestock/stats.ts';
import { evaluatePair, relationBetween, suggestPairs } from '../domain/livestock/pairing.ts';
import {
  WILD_GENOTYPE,
  breedsTrue,
  conditionOnExpressed,
  exactGenotype,
  hidesBad,
  inferHerdGenotypes,
  offspringGenotype
} from '../domain/livestock/genotype.ts';

function row(levels: string, marker: LivestockGeneRow['marker'] = { value: 0, color: 'pink' }): LivestockGeneRow {
  const map: Record<string, GeneLevel | null> = { r: 'low', n: 'mid', g: 'high', '?': null };
  return { levels: levels.split('').map((ch) => map[ch]), marker };
}

describe('livestock gene rows', () => {
  it('round-trips the compact row code', () => {
    const original = row('rng?g', { value: 12, color: 'blue' });
    const code = encodeGeneRow(original);
    expect(code).toBe('rng?g|12b');
    expect(decodeGeneRow(code)).toEqual(original);
  });

  it('rejects malformed codes', () => {
    expect(decodeGeneRow('rng|0p')).toBeNull();
    expect(decodeGeneRow('rngxx|0p')).toBeNull();
    expect(decodeGeneRow('rnggg-0p')).toBeNull();
  });

  it('encodes both rows so two reads of one panel compare equal', () => {
    const a = encodeAnimalGenes([row('rrrrr'), row('rrrrr', { value: 1, color: 'blue' })]);
    const b = encodeAnimalGenes([row('rrrrr'), row('rrrrr', { value: 1, color: 'blue' })]);
    expect(a).toBe(b);
    expect(a).toBe('rrrrr|0p/rrrrr|1b');
  });

  it('cycles badge levels neutral -> green -> red -> neutral', () => {
    expect(cycleLevel('mid')).toBe('high');
    expect(cycleLevel('high')).toBe('low');
    expect(cycleLevel('low')).toBe('mid');
    expect(cycleLevel(null)).toBe('high');
  });

  it('numbers default names within a kind', () => {
    const herd = [
      createAnimal({ species: 'cattle', sex: 'female', name: 'Cow 1' }),
      createAnimal({ species: 'cattle', sex: 'male', name: 'Bull 1' })
    ];
    expect(suggestAnimalName('cattle', 'female', herd)).toBe('Cow 2');
    expect(suggestAnimalName('sheep', 'male', herd)).toBe('Ram 1');
  });
});

describe('livestock sale price', () => {
  it('reaches the measured maximum of 78 for an all-green young cow', () => {
    const estimate = estimateSalePrice({ species: 'cattle', row: row('ggggg'), inbred: false });
    expect(estimate.price).toBe(78);
  });

  it('reaches the measured minimum of 3 for the worst possible sheep', () => {
    const estimate = estimateSalePrice({
      species: 'sheep',
      row: row('rrrrr'),
      inbred: true,
      healthState: 0.1,
      ageLived: 1
    });
    expect(estimate.price).toBe(3);
  });

  it('prices a baseline cow at 50', () => {
    expect(estimateSalePrice({ species: 'cattle', row: row('nnnnn'), inbred: false }).price).toBe(50);
  });

  it('hits the health and age anchors exactly and interpolates between them', () => {
    expect(healthFactor(0.1)).toBeCloseTo(0.4);
    expect(healthFactor(0.475)).toBeCloseTo(0.7);
    expect(healthFactor(0.9)).toBeCloseTo(1);
    expect(healthFactor(0.6375)).toBeCloseTo(0.85);
    expect(ageFactor(0.3)).toBe(1);
    expect(ageFactor(0.75)).toBeCloseTo(0.75);
    expect(ageFactor(1)).toBeCloseTo(0.5);
  });

  it('treats an unread badge as baseline', () => {
    const known = estimateSalePrice({ species: 'cattle', row: row('nnnnn'), inbred: false });
    const unread = estimateSalePrice({ species: 'cattle', row: row('?????'), inbred: false });
    expect(unread.exact).toBe(known.exact);
  });
});

describe('livestock stats (game values)', () => {
  it('reads a cow\'s effects from its expressed genes', () => {
    const cow = createAnimal({ species: 'cattle', sex: 'female', rows: [row('ggrgn')] });
    const byLabel = Object.fromEntries(animalStats(cow).map((s) => [s.label, s.value]));
    expect(byLabel['Dung interval']).toBe('12m 30s (4.8/h)');
    expect(byLabel['Adult lifespan']).toBe('72h');
    expect(byLabel['Milking cooldown']).toBe('8m 20s');
    expect(byLabel['Milk per milking']).toBe('1');
    expect(byLabel['Cooldown after giving birth']).toBe('41m 15s');
    expect(byLabel['Twins chance']).toBe('60%');
    expect(byLabel['Male breeding cooldown']).toBeUndefined();
  });

  it('matches the documented Ok and Bad values', () => {
    const bull = createAnimal({ species: 'cattle', sex: 'male', rows: [row('rnrrr')] });
    const byLabel = Object.fromEntries(animalStats(bull).map((s) => [s.label, s.value]));
    expect(byLabel['Male breeding cooldown']).toBe('15m 23s');
    expect(byLabel['Adult lifespan']).toBe('48h');
    expect(byLabel['Dung interval']).toBe('33m 20s (1.8/h)');
    expect(byLabel['Lowest need for full condition']).toBe('71%');
  });

  it('gives sheep wool and dung', () => {
    const ram = createAnimal({ species: 'sheep', sex: 'male', rows: [row('nnnnn')] });
    const byLabel = Object.fromEntries(animalStats(ram).map((s) => [s.label, s.value]));
    expect(byLabel['Wool per full fleece']).toBe('10');
    expect(byLabel['Fleece regrow']).toBe('5m');
    expect(byLabel['Dung interval']).toBe('20m (3/h)');
  });

  it('needs 21 Good-dung adults per Biofuel Generator, and no dung from babies', () => {
    const cow = createAnimal({ species: 'cattle', sex: 'female', rows: [row('gnnnn')] });
    expect(animalStats(cow).find((s) => s.gene === 'D')?.note).toMatch(/^21 adults/);
    const calf = createAnimal({ species: 'cattle', rows: [row('gnnnn')], observed: { ageSeconds: 600, overall: 1, at: Date.now() } });
    expect(animalStats(calf).find((s) => s.gene === 'D')?.applies).toBe(false);
  });

  it('weakens every inbred gene effect to x0.7', () => {
    const lifespan = (genes: string) =>
      animalStats(createAnimal({ inbred: true, rows: [row(genes)] })).find((s) => s.gene === 'L')?.value;
    expect(lifespan('nrnnn')).toBe('21h 50m');
    expect(lifespan('nnnnn')).toBe('33h 36m');
    expect(lifespan('ngnnn')).toBe('50h 24m');

    const cow = createAnimal({ inbred: true, species: 'cattle', sex: 'female', rows: [row('nngnn')] });
    expect(animalStats(cow).find((s) => s.label === 'Milk per milking')?.value).toBe('1');
    const sheep = createAnimal({ inbred: true, species: 'sheep', rows: [row('nngnn')] });
    expect(animalStats(sheep).find((s) => s.label === 'Wool per full fleece')?.value).toBe('11');
  });
});

describe('hidden copies', () => {
  it('knows a Bad badge means two Bad copies', () => {
    const d = conditionOnExpressed(WILD_GENOTYPE, 'low');
    expect(breedsTrue(d)).toBe(0);
    expect(hidesBad(d)).toBeCloseTo(1);
  });

  it('estimates a wild Ok badge: 25 of 55 pure Ok, the rest hiding Bad', () => {
    const d = conditionOnExpressed(WILD_GENOTYPE, 'mid');
    expect(hidesBad(d)).toBeCloseTo(0.3 / 0.55);
  });

  it('estimates a wild Good badge: 1 in 9 pure Good', () => {
    const d = conditionOnExpressed(WILD_GENOTYPE, 'high');
    expect(breedsTrue(d)).toBeCloseTo(0.04 / 0.36);
  });

  it('passes one random copy from each parent', () => {
    const pure = exactGenotype('high', 'high');
    const carrier = exactGenotype('high', 'low');
    const child = offspringGenotype(pure, carrier);
    // GG x GB -> half GG, half GB: always shows Good, pure half the time.
    expect(breedsTrue(child)).toBeCloseTo(0.5);
    expect(hidesBad(child)).toBeCloseTo(0.5);
  });

  it('uses recorded parents: two pure Good parents make a pure Good calf', () => {
    const mum = createAnimal({ id: 'm', sex: 'female', rows: [row('ggggg'), row('ggggg')] });
    const dad = createAnimal({ id: 'd', sex: 'male', rows: [row('ggggg'), row('ggggg')] });
    const calf = createAnimal({ id: 'c', motherId: 'm', fatherId: 'd', rows: [row('ggggg')] });
    const genotypes = inferHerdGenotypes([mum, dad, calf]);
    expect(breedsTrue(genotypes.get('c')![0])).toBeCloseTo(1);
  });

  it('survives a parent loop in the records', () => {
    const a = createAnimal({ id: 'a', motherId: 'b', rows: [row('nnnnn')] });
    const b = createAnimal({ id: 'b', motherId: 'a', rows: [row('nnnnn')] });
    expect(inferHerdGenotypes([a, b]).size).toBe(2);
  });
});

describe('livestock pairing (documented inheritance)', () => {
  // Two-row animals are known exactly: the rows are their two copies.
  const pureBull = createAnimal({ id: 'bull', species: 'cattle', sex: 'male', rows: [row('ggggg'), row('ggggg')] });
  const carrierCow = createAnimal({ id: 'cow', species: 'cattle', sex: 'female', rows: [row('ggggg'), row('rrrrr')] });

  it('a pure Good parent makes every calf show Good, and half of them pure', () => {
    const pair = evaluatePair(pureBull, carrierCow, [pureBull, carrierCow]);
    expect(pair.perGene[0].high).toBeCloseTo(1);
    expect(pair.allHighChance).toBeCloseTo(1);
    expect(pair.godCloneChance).toBeCloseTo(0.5 ** 5);
    expect(pair.relation).toBeNull();
  });

  it('two carriers of Bad can throw a Bad calf', () => {
    const carrierBull = createAnimal({ id: 'cb', species: 'cattle', sex: 'male', rows: [row('ggggg'), row('rrrrr')] });
    const pair = evaluatePair(carrierBull, carrierCow, [carrierBull, carrierCow]);
    expect(pair.perGene[2].low).toBeCloseTo(0.25);
    expect(pair.perGene[2].high).toBeCloseTo(0.75);
  });

  it('only pairs opposite sexes of the same species', () => {
    const ewe = createAnimal({ id: 'ewe', species: 'sheep', sex: 'female' });
    const cow2 = createAnimal({ id: 'cow2', species: 'cattle', sex: 'female' });
    const pairs = suggestPairs([pureBull, carrierCow, ewe, cow2]);
    expect(pairs).toHaveLength(2);
    for (const pair of pairs) {
      expect(pair.male.id).toBe('bull');
      expect(pair.female.species).toBe('cattle');
    }
  });

  it('ranks the cow most likely to give all-Good calves first', () => {
    const carrierBull = createAnimal({ id: 'cb', species: 'cattle', sex: 'male', rows: [row('ggggg'), row('rrrrr')] });
    const weakCow = createAnimal({ id: 'weak', species: 'cattle', sex: 'female', rows: [row('rrrrr'), row('rrrrr')] });
    const pairs = suggestPairs([carrierBull, weakCow, carrierCow], 'allHigh');
    expect(pairs[0].female.id).toBe('cow');
  });

  it('flags recorded relatives and scores them as inbred', () => {
    const calf = createAnimal({ id: 'calf', species: 'cattle', sex: 'female', motherId: 'cow', fatherId: 'bull', rows: [row('nnnnn'), row('nnnnn')] });
    const herd = [pureBull, carrierCow, calf];
    expect(relationBetween(pureBull, calf, herd)).toBe('parent-child');
    const sibling = createAnimal({ id: 'sib', species: 'cattle', sex: 'male', motherId: 'cow', fatherId: 'bull' });
    expect(relationBetween(sibling, calf, [...herd, sibling])).toBe('siblings');

    const inbred = evaluatePair(pureBull, calf, herd);
    expect(inbred.relation).toBe('parent-child');
    // Every calf shows Good, weakened to x0.7 by inbreeding: mean 1.56 x 0.7.
    expect(inbred.expectedGeneFactor).toBeCloseTo(1.092);
  });

  it('finds a shared grandparent', () => {
    const grandma = createAnimal({ id: 'gm', species: 'cattle', sex: 'female' });
    const mumA = createAnimal({ id: 'ma', species: 'cattle', sex: 'female', motherId: 'gm' });
    const mumB = createAnimal({ id: 'mb', species: 'cattle', sex: 'female', motherId: 'gm' });
    const a = createAnimal({ id: 'a', species: 'cattle', sex: 'male', motherId: 'ma' });
    const b = createAnimal({ id: 'b', species: 'cattle', sex: 'female', motherId: 'mb' });
    expect(relationBetween(a, b, [grandma, mumA, mumB, a, b])).toBe('shared-ancestor');
  });
});

describe('no duplicates: a scan finds the animal it belongs to', () => {
  const naomi = createAnimal({ id: 'n', species: 'sheep', gameName: 'Naomi', rows: [row('nggng', { value: 10, color: 'lime' })] });
  const unnamed = createAnimal({ id: 'u', species: 'cattle', rows: [row('rrgnn', { value: 6, color: 'purple' })] });
  const herd = [naomi, unnamed];

  it('finds an animal by name even when its genes and number read differently', () => {
    const scan = { rows: [row('rrrrr', { value: 3, color: 'blue' })], name: 'Naomj', species: 'sheep' as const };
    expect(findScannedAnimal(herd, scan)?.id).toBe('n');
  });

  it('gives a name to the same animal scanned before names were read', () => {
    const scan = { rows: [row('rrgnn', { value: 6, color: 'purple' })], name: 'Desiree', species: 'cattle' as const };
    expect(findScannedAnimal(herd, scan)?.id).toBe('u');
  });

  it('treats a different name as a different animal, even with the same genes', () => {
    const scan = { rows: [row('nggng', { value: 10, color: 'lime' })], name: 'Miriam', species: 'sheep' as const };
    expect(findScannedAnimal(herd, scan)).toBeNull();
  });

  it('falls back to identical genes when no name could be read', () => {
    expect(findScannedAnimal(herd, { rows: naomi.rows, name: null, species: null })?.id).toBe('n');
    expect(findScannedAnimal(herd, { rows: [row('ggggg')], name: null, species: null })).toBeNull();
  });

  it('never matches across species', () => {
    expect(findScannedAnimal(herd, { rows: naomi.rows, name: 'Naomi', species: 'cattle' })).toBeNull();
  });
});
