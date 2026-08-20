/**
 * Allocation-free crossbreeding core.
 *
 * Semantics are identical to `crossbreeding.ts` + `sorting.ts`; only the data
 * representation and the order of work changed. The three ideas that make it
 * fast, in order of impact:
 *
 * 1. **Admission test before allocation.** A map's rank inside its result group
 *    is decided entirely by the quality vector
 *    (resultGeneration, chance, sumOfParentGenerations, plantCount), all of
 *    which are integers known before any object exists. Groups keep only the
 *    best 3, so once a group holds three maps no worse than a candidate, that
 *    candidate is provably unreachable and is dropped for free. On a real
 *    124-plant input the reference path builds ~1.8M maps to retain ~5.1k;
 *    this test removes essentially all of that work.
 *
 * 2. **Flat typed-array group index.** A result genotype packs into 18 bits, so
 *    the admission test is one `Int32Array` read on a direct-indexed table
 *    rather than a string build plus hash-map lookup.
 *
 * 3. **Incremental DFS column state.** Enumeration walks combinations as a DFS
 *    tree, pushing/popping one plant at a time and saving/restoring the per
 *    column (max, winner mask, first-seen order) on the stack. Shared prefixes
 *    are computed once instead of once per combination.
 *
 * Gene weights are held as integers scaled by 10 (green 6, red 10), which
 * reproduces the reference's `Math.round(x * 100) / 100` accumulation exactly
 * while removing all floating-point comparison epsilons.
 */

import { GeneType } from './Gene.ts';
import { GeneScores } from './Sapling.ts';

/** Gene type ordering must match `crossbreeding.ts` TYPE_TO_INDEX. */
export const TYPE_INDEX: Record<GeneType, number> = { G: 0, H: 1, Y: 2, W: 3, X: 4 };
export const INDEX_TYPE: GeneType[] = ['G', 'H', 'Y', 'W', 'X'];

/** Crossbreeding weights scaled by 10 so all column arithmetic is integral. */
const WEIGHT10 = new Int32Array([6, 6, 6, 10, 10]);
const RED_WEIGHT10 = 10;
const GREEN_WEIGHT10 = 6;

export const COLUMNS = 6;
/** 3 bits per column x 6 columns. */
export const GENOTYPE_SPACE = 1 << 18;

const charToIndex = (ch: string): number => {
  switch (ch) {
    case 'G': return 0;
    case 'H': return 1;
    case 'Y': return 2;
    case 'W': return 3;
    case 'X': return 4;
    default: return -1;
  }
};

/** Packs a 6-gene string into an 18-bit code. Returns -1 for invalid input. */
export function encodeGenotype(genes: string): number {
  if (genes.length !== COLUMNS) return -1;
  let code = 0;
  for (let c = 0; c < COLUMNS; c++) {
    const t = charToIndex(genes.charCodeAt(c) >= 97 ? genes[c].toUpperCase() : genes[c]);
    if (t < 0) return -1;
    code |= t << (c * 3);
  }
  return code;
}

export function decodeGenotype(code: number): string {
  let s = '';
  for (let c = 0; c < COLUMNS; c++) {
    s += INDEX_TYPE[(code >> (c * 3)) & 7];
  }
  return s;
}

// ---------------------------------------------------------------------------
// Source population
// ---------------------------------------------------------------------------

export interface PackedSource {
  count: number;
  /** 18-bit genotype code per plant. */
  codes: Int32Array;
  /** Gene type index per (plant * 6 + column). */
  genes: Uint8Array;
  /**
   * 30-bit mask, bit (column * 5 + type) set for the plant's gene in that
   * column. Rule B ("every surrounding plant contributes to a winning type")
   * becomes a single AND against the combined winner mask.
   */
  presence: Int32Array;
  generations: Int32Array;
  /**
   * Original source index, or -1 when the plant is a generated intermediate.
   * Reference behaviour: intermediates have `index === undefined` and are
   * therefore always eligible as a center even when used as a surrounding
   * plant, while indexed inputs are excluded from their own combination.
   */
  indexes: Int32Array;
}

export interface SourcePlantInput {
  genes: string;
  generationIndex: number;
  index?: number;
}

export function packSource(plants: SourcePlantInput[]): PackedSource {
  const n = plants.length;
  const codes = new Int32Array(n);
  const genes = new Uint8Array(n * COLUMNS);
  const presence = new Int32Array(n);
  const generations = new Int32Array(n);
  const indexes = new Int32Array(n);

  for (let p = 0; p < n; p++) {
    const plant = plants[p];
    const code = encodeGenotype(plant.genes);
    codes[p] = code;
    generations[p] = plant.generationIndex;
    indexes[p] = plant.index === undefined ? -1 : plant.index;
    let mask = 0;
    for (let c = 0; c < COLUMNS; c++) {
      const t = (code >> (c * 3)) & 7;
      genes[p * COLUMNS + c] = t;
      mask |= 1 << (c * 5 + t);
    }
    presence[p] = mask;
  }

  return { count: n, codes, genes, presence, generations, indexes };
}

// ---------------------------------------------------------------------------
// Retained results
// ---------------------------------------------------------------------------

/**
 * A map that survived the admission test. Only these are ever materialised, so
 * the record can afford to carry everything needed to rebuild a `GeneticsMap`.
 */
export interface RetainedMap {
  qualityKey: number;
  resultCode: number;
  resultGeneration: number;
  score: number;
  chance: number;
  branchCount: number;
  sumGenerations: number;
  /** Genotype codes of the surrounding plants, in combination order. */
  surrounding: Int32Array;
  surroundingGenerations: Int32Array;
  /** -1 when the plan has no center plant. */
  centerCode: number;
  centerGeneration: number;
  tieWinningIndexes?: number[];
  tieLosingIndexes?: number[];
}

const MAPS_PER_GROUP = 3;
/** Sentinel meaning "this group has fewer than MAPS_PER_GROUP maps". */
const NOT_FULL = 0x7fffffff;

/**
 * Encodes the reference `resultMapsSortingFunction` ordering into one integer
 * where smaller is better:
 *   result generation ASC -> chance DESC -> sumGenerations ASC -> plants ASC.
 * Chance is always 1/branchCount, so ordering by branchCount ASC is exactly
 * ordering by chance DESC.
 */
export function qualityKey(
  resultGeneration: number,
  branchCount: number,
  sumGenerations: number,
  plantCount: number
): number {
  return (
    resultGeneration * 0x100000 +
    branchCount * 0x8000 +
    (sumGenerations & 0x3ff) * 0x20 +
    (plantCount & 0x1f)
  );
}

/**
 * Per-result-genotype top-3 store.
 *
 * `worstKey` is a direct-indexed table over the 18-bit genotype space, so the
 * reject path is a single array read. The `groups` map only ever sees
 * genotypes that actually retained something.
 */
export class ResultStore {
  private worstKey = new Int32Array(GENOTYPE_SPACE).fill(NOT_FULL);
  private groups = new Map<number, RetainedMap[]>();
  /**
   * Genotypes whose retained list changed since the last `takeDirty`. Lets the
   * materialiser rebuild only what moved, so unchanged groups keep their object
   * identity across streaming updates - which is what makes `React.memo`,
   * per-group analysis caching and FLIP reordering work.
   */
  private dirty = new Set<number>();

  /**
   * Cheap pre-check: true when a candidate with this quality could still be
   * retained. Called on the hot path before anything is built.
   */
  public admits(resultCode: number, key: number): boolean {
    return key < this.worstKey[resultCode];
  }

  public insert(map: RetainedMap): void {
    const code = map.resultCode;
    let list = this.groups.get(code);
    if (list === undefined) {
      list = [];
      this.groups.set(code, list);
    } else if (isStructuralDuplicate(list, map)) {
      // The reference drops exact structural repeats rather than letting them
      // occupy two of the three slots.
      return;
    }

    // Insertion sort into a list of at most three; stable on ties so the
    // incumbent keeps its slot, matching push-sort-truncate in the reference.
    let i = list.length;
    while (i > 0 && list[i - 1].qualityKey > map.qualityKey) i--;
    list.splice(i, 0, map);
    if (list.length > MAPS_PER_GROUP) list.length = MAPS_PER_GROUP;

    this.worstKey[code] =
      list.length >= MAPS_PER_GROUP ? list[MAPS_PER_GROUP - 1].qualityKey : NOT_FULL;
    this.dirty.add(code);
  }

  /** Returns the genotypes changed since the last call and resets the set. */
  public takeDirty(): Set<number> {
    const changed = this.dirty;
    this.dirty = new Set<number>();
    return changed;
  }

  public size(): number {
    return this.groups.size;
  }

  public entries(): IterableIterator<[number, RetainedMap[]]> {
    return this.groups.entries();
  }

  public get(code: number): RetainedMap[] | undefined {
    return this.groups.get(code);
  }

  /** Merges another store's retained maps in, respecting the same limits. */
  public mergeFrom(other: ResultStore): void {
    for (const [, list] of other.entries()) {
      for (const map of list) this.insert(map);
    }
  }
}

function isStructuralDuplicate(list: RetainedMap[], candidate: RetainedMap): boolean {
  for (let i = 0; i < list.length; i++) {
    const existing = list[i];
    if (existing.branchCount !== candidate.branchCount) continue;
    if (existing.centerCode !== candidate.centerCode) continue;
    if (existing.surrounding.length !== candidate.surrounding.length) continue;
    let same = true;
    for (let s = 0; s < existing.surrounding.length; s++) {
      if (existing.surrounding[s] !== candidate.surrounding[s]) {
        same = false;
        break;
      }
    }
    if (same) return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// Evaluator
// ---------------------------------------------------------------------------

export interface EvaluatorConfig {
  minK: number;
  maxK: number;
  withRepetitions: boolean;
  minimumTrackedScore: number;
  geneScores: GeneScores;
  /** Generation index stamped on produced results. */
  generationIndex: number;
  /**
   * Byte table over the genotype space: 1 = discard this result. Covers both
   * already-owned genotypes and (final generation only) target pruning.
   * See `targetFilter.ts`.
   */
  reject: Uint8Array;
}

const MAX_DEPTH = 16;

/**
 * Holds all mutable per-combination state in preallocated buffers so a full
 * generation runs without allocating.
 */
export class Evaluator {
  private readonly src: PackedSource;
  private readonly cfg: EvaluatorConfig;
  private readonly store: ResultStore;

  /** Accumulated weight x10 per (column * 5 + type). */
  private counts = new Int32Array(COLUMNS * 5);
  private max10 = new Int32Array(COLUMNS);
  private winMask = new Int32Array(COLUMNS);
  /** First-seen type order per column, 3 bits per entry. */
  private seenOrder = new Int32Array(COLUMNS);
  private seenLen = new Int32Array(COLUMNS);

  /** Saved column state per DFS depth, for O(1) pop. */
  private stackMax = new Int32Array(MAX_DEPTH * COLUMNS);
  private stackMask = new Int32Array(MAX_DEPTH * COLUMNS);
  private stackOrder = new Int32Array(MAX_DEPTH * COLUMNS);
  private stackLen = new Int32Array(MAX_DEPTH * COLUMNS);

  private positions = new Int32Array(MAX_DEPTH);
  private scoreOf = new Float64Array(5);

  /** Per-combination scratch, recomputed once in `evaluate` and reused by every center. */
  private winner = new Int32Array(COLUMNS);
  private weakColumns = new Int32Array(COLUMNS);
  private weakCount = 0;
  /** Result bits contributed by columns no center can ever override. */
  private baseFixed = 0;

  private combosProcessed = 0;

  constructor(src: PackedSource, cfg: EvaluatorConfig, store: ResultStore) {
    this.src = src;
    this.cfg = cfg;
    this.store = store;
    for (let t = 0; t < 5; t++) {
      this.scoreOf[t] = cfg.geneScores[INDEX_TYPE[t]] ?? 0;
    }
  }

  public get processed(): number {
    return this.combosProcessed;
  }

  public resetProcessed(): void {
    this.combosProcessed = 0;
  }

  /**
   * Enumerates every combination of exactly `k` plants whose first index is
   * `p0`, evaluating each. This is the same slice the reference `getWorkChunks`
   * describes, so chunking, progress counts and worker distribution are
   * unchanged.
   */
  public runSlice(k: number, p0: number): void {
    if (k > MAX_DEPTH) {
      // The per-depth stack buffers are fixed size; silently overrunning them
      // would corrupt column state instead of failing loudly.
      throw new Error(`Combination size ${k} exceeds MAX_DEPTH ${MAX_DEPTH}`);
    }
    this.counts.fill(0);
    this.max10.fill(0);
    this.winMask.fill(0);
    this.seenOrder.fill(0);
    this.seenLen.fill(0);

    this.positions[0] = p0;
    this.push(0, p0);
    if (k === 1) {
      this.evaluate(1);
    } else {
      this.descend(1, k, this.cfg.withRepetitions ? p0 : p0 + 1);
    }
    this.pop(0, p0);
  }

  private descend(depth: number, k: number, from: number): void {
    const n = this.src.count;
    const last = depth === k - 1;
    // Without repetitions the tail must still fit: leave room for the remaining
    // slots after this one.
    const limit = this.cfg.withRepetitions ? n - 1 : n - (k - depth);

    for (let p = from; p <= limit; p++) {
      this.positions[depth] = p;
      this.push(depth, p);
      if (last) {
        this.evaluate(k);
      } else {
        this.descend(depth + 1, k, this.cfg.withRepetitions ? p : p + 1);
      }
      this.pop(depth, p);
    }
  }

  private push(depth: number, plant: number): void {
    const { genes } = this.src;
    const base = plant * COLUMNS;
    const sBase = depth * COLUMNS;

    for (let c = 0; c < COLUMNS; c++) {
      this.stackMax[sBase + c] = this.max10[c];
      this.stackMask[sBase + c] = this.winMask[c];
      this.stackOrder[sBase + c] = this.seenOrder[c];
      this.stackLen[sBase + c] = this.seenLen[c];

      const t = genes[base + c];
      const idx = c * 5 + t;
      const prev = this.counts[idx];
      if (prev === 0) {
        this.seenOrder[c] |= t << (3 * this.seenLen[c]);
        this.seenLen[c]++;
      }
      const w = prev + WEIGHT10[t];
      this.counts[idx] = w;

      if (w > this.max10[c]) {
        this.max10[c] = w;
        this.winMask[c] = 1 << t;
      } else if (w === this.max10[c]) {
        this.winMask[c] |= 1 << t;
      }
    }
  }

  private pop(depth: number, plant: number): void {
    const { genes } = this.src;
    const base = plant * COLUMNS;
    const sBase = depth * COLUMNS;

    for (let c = 0; c < COLUMNS; c++) {
      const t = genes[base + c];
      this.counts[c * 5 + t] -= WEIGHT10[t];
      this.max10[c] = this.stackMax[sBase + c];
      this.winMask[c] = this.stackMask[sBase + c];
      this.seenOrder[c] = this.stackOrder[sBase + c];
      this.seenLen[c] = this.stackLen[sBase + c];
    }
  }

  /** Returns the first-seen type among those tied for the column maximum. */
  private firstWinner(c: number): number {
    const mask = this.winMask[c];
    const order = this.seenOrder[c];
    const len = this.seenLen[c];
    for (let i = 0; i < len; i++) {
      const t = (order >> (3 * i)) & 7;
      if (mask & (1 << t)) return t;
    }
    return 0;
  }

  private evaluate(k: number): void {
    this.combosProcessed++;

    // --- Rule A: at most one definitive tie column -------------------------
    let definitiveTies = 0;
    let tieColumn = -1;
    let anyWeakColumn = false;
    let winAll = 0;

    for (let c = 0; c < COLUMNS; c++) {
      const mask = this.winMask[c];
      winAll |= mask << (c * 5);
      const max = this.max10[c];
      if (max <= RED_WEIGHT10) anyWeakColumn = true;
      // popcount of a 5-bit mask
      if (mask & (mask - 1)) {
        if (max > RED_WEIGHT10) {
          definitiveTies++;
          if (definitiveTies > 1) return;
          tieColumn = c;
        }
      }
    }

    // --- Rule B: every surrounding plant contributes to a winning type -----
    const presence = this.src.presence;
    for (let d = 0; d < k; d++) {
      if ((presence[this.positions[d]] & winAll) === 0) return;
    }

    let sumGenerations = 0;
    for (let d = 0; d < k; d++) sumGenerations += this.src.generations[this.positions[d]];

    // Per-combination scratch shared by every candidate center. A definitive
    // tie needs max > 10 while a center weighs at most 10, so a tie column can
    // never be a weak (overridable) column - the two sets are disjoint.
    let fixed = 0;
    let weak = 0;
    for (let c = 0; c < COLUMNS; c++) {
      const w = this.firstWinner(c);
      this.winner[c] = w;
      if (this.max10[c] <= RED_WEIGHT10) {
        this.weakColumns[weak++] = c;
      } else if (c !== tieColumn) {
        fixed |= w << (c * 3);
      }
    }
    this.weakCount = weak;
    this.baseFixed = fixed;

    const needsCenter = k <= 5 && anyWeakColumn;

    if (!needsCenter) {
      this.emit(k, -1, sumGenerations, tieColumn);
      return;
    }

    // --- Center candidates -------------------------------------------------
    const { count, indexes, generations } = this.src;
    for (let cand = 0; cand < count; cand++) {
      const candIndex = indexes[cand];
      if (candIndex >= 0) {
        let used = false;
        for (let d = 0; d < k; d++) {
          if (indexes[this.positions[d]] === candIndex) {
            used = true;
            break;
          }
        }
        if (used) continue;
      }
      this.emit(k, cand, sumGenerations + generations[cand], tieColumn);
    }
  }

  /**
   * Builds the result genotype for one (combination, center) pair and offers it
   * to the store. Everything up to the admission test is integer arithmetic.
   */
  private emit(k: number, center: number, sumGenerations: number, tieColumn: number): void {
    const cfg = this.cfg;
    const genes = this.src.genes;

    // Strong, non-tie columns are already folded into `baseFixed`; only the
    // weak columns can differ between centers.
    let baseCode = this.baseFixed;
    const weak = this.weakColumns;
    const weakCount = this.weakCount;

    if (center >= 0) {
      const centerBase = center * COLUMNS;
      for (let i = 0; i < weakCount; i++) {
        const c = weak[i];
        const ct = genes[centerBase + c];
        const cw = ct >= 3 ? RED_WEIGHT10 : GREEN_WEIGHT10;
        baseCode |= (cw >= this.max10[c] ? ct : this.winner[c]) << (c * 3);
      }
    } else {
      for (let i = 0; i < weakCount; i++) {
        const c = weak[i];
        baseCode |= this.winner[c] << (c * 3);
      }
    }

    let branchCount = 1;
    let branchColumn = -1;
    if (tieColumn >= 0) {
      const mask = this.winMask[tieColumn];
      if (mask & (mask - 1)) {
        branchColumn = tieColumn;
        branchCount = popcount5(mask);
      } else {
        baseCode |= this.winner[tieColumn] << (tieColumn * 3);
      }
    }

    const resultGeneration = cfg.generationIndex;

    if (branchColumn < 0) {
      this.offer(baseCode, k, center, sumGenerations, 1, resultGeneration, -1, 0);
      return;
    }

    // One result per tied type, in first-seen order (matches the reference).
    const mask = this.winMask[branchColumn];
    const order = this.seenOrder[branchColumn];
    const len = this.seenLen[branchColumn];
    for (let i = 0; i < len; i++) {
      const t = (order >> (3 * i)) & 7;
      if ((mask & (1 << t)) === 0) continue;
      this.offer(
        baseCode | (t << (branchColumn * 3)),
        k,
        center,
        sumGenerations,
        branchCount,
        resultGeneration,
        branchColumn,
        t
      );
    }
  }

  private offer(
    resultCode: number,
    k: number,
    center: number,
    sumGenerations: number,
    branchCount: number,
    resultGeneration: number,
    branchColumn: number,
    branchType: number
  ): void {
    if (this.cfg.reject[resultCode] === 1) return;

    const key = qualityKey(resultGeneration, branchCount, sumGenerations, k);
    if (!this.store.admits(resultCode, key)) return;

    let score = 0;
    for (let c = 0; c < COLUMNS; c++) {
      score += this.scoreOf[(resultCode >> (c * 3)) & 7];
    }
    score = Math.round(score * 100) / 100;
    if (score < this.cfg.minimumTrackedScore) return;

    const surrounding = new Int32Array(k);
    const surroundingGenerations = new Int32Array(k);
    for (let d = 0; d < k; d++) {
      const p = this.positions[d];
      surrounding[d] = this.src.codes[p];
      surroundingGenerations[d] = this.src.generations[p];
    }

    let tieWinningIndexes: number[] | undefined;
    let tieLosingIndexes: number[] | undefined;
    if (branchColumn >= 0) {
      tieWinningIndexes = [];
      tieLosingIndexes = [];
      const mask = this.winMask[branchColumn];
      for (let d = 0; d < k; d++) {
        const t = this.src.genes[this.positions[d] * COLUMNS + branchColumn];
        if (t === branchType) tieWinningIndexes.push(d);
        else if (mask & (1 << t)) tieLosingIndexes.push(d);
      }
    }

    this.store.insert({
      qualityKey: key,
      resultCode,
      resultGeneration,
      score,
      chance: 1 / branchCount,
      branchCount,
      sumGenerations,
      surrounding,
      surroundingGenerations,
      centerCode: center >= 0 ? this.src.codes[center] : -1,
      centerGeneration: center >= 0 ? this.src.generations[center] : 0,
      tieWinningIndexes,
      tieLosingIndexes
    });
  }
}

function popcount5(mask: number): number {
  let n = 0;
  for (let i = 0; i < 5; i++) if (mask & (1 << i)) n++;
  return n;
}

/** Builds the "already owned genotype" lookup used to discard known results. */
export function buildExistingTable(genotypes: Iterable<string>): Uint8Array {
  const table = new Uint8Array(GENOTYPE_SPACE);
  for (const g of genotypes) {
    const code = encodeGenotype(g);
    if (code >= 0) table[code] = 1;
  }
  return table;
}
