import { RasterImage } from '../scannerTypes.ts';
import {
  BadgeColor,
  BadgeLayout,
  BadgeLayoutHint,
  GENES_PER_ROW,
  isGreenBadgePixel,
  isRedBadgePixel,
  locateBadgeColumns
} from './badgeColumns.ts';
import {
  GeneLetter,
  classifyGlyphFeatures,
  despeckle,
  extractGlyphFeatures,
  isolateGlyphInk
} from './glyphTemplates.ts';

/**
 * Reads a six-gene row out of a desktop screen-capture ROI.
 *
 * Two things about the desktop path make it different from the camera one, and the old
 * implementation was built as though neither were true.
 *
 * A tooltip is a still image. Frames are pixel-identical for as long as the cursor rests on
 * an item, so a read that fails once fails identically forever -- there is no "try again
 * next frame" the way there is when a hand-held phone re-frames a row. Temporal voting
 * cannot rescue a systematic failure, only a flickering one. That makes every hard gate a
 * potential permanent dead end, which is why this stage reports a verdict per slot instead
 * of collapsing the row into a single null: five good letters and one abstention is
 * something the caller can act on, and a bare null is not.
 *
 * The pixels are exact. No lens, no moire, no motion blur, and a known five-letter alphabet
 * split across two badge colours -- green carries G, Y and H, red carries W and X. Deciding
 * between three shapes, or two, on clean pixels is close to free, so the accuracy budget is
 * better spent on finding the letter than on doubting it.
 */

export type DesktopSlotReject =
  /** Nothing bright enough to be a letter inside the badge. */
  | 'no-ink'
  /** Thresholding caught the badge rather than the letter. */
  | 'flooded'
  /** Ink is present but resembles no template closely enough to name. */
  | 'shapeless';

export interface DesktopSlotRead {
  index: number;
  gene: GeneLetter | null;
  confidence: number;
  color: BadgeColor;
  /** Zoning distance to the winning template, lower is better. */
  distance: number;
  /** How much better the winner scored than the runner-up, 0..1. */
  margin: number;
  /** Ink share of the glyph's own bounding box. */
  density: number;
  /** Ink share of the whole letter zone. */
  inkShare: number;
  reject: DesktopSlotReject | null;
  /** Present only when `includeImages` was set: the exact mask handed to the classifier. */
  image?: RasterImage;
}

export interface DesktopRowRead {
  /** Six letters, or null when any slot abstained. */
  geneString: string | null;
  confidence: number;
  /** Lowest per-slot confidence among the resolved slots. */
  minSlotConfidence: number;
  slots: DesktopSlotRead[];
  resolvedCount: number;
  /** Slots whose badge colour was identified. The strongest signal that this is a real row. */
  badgeCount: number;
  layoutSource: BadgeLayout['source'];
  reject: 'geometry' | 'not-a-row' | null;
  latencyMs: number;
}

export interface DesktopReadOptions {
  /** Minimum ink share of the letter zone before a slot is credited with a glyph. */
  minInkShare: number;
  /** Above this the threshold caught the badge, not the letter. */
  maxInkShare: number;
  /** A glyph bounding box this solid is a block, not a letter. */
  maxDensity: number;
  /** Worst tolerated zoning distance to the winning template. */
  maxDistance: number;
  /** Below this the winner is not meaningfully better than the runner-up. */
  minMargin: number;
  /**
   * Ink score floor.
   *
   * Measured on Rust's own badge colours: white letters score about 255 and their softest
   * antialiased edge about 100, while #659A2B scores 46 and #B44437 scores 10. The floor
   * sits in that empty band, so a badge with no letter in it cannot be split into halves by
   * the adaptive threshold and mistaken for a glyph.
   */
  minInkScore: number;
  /** Working height the slot mask is scaled up to before zoning features are taken. */
  targetGlyphHeight: number;
  /** Produce a black-on-white raster per slot, for a second-opinion recogniser. */
  includeImages: boolean;
}

export const DEFAULT_DESKTOP_READ_OPTIONS: DesktopReadOptions = {
  minInkShare: 0.015,
  maxInkShare: 0.6,
  maxDensity: 0.9,
  maxDistance: 0.45,
  minMargin: 0.06,
  minInkScore: 80,
  targetGlyphHeight: 48,
  includeImages: false
};

const GREEN_GENES: readonly GeneLetter[] = ['G', 'Y', 'H'];
const RED_GENES: readonly GeneLetter[] = ['W', 'X'];

/** Whiteness, with saturation subtracted so a bright badge cannot pass for a letter. */
function inkScore(r: number, g: number, b: number): number {
  const lum = (r * 299 + g * 587 + b * 114) / 1000;
  const sat = Math.max(r, g, b) - Math.min(r, g, b);
  return Math.max(0, lum - sat * 0.72);
}

/**
 * Otsu's threshold over a 256-bin histogram.
 *
 * A fixed cut cannot serve every UI scale at once: at 0.5 scale a letter is a couple of
 * antialiased pixels that never reach full white, and at 1.0 it is a solid block of it. Otsu
 * picks the split that best separates whatever this particular slot actually contains.
 */
function otsuThreshold(histogram: Int32Array, total: number): number {
  if (total === 0) return 0;

  let sum = 0;
  for (let i = 0; i < 256; i++) sum += i * histogram[i];

  let sumBackground = 0;
  let weightBackground = 0;
  let best = 0;
  let bestVariance = -1;

  for (let t = 0; t < 256; t++) {
    weightBackground += histogram[t];
    if (weightBackground === 0) continue;

    const weightForeground = total - weightBackground;
    if (weightForeground === 0) break;

    sumBackground += t * histogram[t];
    const meanBackground = sumBackground / weightBackground;
    const meanForeground = (sum - sumBackground) / weightForeground;
    const delta = meanBackground - meanForeground;
    const variance = weightBackground * weightForeground * delta * delta;

    if (variance > bestVariance) {
      bestVariance = variance;
      best = t;
    }
  }

  return best;
}

interface LetterZone {
  x0: number;
  x1: number;
  y0: number;
  y1: number;
  /** One flag per zone pixel: whether it lies inside the badge and may count as ink. */
  allowed: Uint8Array;
}

/**
 * Narrows a slot to the badge's interior, scan line by scan line.
 *
 * The bounding box of the badge is not enough. A disc inscribed in its own box leaves about
 * a fifth of that box in the corners, and whatever is behind the tooltip shows through
 * there. Against Rust's dark panel that is harmless; against a snow bank or a lit wall those
 * corners are brighter than the letter, and a W surrounded by four bright corners is an
 * extremely convincing X.
 *
 * Taking the badge's own horizontal extent on each row instead confines the letter to where
 * a letter can physically be. The separator dash, the neighbouring badge and the background
 * are excluded by construction rather than by a threshold that has to be tuned, and a badge
 * the ROI cropped in half still yields exactly the rows that survived.
 */
function badgeInteriorFor(
  data: Uint8ClampedArray,
  width: number,
  height: number,
  x0: number,
  x1: number,
  fallbackY0: number,
  fallbackY1: number
): LetterZone {
  const rowMin = new Int32Array(height).fill(x1);
  const rowMax = new Int32Array(height).fill(x0 - 1);

  let minX = x1;
  let maxX = x0 - 1;
  let minY = height;
  let maxY = -1;

  for (let y = 0; y < height; y++) {
    const rowOffset = y * width * 4;
    for (let x = x0; x < x1; x++) {
      const i = rowOffset + x * 4;
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      if (!isGreenBadgePixel(r, g, b) && !isRedBadgePixel(r, g, b)) continue;
      if (x < rowMin[y]) rowMin[y] = x;
      if (x > rowMax[y]) rowMax[y] = x;
    }

    if (rowMax[y] < rowMin[y]) continue;
    // The rim is a blend of badge and background and clears neither colour test, so the
    // measured span already stops just inside it. One more pixel covers the rounding.
    rowMin[y] += 1;
    rowMax[y] -= 1;
    if (rowMax[y] < rowMin[y]) continue;

    if (rowMin[y] < minX) minX = rowMin[y];
    if (rowMax[y] > maxX) maxX = rowMax[y];
    if (y < minY) minY = y;
    if (y > maxY) maxY = y;
  }

  if (maxX < minX || maxY < minY) {
    // No badge here. Fall back to the calibrated box and let the ink decide.
    const zoneW = Math.max(0, x1 - x0);
    const zoneH = Math.max(0, fallbackY1 - fallbackY0);
    return { x0, x1, y0: fallbackY0, y1: fallbackY1, allowed: new Uint8Array(zoneW * zoneH).fill(1) };
  }

  const zoneW = maxX - minX + 1;
  const zoneH = maxY - minY + 1;
  const allowed = new Uint8Array(zoneW * zoneH);

  for (let y = minY; y <= maxY; y++) {
    if (rowMax[y] < rowMin[y]) continue;
    const rowOffset = (y - minY) * zoneW;
    const from = Math.max(minX, rowMin[y]);
    const to = Math.min(maxX, rowMax[y]);
    for (let x = from; x <= to; x++) allowed[rowOffset + (x - minX)] = 1;
  }

  return { x0: minX, x1: maxX + 1, y0: minY, y1: maxY + 1, allowed };
}

interface SlotMask {
  mask: Uint8Array;
  width: number;
  height: number;
  inkShare: number;
  image?: RasterImage;
}

/**
 * Binarises one letter zone and scales the result up for zoning.
 *
 * The mask is built at native resolution and replicated afterwards, never the other way
 * round. At a 0.5 UI scale a stroke is one pixel wide; resampling the colour image first
 * blends that stroke into the badge behind it and the letter simply stops existing.
 */
function buildSlotMask(
  data: Uint8ClampedArray,
  width: number,
  zone: LetterZone,
  options: DesktopReadOptions
): SlotMask | null {
  const zoneW = zone.x1 - zone.x0;
  const zoneH = zone.y1 - zone.y0;
  if (zoneW <= 1 || zoneH <= 1) return null;

  const histogram = new Int32Array(256);
  const scores = new Uint8Array(zoneW * zoneH);
  let considered = 0;

  for (let y = 0; y < zoneH; y++) {
    const rowOffset = (zone.y0 + y) * width * 4;
    for (let x = 0; x < zoneW; x++) {
      const p = y * zoneW + x;
      if (!zone.allowed[p]) continue;
      const i = rowOffset + (zone.x0 + x) * 4;
      const score = Math.round(inkScore(data[i], data[i + 1], data[i + 2]));
      scores[p] = score;
      histogram[score]++;
      considered++;
    }
  }

  if (considered === 0) return null;

  const threshold = Math.max(otsuThreshold(histogram, considered), options.minInkScore);

  const scale = Math.max(1, Math.min(8, Math.round(options.targetGlyphHeight / zoneH)));
  const maskW = zoneW * scale;
  const maskH = zoneH * scale;
  const mask = new Uint8Array(maskW * maskH);
  const image: RasterImage | undefined = options.includeImages
    ? { data: new Uint8ClampedArray(maskW * maskH * 4), width: maskW, height: maskH }
    : undefined;

  let inkCount = 0;

  for (let y = 0; y < zoneH; y++) {
    for (let x = 0; x < zoneW; x++) {
      const p = y * zoneW + x;
      const isInk = zone.allowed[p] && scores[p] >= threshold ? 1 : 0;
      if (isInk) inkCount++;

      for (let dy = 0; dy < scale; dy++) {
        const rowOffset = (y * scale + dy) * maskW + x * scale;
        for (let dx = 0; dx < scale; dx++) {
          const p = rowOffset + dx;
          mask[p] = isInk;
          if (image) {
            const i = p * 4;
            const value = isInk ? 0 : 255;
            image.data[i] = value;
            image.data[i + 1] = value;
            image.data[i + 2] = value;
            image.data[i + 3] = 255;
          }
        }
      }
    }
  }

  return {
    mask,
    width: maskW,
    height: maskH,
    inkShare: inkCount / considered,
    image
  };
}

function confidenceFor(distance: number, margin: number, maxDistance: number): number {
  const shape = Math.max(0, 1 - distance / maxDistance);
  const separation = Math.min(1, margin / 0.5);
  return Math.max(0, Math.min(100, Math.round(40 + shape * 45 + separation * 15)));
}

function unreadableSlot(index: number, color: BadgeColor, reject: DesktopSlotReject, inkShare = 0): DesktopSlotRead {
  return {
    index,
    gene: null,
    confidence: 0,
    color,
    distance: 1,
    margin: 0,
    density: 0,
    inkShare,
    reject
  };
}

/**
 * Reads one ROI.
 *
 * `hint` is the calibrated geometry. It is consulted for the pitch when the badges cannot be
 * found, and otherwise only to decide which index the leftmost visible badge holds.
 */
export function readDesktopGeneRow(
  data: Uint8ClampedArray,
  width: number,
  height: number,
  hint: BadgeLayoutHint,
  overrides: Partial<DesktopReadOptions> = {}
): DesktopRowRead {
  const started =
    typeof performance !== 'undefined' && typeof performance.now === 'function' ? performance.now() : Date.now();
  const options = { ...DEFAULT_DESKTOP_READ_OPTIONS, ...overrides };

  const empty = (reject: DesktopRowRead['reject'], layout?: BadgeLayout): DesktopRowRead => ({
    geneString: null,
    confidence: 0,
    minSlotConfidence: 0,
    slots: [],
    resolvedCount: 0,
    badgeCount: layout ? layout.columns.filter(c => c.color !== 'unknown').length : 0,
    layoutSource: layout ? layout.source : 'calibration',
    reject,
    latencyMs:
      (typeof performance !== 'undefined' && typeof performance.now === 'function'
        ? performance.now()
        : Date.now()) - started
  });

  const layout = locateBadgeColumns(data, width, height, hint);
  if (!layout) return empty('geometry');

  const badgeCount = layout.columns.filter(column => column.color !== 'unknown').length;

  // Terrain, cliffs and UI chrome do not contain five saturated discs in a straight line at
  // an even pitch. This is the guard that keeps the scanner from inventing plants, and it is
  // deliberately the only row-level guard: everything else is decided per slot.
  if (badgeCount < GENES_PER_ROW - 1) return empty('not-a-row', layout);

  const slots: DesktopSlotRead[] = [];
  let inkedSlots = 0;

  for (const column of layout.columns) {
    const zone = badgeInteriorFor(data, width, height, column.x0, column.x1, layout.bandY0, layout.bandY1);
    const built = buildSlotMask(data, width, zone, options);

    if (!built || built.inkShare < options.minInkShare) {
      slots.push(unreadableSlot(column.index, column.color, 'no-ink', built?.inkShare ?? 0));
      continue;
    }

    if (built.inkShare > options.maxInkShare) {
      slots.push(unreadableSlot(column.index, column.color, 'flooded', built.inkShare));
      continue;
    }

    inkedSlots++;

    // Order matters. Isolation runs on the despeckled mask so a speck that despeckle already
    // removed cannot be mistaken for the largest component in a slot whose letter is faint.
    const cleaned = built.width >= 24 ? despeckle(built.mask, built.width, built.height) : built.mask;
    const isolated = isolateGlyphInk(cleaned, built.width, built.height);
    const features = extractGlyphFeatures(isolated, built.width, built.height);

    if (!features) {
      slots.push(unreadableSlot(column.index, column.color, 'no-ink', built.inkShare));
      continue;
    }

    if (features.density > options.maxDensity) {
      slots.push(unreadableSlot(column.index, column.color, 'flooded', built.inkShare));
      continue;
    }

    // The badge colour halves the problem before the classifier sees it: three candidates in
    // a green badge, two in a red one. Cross-colour confusion -- a W read as an H -- stops
    // being expressible rather than being caught afterwards.
    const allowed =
      column.color === 'green' ? GREEN_GENES : column.color === 'red' ? RED_GENES : undefined;
    const match = classifyGlyphFeatures(features, allowed);

    if (!match || match.distance > options.maxDistance || match.margin < options.minMargin) {
      const slot = unreadableSlot(column.index, column.color, 'shapeless', built.inkShare);
      if (match) {
        slot.gene = match.gene;
        slot.distance = match.distance;
        slot.margin = match.margin;
      }
      slot.density = features.density;
      if (options.includeImages && built.image) slot.image = built.image;
      slots.push(slot);
      continue;
    }

    slots.push({
      index: column.index,
      gene: match.gene,
      confidence: confidenceFor(match.distance, match.margin, options.maxDistance),
      color: column.color,
      distance: match.distance,
      margin: match.margin,
      density: features.density,
      inkShare: built.inkShare,
      reject: null,
      image: options.includeImages ? built.image : undefined
    });
  }

  // Badges with no letters in them are a planter UI element, not a plant.
  if (inkedSlots < GENES_PER_ROW - 1) return empty('not-a-row', layout);

  const resolved = slots.filter(slot => slot.reject === null && slot.gene);
  const geneString = resolved.length === GENES_PER_ROW ? slots.map(slot => slot.gene).join('') : null;
  const confidence =
    resolved.length > 0 ? Math.round(resolved.reduce((sum, slot) => sum + slot.confidence, 0) / resolved.length) : 0;

  return {
    geneString,
    confidence: geneString ? confidence : 0,
    minSlotConfidence: resolved.length > 0 ? Math.min(...resolved.map(slot => slot.confidence)) : 0,
    slots,
    resolvedCount: resolved.length,
    badgeCount,
    layoutSource: layout.source,
    reject: null,
    latencyMs:
      (typeof performance !== 'undefined' && typeof performance.now === 'function'
        ? performance.now()
        : Date.now()) - started
  };
}
