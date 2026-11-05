import { GeneLevel } from '../../domain/livestock/livestockGenes.ts';
import { LivestockGeneRow, MarkerColor } from '../../domain/livestock/animal.ts';
import { RasterImage } from '../scanner/scannerTypes.ts';
import { classifyGlyph } from './panelText.ts';
import { extractMarkerGlyph } from './markerDigit.ts';

/**
 * Finds and reads the livestock gene panel in a frame.
 *
 * The panel is two rows of six round badges: a large top row and a smaller row under it,
 * columns aligned. In each row the first five badges are the genes D, L, Y, F, H -- the
 * letter never changes, the badge colour is the gene's quality -- and the sixth carries a
 * number on a pink or blue badge.
 *
 * Reading letters is deliberately not part of this. On a real capture the badge letters are
 * four or five pixels tall and fall apart under any threshold, while the badge colours are
 * large flat areas that survive capture, scaling and a phone camera. So the reader:
 *
 * 1. collects badge candidates two ways: saturated discs (red, green, pink, blue), and
 *    "a bright mark on a flat ring" for the neutral badges, which are too dark to find by
 *    colour against the panel,
 * 2. fits evenly spaced rows of six through them, probing the gaps for badges the first
 *    pass missed,
 * 3. pairs a large row with a smaller row directly beneath it, and
 * 4. samples each badge's ring colour.
 *
 * Pure: plain RGBA in, plain data out, so it is tested with synthetic and real pixels.
 */

export type BadgeHue = 'red' | 'pink' | 'lime' | 'green' | 'teal' | 'blue' | 'purple' | 'grey' | 'unknown';

/**
 * The gene badges only ever use these three. The marker badge can be any colour, including
 * the gene green (seen: pink, lime, green, teal, blue, purple), so it is never used to
 * decide whether a row is a livestock panel.
 */
const GENE_HUES: ReadonlySet<BadgeHue> = new Set<BadgeHue>(['red', 'green', 'grey'])

export interface LivestockBadgeRead {
  cx: number;
  cy: number;
  diameter: number;
  hue: BadgeHue;
  rgb: [number, number, number];
  /** Found by the candidate pass (true) or by probing a gap in the row (false). */
  detected: boolean;
}

export interface LivestockRowRead {
  badges: LivestockBadgeRead[];
  pitch: number;
  diameter: number;
  cy: number;
}

export interface LivestockPanelRead {
  /** The animal's own row. The live panel shows only this one. */
  top: LivestockRowRead;
  /** The smaller second row of the earlier two-row panel, when present. */
  bottom: LivestockRowRead | null;
  rows: LivestockGeneRow[];
  /** 0..1, how cleanly the geometry and colours fit the expected panel. */
  confidence: number;
  bounds: { x0: number; y0: number; x1: number; y1: number };
}

export interface LivestockReaderOptions {
  /** Smallest badge diameter to consider, in frame pixels. */
  minDiameter: number;
  /** Largest badge diameter, as a fraction of the frame height. */
  maxDiameterFraction: number;
}

export const DEFAULT_LIVESTOCK_READER_OPTIONS: LivestockReaderOptions = {
  minDiameter: 7,
  maxDiameterFraction: 0.12
};

const BADGES_PER_ROW = 6;
const GENES_PER_ROW = 5;

interface Candidate {
  cx: number;
  cy: number;
  diameter: number;
}

/* ------------------------------------------------------------------ *
 * Colour
 * ------------------------------------------------------------------ */

function luminance(r: number, g: number, b: number): number {
  return 0.299 * r + 0.587 * g + 0.114 * b;
}

function hueDegrees(r: number, g: number, b: number): number {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  if (d === 0) return 0;
  let h: number;
  if (max === r) h = ((g - b) / d) % 6;
  else if (max === g) h = (b - r) / d + 2;
  else h = (r - g) / d + 4;
  h *= 60;
  return h < 0 ? h + 360 : h;
}

/**
 * Badge colour from a ring average. The thresholds compare channels rather than test fixed
 * values, so capture gamma and monitor brightness move them very little.
 */
export function classifyBadgeColor(r: number, g: number, b: number): BadgeHue {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const chroma = max - min;
  if (max < 18) return 'unknown';
  // Flat, unsaturated: the neutral badge (or the panel itself).
  if (chroma < 26 || chroma < max * 0.2) return 'grey';

  // Measured in game: gene green ~81 deg, red ~11 deg; markers teal ~168, blue ~236,
  // purple ~255, pink ~0 (rose).
  const h = hueDegrees(r, g, b);
  // Lime (a marker colour, ~70 deg) sits just short of the gene green (~81 deg).
  if (h >= 45 && h < 75) return 'lime';
  if (h >= 75 && h < 150) return 'green';
  if (h >= 150 && h < 200) return 'teal';
  if (h >= 200 && h < 248) return 'blue';
  if (h >= 248 && h < 290) return 'purple';
  if (h >= 290 && h < 345) return 'pink';
  if (h >= 345 || h < 45) {
    // Both are red-hued. The gene badge leans orange (green above blue); the marker badge
    // leans rose (blue level with or above green) and is paler.
    return b >= g - 6 ? 'pink' : 'red';
  }
  return 'unknown';
}

export function hueToLevel(hue: BadgeHue): GeneLevel | null {
  if (hue === 'red' || hue === 'pink') return 'low';
  if (hue === 'green' || hue === 'lime') return 'high';
  if (hue === 'grey') return 'mid';
  return null;
}

export function hueToMarker(hue: BadgeHue): MarkerColor {
  return hue === 'unknown' ? 'unknown' : hue;
}

/* ------------------------------------------------------------------ *
 * Pixel helpers
 * ------------------------------------------------------------------ */

interface Lum {
  values: Float32Array;
  width: number;
  height: number;
}

function buildLuminance(image: RasterImage): Lum {
  const { data, width, height } = image;
  const values = new Float32Array(width * height);
  for (let i = 0, p = 0; i < values.length; i++, p += 4) {
    values[i] = luminance(data[p], data[p + 1], data[p + 2]);
  }
  return { values, width, height };
}

interface RingSample {
  rgb: [number, number, number];
  /** Mean absolute deviation from the ring mean, summed over channels. */
  spread: number;
  lum: number;
  count: number;
}

/**
 * Average colour of an annulus around (cx, cy), skipping pixels much brighter than the ring
 * (the badge's own letter bleeding outwards). Radii are fractions of the badge diameter.
 */
function sampleRing(
  image: RasterImage,
  lum: Lum,
  cx: number,
  cy: number,
  diameter: number,
  inner = 0.3,
  outer = 0.42
): RingSample | null {
  const { data, width, height } = image;
  const pixels: number[] = [];
  const steps = Math.max(16, Math.round(diameter * 2.2));
  for (const fraction of [inner, (inner + outer) / 2, outer]) {
    const radius = fraction * diameter;
    for (let s = 0; s < steps; s++) {
      const angle = (s / steps) * Math.PI * 2;
      const x = Math.round(cx + Math.cos(angle) * radius);
      const y = Math.round(cy + Math.sin(angle) * radius);
      if (x < 0 || y < 0 || x >= width || y >= height) continue;
      pixels.push(y * width + x);
    }
  }
  if (pixels.length < 8) return null;

  // Median luminance first, so the letter's bright pixels can be dropped before averaging.
  const lums = pixels.map((i) => lum.values[i]).sort((a, b) => a - b);
  const medianLum = lums[lums.length >> 1];
  const kept = pixels.filter((i) => lum.values[i] <= medianLum + 45);
  if (kept.length < pixels.length * 0.5) return null;

  let r = 0;
  let g = 0;
  let b = 0;
  for (const i of kept) {
    r += data[i * 4];
    g += data[i * 4 + 1];
    b += data[i * 4 + 2];
  }
  r /= kept.length;
  g /= kept.length;
  b /= kept.length;

  let spread = 0;
  for (const i of kept) {
    spread += Math.abs(data[i * 4] - r) + Math.abs(data[i * 4 + 1] - g) + Math.abs(data[i * 4 + 2] - b);
  }
  spread /= kept.length;

  return { rgb: [r, g, b], spread, lum: luminance(r, g, b), count: kept.length };
}

/** Brightest values in the badge centre, where the letter or number sits. */
function centreBrightness(lum: Lum, cx: number, cy: number, diameter: number): number {
  const radius = Math.max(1, diameter * 0.22);
  const values: number[] = [];
  const x0 = Math.max(0, Math.floor(cx - radius));
  const x1 = Math.min(lum.width - 1, Math.ceil(cx + radius));
  const y0 = Math.max(0, Math.floor(cy - radius));
  const y1 = Math.min(lum.height - 1, Math.ceil(cy + radius));
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) values.push(lum.values[y * lum.width + x]);
  }
  if (values.length === 0) return 0;
  values.sort((a, b) => b - a);
  // Mean of the top few: one hot pixel is not a letter, a handful is.
  const take = Math.max(1, Math.round(values.length * 0.12));
  let sum = 0;
  for (let i = 0; i < take; i++) sum += values[i];
  return sum / take;
}

/**
 * Is there a badge centred here? A badge is a flat-coloured disc with a markedly brighter
 * letter in the middle, and -- the part that matters -- a different colour from whatever is
 * just outside it. Without that last test, a word of panel text (the GENETICS label) passes
 * as a neutral badge: flat surroundings, bright middle. That shifted whole rows by a slot.
 */
function probeBadge(
  image: RasterImage,
  lum: Lum,
  cx: number,
  cy: number,
  diameter: number
): { ring: RingSample; contrast: number } | null {
  const ring = sampleRing(image, lum, cx, cy, diameter);
  if (!ring) return null;
  const contrast = centreBrightness(lum, cx, cy, diameter) - ring.lum;
  if (ring.spread > 70 || contrast < 40) return null;
  const outside = sampleRing(image, lum, cx, cy, diameter, 0.6, 0.68);
  if (outside) {
    const edge =
      Math.abs(ring.rgb[0] - outside.rgb[0]) + Math.abs(ring.rgb[1] - outside.rgb[1]) + Math.abs(ring.rgb[2] - outside.rgb[2]);
    if (edge < 40) return null;
  }
  return { ring, contrast };
}

/* ------------------------------------------------------------------ *
 * Candidates
 * ------------------------------------------------------------------ */

interface ComponentBox {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
  count: number;
}

interface Region {
  x0: number;
  y0: number;
  x1: number;
  y1: number;
}

/**
 * 8-connected labelling over a code map: neighbours join only when they carry the same
 * non-zero code. `region` limits the work to a rectangle. A typed-array stack keeps a frame
 * full of grass (one huge component) from turning into millions of array reallocations.
 */
function labelComponents(
  codes: Uint8Array,
  width: number,
  height: number,
  onComponent: (box: ComponentBox, code: number) => void,
  region: Region = { x0: 0, y0: 0, x1: width, y1: height },
  visited: Uint8Array = new Uint8Array(codes.length)
): void {
  const stack = new Int32Array(Math.max(16, (region.x1 - region.x0) * (region.y1 - region.y0)));
  for (let sy = region.y0; sy < region.y1; sy++) {
    for (let sx = region.x0; sx < region.x1; sx++) {
      const seed = sy * width + sx;
      const code = codes[seed];
      if (!code || visited[seed]) continue;
      visited[seed] = 1;
      let top = 0;
      stack[top++] = seed;
      let minX = sx;
      let minY = sy;
      let maxX = sx;
      let maxY = sy;
      let count = 0;
      while (top > 0) {
        const index = stack[--top];
        const x = index % width;
        const y = (index - x) / width;
        count++;
        if (x < minX) minX = x;
        else if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        else if (y > maxY) maxY = y;
        const yLo = y > region.y0 ? -1 : 0;
        const yHi = y < region.y1 - 1 ? 1 : 0;
        const xLo = x > region.x0 ? -1 : 0;
        const xHi = x < region.x1 - 1 ? 1 : 0;
        for (let dy = yLo; dy <= yHi; dy++) {
          const row = index + dy * width;
          for (let dx = xLo; dx <= xHi; dx++) {
            const n = row + dx;
            if (codes[n] === code && !visited[n]) {
              visited[n] = 1;
              stack[top++] = n;
            }
          }
        }
      }
      onComponent({ minX, minY, maxX, maxY, count }, code);
    }
  }
}

/**
 * Largest plausible badge. A full-screen capture caps it at a fraction of the frame; a tight
 * crop around the panel (a camera region, a test fixture) may be little taller than the
 * panel itself, so small frames get an absolute allowance instead.
 */
function maxBadgeDiameter(image: RasterImage, options: LivestockReaderOptions): number {
  return Math.max(
    options.minDiameter + 1,
    image.height * options.maxDiameterFraction,
    Math.min(image.height * 0.42, 80)
  );
}

const FAMILY_RED = 1;
const FAMILY_GREEN = 2;
const FAMILY_BLUE = 3;

/**
 * Hue family of each saturated pixel, without trigonometry: which channel is largest and how
 * the other two compare is enough to place it. Red/rose spans roughly -15..45 degrees, green
 * 60..175, blue/pink the rest. Separate families keep a green badge from merging with grass
 * behind a red one, and two touching badges of different colours apart.
 */
function hueFamilyCodes(image: RasterImage): Uint8Array {
  const { data, width, height } = image;
  const codes = new Uint8Array(width * height);
  for (let i = 0, p = 0; i < codes.length; i++, p += 4) {
    const r = data[p];
    const g = data[p + 1];
    const b = data[p + 2];
    let max = r;
    let min = r;
    if (g > max) max = g;
    else if (g < min) min = g;
    if (b > max) max = b;
    else if (b < min) min = b;
    const chroma = max - min;
    if (max < 70 || chroma < 40 || chroma * 100 < max * 28) continue;
    if (max === r) {
      const t = (g - b) / chroma; // hue in units of 60 degrees
      if (t >= -0.25 && t < 0.75) codes[i] = FAMILY_RED;
      else if (t < -0.25) codes[i] = FAMILY_BLUE; // magenta / pink side
    } else if (max === g) {
      if ((b - r) / chroma < 0.92) codes[i] = FAMILY_GREEN;
    } else {
      codes[i] = FAMILY_BLUE;
    }
  }
  return codes;
}

/** Saturated, disc-shaped blobs: every badge except the neutral ones. */
function colouredDiscCandidates(image: RasterImage, options: LivestockReaderOptions): Candidate[] {
  const maxDiameter = maxBadgeDiameter(image, options);
  const candidates: Candidate[] = [];
  labelComponents(hueFamilyCodes(image), image.width, image.height, (box) => {
    const w = box.maxX - box.minX + 1;
    const h = box.maxY - box.minY + 1;
    const d = (w + h) / 2;
    if (d < options.minDiameter || d > maxDiameter) return;
    const aspect = w / h;
    if (aspect < 0.72 || aspect > 1.38) return;
    // A disc fills ~79% of its box; the letter punched into it takes some of that.
    const fill = box.count / (w * h);
    if (fill < 0.42 || fill > 0.95) return;
    candidates.push({ cx: (box.minX + box.maxX) / 2, cy: (box.minY + box.maxY) / 2, diameter: d });
  });
  return candidates;
}

/**
 * Where neutral badges can be, given the coloured ones. Every panel carries coloured marker
 * badges, so a horizontal band through each coloured badge, wide enough for a whole row,
 * covers every neutral badge without labelling the sky.
 */
function bandsAround(candidates: Candidate[], width: number, height: number): Region[] {
  const bands: Region[] = [];
  for (const c of candidates) {
    const halfH = c.diameter * 1.2;
    const reach = c.diameter * 2.7 * 6;
    const band = {
      x0: Math.max(0, Math.floor(c.cx - reach)),
      y0: Math.max(0, Math.floor(c.cy - halfH)),
      x1: Math.min(width, Math.ceil(c.cx + reach)),
      y1: Math.min(height, Math.ceil(c.cy + halfH))
    };
    const overlap = bands.find(
      (b) => b.y0 <= band.y1 && band.y0 <= b.y1 && b.x0 <= band.x1 && band.x0 <= b.x1
    );
    if (overlap) {
      overlap.x0 = Math.min(overlap.x0, band.x0);
      overlap.y0 = Math.min(overlap.y0, band.y0);
      overlap.x1 = Math.max(overlap.x1, band.x1);
      overlap.y1 = Math.max(overlap.y1, band.y1);
    } else {
      bands.push(band);
    }
  }
  return bands;
}

/**
 * Neutral badges: a bright letter whose surroundings form a flat ring. Only worth running
 * where letters are large enough to survive capture; below that, gaps are probed instead.
 */
function glyphDiscCandidates(
  image: RasterImage,
  lum: Lum,
  options: LivestockReaderOptions,
  regions: Region[]
): Candidate[] {
  const { data, width, height } = image;
  const codes = new Uint8Array(width * height);
  for (const region of regions) {
    for (let y = region.y0; y < region.y1; y++) {
      for (let x = region.x0; x < region.x1; x++) {
        const i = y * width + x;
        const p = i * 4;
        const max = Math.max(data[p], data[p + 1], data[p + 2]);
        const chroma = max - Math.min(data[p], data[p + 1], data[p + 2]);
        if (lum.values[i] >= 175 && chroma <= 70) codes[i] = 1;
      }
    }
  }

  const maxGlyph = maxBadgeDiameter(image, options) * 0.6;
  const candidates: Candidate[] = [];
  const visited = new Uint8Array(codes.length);
  const onGlyph = (box: ComponentBox) => {
    const w = box.maxX - box.minX + 1;
    const h = box.maxY - box.minY + 1;
    if (h < 5 || h > maxGlyph || w > h * 1.5) return;
    if (box.minX === 0 || box.minY === 0 || box.maxX === width - 1 || box.maxY === height - 1) return;
    const cx = (box.minX + box.maxX) / 2;
    const cy = (box.minY + box.maxY) / 2;
    // Letters sit at about 40-48% of the badge diameter.
    const diameter = h / 0.44;
    if (diameter < options.minDiameter) return;
    if (!probeBadge(image, lum, cx, cy, diameter)) return;
    candidates.push({ cx, cy, diameter });
  };
  for (const region of regions) labelComponents(codes, width, height, onGlyph, region, visited);
  return candidates;
}

/** Collapse candidates that describe the same badge (found by both passes). */
function mergeCandidates(candidates: Candidate[]): Candidate[] {
  const merged: Candidate[] = [];
  for (const c of candidates) {
    const twin = merged.find(
      (m) => Math.hypot(m.cx - c.cx, m.cy - c.cy) < Math.min(m.diameter, c.diameter) * 0.45
    );
    if (twin) {
      // The colour pass measures the disc itself; prefer it over the letter-derived size.
      twin.diameter = Math.max(twin.diameter, c.diameter);
    } else {
      merged.push({ ...c });
    }
  }
  return merged;
}

/**
 * Coloured badges first; neutral ones are then looked for only in bands through them. A frame
 * with no coloured badge at all is searched whole, which costs more but keeps an all-neutral
 * panel with an unusual marker colour findable.
 */
function collectCandidates(image: RasterImage, lum: Lum, opts: LivestockReaderOptions): Candidate[] {
  const coloured = colouredDiscCandidates(image, opts);
  const regions =
    coloured.length > 0
      ? bandsAround(coloured, image.width, image.height)
      : [{ x0: 0, y0: 0, x1: image.width, y1: image.height }];
  return mergeCandidates([...coloured, ...glyphDiscCandidates(image, lum, opts, regions)]);
}

/* ------------------------------------------------------------------ *
 * Rows
 * ------------------------------------------------------------------ */

interface RowFit {
  slots: Array<Candidate | null>;
  pitch: number;
  diameter: number;
  cy: number;
  startX: number;
  detected: number;
}

function median(values: number[]): number {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[sorted.length >> 1];
}

/**
 * All plausible evenly spaced runs through the candidates. A run is anchored on a pair of
 * neighbours, which fixes the pitch; every other slot is then looked up on that pitch.
 */
function fitRows(candidates: Candidate[]): RowFit[] {
  const fits: RowFit[] = [];
  const seen = new Set<string>();

  for (const a of candidates) {
    for (const b of candidates) {
      if (b === a || b.cx <= a.cx) continue;
      const sizeRatio = a.diameter / b.diameter;
      if (sizeRatio < 0.75 || sizeRatio > 1.33) continue;
      const d = (a.diameter + b.diameter) / 2;
      if (Math.abs(a.cy - b.cy) > d * 0.3) continue;

      // a and b may be neighbours or have one undetected badge between them.
      for (const span of [1, 2]) {
        const pitch = (b.cx - a.cx) / span;
        if (pitch < d * 1.05 || pitch > d * 2.6) continue;

        // Try every placement of `a` within a six-slot row.
        for (let aSlot = 0; aSlot + span < BADGES_PER_ROW; aSlot++) {
          const startX = a.cx - aSlot * pitch;
          const key = `${Math.round(startX / 2)}:${Math.round(a.cy / 2)}:${Math.round(pitch)}`;
          if (seen.has(key)) continue;
          seen.add(key);

          const slots: Array<Candidate | null> = [];
          let detected = 0;
          for (let s = 0; s < BADGES_PER_ROW; s++) {
            const x = startX + s * pitch;
            let best: Candidate | null = null;
            let bestDist = Infinity;
            for (const c of candidates) {
              const ratio = c.diameter / d;
              if (ratio < 0.7 || ratio > 1.43) continue;
              const dist = Math.hypot(c.cx - x, c.cy - a.cy);
              if (dist < pitch * 0.22 && dist < bestDist) {
                best = c;
                bestDist = dist;
              }
            }
            slots.push(best);
            if (best) detected++;
          }
          if (detected < 3) continue;

          const members = slots.filter((s): s is Candidate => !!s);
          fits.push({
            slots,
            pitch,
            diameter: median(members.map((m) => m.diameter)),
            cy: median(members.map((m) => m.cy)),
            startX,
            detected
          });
        }
      }
    }
  }
  return fits;
}

function readRow(image: RasterImage, lum: Lum, fit: RowFit): LivestockRowRead | null {
  const badges: LivestockBadgeRead[] = [];
  for (let s = 0; s < BADGES_PER_ROW; s++) {
    const found = fit.slots[s];
    const cx = found ? found.cx : fit.startX + s * fit.pitch;
    const cy = found ? found.cy : fit.cy;
    let ring: RingSample | null;
    if (found) {
      ring = sampleRing(image, lum, cx, cy, fit.diameter);
    } else {
      // A gap is only accepted when something badge-shaped is actually there.
      const probe = probeBadge(image, lum, cx, cy, fit.diameter);
      ring = probe ? probe.ring : null;
    }
    if (!ring) return null;
    badges.push({
      cx,
      cy,
      diameter: fit.diameter,
      hue: classifyBadgeColor(ring.rgb[0], ring.rgb[1], ring.rgb[2]),
      rgb: [Math.round(ring.rgb[0]), Math.round(ring.rgb[1]), Math.round(ring.rgb[2])],
      detected: !!found
    });
  }
  return { badges, pitch: fit.pitch, diameter: fit.diameter, cy: fit.cy };
}

/* ------------------------------------------------------------------ *
 * Panel
 * ------------------------------------------------------------------ */

function rowToGenes(row: LivestockRowRead, markerValue: number | null): LivestockGeneRow {
  return {
    levels: row.badges.slice(0, GENES_PER_ROW).map((badge) => hueToLevel(badge.hue)),
    marker: { value: markerValue, color: hueToMarker(row.badges[GENES_PER_ROW].hue) }
  };
}

/** Gene badges that are not a gene colour; a real panel has none, or one misread at most. */
function strayGeneHues(row: LivestockRowRead): number {
  return row.badges.slice(0, GENES_PER_ROW).filter((b) => !GENE_HUES.has(b.hue)).length;
}

/**
 * Is this six-badge row an animal's, not a plant clone's? Any neutral (grey) badge settles
 * it: plant genes are only ever red or green. Otherwise the letters decide. An animal's
 * second and fourth badges are always L and F, and neither letter exists in plant genetics
 * (G, H, Y, W, X), so reading those two is enough. Letters too small to read leave the
 * question open; the row is then accepted only if its marker is not a gene colour.
 */
function looksLikeLivestockRow(image: RasterImage, row: LivestockRowRead): boolean {
  if (row.badges.slice(0, GENES_PER_ROW).some((b) => b.hue === 'grey')) return true;
  let yes = 0;
  let no = 0;
  for (const [slot, letter] of [[1, 'L'], [3, 'F']] as const) {
    const glyph = extractMarkerGlyph(image, row.badges[slot]);
    if (!glyph || glyph.height < 7) continue;
    const match = classifyGlyph({ ...glyph, x0: 0, y0: 0 }, 'LFGHYWX');
    if (!match) continue;
    if (match.char === letter) yes++;
    else if (match.margin > 0.03) no++;
  }
  if (no > 0) return false;
  if (yes > 0) return true;
  const marker = row.badges[GENES_PER_ROW].hue;
  return marker !== 'red' && marker !== 'green';
}

function rowBounds(row: LivestockRowRead) {
  const first = row.badges[0];
  const last = row.badges[BADGES_PER_ROW - 1];
  const r = row.diameter / 2;
  return { x0: first.cx - r, y0: row.cy - r, x1: last.cx + r, y1: row.cy + r };
}

export interface ReadPanelOptions extends Partial<LivestockReaderOptions> {
  /** Reads the number on a marker badge; returns null when unsure. */
  readMarkerDigit?: (image: RasterImage, badge: LivestockBadgeRead) => number | null;
}

/**
 * Finds the gene panel. Two shapes are accepted:
 *
 * - one row of six (the live panel): five badges in gene colours, then the numbered marker
 *   in any colour.
 * - two rows (an earlier panel): a large row over a smaller one on the same pitch.
 *
 * The candidate with the most directly detected badges wins, so a two-row panel is never
 * reported as just its top row.
 */
export function readLivestockPanel(
  image: RasterImage,
  options: ReadPanelOptions = {}
): LivestockPanelRead | null {
  const opts: LivestockReaderOptions = { ...DEFAULT_LIVESTOCK_READER_OPTIONS, ...options };
  if (image.width < 40 || image.height < 16) return null;

  const lum = buildLuminance(image);
  const candidates = collectCandidates(image, lum, opts);
  if (candidates.length < 3) return null;

  const fits = fitRows(candidates).sort((x, y) => y.detected - x.detected);
  if (fits.length === 0) return null;

  const rowCache = new Map<RowFit, LivestockRowRead | null>();
  const readCached = (fit: RowFit) => {
    if (!rowCache.has(fit)) rowCache.set(fit, readRow(image, lum, fit));
    return rowCache.get(fit)!;
  };

  interface Best {
    top: LivestockRowRead;
    bottom: LivestockRowRead | null;
    score: number;
    confidence: number;
  }
  const found: { best: Best | null } = { best: null };
  const bestScore = () => (found.best ? found.best.score : -Infinity);
  const consider = (top: LivestockRowRead, bottom: LivestockRowRead | null, detected: number, slots: number) => {
    const stray = strayGeneHues(top) + (bottom ? strayGeneHues(bottom) : 0);
    const confidence = Math.max(0, Math.min(1, (detected / slots) * 0.6 + 0.4 - stray * 0.15));
    const score = detected + confidence;
    if (score > bestScore()) found.best = { top, bottom, score, confidence };
  };

  // One row: the live panel.
  for (const fit of fits) {
    if (fit.detected + 1 < bestScore()) break;
    const row = readCached(fit);
    if (!row) continue;
    if (row.badges[GENES_PER_ROW].hue === 'unknown') continue;
    if (strayGeneHues(row) > 1) continue;
    if (!looksLikeLivestockRow(image, row)) continue;
    consider(row, null, fit.detected, BADGES_PER_ROW);
  }

  // Two rows: the earlier panel.
  for (const top of fits) {
    for (const bottom of fits) {
      if (bottom === top) continue;
      const sizeRatio = bottom.diameter / top.diameter;
      if (sizeRatio < 0.45 || sizeRatio > 0.95) continue;
      const pitchRatio = bottom.pitch / top.pitch;
      if (pitchRatio < 0.85 || pitchRatio > 1.18) continue;
      const drop = bottom.cy - top.cy;
      if (drop < top.diameter * 0.7 || drop > top.diameter * 2.6) continue;
      if (Math.abs(bottom.startX - top.startX) > top.pitch * 0.3) continue;
      const detected = top.detected + bottom.detected;
      if (detected + 1 < bestScore()) continue;

      const topRow = readCached(top);
      const bottomRow = topRow ? readCached(bottom) : null;
      if (!topRow || !bottomRow) continue;
      if (strayGeneHues(topRow) + strayGeneHues(bottomRow) > 2) continue;
      consider(topRow, bottomRow, detected, BADGES_PER_ROW * 2);
    }
  }

  if (!found.best) return null;
  const { top, bottom, confidence } = found.best;
  const read = options.readMarkerDigit;
  const rows = [rowToGenes(top, read ? read(image, top.badges[GENES_PER_ROW]) : null)];
  if (bottom) rows.push(rowToGenes(bottom, read ? read(image, bottom.badges[GENES_PER_ROW]) : null));

  const topBox = rowBounds(top);
  const bottomBox = bottom ? rowBounds(bottom) : topBox;
  return {
    top,
    bottom,
    rows,
    confidence,
    bounds: {
      x0: Math.max(0, Math.floor(Math.min(topBox.x0, bottomBox.x0))),
      y0: Math.max(0, Math.floor(topBox.y0)),
      x1: Math.min(image.width, Math.ceil(Math.max(topBox.x1, bottomBox.x1))),
      y1: Math.min(image.height, Math.ceil(bottomBox.y1))
    }
  };
}

/** Intermediate stages, for diagnostics and tests. */
export function inspectLivestockFrame(image: RasterImage, options: Partial<LivestockReaderOptions> = {}) {
  const opts: LivestockReaderOptions = { ...DEFAULT_LIVESTOCK_READER_OPTIONS, ...options };
  const t0 = performance.now();
  const lum = buildLuminance(image);
  const coloured = colouredDiscCandidates(image, opts);
  const t1 = performance.now();
  const candidates = collectCandidates(image, lum, opts);
  const t2 = performance.now();
  const fits = fitRows(candidates);
  const t3 = performance.now();
  return { coloured, candidates, fits, timings: { coloured: t1 - t0, candidates: t2 - t1, rows: t3 - t2 } };
}
