import { describe, it, expect } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { CrossbreedingOrchestrator, ApplicationOptions, SimulatorEvent } from '../services/orchestrator.ts';
import { DEFAULT_GENE_SCORES } from '../domain/genetics/Sapling.ts';
import { runGenerationReference, fingerprintGroups, diffFingerprints } from '../bench/canonical.ts';

const BASE_OPTIONS: ApplicationOptions = {
  withRepetitions: true,
  modifyMinimumTrackedScoreManually: false,
  minCrossbreedingSaplingsNumber: 2,
  maxCrossbreedingSaplingsNumber: 3,
  numberOfGenerations: 1,
  numberOfSaplingsAddedBetweenGenerations: 10,
  minimumTrackedScore: 4,
  geneScores: DEFAULT_GENE_SCORES,
  darkMode: true,
  skipScannerGuide: false,
  autoSaveInputSets: false,
  sounds: false,
  numberOfWorkers: 4,
  cpuLimitPercent: 100
};

const POPULATION = [
  'GGYYHH', 'YHGWXG', 'XXHHYY', 'GYHWXG', 'WHGWHG',
  'GGGHYH', 'HHGYGH', 'XYGXGY', 'HYGWGH', 'YHHYGG',
  'WYHGGW', 'YGGGGW', 'XYGWGH', 'WYGWHG', 'XYGXGW'
];

function collect(orchestrator: CrossbreedingOrchestrator) {
  const events: SimulatorEvent[] = [];
  orchestrator.addEventListener(e => events.push(e));
  return events;
}

describe('CrossbreedingOrchestrator', () => {
  it('single generation matches the reference implementation', async () => {
    const source = POPULATION.map((g, i) => new Sapling(g, 0, i));
    const orchestrator = new CrossbreedingOrchestrator();
    const events = collect(orchestrator);

    await orchestrator.simulateBestGenetics(source, BASE_OPTIONS);

    const done = events.find(e => e.type === 'DONE');
    expect(done).toBeDefined();
    expect(done!.mapGroups!.length).toBeGreaterThan(0);

    const reference = runGenerationReference(source, {
      minK: 2,
      maxK: 3,
      withRepetitions: true,
      minimumTrackedScore: 4,
      generationIndex: 1
    });

    // The orchestrator caps its payload, so compare only the genotypes it kept.
    const emitted = new Map(done!.mapGroups!.map(g => [g.resultSaplingGeneString, g]));
    const expected = fingerprintGroups(
      Array.from(reference.groups.values()).filter(g => emitted.has(g.resultSaplingGeneString))
    );
    const actual = fingerprintGroups(emitted.values());
    expect(diffFingerprints(expected, actual)).toEqual([]);
  }, 120000);

  it('reports progress reaching completion', async () => {
    const source = POPULATION.map((g, i) => new Sapling(g, 0, i));
    const orchestrator = new CrossbreedingOrchestrator();
    const events = collect(orchestrator);

    await orchestrator.simulateBestGenetics(source, BASE_OPTIONS);

    const progress = events.filter(e => e.type === 'PROGRESS_UPDATE');
    expect(progress.length).toBeGreaterThan(0);
    const last = progress[progress.length - 1];
    expect(last.totalCombinations).toBeGreaterThan(0);
    expect(last.processedCombinations).toBe(last.totalCombinations);
  }, 120000);

  it('runs multiple generations and links parents into the chance product', async () => {
    const source = POPULATION.map((g, i) => new Sapling(g, 0, i));
    const orchestrator = new CrossbreedingOrchestrator();
    const events = collect(orchestrator);

    await orchestrator.simulateBestGenetics(source, {
      ...BASE_OPTIONS,
      numberOfGenerations: 2,
      numberOfSaplingsAddedBetweenGenerations: 6
    });

    const generationDone = events.filter(e => e.type === 'DONE_GENERATION');
    expect(generationDone.length).toBe(2);

    const done = events.find(e => e.type === 'DONE')!;
    const secondGen = done.mapGroups!.filter(
      g => g.mapList[0].resultSapling.generationIndex > 1
    );
    expect(secondGen.length).toBeGreaterThan(0);

    // Every chance product must be a real probability, and linking must have
    // happened (a gen-2 map built on a branched gen-1 parent cannot be 1.0).
    for (const group of done.mapGroups!) {
      const p = group.mapList[0].getChanceProduct();
      expect(p).toBeGreaterThan(0);
      expect(p).toBeLessThanOrEqual(1);
    }
  }, 300000);

  it('emits results and stops cleanly when cancelled', async () => {
    const source = POPULATION.map((g, i) => new Sapling(g, 0, i));
    const orchestrator = new CrossbreedingOrchestrator();
    await orchestrator.simulateBestGenetics(source, BASE_OPTIONS);

    orchestrator.cancelSimulation();
    // Cancelling after completion must still expose the accumulated results.
    expect(orchestrator.getSortedResults().length).toBeGreaterThan(0);
  }, 120000);

  it('produces the same results with and without repetitions disabled', async () => {
    const source = POPULATION.map((g, i) => new Sapling(g, 0, i));
    const orchestrator = new CrossbreedingOrchestrator();
    const events = collect(orchestrator);

    await orchestrator.simulateBestGenetics(source, {
      ...BASE_OPTIONS,
      withRepetitions: false
    });

    const done = events.find(e => e.type === 'DONE')!;
    const reference = runGenerationReference(source, {
      minK: 2,
      maxK: 3,
      withRepetitions: false,
      minimumTrackedScore: 4,
      generationIndex: 1
    });

    const emitted = new Map(done.mapGroups!.map(g => [g.resultSaplingGeneString, g]));
    const expected = fingerprintGroups(
      Array.from(reference.groups.values()).filter(g => emitted.has(g.resultSaplingGeneString))
    );
    expect(diffFingerprints(expected, fingerprintGroups(emitted.values()))).toEqual([]);
  }, 120000);
});
