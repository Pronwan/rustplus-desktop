import { LivestockGeneRow, encodeAnimalGenes } from '../../domain/livestock/animal.ts';

/**
 * Turns a stream of per-frame panel reads into "this animal" events.
 *
 * A read is accepted once two frames agree on every badge, which at the scanner's cadence is
 * well under half a second -- fast enough to walk along a pen. A very clean single read
 * (every badge found directly) is accepted on its own. The same animal is not reported again
 * until the panel has gone away or shows a different animal, so standing still in front of
 * one cow does not add her ten times.
 */

export interface StableRead {
  key: string;
  rows: LivestockGeneRow[];
  confidence: number;
}

export type StabilizerEvent =
  | { type: 'confirmed'; read: StableRead }
  | { type: 'lost' }
  | null;

export interface StabilizerOptions {
  /** Agreeing reads needed before accepting. */
  agreeing: number;
  /** A single read at or above this confidence is accepted straight away. */
  instantConfidence: number;
  /** How long the panel must be absent before it counts as gone. */
  lostAfterMs: number;
}

export const DEFAULT_STABILIZER_OPTIONS: StabilizerOptions = {
  agreeing: 2,
  instantConfidence: 0.97,
  lostAfterMs: 900
};

export class LivestockReadStabilizer {
  private readonly options: StabilizerOptions;
  private candidateKey: string | null = null;
  private candidateCount = 0;
  private confirmedKey: string | null = null;
  private lastSeenAt = 0;

  constructor(options: Partial<StabilizerOptions> = {}) {
    this.options = { ...DEFAULT_STABILIZER_OPTIONS, ...options };
  }

  reset(): void {
    this.candidateKey = null;
    this.candidateCount = 0;
    this.confirmedKey = null;
    this.lastSeenAt = 0;
  }

  /** Forget the last confirmed animal, so the same panel can be reported again. */
  rearm(): void {
    this.confirmedKey = null;
    this.candidateKey = null;
    this.candidateCount = 0;
  }

  push(
    read: { rows: LivestockGeneRow[]; confidence: number; name?: string | null } | null,
    now: number
  ): StabilizerEvent {
    if (!read) {
      if (this.confirmedKey && now - this.lastSeenAt >= this.options.lostAfterMs) {
        this.confirmedKey = null;
        this.candidateKey = null;
        this.candidateCount = 0;
        return { type: 'lost' };
      }
      return null;
    }

    this.lastSeenAt = now;
    // The name is part of the identity: two animals with the same genes are still two.
    const key = read.name ? `${encodeAnimalGenes(read.rows)}#${read.name}` : encodeAnimalGenes(read.rows);
    if (key === this.confirmedKey) return null;

    if (key === this.candidateKey) {
      this.candidateCount++;
    } else {
      this.candidateKey = key;
      this.candidateCount = 1;
    }

    if (
      this.candidateCount >= this.options.agreeing ||
      read.confidence >= this.options.instantConfidence
    ) {
      this.confirmedKey = key;
      this.candidateKey = null;
      this.candidateCount = 0;
      return { type: 'confirmed', read: { key, rows: read.rows, confidence: read.confidence } };
    }
    return null;
  }
}
