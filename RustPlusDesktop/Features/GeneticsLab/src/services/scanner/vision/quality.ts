import { CameraQualityIssue } from '../scannerTypes.ts';
import { Quad, quadDrift } from './geometry.ts';
import { RasterImage } from './perspective.ts';

/**
 * Camera-only quality measurement.
 *
 * Everything is measured on the normalised row rather than the whole frame: a dark room or a
 * bright desk lamp elsewhere in shot says nothing about whether these six genes can be read.
 */

export interface ExposureReport {
  meanLuminance: number;
  nearWhiteRatio: number;
  nearBlackRatio: number;
}

function luminanceBuffer(image: RasterImage): Float32Array {
  const out = new Float32Array(image.width * image.height);
  for (let p = 0, i = 0; p < out.length; p++, i += 4) {
    out[p] = 0.299 * image.data[i] + 0.587 * image.data[i + 1] + 0.114 * image.data[i + 2];
  }
  return out;
}

export function measureExposure(image: RasterImage): ExposureReport {
  const luminance = luminanceBuffer(image);
  if (luminance.length === 0) {
    return { meanLuminance: 0, nearWhiteRatio: 0, nearBlackRatio: 0 };
  }

  let sum = 0;
  let nearWhite = 0;
  let nearBlack = 0;
  for (let p = 0; p < luminance.length; p++) {
    const value = luminance[p];
    sum += value;
    if (value >= 245) nearWhite++;
    else if (value <= 8) nearBlack++;
  }

  return {
    meanLuminance: sum / luminance.length,
    nearWhiteRatio: nearWhite / luminance.length,
    nearBlackRatio: nearBlack / luminance.length
  };
}

/**
 * Variance of the Laplacian, restricted to letter edges.
 *
 * Measuring the whole row would let the large flat badge interiors dominate and report a
 * perfectly sharp row as blurred.
 */
export function measureSharpness(image: RasterImage): number {
  const { width, height } = image;
  if (width < 3 || height < 3) return 0;

  const luminance = luminanceBuffer(image);

  let sum = 0;
  for (let p = 0; p < luminance.length; p++) sum += luminance[p];
  const mean = sum / luminance.length;

  let varianceAcc = 0;
  for (let p = 0; p < luminance.length; p++) {
    const d = luminance[p] - mean;
    varianceAcc += d * d;
  }
  const stdDev = Math.sqrt(varianceAcc / luminance.length);
  const letterThreshold = mean + stdDev * 0.5;

  let count = 0;
  let laplacianSum = 0;
  let laplacianSquareSum = 0;

  for (let y = 1; y < height - 1; y++) {
    const row = y * width;
    for (let x = 1; x < width - 1; x++) {
      const index = row + x;
      const center = luminance[index];
      const up = luminance[index - width];
      const down = luminance[index + width];
      const left = luminance[index - 1];
      const right = luminance[index + 1];

      const nearLetter =
        center > letterThreshold ||
        up > letterThreshold ||
        down > letterThreshold ||
        left > letterThreshold ||
        right > letterThreshold;
      if (!nearLetter) continue;

      const laplacian = up + down + left + right - 4 * center;
      laplacianSum += laplacian;
      laplacianSquareSum += laplacian * laplacian;
      count++;
    }
  }

  if (count < 16) return 0;
  const laplacianMean = laplacianSum / count;
  return laplacianSquareSum / count - laplacianMean * laplacianMean;
}

export interface RowQualityThresholds {
  minPixelsPerGene: number;
  maxPixelsPerGene: number;
  maxPerspectiveDegrees: number;
  minSharpness: number;
  maxGlareRatio: number;
  minMeanLuminance: number;
}

/** Beta starting values. Phase 6 replaces these from labelled device fixtures. */
export const DEFAULT_ROW_QUALITY_THRESHOLDS: RowQualityThresholds = {
  minPixelsPerGene: 24,
  maxPixelsPerGene: 110,
  maxPerspectiveDegrees: 35,
  minSharpness: 55,
  maxGlareRatio: 0.35,
  minMeanLuminance: 32
};

export interface RowQualityInput {
  normalized: RasterImage;
  /** Source pixels per gene, measured in camera pixels rather than analysis pixels. */
  pixelsPerGene: number;
  perspectiveDegrees: number;
  clipped: boolean;
  isStable: boolean;
}

export interface RowQualityReport extends ExposureReport {
  issues: CameraQualityIssue[];
  sharpness: number;
  pixelsPerGene: number;
  perspectiveDegrees: number;
}

/**
 * Produces the explicit list of reasons a row cannot be read right now. An empty list is the
 * only thing that allows OCR to run.
 */
export function assessRowQuality(
  input: RowQualityInput,
  thresholds: RowQualityThresholds = DEFAULT_ROW_QUALITY_THRESHOLDS
): RowQualityReport {
  const exposure = measureExposure(input.normalized);
  const sharpness = measureSharpness(input.normalized);
  const issues: CameraQualityIssue[] = [];

  if (input.pixelsPerGene < thresholds.minPixelsPerGene) issues.push('too-far');
  else if (input.pixelsPerGene > thresholds.maxPixelsPerGene) issues.push('too-close');

  if (input.clipped) issues.push('clipped');
  if (input.perspectiveDegrees > thresholds.maxPerspectiveDegrees) issues.push('extreme-perspective');
  if (exposure.meanLuminance < thresholds.minMeanLuminance) issues.push('too-dark');
  if (exposure.nearWhiteRatio > thresholds.maxGlareRatio) issues.push('glare');

  // A blur verdict on an over-exposed or under-exposed row would be misleading: fix the
  // light first, then judge focus.
  const exposureIsUsable =
    exposure.meanLuminance >= thresholds.minMeanLuminance &&
    exposure.nearWhiteRatio <= thresholds.maxGlareRatio;
  if (exposureIsUsable && sharpness < thresholds.minSharpness) issues.push('blurred');

  if (!input.isStable) issues.push('moving');

  return {
    issues,
    sharpness,
    pixelsPerGene: input.pixelsPerGene,
    perspectiveDegrees: input.perspectiveDegrees,
    ...exposure
  };
}

/**
 * Tracks how long the row quadrilateral has held still.
 *
 * Separate from the desktop `FrameStabilityDetector` on purpose: handheld camera motion and
 * a static screen-capture ROI need different thresholds, and the desktop one is frozen.
 */
export class RowStabilityTracker {
  private lastQuad: Quad | null = null;
  private steadySince = 0;

  constructor(
    private readonly minStableMs: number,
    /** Allowed corner drift, as a fraction of the row's own height. */
    private readonly driftFraction = 0.18
  ) {}

  update(quad: Quad, nowMs: number): boolean {
    const rowHeight = Math.max(
      1,
      (Math.hypot(quad[3].x - quad[0].x, quad[3].y - quad[0].y) +
        Math.hypot(quad[2].x - quad[1].x, quad[2].y - quad[1].y)) / 2
    );
    const allowedDrift = rowHeight * this.driftFraction;

    if (!this.lastQuad) {
      this.lastQuad = quad;
      this.steadySince = nowMs;
      return false;
    }

    const drift = quadDrift(this.lastQuad, quad);
    this.lastQuad = quad;

    if (drift > allowedDrift) {
      this.steadySince = nowMs;
      return false;
    }

    return nowMs - this.steadySince >= this.minStableMs;
  }

  reset(): void {
    this.lastQuad = null;
    this.steadySince = 0;
  }
}
