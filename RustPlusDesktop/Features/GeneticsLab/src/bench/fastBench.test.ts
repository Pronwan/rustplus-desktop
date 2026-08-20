import { describe, it } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { SAMPLE_INPUT_124 } from './sampleInput.ts';
import { runGenerationFast } from '../domain/genetics/fastGeneration.ts';
import { TargetConstraint } from '../domain/genetics/targetFilter.ts';

const SCENARIOS: { label: string; maxK: number; target?: TargetConstraint }[] = [
  { label: 'k=2..3, no target', maxK: 3 },
  { label: 'k=2..4, no target', maxK: 4 },
  { label: 'k=2..3, at-least GGY', maxK: 3, target: { targetGenetics: 'GGY***', matchMode: 'at-least' } },
  { label: 'k=2..4, at-least GGY', maxK: 4, target: { targetGenetics: 'GGY***', matchMode: 'at-least' } },
  { label: 'k=2..4, exact GGYYHH', maxK: 4, target: { targetGenetics: 'GGYYHH', matchMode: 'exact' } }
];

describe.skipIf(!process.env.GL_BENCH)('fast core benchmark', () => {
  for (const s of SCENARIOS) {
    it(s.label, () => {
      const pop = SAMPLE_INPUT_124.map((g, i) => new Sapling(g, 0, i));
      const t0 = performance.now();
      const r = runGenerationFast(pop, {
        minK: 2, maxK: s.maxK, withRepetitions: true,
        minimumTrackedScore: 4, generationIndex: 1, target: s.target
      });
      const ms = performance.now() - t0;
      // eslint-disable-next-line no-console
      console.log(
        `${s.label.padEnd(26)} combos=${r.combos.toLocaleString().padStart(11)} ` +
        `groups=${String(r.groups.size).padStart(5)} retained=${String(r.mapsRetained).padStart(6)} ` +
        `${(ms/1000).toFixed(2).padStart(6)}s ${Math.round(r.combos/(ms/1000)).toLocaleString().padStart(11)} combos/s`
      );
    }, 1800000);
  }
});
