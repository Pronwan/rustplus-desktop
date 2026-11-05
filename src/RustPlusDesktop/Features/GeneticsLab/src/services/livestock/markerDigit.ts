import { RasterImage } from '../scanner/scannerTypes.ts';
import { LivestockBadgeRead } from './livestockPanelReader.ts';
import { readDigits, segmentGlyphs } from './panelText.ts';

/**
 * Instant read of the number on a marker badge, for the shapes that are unambiguous even a
 * few pixels tall: `1` is a bar with no hole, `0` is a ring around one centred hole. Every
 * capture seen so far shows one of those two. Anything else returns null and is left to OCR
 * or to the player, never guessed.
 */

export interface MarkerGlyph {
  /** Glyph mask, row-major, 1 = ink. */
  mask: Uint8Array;
  width: number;
  height: number;
}

function lum(data: Uint8ClampedArray, p: number): number {
  return 0.299 * data[p] + 0.587 * data[p + 1] + 0.114 * data[p + 2];
}

/** The badge's bright mark, cropped to its own bounding box. */
export function extractMarkerGlyph(image: RasterImage, badge: LivestockBadgeRead): MarkerGlyph | null {
  const { data, width, height } = image;
  const radius = badge.diameter * 0.36;
  const x0 = Math.max(0, Math.floor(badge.cx - radius));
  const x1 = Math.min(width - 1, Math.ceil(badge.cx + radius));
  const y0 = Math.max(0, Math.floor(badge.cy - radius));
  const y1 = Math.min(height - 1, Math.ceil(badge.cy + radius));
  if (x1 - x0 < 3 || y1 - y0 < 3) return null;

  const ringLum = 0.299 * badge.rgb[0] + 0.587 * badge.rgb[1] + 0.114 * badge.rgb[2];
  let peak = 0;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) peak = Math.max(peak, lum(data, (y * width + x) * 4));
  }
  if (peak - ringLum < 40) return null;
  const threshold = ringLum + (peak - ringLum) * 0.5;

  let minX = Infinity;
  let minY = Infinity;
  let maxX = -1;
  let maxY = -1;
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      if (Math.hypot(x - badge.cx, y - badge.cy) > radius) continue;
      if (lum(data, (y * width + x) * 4) >= threshold) {
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  if (maxX < 0) return null;

  const w = maxX - minX + 1;
  const h = maxY - minY + 1;
  const mask = new Uint8Array(w * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (lum(data, ((minY + y) * width + (minX + x)) * 4) >= threshold) mask[y * w + x] = 1;
    }
  }
  return { mask, width: w, height: h };
}

/** Background regions fully enclosed by ink, with the vertical centre of each. */
export function enclosedHoles(glyph: MarkerGlyph): Array<{ cy: number; height: number; size: number }> {
  const { mask, width, height } = glyph;
  // Pad by one so the outside is a single connected region.
  const W = width + 2;
  const H = height + 2;
  const label = new Int32Array(W * H);
  const ink = (x: number, y: number) =>
    x >= 1 && y >= 1 && x <= width && y <= height && mask[(y - 1) * width + (x - 1)] === 1;

  const holes: Array<{ cy: number; height: number; size: number }> = [];
  let next = 1;
  for (let seed = 0; seed < W * H; seed++) {
    const sx = seed % W;
    const sy = (seed - sx) / W;
    if (label[seed] || ink(sx, sy)) continue;
    const id = next++;
    const stack = [seed];
    label[seed] = id;
    let touchesBorder = false;
    let size = 0;
    let minY = Infinity;
    let maxY = -1;
    let sumY = 0;
    while (stack.length) {
      const i = stack.pop()!;
      const x = i % W;
      const y = (i - x) / W;
      size++;
      sumY += y;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
      if (x === 0 || y === 0 || x === W - 1 || y === H - 1) touchesBorder = true;
      // 4-connectivity for background, so a diagonal gap in the ink does not open a hole.
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx;
        const ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
        const n = ny * W + nx;
        if (!label[n] && !ink(nx, ny)) {
          label[n] = id;
          stack.push(n);
        }
      }
    }
    if (!touchesBorder) holes.push({ cy: sumY / size - 1, height: maxY - minY + 1, size });
  }
  return holes;
}

export function classifyMarkerGlyph(glyph: MarkerGlyph): number | null {
  if (glyph.height < 5) return null;
  const aspect = glyph.width / glyph.height;
  const holes = enclosedHoles(glyph).filter((h) => h.size >= 2);

  if (holes.length === 0 && aspect < 0.5) return 1;
  if (holes.length === 1 && aspect >= 0.4 && aspect <= 0.95) {
    const hole = holes[0];
    // 6, 9 and 4 also have one hole, but it sits clearly above or below the middle.
    const centred = Math.abs(hole.cy - (glyph.height - 1) / 2) <= glyph.height * 0.12;
    if (centred && hole.height >= glyph.height * 0.3) return 0;
  }
  return null;
}

/**
 * The marker's number: template matching first (any value, one or more digits), then the
 * shape rules for a 0 or 1 too small for templates to be trusted.
 */
export function readMarkerDigit(image: RasterImage, badge: LivestockBadgeRead): number | null {
  const glyph = extractMarkerGlyph(image, badge);
  if (!glyph) return null;
  const digits = segmentGlyphs(glyph.mask, glyph.width, glyph.height).filter(
    (g) => g.height >= glyph.height * 0.6
  );
  const value = readDigits(digits);
  if (value !== null) return value;
  return classifyMarkerGlyph(glyph);
}
