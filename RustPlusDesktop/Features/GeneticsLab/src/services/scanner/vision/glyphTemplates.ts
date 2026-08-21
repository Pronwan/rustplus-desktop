import { RasterImage } from '../scannerTypes.ts';

/**
 * Gene glyph classification by template matching.
 *
 * Rust genes are drawn from a five-letter alphabet, so this is a five-class classification
 * problem, not a text recognition problem. A general OCR engine has to find a text line,
 * segment it, and choose from a whole character set; on a photographed monitor it drops
 * glyphs, returns five characters for a six-badge row, and costs tens of milliseconds a
 * frame. Matching against five references cannot drop a glyph -- every slot yields exactly
 * one answer -- and runs in microseconds.
 *
 * Templates are rasterised from stroke geometry rather than a font file, and compared with
 * coarse zoning features, so the match tolerates a different typeface, stroke weight and
 * moderate blur.
 */

export const GENE_ALPHABET = ['G', 'H', 'Y', 'W', 'X'] as const;
export type GeneLetter = (typeof GENE_ALPHABET)[number];

/** Zoning grid resolution. Coarse on purpose: fine grids overfit one typeface. */
const ZONES = 5;
const TEMPLATE_SIZE = 64;
const STROKE = 0.15;

export interface GlyphFeatures {
  /** Ink density per zone, row-major, ZONES * ZONES entries in 0..1. */
  zones: number[];
  /** Enclosed background regions. Separates G from the rest when the ring survives. */
  holes: number;
  /** Ink bounding-box aspect, width over height. */
  aspect: number;
  /** Ink share of the bounding box. */
  density: number;
}

interface Point {
  x: number;
  y: number;
}

function distanceToSegment(px: number, py: number, a: Point, b: Point): number {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const lengthSquared = dx * dx + dy * dy;
  if (lengthSquared === 0) return Math.hypot(px - a.x, py - a.y);

  let t = ((px - a.x) * dx + (py - a.y) * dy) / lengthSquared;
  t = t < 0 ? 0 : t > 1 ? 1 : t;
  return Math.hypot(px - (a.x + t * dx), py - (a.y + t * dy));
}

/** Stroke polylines in a unit box, y pointing down. */
const GLYPH_STROKES: Record<GeneLetter, Point[][]> = {
  H: [
    [{ x: 0.2, y: 0.05 }, { x: 0.2, y: 0.95 }],
    [{ x: 0.8, y: 0.05 }, { x: 0.8, y: 0.95 }],
    [{ x: 0.2, y: 0.5 }, { x: 0.8, y: 0.5 }]
  ],
  Y: [
    [{ x: 0.13, y: 0.06 }, { x: 0.5, y: 0.52 }],
    [{ x: 0.87, y: 0.06 }, { x: 0.5, y: 0.52 }],
    [{ x: 0.5, y: 0.52 }, { x: 0.5, y: 0.94 }]
  ],
  W: [
    [
      { x: 0.06, y: 0.06 },
      { x: 0.28, y: 0.94 },
      { x: 0.5, y: 0.4 },
      { x: 0.72, y: 0.94 },
      { x: 0.94, y: 0.06 }
    ]
  ],
  X: [
    [{ x: 0.12, y: 0.06 }, { x: 0.88, y: 0.94 }],
    [{ x: 0.88, y: 0.06 }, { x: 0.12, y: 0.94 }]
  ],
  // The bar that closes G's mouth. The ring itself is added separately.
  G: [[{ x: 0.52, y: 0.56 }, { x: 0.88, y: 0.56 }]]
};

/** Rasterises one glyph into a binary ink mask of `size` x `size`. */
export function rasterizeGlyph(gene: GeneLetter, size = TEMPLATE_SIZE): Uint8Array {
  const mask = new Uint8Array(size * size);
  const strokes = GLYPH_STROKES[gene];
  const half = STROKE / 2;

  for (let y = 0; y < size; y++) {
    const uy = (y + 0.5) / size;
    for (let x = 0; x < size; x++) {
      const ux = (x + 0.5) / size;
      let ink = false;

      for (const polyline of strokes) {
        for (let i = 1; i < polyline.length && !ink; i++) {
          if (distanceToSegment(ux, uy, polyline[i - 1], polyline[i]) <= half) ink = true;
        }
        if (ink) break;
      }

      if (!ink && gene === 'G') {
        // Elliptical ring with a gap on the right, which is what makes a G rather than an O.
        const nx = (ux - 0.5) / 0.4;
        const ny = (uy - 0.5) / 0.45;
        const radius = Math.hypot(nx, ny);
        const angle = Math.atan2(ny, nx);
        const inGap = angle > -0.5 && angle < 0.18;
        if (!inGap && Math.abs(radius - 1) <= half / 0.4) ink = true;
      }

      if (ink) mask[y * size + x] = 1;
    }
  }

  return mask;
}

/** Counts background regions not reachable from the border: the glyph's enclosed holes. */
export function countHoles(mask: Uint8Array, width: number, height: number): number {
  const seen = new Uint8Array(mask.length);
  const stack: number[] = [];

  const visit = (index: number) => {
    if (mask[index] || seen[index]) return;
    seen[index] = 1;
    stack.push(index);
  };

  for (let x = 0; x < width; x++) {
    visit(x);
    visit((height - 1) * width + x);
  }
  for (let y = 0; y < height; y++) {
    visit(y * width);
    visit(y * width + width - 1);
  }

  while (stack.length > 0) {
    const index = stack.pop()!;
    const x = index % width;
    const y = (index - x) / width;
    if (x > 0) visit(index - 1);
    if (x < width - 1) visit(index + 1);
    if (y > 0) visit(index - width);
    if (y < height - 1) visit(index + width);
  }

  let holes = 0;
  const counted = new Uint8Array(mask.length);
  for (let start = 0; start < mask.length; start++) {
    if (mask[start] || seen[start] || counted[start]) continue;

    // An enclosed pocket. Sweep it so it is only counted once, and ignore specks.
    let area = 0;
    counted[start] = 1;
    stack.push(start);
    while (stack.length > 0) {
      const index = stack.pop()!;
      area++;
      const x = index % width;
      const y = (index - x) / width;
      const neighbours = [
        x > 0 ? index - 1 : -1,
        x < width - 1 ? index + 1 : -1,
        y > 0 ? index - width : -1,
        y < height - 1 ? index + width : -1
      ];
      for (const neighbour of neighbours) {
        if (neighbour < 0 || mask[neighbour] || counted[neighbour]) continue;
        counted[neighbour] = 1;
        stack.push(neighbour);
      }
    }

    if (area >= mask.length * 0.01) holes++;
  }

  return holes;
}

/**
 * Zoning features over the ink bounding box.
 *
 * Cropping to the ink first is what makes this tolerant of where the glyph sits inside its
 * badge and how much padding the crop happened to include.
 */
export function extractGlyphFeatures(
  mask: Uint8Array,
  width: number,
  height: number
): GlyphFeatures | null {
  let minX = width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  let inkCount = 0;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      if (!mask[y * width + x]) continue;
      inkCount++;
      if (x < minX) minX = x;
      if (x > maxX) maxX = x;
      if (y < minY) minY = y;
      if (y > maxY) maxY = y;
    }
  }

  if (maxX < minX || maxY < minY) return null;

  const boxWidth = maxX - minX + 1;
  const boxHeight = maxY - minY + 1;
  const zones = new Array(ZONES * ZONES).fill(0);
  const zoneCounts = new Array(ZONES * ZONES).fill(0);

  for (let y = minY; y <= maxY; y++) {
    const zy = Math.min(ZONES - 1, Math.floor(((y - minY) / boxHeight) * ZONES));
    for (let x = minX; x <= maxX; x++) {
      const zx = Math.min(ZONES - 1, Math.floor(((x - minX) / boxWidth) * ZONES));
      const zone = zy * ZONES + zx;
      zoneCounts[zone]++;
      if (mask[y * width + x]) zones[zone]++;
    }
  }

  for (let zone = 0; zone < zones.length; zone++) {
    zones[zone] = zoneCounts[zone] > 0 ? zones[zone] / zoneCounts[zone] : 0;
  }

  return {
    zones,
    holes: countHoles(mask, width, height),
    aspect: boxWidth / boxHeight,
    density: inkCount / (boxWidth * boxHeight)
  };
}

let templateCache: Array<{ gene: GeneLetter; features: GlyphFeatures }> | null = null;

export function getGlyphTemplates(): Array<{ gene: GeneLetter; features: GlyphFeatures }> {
  if (templateCache) return templateCache;

  templateCache = GENE_ALPHABET.map(gene => {
    const mask = rasterizeGlyph(gene);
    const features = extractGlyphFeatures(mask, TEMPLATE_SIZE, TEMPLATE_SIZE);
    if (!features) throw new Error(`Failed to rasterise glyph template ${gene}`);
    return { gene, features };
  });

  return templateCache;
}

export interface GlyphMatch {
  gene: GeneLetter;
  /** Zoning distance to the winning template, lower is better. */
  distance: number;
  /** How much better the winner is than the runner-up, 0..1. */
  margin: number;
}

/**
 * Angle between the two zoning vectors, which compares where the ink is and ignores how
 * much of it there is.
 *
 * Straight Euclidean distance could not do that. Per-slot thresholding on a photographed
 * monitor lands anywhere from twice too fat to half too thin across a single row -- device
 * captures showed six cells of one word binarising at ink shares from 0.12 to 0.74 -- and
 * that scales every zone up or down together. The mismatch in overall ink then dominates
 * the sum, so all five templates sit at roughly the same distance, the margin collapses to
 * nothing, and the winner is decided by whichever template happens to carry a similar
 * amount of ink. Measured on dilated glyphs, Euclidean fell from 0.93 margin to 0.37 on
 * stroke weight alone.
 */
function featureDistance(a: GlyphFeatures, b: GlyphFeatures): number {
  let dot = 0;
  let normA = 0;
  let normB = 0;

  for (let i = 0; i < a.zones.length; i++) {
    dot += a.zones[i] * b.zones[i];
    normA += a.zones[i] * a.zones[i];
    normB += b.zones[i] * b.zones[i];
  }

  if (normA === 0 || normB === 0) return 1;

  const cosine = dot / Math.sqrt(normA * normB);
  const shape = 1 - Math.max(0, Math.min(1, cosine));

  // A surviving enclosed ring is strong evidence for G, but camera blur can break it, so
  // this nudges rather than decides.
  const holePenalty = Math.min(1, Math.abs(a.holes - b.holes)) * 0.05;
  return shape + holePenalty;
}

export function classifyGlyphFeatures(features: GlyphFeatures): GlyphMatch | null {
  const scored = getGlyphTemplates()
    .map(template => ({ gene: template.gene, distance: featureDistance(features, template.features) }))
    .sort((a, b) => a.distance - b.distance);

  if (scored.length < 2) return null;

  const [best, runnerUp] = scored;
  const margin = runnerUp.distance > 0 ? (runnerUp.distance - best.distance) / runnerUp.distance : 0;

  return { gene: best.gene, distance: best.distance, margin };
}

/** Ink mask from a rendered glyph cell: black strokes on white. */
export function maskFromImage(image: RasterImage, inkThreshold = 128): Uint8Array {
  const mask = new Uint8Array(image.width * image.height);
  for (let p = 0, i = 0; p < mask.length; p++, i += 4) {
    mask[p] = image.data[i] < inkThreshold ? 1 : 0;
  }
  return mask;
}

/**
 * 3x3 majority filter.
 *
 * Monitor moire and sensor noise survive thresholding as isolated specks, which inflate
 * every zone roughly equally and wash out the differences the classifier relies on. In
 * testing this is the difference between H reading as H and H reading as X.
 */
export function despeckle(mask: Uint8Array, width: number, height: number): Uint8Array {
  const out = new Uint8Array(mask.length);

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      let neighbours = 0;
      let total = 0;

      for (let dy = -1; dy <= 1; dy++) {
        const ny = y + dy;
        if (ny < 0 || ny >= height) continue;
        for (let dx = -1; dx <= 1; dx++) {
          const nx = x + dx;
          if (nx < 0 || nx >= width) continue;
          if (dx === 0 && dy === 0) continue;
          total++;
          neighbours += mask[ny * width + nx];
        }
      }

      const index = y * width + x;
      // Keep ink only if the neighbourhood agrees, and fill a hole the neighbourhood surrounds.
      out[index] = mask[index] ? (neighbours >= total * 0.3 ? 1 : 0) : neighbours >= total * 0.75 ? 1 : 0;
    }
  }

  return out;
}

/** Why a cell produced no letter, or null when it produced one. */
export type GlyphRejection = 'blank' | 'empty' | 'solid';

export interface GlyphInspection {
  /** Ink share of the ink bounding box, 0 when the cell held no ink at all. */
  density: number;
  holes: number;
  /** Nearest template and its scores. Present even when a gate rejects the cell. */
  match: GlyphMatch | null;
  reject: GlyphRejection | null;
}

/**
 * Classifies a cell and reports what it saw either way.
 *
 * `classifyGlyphImage` collapses every failure into `null`, which makes a blank cell, an
 * over-inked cell and a genuinely ambiguous glyph indistinguishable from each other. On a
 * device that only shows a status line, that is the difference between knowing which stage
 * to fix and guessing.
 */
export function inspectGlyphImage(image: RasterImage): GlyphInspection {
  const mask = despeckle(maskFromImage(image), image.width, image.height);
  const features = extractGlyphFeatures(mask, image.width, image.height);
  if (!features) return { density: 0, holes: 0, match: null, reject: 'blank' };

  const base = { density: features.density, holes: features.holes };
  // A cell that is almost empty or almost solid carries no glyph, whatever the nearest
  // template happens to be. Still report the nearest one so the numbers are inspectable.
  const match = classifyGlyphFeatures(features);
  if (features.density < 0.05) return { ...base, match, reject: 'empty' };
  if (features.density > 0.85) return { ...base, match, reject: 'solid' };

  return { ...base, match, reject: null };
}

export function classifyGlyphImage(image: RasterImage): GlyphMatch | null {
  const inspection = inspectGlyphImage(image);
  return inspection.reject ? null : inspection.match;
}
