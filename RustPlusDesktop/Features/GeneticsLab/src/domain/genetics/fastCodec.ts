/**
 * Compact transport for retained maps.
 *
 * The old worker protocol serialized every produced map into an object graph of
 * DTOs, sent it twice (250ms deltas plus a full final set), and rebuilt objects
 * on the main thread. Retained maps are few (thousands, not millions) and every
 * field is an integer, so they pack into a single transferable `Int32Array`.
 *
 * `score` and `chance` are intentionally not transported: score is a pure
 * function of the result genotype and the gene-score table, and chance is
 * always `1 / branchCount`. Both are recomputed on arrival.
 */

import { RetainedMap, ResultStore, COLUMNS, INDEX_TYPE } from './fastCore.ts';
import { GeneScores } from './Sapling.ts';

const HEADER = 10;

/**
 * Packs the store's retained maps, or only the genotypes in `codes`.
 *
 * Workers ship deltas rather than their whole store: a generation completes
 * dozens of batches per worker, and re-sending every retained map each time
 * moved the bottleneck onto the main thread's unpack-and-merge loop. The
 * receiving store applies the same top-3 rule, so a delta that supersedes an
 * earlier map converges to the same result.
 */
export function packRecords(store: ResultStore, codes?: Iterable<number>): Int32Array {
  const lists: RetainedMap[][] = [];
  if (codes === undefined) {
    for (const [, list] of store.entries()) lists.push(list);
  } else {
    for (const code of codes) {
      const list = store.get(code);
      if (list) lists.push(list);
    }
  }

  let total = 1;
  let count = 0;
  for (const list of lists) {
    for (const r of list) {
      total += HEADER + 3 * r.surrounding.length +
        (r.tieWinningIndexes?.length ?? 0) + (r.tieLosingIndexes?.length ?? 0);
      count++;
    }
  }

  const buffer = new Int32Array(total);
  buffer[0] = count;
  let o = 1;
  for (const list of lists) {
    for (const r of list) {
      const k = r.surrounding.length;
      const tw = r.tieWinningIndexes ?? [];
      const tl = r.tieLosingIndexes ?? [];
      buffer[o++] = r.resultCode;
      buffer[o++] = r.resultGeneration;
      buffer[o++] = r.branchCount;
      buffer[o++] = r.sumGenerations;
      buffer[o++] = r.centerCode;
      buffer[o++] = r.centerGeneration;
      buffer[o++] = r.centerIndex;
      buffer[o++] = k;
      buffer[o++] = tw.length;
      buffer[o++] = tl.length;
      for (let i = 0; i < k; i++) buffer[o++] = r.surrounding[i];
      for (let i = 0; i < k; i++) buffer[o++] = r.surroundingGenerations[i];
      for (let i = 0; i < k; i++) buffer[o++] = r.surroundingIndexes[i];
      for (let i = 0; i < tw.length; i++) buffer[o++] = tw[i];
      for (let i = 0; i < tl.length; i++) buffer[o++] = tl[i];
    }
  }
  return buffer;
}

export function unpackRecords(buffer: Int32Array, geneScores: GeneScores): RetainedMap[] {
  const scoreOf = new Float64Array(5);
  for (let t = 0; t < 5; t++) scoreOf[t] = geneScores[INDEX_TYPE[t]] ?? 0;

  const count = buffer[0];
  const out: RetainedMap[] = new Array(count);
  let o = 1;
  for (let i = 0; i < count; i++) {
    const resultCode = buffer[o++];
    const resultGeneration = buffer[o++];
    const branchCount = buffer[o++];
    const sumGenerations = buffer[o++];
    const centerCode = buffer[o++];
    const centerGeneration = buffer[o++];
    const centerIndex = buffer[o++];
    const k = buffer[o++];
    const twLen = buffer[o++];
    const tlLen = buffer[o++];

    const surrounding = buffer.subarray(o, o + k);
    o += k;
    const surroundingGenerations = buffer.subarray(o, o + k);
    o += k;
    const surroundingIndexes = buffer.subarray(o, o + k);
    o += k;

    let tieWinningIndexes: number[] | undefined;
    let tieLosingIndexes: number[] | undefined;
    if (twLen > 0 || tlLen > 0) {
      tieWinningIndexes = Array.from(buffer.subarray(o, o + twLen));
      o += twLen;
      tieLosingIndexes = Array.from(buffer.subarray(o, o + tlLen));
      o += tlLen;
    }

    let score = 0;
    for (let c = 0; c < COLUMNS; c++) score += scoreOf[(resultCode >> (c * 3)) & 7];
    score = Math.round(score * 100) / 100;

    out[i] = {
      qualityKey:
        resultGeneration * 0x100000 +
        branchCount * 0x8000 +
        (sumGenerations & 0x3ff) * 0x20 +
        (k & 0x1f),
      resultCode,
      resultGeneration,
      score,
      chance: 1 / branchCount,
      branchCount,
      sumGenerations,
      surrounding: surrounding as Int32Array,
      surroundingGenerations: surroundingGenerations as Int32Array,
      surroundingIndexes: surroundingIndexes as Int32Array,
      centerCode,
      centerGeneration,
      centerIndex,
      tieWinningIndexes,
      tieLosingIndexes
    };
  }
  return out;
}
