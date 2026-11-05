import { CameraRowSlots, RasterImage } from '../scannerTypes.ts';

/**
 * Builds the image handed to Tesseract on the camera path.
 *
 * The desktop stitcher cannot be reused here. It fills the strip white, draws the slots, and
 * *then* binarises the whole thing with one global threshold. White padding sits at the top
 * of that range, so it flips to black along with the letters, and the recogniser receives a
 * black frame with five full-height black bars between the glyph cells. It returns nothing.
 *
 * This builder thresholds each slot against its own local range and only ever paints black
 * where a letter is, so the background and gaps stay genuinely white.
 */

export interface CameraStripOptions {
  /** Height of one glyph cell in the output, in pixels. */
  cellHeight: number;
  /** White margin around the whole strip. */
  padding: number;
  /** White space between glyph cells. */
  gap: number;
}

export const DEFAULT_CAMERA_STRIP_OPTIONS: CameraStripOptions = {
  cellHeight: 120,
  padding: 16,
  gap: 16
};

export interface CameraStripResult {
  /** All six glyphs on one line, for single-line recognition. */
  strip: RasterImage;
  /** The same glyphs individually, for the single-character fallback. */
  slotImages: RasterImage[];
  /** Ink share per slot. A slot at 0 or near 1 produced nothing a recogniser can use. */
  slotInk: number[];
  /** True when the measured slot rectangles actually fall inside the normalised row. */
  slotsWithinBounds: boolean;
}

const GENES_PER_ROW = 6;

function createWhiteImage(width: number, height: number): RasterImage {
  const data = new Uint8ClampedArray(width * height * 4);
  data.fill(255);
  return { data, width, height };
}

/** Minimum channel value. White text scores high on this even over a saturated badge. */
function minChannelAt(source: RasterImage, x: number, y: number): number {
  const px = Math.min(source.width - 1, Math.max(0, Math.round(x)));
  const py = Math.min(source.height - 1, Math.max(0, Math.round(y)));
  const i = (py * source.width + px) * 4;
  const r = source.data[i];
  const g = source.data[i + 1];
  const b = source.data[i + 2];
  return r < g ? (r < b ? r : b) : g < b ? g : b;
}

/**
 * Adaptive threshold over one slot only.
 *
 * Local range is what makes this work: a badge is a small patch of fairly uniform colour with
 * bright glyph strokes on it, so min and max separate cleanly. Measured across the whole
 * strip, the white background would swamp that separation.
 */
function slotThreshold(
  source: RasterImage,
  x0: number,
  y0: number,
  width: number,
  height: number
): number {
  let min = 255;
  let max = 0;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const value = minChannelAt(source, x0 + x, y0 + y);
      if (value < min) min = value;
      if (value > max) max = value;
    }
  }

  // A flat slot has no letter in it. Splitting noise would invent strokes.
  if (max - min <= 35) return 255;

  // Otsu: pick the cut that best separates the slot's two populations, glyph and badge.
  // A fixed fraction of the range guesses where that boundary is and gets it wrong whenever
  // exposure or badge colour shifts, which on a photographed monitor is constantly.
  const histogram = new Int32Array(256);
  let total = 0;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      histogram[Math.round(minChannelAt(source, x0 + x, y0 + y))]++;
      total++;
    }
  }

  let sum = 0;
  for (let level = 0; level < 256; level++) sum += level * histogram[level];

  let backgroundWeight = 0;
  let backgroundSum = 0;
  let bestVariance = -1;
  let bestThreshold = min + (max - min) * 0.45;

  for (let level = 0; level < 256; level++) {
    backgroundWeight += histogram[level];
    if (backgroundWeight === 0) continue;
    const foregroundWeight = total - backgroundWeight;
    if (foregroundWeight === 0) break;

    backgroundSum += level * histogram[level];
    const backgroundMean = backgroundSum / backgroundWeight;
    const foregroundMean = (sum - backgroundSum) / foregroundWeight;
    const delta = backgroundMean - foregroundMean;
    const variance = backgroundWeight * foregroundWeight * delta * delta;

    if (variance > bestVariance) {
      bestVariance = variance;
      bestThreshold = level;
    }
  }

  return bestThreshold;
}

/**
 * Samples one slot into a cell, upscaling and thresholding in the same pass so glyph edges
 * come out of the interpolated values rather than from a pre-binarised staircase.
 */
function renderSlot(
  source: RasterImage,
  target: RasterImage,
  sourceX: number,
  sourceY: number,
  sourceWidth: number,
  sourceHeight: number,
  destX: number,
  destY: number,
  cellWidth: number,
  cellHeight: number,
  threshold: number
): void {
  const scaleX = sourceWidth / cellWidth;
  const scaleY = sourceHeight / cellHeight;

  for (let y = 0; y < cellHeight; y++) {
    const sy = sourceY + (y + 0.5) * scaleY;
    for (let x = 0; x < cellWidth; x++) {
      const sx = sourceX + (x + 0.5) * scaleX;
      // Otsu returns the top of the background class, so the glyph is strictly above it.
      if (minChannelAt(source, sx, sy) <= threshold) continue;

      // Letter stroke: paint it black. Everything else stays the white the strip began with.
      const i = ((destY + y) * target.width + (destX + x)) * 4;
      target.data[i] = 0;
      target.data[i + 1] = 0;
      target.data[i + 2] = 0;
      target.data[i + 3] = 255;
    }
  }
}

export function buildCameraGeneStrip(
  normalizedRow: RasterImage,
  slots: CameraRowSlots,
  options: CameraStripOptions = DEFAULT_CAMERA_STRIP_OPTIONS
): CameraStripResult | null {
  const { cellHeight, padding, gap } = options;
  if (slots.geneWidth <= 0 || slots.height <= 0) return null;

  const cellWidth = Math.max(1, Math.round((slots.geneWidth / slots.height) * cellHeight));
  const stripWidth = padding * 2 + cellWidth * GENES_PER_ROW + gap * (GENES_PER_ROW - 1);
  const stripHeight = padding * 2 + cellHeight;

  const strip = createWhiteImage(stripWidth, stripHeight);
  const slotImages: RasterImage[] = [];
  const slotInk: number[] = [];
  const pitch = slots.geneWidth + slots.gapWidth;

  // If the measured slots sit outside the normalised row, every sample clamps to an edge
  // pixel and the strip comes out blank. Worth knowing about explicitly.
  const lastSlotRight = slots.baseX + 5 * pitch + slots.geneWidth;
  const slotsWithinBounds =
    slots.baseX >= -1 &&
    slots.baseY >= -1 &&
    lastSlotRight <= normalizedRow.width + 1 &&
    slots.baseY + slots.height <= normalizedRow.height + 1;

  for (let slot = 0; slot < GENES_PER_ROW; slot++) {
    const sourceX = slots.baseX + slot * pitch;
    const threshold = slotThreshold(normalizedRow, sourceX, slots.baseY, slots.geneWidth, slots.height);

    renderSlot(
      normalizedRow,
      strip,
      sourceX,
      slots.baseY,
      slots.geneWidth,
      slots.height,
      padding + slot * (cellWidth + gap),
      padding,
      cellWidth,
      cellHeight,
      threshold
    );

    const slotImage = createWhiteImage(cellWidth + padding * 2, cellHeight + padding * 2);
    renderSlot(
      normalizedRow,
      slotImage,
      sourceX,
      slots.baseY,
      slots.geneWidth,
      slots.height,
      padding,
      padding,
      cellWidth,
      cellHeight,
      threshold
    );
    slotImages.push(slotImage);
    slotInk.push(inkCoverage(slotImage));
  }

  return { strip, slotImages, slotInk, slotsWithinBounds };
}

/** Share of black pixels in an image. Used to spot a strip that came out empty or solid. */
export function inkCoverage(image: RasterImage): number {
  let ink = 0;
  const total = image.width * image.height;
  for (let i = 0; i < image.data.length; i += 4) {
    if (image.data[i] === 0) ink++;
  }
  return total > 0 ? ink / total : 0;
}
