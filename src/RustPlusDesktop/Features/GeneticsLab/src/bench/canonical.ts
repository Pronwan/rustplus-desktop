import { Sapling, GeneScores, DEFAULT_GENE_SCORES } from '../domain/genetics/Sapling.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { evaluateCombination } from '../domain/genetics/crossbreeding.ts';
import { appendAndOrganizeResults } from '../domain/genetics/sorting.ts';
import { getWorkChunks, setNextPositionInChunk } from '../domain/genetics/combinations.ts';

/**
 * Canonical, tie-robust fingerprint of one generation's results.
 *
 * A map's rank inside its group is decided purely by the quality vector
 * (resultGeneration, chance, sumOfParentGenerations, plantCount). When several
 * maps share a quality vector the retained representative is arbitrary — today
 * it depends on Web Worker arrival order, so it is not even stable run to run.
 *
 * The fingerprint therefore records, per result genotype, the score and the
 * ordered list of retained *quality vectors* rather than the representative
 * parent sets. Two implementations agreeing on this fingerprint are equivalent
 * in everything the UI ranks, filters or displays as a headline number.
 */
export interface GroupFingerprint {
  genotype: string;
  score: number;
  /** One `gen:chance:sumGen:plants` entry per retained map, in retained order. */
  quality: string[];
}

export type Fingerprint = GroupFingerprint[];

export function fingerprintGroups(groups: Iterable<GeneticsMapGroup>): Fingerprint {
  const out: Fingerprint = [];
  for (const group of groups) {
    const best = group.mapList[0];
    if (!best) continue;
    out.push({
      genotype: group.resultSaplingGeneString,
      score: best.score,
      quality: group.mapList.map(
        m =>
          `${m.resultSapling.generationIndex}:${m.chance.toFixed(6)}:` +
          `${m.sumOfComposingSaplingsGenerations}:${m.crossbreedingSaplings.length}:` +
          `${m.baseSapling ? 'C' : '-'}`
      )
    });
  }
  out.sort((a, b) => a.genotype.localeCompare(b.genotype));
  return out;
}

/** Human-readable diff of two fingerprints; empty array means equivalent. */
export function diffFingerprints(expected: Fingerprint, actual: Fingerprint): string[] {
  const problems: string[] = [];
  const byGenotype = new Map(actual.map(g => [g.genotype, g]));
  const seen = new Set<string>();

  for (const exp of expected) {
    const act = byGenotype.get(exp.genotype);
    seen.add(exp.genotype);
    if (!act) {
      problems.push(`missing genotype ${exp.genotype}`);
      continue;
    }
    if (act.score !== exp.score) {
      problems.push(`${exp.genotype}: score ${act.score} !== ${exp.score}`);
    }
    if (act.quality.join(',') !== exp.quality.join(',')) {
      problems.push(
        `${exp.genotype}: quality [${act.quality.join(' ')}] !== [${exp.quality.join(' ')}]`
      );
    }
  }
  for (const act of actual) {
    if (!seen.has(act.genotype)) problems.push(`unexpected genotype ${act.genotype}`);
  }
  return problems;
}

export interface GenerationParams {
  minK: number;
  maxK: number;
  withRepetitions: boolean;
  minimumTrackedScore: number;
  geneScores?: GeneScores;
  generationIndex?: number;
}

/**
 * Reference implementation: runs one generation through the ORIGINAL
 * `evaluateCombination` + `appendAndOrganizeResults` path, single-threaded.
 * This is the oracle every optimised implementation is compared against.
 */
export function runGenerationReference(
  source: Sapling[],
  params: GenerationParams
): { groups: Map<string, GeneticsMapGroup>; combos: number; mapsProduced: number } {
  const existing = new Set(source.map(s => s.toString()));
  const opts = {
    geneScores: params.geneScores ?? DEFAULT_GENE_SCORES,
    minimumTrackedScore: params.minimumTrackedScore
  };
  const groups = new Map<string, GeneticsMapGroup>();
  const chunks = getWorkChunks(source.length, {
    withRepetitions: params.withRepetitions,
    minCrossbreedingSaplingsNumber: params.minK,
    maxCrossbreedingSaplingsNumber: params.maxK
  });

  let combos = 0;
  let mapsProduced = 0;
  for (const chunk of chunks) {
    const positions = [...chunk.startingPositions];
    for (let c = 0; c < chunk.combinationsToProcess; c++) {
      const surrounding = positions.map(i => source[i]);
      const maps = evaluateCombination(
        surrounding,
        source,
        existing,
        opts,
        params.generationIndex ?? 1
      );
      combos++;
      if (maps.length > 0) {
        mapsProduced += maps.length;
        appendAndOrganizeResults(groups, maps);
      }
      if (c < chunk.combinationsToProcess - 1) {
        setNextPositionInChunk(positions, source.length, params.withRepetitions);
      }
    }
  }
  return { groups, combos, mapsProduced };
}
