import { describe, it, expect } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import {
  fingerprintGroups,
  diffFingerprints,
  runGenerationReference,
  Fingerprint
} from '../bench/canonical.ts';
import { runGenerationFast } from '../domain/genetics/fastGeneration.ts';
import { SAMPLE_INPUT_124 } from '../bench/sampleInput.ts';

const GENES = 'GHYWX';

function makeRng(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    return s / 0x100000000;
  };
}

function randomPopulation(n: number, seed: number, greenBias = 0.6): Sapling[] {
  const rng = makeRng(seed);
  const out: Sapling[] = [];
  for (let i = 0; i < n; i++) {
    let g = '';
    for (let c = 0; c < 6; c++) {
      g += rng() < greenBias ? GENES[Math.floor(rng() * 3)] : GENES[3 + Math.floor(rng() * 2)];
    }
    out.push(new Sapling(g, 0, i));
  }
  return out;
}

function compare(source: Sapling[], minK: number, maxK: number, withRepetitions: boolean, minScore: number) {
  const params = {
    minK,
    maxK,
    withRepetitions,
    minimumTrackedScore: minScore,
    generationIndex: 1
  };
  const reference = runGenerationReference(source, params);
  const fast = runGenerationFast(source, params);

  const expected: Fingerprint = fingerprintGroups(reference.groups.values());
  const actual: Fingerprint = fingerprintGroups(fast.groups.values());
  return { expected, actual, reference, fast };
}

describe('fastCore equivalence with reference crossbreeding', () => {
  it('matches on a small hand-checkable population', () => {
    const source = [
      new Sapling('GGGGGG', 0, 0),
      new Sapling('YYYYYY', 0, 1),
      new Sapling('WWWWWW', 0, 2),
      new Sapling('GYHWXG', 0, 3),
      new Sapling('XXHHYY', 0, 4)
    ];
    const { expected, actual } = compare(source, 2, 3, true, 0);
    expect(expected.length).toBeGreaterThan(0);
    expect(diffFingerprints(expected, actual)).toEqual([]);
  });

  it.each([
    ['k=2..3 with repetitions', 2, 3, true, 4],
    ['k=2..3 without repetitions', 2, 3, false, 4],
    ['k=2..4 with repetitions', 2, 4, true, 4],
    ['k=1..3 with repetitions', 1, 3, true, 0],
    ['k=2..3 no score floor', 2, 3, true, 0]
  ])('matches on 24 random plants (%s)', (_label, minK, maxK, reps, minScore) => {
    const source = randomPopulation(24, 7);
    const { expected, actual } = compare(source, minK as number, maxK as number, reps as boolean, minScore as number);
    expect(expected.length).toBeGreaterThan(0);
    expect(diffFingerprints(expected, actual)).toEqual([]);
  });

  it('matches across many random populations', () => {
    for (let seed = 1; seed <= 40; seed++) {
      const source = randomPopulation(10 + (seed % 9), seed, 0.3 + (seed % 7) / 10);
      const { expected, actual } = compare(source, 2, 3, true, seed % 2 === 0 ? 4 : 0);
      const problems = diffFingerprints(expected, actual);
      expect(problems, `seed ${seed}`).toEqual([]);
    }
  });

  it('matches on red-heavy populations that force ties', () => {
    // W and X both weigh 1.0, so these populations generate many columns whose
    // maximum is exactly the red weight - the non-definitive tie path.
    for (let seed = 100; seed <= 120; seed++) {
      const source = randomPopulation(12, seed, 0.05);
      const { expected, actual } = compare(source, 2, 3, true, 0);
      expect(diffFingerprints(expected, actual), `seed ${seed}`).toEqual([]);
    }
  });

  it('matches on generated intermediates that have no source index', () => {
    // Intermediates (generationIndex > 0, index undefined) stay center-eligible
    // even when used as a surrounding plant.
    const source = [
      new Sapling('GGGGGG', 1),
      new Sapling('YYYYYY', 1),
      new Sapling('GYHWXG', 0, 0),
      new Sapling('XXHHYY', 0, 1),
      new Sapling('WHGWHG', 0, 2)
    ];
    const { expected, actual } = compare(source, 2, 3, true, 0);
    expect(diffFingerprints(expected, actual)).toEqual([]);
  });

  it('matches on the real 124-plant input at k=2..3', () => {
    const source = SAMPLE_INPUT_124.map((g, i) => new Sapling(g, 0, i));
    const { expected, actual, reference, fast } = compare(source, 2, 3, true, 4);

    expect(reference.combos).toBe(fast.combos);
    expect(diffFingerprints(expected, actual)).toEqual([]);
    // Sanity: the admission test must be doing real work.
    expect(fast.mapsRetained).toBeLessThan(reference.mapsProduced / 50);
  }, 300000);
});
