/**
 * Pixel-level operations for the phone camera locator.
 *
 * Everything here is pure and works on plain RGBA buffers, so the whole detection front end
 * can be exercised in tests with synthetic frames — no canvas, no DOM, no WASM runtime.
 */

export interface AnalysisFrame {
  /** RGBA, row-major, `width * height * 4` bytes. */
  data: Uint8ClampedArray;
  width: number;
  height: number;
  /**
   * Downscale factor applied to the camera frame, i.e. `width / cameraWidth`.
   * Multiply a measurement here by `1 / scale` to express it in camera pixels.
   */
  scale: number;
}

export type BadgeColor = 'green' | 'red';

export interface BadgeComponent {
  id: number;
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  width: number;
  height: number;
  /** Centroid of the masked pixels, not of the bounding box. */
  cx: number;
  cy: number;
  pixelCount: number;
  /** Masked pixels divided by bounding-box area. Badge letters punch a hole, so this is < 1. */
  fillRatio: number;
  aspect: number;
  color: BadgeColor;
  /** Share of the component's pixels that carried a confident badge colour, 0..1. */
  colorStrength: number;
  touchesEdge: boolean;
}

export const MASK_BACKGROUND = 0;
export const MASK_GREEN = 1;
export const MASK_RED = 2;

export interface BadgeMaskOptions {
  /** Minimum channel maximum. Rejects near-black pixels whose hue is meaningless. */
  minValue: number;
  /** Minimum max-min channel spread. Rejects greys, whites and the monitor bezel. */
  minSaturation: number;
  /** How far the dominant channel must lead the others. */
  minDominance: number;
}

/**
 * Broad, relative colour test rather than fixed hue windows.
 *
 * Monitor white balance, HDR, phone exposure and camera post-processing all shift the
 * absolute colour of a Rust gene badge, but the *dominant channel* survives all of them.
 */
export const DEFAULT_BADGE_MASK_OPTIONS: BadgeMaskOptions = {
  minValue: 45,
  minSaturation: 26,
  minDominance: 16
};

export interface BadgeMask {
  /** One byte per pixel: MASK_BACKGROUND | MASK_GREEN | MASK_RED. */
  labels: Uint8Array;
  width: number;
  height: number;
}

export function buildBadgeMask(
  frame: AnalysisFrame,
  options: BadgeMaskOptions = DEFAULT_BADGE_MASK_OPTIONS
): BadgeMask {
  const { width, height, data } = frame;
  const labels = new Uint8Array(width * height);
  const { minValue, minSaturation, minDominance } = options;

  for (let i = 0, p = 0; p < labels.length; p++, i += 4) {
    const r = data[i];
    const g = data[i + 1];
    const b = data[i + 2];

    const max = r > g ? (r > b ? r : b) : (g > b ? g : b);
    if (max < minValue) continue;

    const min = r < g ? (r < b ? r : b) : (g < b ? g : b);
    if (max - min < minSaturation) continue;

    if (g === max && g - (r > b ? r : b) >= minDominance) {
      labels[p] = MASK_GREEN;
    } else if (r === max && r - (g > b ? g : b) >= minDominance) {
      labels[p] = MASK_RED;
    }
  }

  return { labels, width, height };
}

/**
 * 3x3 max filter, applied as two 1-D passes.
 * `presence` is a 0/1 buffer; the result is written into a fresh buffer.
 */
function dilate(presence: Uint8Array, width: number, height: number): Uint8Array {
  const horizontal = new Uint8Array(presence.length);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    for (let x = 0; x < width; x++) {
      const left = x > 0 ? presence[row + x - 1] : 0;
      const right = x < width - 1 ? presence[row + x + 1] : 0;
      horizontal[row + x] = presence[row + x] | left | right;
    }
  }

  const out = new Uint8Array(presence.length);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    const up = y > 0 ? row - width : -1;
    const down = y < height - 1 ? row + width : -1;
    for (let x = 0; x < width; x++) {
      const a = up >= 0 ? horizontal[up + x] : 0;
      const b = down >= 0 ? horizontal[down + x] : 0;
      out[row + x] = horizontal[row + x] | a | b;
    }
  }
  return out;
}

/** 3x3 min filter. Border pixels erode away, which is the behaviour we want at the frame edge. */
function erode(presence: Uint8Array, width: number, height: number): Uint8Array {
  const horizontal = new Uint8Array(presence.length);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    for (let x = 0; x < width; x++) {
      const left = x > 0 ? presence[row + x - 1] : 0;
      const right = x < width - 1 ? presence[row + x + 1] : 0;
      horizontal[row + x] = presence[row + x] & left & right;
    }
  }

  const out = new Uint8Array(presence.length);
  for (let y = 0; y < height; y++) {
    const row = y * width;
    const up = y > 0 ? row - width : -1;
    const down = y < height - 1 ? row + width : -1;
    for (let x = 0; x < width; x++) {
      const a = up >= 0 ? horizontal[up + x] : 0;
      const b = down >= 0 ? horizontal[down + x] : 0;
      out[row + x] = horizontal[row + x] & a & b;
    }
  }
  return out;
}

/**
 * Fills background regions that are completely enclosed by the mask.
 *
 * This is what recovers the hole punched by the white gene letter. Morphological closing
 * would do it too, but a structuring element wide enough to span the letter is also wide
 * enough to bridge the gap between neighbouring badges and weld the whole row into one blob.
 * Flooding the background inward from the frame border cannot do that.
 */
export function fillEnclosedHoles(presence: Uint8Array, width: number, height: number): Uint8Array {
  const reachable = new Uint8Array(presence.length);
  const stack: number[] = [];

  const seed = (index: number) => {
    if (presence[index] || reachable[index]) return;
    reachable[index] = 1;
    stack.push(index);
  };

  for (let x = 0; x < width; x++) {
    seed(x);
    seed((height - 1) * width + x);
  }
  for (let y = 0; y < height; y++) {
    seed(y * width);
    seed(y * width + width - 1);
  }

  // 4-connected background flood, the complement of the 8-connected foreground labelling.
  while (stack.length > 0) {
    const index = stack.pop()!;
    const x = index % width;
    const y = (index - x) / width;

    if (x > 0) seed(index - 1);
    if (x < width - 1) seed(index + 1);
    if (y > 0) seed(index - width);
    if (y < height - 1) seed(index + width);
  }

  const out = new Uint8Array(presence.length);
  for (let p = 0; p < presence.length; p++) {
    out[p] = presence[p] || !reachable[p] ? 1 : 0;
  }
  return out;
}

/**
 * Removes speckle from monitor subpixel noise and moire, then restores the gene letters to
 * their badges. Returns a 0/1 presence buffer.
 */
export function cleanBadgeMask(mask: BadgeMask, openIterations = 1): Uint8Array {
  const { width, height } = mask;
  let presence: Uint8Array = new Uint8Array(mask.labels.length);
  for (let p = 0; p < presence.length; p++) {
    presence[p] = mask.labels[p] === MASK_BACKGROUND ? 0 : 1;
  }

  for (let i = 0; i < openIterations; i++) presence = erode(presence, width, height);
  for (let i = 0; i < openIterations; i++) presence = dilate(presence, width, height);

  return fillEnclosedHoles(presence, width, height);
}

/**
 * 8-connected labelling with an explicit stack. Recursion would blow up on a large
 * connected region, which is exactly what a glare patch produces.
 */
export function findComponents(
  presence: Uint8Array,
  mask: BadgeMask,
  maxComponents = 4096
): BadgeComponent[] {
  const { width, height } = mask;
  const visited = new Uint8Array(presence.length);
  const stack: number[] = [];
  const components: BadgeComponent[] = [];

  for (let seed = 0; seed < presence.length; seed++) {
    if (!presence[seed] || visited[seed]) continue;
    if (components.length >= maxComponents) break;

    visited[seed] = 1;
    stack.push(seed);

    let minX = width;
    let minY = height;
    let maxX = -1;
    let maxY = -1;
    let count = 0;
    let sumX = 0;
    let sumY = 0;
    let greenPixels = 0;
    let redPixels = 0;

    while (stack.length > 0) {
      const index = stack.pop()!;
      const x = index % width;
      const y = (index - x) / width;

      count++;
      sumX += x;
      sumY += y;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;

      const label = mask.labels[index];
      if (label === MASK_GREEN) greenPixels++;
      else if (label === MASK_RED) redPixels++;

      const xStart = x > 0 ? -1 : 0;
      const xEnd = x < width - 1 ? 1 : 0;
      const yStart = y > 0 ? -1 : 0;
      const yEnd = y < height - 1 ? 1 : 0;

      for (let dy = yStart; dy <= yEnd; dy++) {
        for (let dx = xStart; dx <= xEnd; dx++) {
          if (dx === 0 && dy === 0) continue;
          const neighbour = index + dy * width + dx;
          if (visited[neighbour] || !presence[neighbour]) continue;
          visited[neighbour] = 1;
          stack.push(neighbour);
        }
      }
    }

    const boxWidth = maxX - minX + 1;
    const boxHeight = maxY - minY + 1;
    const coloured = greenPixels + redPixels;

    components.push({
      id: components.length,
      minX,
      minY,
      maxX,
      maxY,
      width: boxWidth,
      height: boxHeight,
      cx: sumX / count,
      cy: sumY / count,
      pixelCount: count,
      fillRatio: count / (boxWidth * boxHeight),
      aspect: boxWidth / boxHeight,
      color: redPixels > greenPixels ? 'red' : 'green',
      colorStrength: count > 0 ? coloured / count : 0,
      touchesEdge: minX === 0 || minY === 0 || maxX === width - 1 || maxY === height - 1
    });
  }

  return components;
}

export interface BadgeFilterOptions {
  /** Smallest acceptable badge side, in analysis-frame pixels. */
  minSide: number;
  /** Largest acceptable badge side as a fraction of the frame's shorter dimension. */
  maxSideFraction: number;
  minPixelCount: number;
  minFillRatio: number;
  minAspect: number;
  maxAspect: number;
  minColorStrength: number;
  /** Keep only the largest N survivors, so grouping stays bounded on a busy frame. */
  maxCandidates: number;
}

export const DEFAULT_BADGE_FILTER_OPTIONS: BadgeFilterOptions = {
  minSide: 5,
  maxSideFraction: 0.2,
  minPixelCount: 20,
  minFillRatio: 0.35,
  minAspect: 0.45,
  maxAspect: 2.4,
  minColorStrength: 0.35,
  maxCandidates: 48
};

export function filterBadgeCandidates(
  components: BadgeComponent[],
  frameWidth: number,
  frameHeight: number,
  options: BadgeFilterOptions = DEFAULT_BADGE_FILTER_OPTIONS
): BadgeComponent[] {
  const maxSide = Math.min(frameWidth, frameHeight) * options.maxSideFraction;

  const kept = components.filter(c =>
    c.width >= options.minSide &&
    c.height >= options.minSide &&
    c.width <= maxSide &&
    c.height <= maxSide &&
    c.pixelCount >= options.minPixelCount &&
    c.fillRatio >= options.minFillRatio &&
    c.aspect >= options.minAspect &&
    c.aspect <= options.maxAspect &&
    c.colorStrength >= options.minColorStrength
  );

  if (kept.length <= options.maxCandidates) return kept;

  return [...kept]
    .sort((a, b) => b.pixelCount - a.pixelCount)
    .slice(0, options.maxCandidates);
}

/**
 * Convenience pipeline: frame -> mask -> cleanup -> components -> filtered badge candidates.
 *
 * `sizeReference` exists because the badge size envelope is a property of the camera frame,
 * not of the buffer being scanned. When tracking searches a small crop, passing the whole
 * frame's dimensions keeps a normally sized badge from looking oversized.
 */
export function detectBadgeCandidates(
  frame: AnalysisFrame,
  maskOptions: BadgeMaskOptions = DEFAULT_BADGE_MASK_OPTIONS,
  filterOptions: BadgeFilterOptions = DEFAULT_BADGE_FILTER_OPTIONS,
  sizeReference?: { width: number; height: number }
): BadgeComponent[] {
  const mask = buildBadgeMask(frame, maskOptions);
  const presence = cleanBadgeMask(mask);
  const components = findComponents(presence, mask);
  const reference = sizeReference ?? frame;
  return filterBadgeCandidates(components, reference.width, reference.height, filterOptions);
}

/**
 * Extracts an axis-aligned sub-frame. Used to confine tracking work to the neighbourhood of
 * the last known row instead of re-scanning the whole frame at the tracking rate.
 */
export function cropAnalysisFrame(
  frame: AnalysisFrame,
  x: number,
  y: number,
  width: number,
  height: number
): { frame: AnalysisFrame; offsetX: number; offsetY: number } | null {
  const left = Math.max(0, Math.floor(x));
  const top = Math.max(0, Math.floor(y));
  const right = Math.min(frame.width, Math.ceil(x + width));
  const bottom = Math.min(frame.height, Math.ceil(y + height));

  const cropWidth = right - left;
  const cropHeight = bottom - top;
  if (cropWidth <= 0 || cropHeight <= 0) return null;

  const data = new Uint8ClampedArray(cropWidth * cropHeight * 4);
  for (let row = 0; row < cropHeight; row++) {
    const sourceStart = ((top + row) * frame.width + left) * 4;
    data.set(frame.data.subarray(sourceStart, sourceStart + cropWidth * 4), row * cropWidth * 4);
  }

  return {
    frame: { data, width: cropWidth, height: cropHeight, scale: frame.scale },
    offsetX: left,
    offsetY: top
  };
}

/** Mean luminance of an axis-aligned region, clamped to the frame. Used for tooltip context. */
export function meanLuminance(
  frame: AnalysisFrame,
  x0: number,
  y0: number,
  x1: number,
  y1: number
): number {
  const left = Math.max(0, Math.floor(Math.min(x0, x1)));
  const right = Math.min(frame.width - 1, Math.ceil(Math.max(x0, x1)));
  const top = Math.max(0, Math.floor(Math.min(y0, y1)));
  const bottom = Math.min(frame.height - 1, Math.ceil(Math.max(y0, y1)));

  if (right < left || bottom < top) return 0;

  let sum = 0;
  let count = 0;
  for (let y = top; y <= bottom; y++) {
    const row = y * frame.width;
    for (let x = left; x <= right; x++) {
      const i = (row + x) * 4;
      sum += 0.299 * frame.data[i] + 0.587 * frame.data[i + 1] + 0.114 * frame.data[i + 2];
      count++;
    }
  }

  return count > 0 ? sum / count : 0;
}
