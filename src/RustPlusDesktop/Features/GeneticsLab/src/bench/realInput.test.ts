import { describe, it } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { SAMPLE_INPUT_124 } from './sampleInput.ts';
import { runGeneration } from './solverBench.test.ts';
import { getNumberOfCrossbreedingCombinations } from '../domain/genetics/combinations.ts';

describe.skipIf(!process.env.GL_BENCH)('real 124-plant input', () => {
  for (const maxK of (process.env.GL_BENCH_FULL ? [3, 4] : [3])) {
    it(`k=2..${maxK}`, () => {
      const pop = SAMPLE_INPUT_124.map((g, i) => new Sapling(g, 0, i));
      const total = getNumberOfCrossbreedingCombinations(pop.length, {
        withRepetitions: true,
        minCrossbreedingSaplingsNumber: 2,
        maxCrossbreedingSaplingsNumber: maxK
      });
      const r = runGeneration(pop, 2, maxK, true, 4);
      // eslint-disable-next-line no-console
      console.log(
        `N=${pop.length} k=2..${maxK}: combos=${r.combos.toLocaleString()} (expected ${total.toLocaleString()}) ` +
        `maps=${r.maps.toLocaleString()} groups=${r.groups} ${(r.ms/1000).toFixed(1)}s ` +
        `${Math.round(r.combos/(r.ms/1000)).toLocaleString()} combos/s`
      );
    }, 1800000);
  }
});
