/**
 * Solver benchmark harness.
 *
 * Skipped by default so `npm test` stays fast. Run with:
 *   npx cross-env GL_BENCH=1 npx vitest run src/bench/solverBench.test.ts
 * or on Windows PowerShell:
 *   $env:GL_BENCH=1; npx vitest run src/bench/solverBench.test.ts
 *
 * Measures the single-threaded cost of the current enumeration + evaluation
 * path so optimisation work has a stable before/after number.
 */
import { describe, it } from 'vitest';
import { Sapling, DEFAULT_GENE_SCORES } from '../domain/genetics/Sapling.ts';
import { evaluateCombination } from '../domain/genetics/crossbreeding.ts';
import { appendAndOrganizeResults } from '../domain/genetics/sorting.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import {
  getWorkChunks,
  getNumberOfCrossbreedingCombinations,
  setNextPositionInChunk
} from '../domain/genetics/combinations.ts';

const GENES = ['G', 'H', 'Y', 'W', 'X'] as const;

/** Deterministic LCG so benchmark inputs are reproducible across runs. */
function makeRng(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (Math.imul(s, 1664525) + 1013904223) >>> 0;
    return s / 0x100000000;
  };
}

/**
 * Builds a realistic scanned population: `distinct` unique genotypes, inflated
 * to `total` plants by duplication (users scan the same genotype repeatedly).
 */
export function makePopulation(total: number, distinct: number, seed = 42): Sapling[] {
  const rng = makeRng(seed);
  const pool: string[] = [];
  for (let i = 0; i < distinct; i++) {
    let s = '';
    for (let c = 0; c < 6; c++) {
      // Bias toward green genes, like a mid-game clone bank.
      const r = rng();
      s += r < 0.62 ? GENES[Math.floor(rng() * 3)] : GENES[3 + Math.floor(rng() * 2)];
    }
    pool.push(s);
  }
  const out: Sapling[] = [];
  for (let i = 0; i < total; i++) {
    out.push(new Sapling(pool[i % distinct], 0, i));
  }
  return out;
}

/** Runs one full generation single-threaded, exactly as the worker would. */
export function runGeneration(
  source: Sapling[],
  minK: number,
  maxK: number,
  withRepetitions: boolean,
  minimumTrackedScore: number
): { combos: number; maps: number; groups: number; ms: number } {
  const existing = new Set(source.map(s => s.toString()));
  const opts = { geneScores: DEFAULT_GENE_SCORES, minimumTrackedScore };
  const groupMap = new Map<string, GeneticsMapGroup>();
  const combinatorics = {
    withRepetitions,
    minCrossbreedingSaplingsNumber: minK,
    maxCrossbreedingSaplingsNumber: maxK
  };

  const chunks = getWorkChunks(source.length, combinatorics);
  let combos = 0;
  let maps = 0;

  const t0 = performance.now();
  for (const chunk of chunks) {
    const positions = [...chunk.startingPositions];
    for (let c = 0; c < chunk.combinationsToProcess; c++) {
      const surrounding = positions.map(i => source[i]);
      const result = evaluateCombination(surrounding, source, existing, opts, 1);
      combos++;
      if (result.length > 0) {
        maps += result.length;
        appendAndOrganizeResults(groupMap, result);
      }
      if (c < chunk.combinationsToProcess - 1) {
        setNextPositionInChunk(positions, source.length, withRepetitions);
      }
    }
  }
  const ms = performance.now() - t0;
  return { combos, maps, groups: groupMap.size, ms };
}

const SCENARIOS = [
  { name: '20 plants / 12 distinct / k=2..3', total: 20, distinct: 12, minK: 2, maxK: 3 },
  { name: '30 plants / 18 distinct / k=2..3', total: 30, distinct: 18, minK: 2, maxK: 3 },
  { name: '40 plants / 15 distinct / k=2..3', total: 40, distinct: 15, minK: 2, maxK: 3 },
  { name: '30 plants / 18 distinct / k=2..4', total: 30, distinct: 18, minK: 2, maxK: 4 },
  { name: '50 plants / 25 distinct / k=2..3', total: 50, distinct: 25, minK: 2, maxK: 3 }
];

describe.skipIf(!process.env.GL_BENCH)('solver baseline benchmark', () => {
  for (const s of SCENARIOS) {
    it(s.name, () => {
      const pop = makePopulation(s.total, s.distinct);
      const total = getNumberOfCrossbreedingCombinations(pop.length, {
        withRepetitions: true,
        minCrossbreedingSaplingsNumber: s.minK,
        maxCrossbreedingSaplingsNumber: s.maxK
      });
      const r = runGeneration(pop, s.minK, s.maxK, true, 4);
      const perSec = Math.round(r.combos / (r.ms / 1000));
      // eslint-disable-next-line no-console
      console.log(
        `${s.name.padEnd(34)} combos=${String(r.combos).padStart(9)} ` +
          `(expected ${total}) maps=${String(r.maps).padStart(8)} ` +
          `groups=${String(r.groups).padStart(5)} ${r.ms.toFixed(0).padStart(7)}ms ` +
          `${perSec.toLocaleString()} combos/s`
      );
    }, 600000);
  }
});
