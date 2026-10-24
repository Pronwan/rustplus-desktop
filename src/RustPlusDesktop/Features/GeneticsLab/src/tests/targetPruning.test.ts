import { describe, it, expect } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { runGenerationFast } from '../domain/genetics/fastGeneration.ts';
import { buildRejectTable, TargetConstraint } from '../domain/genetics/targetFilter.ts';
import { decodeGenotype, GENOTYPE_SPACE, encodeGenotype } from '../domain/genetics/fastCore.ts';
import { isExactMatch, meetsAtLeast } from '../utils/targetMatch.ts';
import { fingerprintGroups, diffFingerprints } from '../bench/canonical.ts';

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

describe('buildRejectTable', () => {
  it('agrees with the string target helpers across the whole genotype space', () => {
    const targets: TargetConstraint[] = [
      { targetGenetics: 'GGGGGG', matchMode: 'exact' },
      { targetGenetics: 'GY**H*', matchMode: 'exact' },
      { targetGenetics: 'G?????', matchMode: 'exact' },
      { targetGenetics: 'GGGYY*', matchMode: 'at-least' },
      { targetGenetics: 'YY****', matchMode: 'at-least' },
      { targetGenetics: 'GHYWX*', matchMode: 'at-least' }
    ];

    for (const target of targets) {
      const table = buildRejectTable([], target);
      let checked = 0;
      for (let code = 0; code < GENOTYPE_SPACE; code++) {
        const genotype = decodeGenotype(code);
        // Skip codes containing the three unused 3-bit values.
        if (encodeGenotype(genotype) !== code) continue;
        const expected =
          target.matchMode === 'exact'
            ? isExactMatch(genotype, target.targetGenetics)
            : meetsAtLeast(genotype, target.targetGenetics);
        expect(table[code] === 0, `${target.targetGenetics}/${target.matchMode} ${genotype}`).toBe(
          expected
        );
        checked++;
      }
      expect(checked).toBe(5 ** 6);
    }
  });

  it('never filters in best-possible mode, and still rejects owned genotypes', () => {
    const table = buildRejectTable(['GGGGGG'], {
      targetGenetics: 'YYYYYY',
      matchMode: 'best-possible'
    });
    expect(table[encodeGenotype('GGGGGG')]).toBe(1);
    expect(table[encodeGenotype('YYYYYY')]).toBe(0);
    expect(table[encodeGenotype('WHWHWH')]).toBe(0);
  });

  it('rejects owned genotypes even when they match the target', () => {
    const table = buildRejectTable(['GGGGGG'], {
      targetGenetics: 'GGGGGG',
      matchMode: 'exact'
    });
    expect(table[encodeGenotype('GGGGGG')]).toBe(1);
  });
});

describe('final-generation target pruning is display-equivalent', () => {
  const cases: TargetConstraint[] = [
    { targetGenetics: 'GGYYHH', matchMode: 'exact' },
    { targetGenetics: 'G*Y*H*', matchMode: 'exact' },
    { targetGenetics: 'GGY***', matchMode: 'at-least' },
    { targetGenetics: 'YY*H**', matchMode: 'at-least' }
  ];

  it.each(cases.map(c => [`${c.matchMode} ${c.targetGenetics}`, c] as const))(
    'pruned run equals unpruned run filtered by the UI rule (%s)',
    (_label, target) => {
      for (let seed = 1; seed <= 12; seed++) {
        const source = randomPopulation(14, seed);
        const params = {
          minK: 2,
          maxK: 3,
          withRepetitions: true,
          minimumTrackedScore: 0,
          generationIndex: 1
        };

        const unpruned = runGenerationFast(source, params);
        const pruned = runGenerationFast(source, { ...params, target });

        // What the UI would show from the unpruned run.
        const keep = Array.from(unpruned.groups.values()).filter(g =>
          target.matchMode === 'exact'
            ? isExactMatch(g.resultSaplingGeneString, target.targetGenetics)
            : meetsAtLeast(g.resultSaplingGeneString, target.targetGenetics)
        );

        expect(
          diffFingerprints(fingerprintGroups(keep), fingerprintGroups(pruned.groups.values())),
          `seed ${seed}`
        ).toEqual([]);
      }
    }
  );

  it('prunes enough to matter on the real input', () => {
    const source = randomPopulation(40, 3);
    const params = {
      minK: 2,
      maxK: 3,
      withRepetitions: true,
      minimumTrackedScore: 4,
      generationIndex: 1
    };
    const unpruned = runGenerationFast(source, params);
    const pruned = runGenerationFast(source, {
      ...params,
      target: { targetGenetics: 'GG****', matchMode: 'at-least' }
    });
    expect(pruned.groups.size).toBeLessThan(unpruned.groups.size);
    expect(pruned.groups.size).toBeGreaterThan(0);
  });
});
