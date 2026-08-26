import { SCANNER_CONFIG } from './scannerConfig.ts';

export class FrameStabilityDetector {
  private stableSince: Record<string | number, number> = {};
  private lastObservedSignature: Record<string | number, number> = {};

  public registerFrame(
    key: string | number,
    signature: number,
    threshold = SCANNER_CONFIG.performance.roiChangeThreshold
  ): boolean {
    const now = Date.now();
    const lastSig = this.lastObservedSignature[key];

    if (lastSig === undefined) {
      this.lastObservedSignature[key] = signature;
      this.stableSince[key] = now;
      return false;
    }

    const diffRatio = Math.abs(signature - lastSig) / Math.max(1, lastSig);
    if (diffRatio > threshold) {
      // Image changed, reset stability timer
      this.lastObservedSignature[key] = signature;
      this.stableSince[key] = now;
      return false;
    }

    // Image remained stable, check if stable duration threshold is reached
    const elapsed = now - (this.stableSince[key] || now);
    return elapsed >= SCANNER_CONFIG.performance.stableDurationMs;
  }

  public reset(key?: string | number): void {
    if (key !== undefined) {
      delete this.stableSince[key];
      delete this.lastObservedSignature[key];
    } else {
      this.stableSince = {};
      this.lastObservedSignature = {};
    }
  }
}
