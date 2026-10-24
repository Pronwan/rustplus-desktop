/**
 * Decides when the camera scanner may emit the same genotype again.
 *
 * A tooltip that stays on screen must produce exactly one clone, no matter how many frames
 * confirm it. Rearming is therefore driven by evidence — the row left the frame, or a clearly
 * different string replaced it — never by a timer alone.
 */
export class CameraTargetRearm {
  private lastEmittedGenes: string | null = null;
  private lostSince: number | null = null;
  private rearmed = false;

  constructor(private readonly rearmAfterLossMs: number) {}

  /** Called on every frame where a row is present, readable or not. */
  markVisible(): void {
    this.lostSince = null;
  }

  /** Called on every frame with no row. Sustained absence is what rearms the scanner. */
  markLost(nowMs: number): void {
    if (this.lostSince === null) {
      this.lostSince = nowMs;
      return;
    }
    if (nowMs - this.lostSince >= this.rearmAfterLossMs) {
      this.rearmed = true;
    }
  }

  shouldEmit(geneString: string): boolean {
    if (this.lastEmittedGenes === null) return true;
    if (geneString !== this.lastEmittedGenes) return true;
    return this.rearmed;
  }

  recordEmit(geneString: string): void {
    this.lastEmittedGenes = geneString;
    this.rearmed = false;
    this.lostSince = null;
  }

  reset(): void {
    this.lastEmittedGenes = null;
    this.lostSince = null;
    this.rearmed = false;
  }
}
