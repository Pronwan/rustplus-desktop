/**
 * Drives one generation through the fast core and materialises the surviving
 * results as `GeneticsMapGroup` objects for the rest of the application.
 *
 * Object construction happens exactly once per retained map (at most three per
 * result genotype) instead of once per candidate, which is where the bulk of
 * the old runtime went.
 */

import { Sapling, GeneScores, DEFAULT_GENE_SCORES } from './Sapling.ts';
import { GeneticsMap } from './GeneticsMap.ts';
import { GeneticsMapGroup } from './GeneticsMapGroup.ts';
import {
  Evaluator,
  ResultStore,
  RetainedMap,
  PackedSource,
  packSource,
  decodeGenotype,
  EvaluatorConfig
} from './fastCore.ts';
import { buildRejectTable, TargetConstraint } from './targetFilter.ts';
import { getWorkChunks } from './combinations.ts';

export interface FastGenerationParams {
  minK: number;
  maxK: number;
  withRepetitions: boolean;
  minimumTrackedScore: number;
  geneScores?: GeneScores;
  generationIndex?: number;
  /** Applied only by callers that know this is the final generation. */
  target?: TargetConstraint | null;
}

export function toPackedSource(source: Sapling[]): PackedSource {
  return packSource(
    source.map(s => ({
      genes: s.toString(),
      generationIndex: s.generationIndex,
      index: s.index
    }))
  );
}

export function buildEvaluatorConfig(
  source: Sapling[],
  params: FastGenerationParams
): EvaluatorConfig {
  return {
    minK: params.minK,
    maxK: params.maxK,
    withRepetitions: params.withRepetitions,
    minimumTrackedScore: params.minimumTrackedScore,
    geneScores: params.geneScores ?? DEFAULT_GENE_SCORES,
    generationIndex: params.generationIndex ?? 1,
    reject: buildRejectTable(source.map(s => s.toString()), params.target)
  };
}

/** Rebuilds a `GeneticsMap` from a retained record. */
export function materializeMap(record: RetainedMap): GeneticsMap {
  const surrounding: Sapling[] = [];
  for (let i = 0; i < record.surrounding.length; i++) {
    surrounding.push(
      new Sapling(decodeGenotype(record.surrounding[i]), record.surroundingGenerations[i])
    );
  }
  const center =
    record.centerCode >= 0
      ? new Sapling(decodeGenotype(record.centerCode), record.centerGeneration)
      : undefined;

  const map = new GeneticsMap(
    new Sapling(decodeGenotype(record.resultCode), record.resultGeneration),
    surrounding,
    center,
    record.chance,
    record.score,
    record.tieWinningIndexes,
    record.tieLosingIndexes
  );
  map.sumOfComposingSaplingsGenerations = record.sumGenerations;
  return map;
}

export function materializeGroups(store: ResultStore): Map<string, GeneticsMapGroup> {
  const groups = new Map<string, GeneticsMapGroup>();
  for (const [code, records] of store.entries()) {
    const genotype = decodeGenotype(code);
    groups.set(genotype, new GeneticsMapGroup(genotype, records.map(materializeMap)));
  }
  return groups;
}

/**
 * Incremental view over a `ResultStore` that preserves object identity for
 * groups whose retained maps did not change.
 *
 * Results stream in every second or so; rebuilding every group each time would
 * hand React a fully new object graph and force the whole results pane to
 * re-render and re-analyse. Only genotypes the store marked dirty are rebuilt,
 * so an untouched route keeps the same `GeneticsMapGroup` instance for the
 * lifetime of the run.
 */
export class GroupMaterializer {
  private cache = new Map<number, GeneticsMapGroup>();

  public sync(store: ResultStore): GeneticsMapGroup[] {
    for (const code of store.takeDirty()) {
      const records = store.get(code);
      if (!records) {
        this.cache.delete(code);
        continue;
      }
      const genotype = decodeGenotype(code);
      this.cache.set(code, new GeneticsMapGroup(genotype, records.map(materializeMap)));
    }
    return Array.from(this.cache.values());
  }

  public asMap(): Map<string, GeneticsMapGroup> {
    const out = new Map<string, GeneticsMapGroup>();
    for (const group of this.cache.values()) out.set(group.resultSaplingGeneString, group);
    return out;
  }

  public clear(): void {
    this.cache.clear();
  }
}

/**
 * Runs a full generation single-threaded. The worker path reuses the same
 * `Evaluator`/`ResultStore` but drives its own subset of chunks.
 */
export function runGenerationFast(
  source: Sapling[],
  params: FastGenerationParams
): {
  groups: Map<string, GeneticsMapGroup>;
  store: ResultStore;
  combos: number;
  mapsRetained: number;
} {
  const packed = toPackedSource(source);
  const cfg = buildEvaluatorConfig(source, params);
  const store = new ResultStore();
  const evaluator = new Evaluator(packed, cfg, store);

  const chunks = getWorkChunks(source.length, {
    withRepetitions: params.withRepetitions,
    minCrossbreedingSaplingsNumber: params.minK,
    maxCrossbreedingSaplingsNumber: params.maxK
  });

  for (const chunk of chunks) {
    evaluator.runSlice(chunk.startingPositions.length, chunk.startingPositions[0]);
  }

  let mapsRetained = 0;
  for (const [, list] of store.entries()) mapsRetained += list.length;

  return {
    groups: materializeGroups(store),
    store,
    combos: evaluator.processed,
    mapsRetained
  };
}
