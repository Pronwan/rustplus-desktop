import { SCANNER_CONFIG } from './scannerConfig.ts';

export class RegionChangeDetector {
  private lastSignatures: Record<string | number, number> = {};

  public computeSignature(ctx: CanvasRenderingContext2D, width: number, height: number): number {
    if (width <= 0 || height <= 0) return 0;

    const imgData = ctx.getImageData(0, 0, width, height);
    const data = imgData.data;
    let sum = 0;
    const samplePoints = 32;
    const step = Math.max(1, Math.floor(data.length / samplePoints));

    for (let i = 0; i < data.length; i += step) {
      sum += data[i] + data[i + 1] + data[i + 2];
    }

    return sum;
  }

  public hasChanged(
    key: string | number,
    currentSignature: number,
    threshold = SCANNER_CONFIG.performance.roiChangeThreshold
  ): boolean {
    const last = this.lastSignatures[key];
    if (last === undefined) {
      this.lastSignatures[key] = currentSignature;
      return true;
    }

    const diffRatio = Math.abs(currentSignature - last) / Math.max(1, last);
    if (diffRatio > threshold) {
      this.lastSignatures[key] = currentSignature;
      return true;
    }

    return false;
  }

  public reset(key?: string | number): void {
    if (key !== undefined) {
      delete this.lastSignatures[key];
    } else {
      this.lastSignatures = {};
    }
  }
}
