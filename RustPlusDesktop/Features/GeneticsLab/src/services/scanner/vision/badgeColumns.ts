/**
 * Finds the six gene badges inside a calibrated desktop ROI.
 *
 * The old path assumed the badges sat exactly where calibration said they did: slot `i`
 * started at `i * (geneWidth + gap)` and was `geneWidth` across. That is true only while the
 * saved region is pixel-perfect, and it stops being true the moment the game UI scale
 * changes, the tooltip renders a pixel lower, or the user nudges the box one step too far.
 * A slot shifted by a couple of pixels clips a stroke off its letter, and because a desktop
 * tooltip is a still image, the same clipped letter arrives on every frame -- the read does
 * not recover, it just never happens.
 *
 * Badges are the one thing in the ROI that is trivially findable: large saturated red or
 * green discs on a dark panel. Locating them from colour and snapping the slots onto what
 * was actually found turns calibration from a requirement into a hint, and removes the
 * separator dashes from the crop as a side effect -- they live in the gaps, and the gaps are
 * no longer part of any slot.
 */

export type BadgeColor = 'green' | 'red' | 'unknown';

export interface BadgeColumn {
  index: number;
  /** Slot bounds in ROI pixels, x1/y1 exclusive. */
  x0: number;
  x1: number;
  y0: number;
  y1: number;
  color: BadgeColor;
  /** Badge-coloured share of the slot box, 0..1. */
  fill: number;
  /** True when this slot was found in the colour profile rather than inferred from pitch. */
  detected: boolean;
}

export interface BadgeLayout {
  columns: BadgeColumn[];
  /**
   * `detected` = all six badges found directly, `fitted` = some found and the rest placed on
   * the measured pitch, `calibration` = too few found, the saved region's pitch was used.
   */
  source: 'detected' | 'fitted' | 'calibration';
  detectedCount: number;
  /** Vertical band the badges occupy, y1 exclusive. */
  bandY0: number;
  bandY1: number;
}

export const GENES_PER_ROW = 6;

/**
 * Rust draws the planter badges at roughly #659A2B and #B44437. These tests compare channels
 * against each other rather than against fixed levels, so they survive the gamma and
 * brightness differences between capture pipelines, and they reject the dark panel behind
 * the tooltip and the grey separator dashes, which are unsaturated.
 */
export function isGreenBadgePixel(r: number, g: number, b: number): boolean {
  return g > 60 && g >= r + 8 && g >= b + 16;
}

export function isRedBadgePixel(r: number, g: number, b: number): boolean {
  return r > 80 && r >= g + 20 && r >= b + 20;
}

/** Largest run of values at or above `threshold`, or null when nothing clears it. */
function dominantRun(profile: Int32Array, threshold: number): { start: number; end: number } | null {
  let best: { start: number; end: number } | null = null;
  let bestWeight = 0;
  let runStart = -1;
  let runWeight = 0;

  for (let i = 0; i <= profile.length; i++) {
    const inRun = i < profile.length && profile[i] >= threshold;
    if (inRun) {
      if (runStart < 0) {
        runStart = i;
        runWeight = 0;
      }
      runWeight += profile[i];
    } else if (runStart >= 0) {
      if (runWeight > bestWeight) {
        bestWeight = runWeight;
        best = { start: runStart, end: i };
      }
      runStart = -1;
    }
  }

  return best;
}

interface ColumnRun {
  start: number;
  /** Exclusive. */
  end: number;
  center: number;
  weight: number;
}

function findColumnRuns(profile: Int32Array, threshold: number, bridgeGap: number): ColumnRun[] {
  const runs: ColumnRun[] = [];
  let start = -1;
  let weight = 0;
  let gap = 0;

  for (let x = 0; x <= profile.length; x++) {
    const inRun = x < profile.length && profile[x] >= threshold;

    if (inRun) {
      if (start < 0) {
        start = x;
        weight = 0;
      }
      weight += profile[x];
      gap = 0;
      continue;
    }

    if (start < 0) continue;

    // A badge can be split by its own letter reaching the edge of the disc, or by a column
    // of antialiasing. Only close the run once the gap is wide enough to be a real gap.
    gap++;
    if (gap <= bridgeGap && x < profile.length) continue;

    const end = x - gap + 1;
    runs.push({ start, end, center: (start + end - 1) / 2, weight });
    start = -1;
    gap = 0;
  }

  return runs;
}

function median(values: number[]): number {
  if (values.length === 0) return 0;
  const sorted = values.slice().sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}

/**
 * Places six evenly spaced slots from however many badges were actually found.
 *
 * Two badges are enough to measure the pitch, and the pitch is what matters: once it is
 * known, a badge that the ROI clipped at either end sits at a predictable offset from the
 * ones that survived. The calibrated pitch is only consulted to decide which index the
 * leftmost survivor holds, which is the one thing the image cannot say on its own.
 */
function fitCenters(runs: ColumnRun[], hintPitch: number, hintFirstCenter: number): number[] | null {
  if (runs.length === 0) return null;

  if (runs.length === 1) {
    if (hintPitch <= 0) return null;
    const index = Math.max(0, Math.min(GENES_PER_ROW - 1, Math.round((runs[0].center - hintFirstCenter) / hintPitch)));
    const centers: number[] = [];
    for (let i = 0; i < GENES_PER_ROW; i++) centers.push(runs[0].center + (i - index) * hintPitch);
    return centers;
  }

  const first = runs[0];
  const last = runs[runs.length - 1];
  const span = last.center - first.center;
  if (span <= 0) return null;

  // Neighbouring gaps give the pitch directly; a missing badge in the middle shows up as a
  // gap of roughly twice the pitch, so the smallest gap is the most trustworthy estimate.
  const gaps: number[] = [];
  for (let i = 1; i < runs.length; i++) gaps.push(runs[i].center - runs[i - 1].center);
  const smallestGap = Math.min(...gaps);
  const seedPitch = smallestGap > 0 ? smallestGap : hintPitch;
  if (seedPitch <= 0) return null;

  const steps = Math.max(1, Math.min(GENES_PER_ROW - 1, Math.round(span / seedPitch)));
  const pitch = span / steps;
  if (!Number.isFinite(pitch) || pitch <= 0) return null;

  const rawIndex = hintPitch > 0 ? Math.round((first.center - hintFirstCenter) / pitch) : 0;
  const firstIndex = Math.max(0, Math.min(GENES_PER_ROW - 1 - steps, rawIndex));

  const centers: number[] = [];
  for (let i = 0; i < GENES_PER_ROW; i++) centers.push(first.center + (i - firstIndex) * pitch);
  return centers;
}

export interface BadgeLayoutHint {
  geneWidthPx: number;
  gapWidthPx: number;
}

/**
 * Locates the six slots in an ROI.
 *
 * Never returns fewer than six columns: a slot the colour profile could not confirm is still
 * placed, marked `detected: false`, and left for the reader to judge on its ink. Dropping it
 * here would turn a recoverable slot into a row that can never be read.
 */
export function locateBadgeColumns(
  data: Uint8ClampedArray,
  width: number,
  height: number,
  hint: BadgeLayoutHint
): BadgeLayout | null {
  if (width <= 0 || height <= 0 || data.length < width * height * 4) return null;

  const colBadge = new Int32Array(width);
  const colGreen = new Int32Array(width);
  const colRed = new Int32Array(width);
  const rowBadge = new Int32Array(height);

  for (let y = 0; y < height; y++) {
    const rowOffset = y * width * 4;
    for (let x = 0; x < width; x++) {
      const i = rowOffset + x * 4;
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];

      if (isGreenBadgePixel(r, g, b)) {
        colBadge[x]++;
        colGreen[x]++;
        rowBadge[y]++;
      } else if (isRedBadgePixel(r, g, b)) {
        colBadge[x]++;
        colRed[x]++;
        rowBadge[y]++;
      }
    }
  }

  let peak = 0;
  for (let x = 0; x < width; x++) if (colBadge[x] > peak) peak = colBadge[x];

  const hintPitch = hint.geneWidthPx > 0 ? hint.geneWidthPx + Math.max(0, hint.gapWidthPx) : 0;
  const hintFirstCenter = hint.geneWidthPx > 0 ? hint.geneWidthPx / 2 : 0;

  // Relative to the strongest column, so the same threshold works for a tall ROI that
  // contains whole badges and a short one cropped to the letters.
  const columnThreshold = Math.max(1, Math.round(peak * 0.35));
  const bridgeGap = Math.max(1, Math.round((hintPitch > 0 ? hintPitch : width / GENES_PER_ROW) * 0.12));
  const runs = peak > 0 ? findColumnRuns(colBadge, columnThreshold, bridgeGap) : [];

  const runWidths = runs.map(run => run.end - run.start);
  const typicalWidth = median(runWidths);
  // A sliver is antialiasing or a badge from a neighbouring row clipped by the ROI edge.
  const usableRuns = runs.filter(run => run.end - run.start >= Math.max(2, typicalWidth * 0.45));

  let rowPeak = 0;
  for (let y = 0; y < height; y++) if (rowBadge[y] > rowPeak) rowPeak = rowBadge[y];
  const bandThreshold = Math.max(1, Math.round(rowPeak * 0.35));
  const band = peak > 0 ? dominantRun(rowBadge, bandThreshold) : null;
  const bandY0 = band ? band.start : 0;
  const bandY1 = band ? band.end : height;

  let centers: number[] | null = null;
  let source: BadgeLayout['source'] = 'calibration';

  if (usableRuns.length === GENES_PER_ROW) {
    centers = usableRuns.map(run => run.center);
    source = 'detected';
  } else if (usableRuns.length >= 1) {
    centers = fitCenters(usableRuns, hintPitch, hintFirstCenter);
    source = centers ? 'fitted' : 'calibration';
  }

  let slotWidth: number;

  if (!centers) {
    if (hint.geneWidthPx <= 0) return null;
    centers = [];
    for (let i = 0; i < GENES_PER_ROW; i++) centers.push(hintFirstCenter + i * hintPitch);
    slotWidth = hint.geneWidthPx;
    source = 'calibration';
  } else if (source === 'detected') {
    slotWidth = Math.max(2, typicalWidth);
  } else {
    // On a fitted layout the measured pitch is more trustworthy than the calibrated width,
    // which is what drifted in the first place.
    const pitch = centers.length > 1 ? centers[1] - centers[0] : hintPitch;
    slotWidth = Math.max(2, Math.min(typicalWidth > 0 ? typicalWidth : pitch * 0.8, pitch * 0.95));
  }

  const half = slotWidth / 2;
  const columns: BadgeColumn[] = [];

  for (let i = 0; i < GENES_PER_ROW; i++) {
    const x0 = Math.max(0, Math.round(centers[i] - half));
    const x1 = Math.min(width, Math.max(x0 + 1, Math.round(centers[i] + half)));

    let green = 0;
    let red = 0;
    for (let x = x0; x < x1; x++) {
      green += colGreen[x];
      red += colRed[x];
    }

    const area = Math.max(1, (x1 - x0) * height);
    const detected =
      source !== 'calibration' &&
      usableRuns.some(run => centers![i] >= run.start - 1 && centers![i] <= run.end);

    columns.push({
      index: i,
      x0,
      x1,
      y0: bandY0,
      y1: bandY1,
      color: green > red * 1.2 ? 'green' : red > green * 1.2 ? 'red' : 'unknown',
      fill: (green + red) / area,
      detected
    });
  }

  return {
    columns,
    source,
    detectedCount: usableRuns.length,
    bandY0,
    bandY1
  };
}
