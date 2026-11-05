import { RasterImage } from '../scanner/scannerTypes.ts';
import { GLYPH_GRID_HEIGHT, GLYPH_GRID_WIDTH, GLYPH_TEMPLATES, GlyphTemplate } from './glyphTemplateData.ts';

/**
 * Small, local text reading for the livestock panel: the marker number, the age, and the
 * condition percentages. Every string here is short, white, bold and drawn in one known
 * face, so template matching reads it on the spot -- no OCR engine, no download, and it runs
 * inside the reader worker on every frame.
 */

export interface BinaryGlyph {
  /** 1 = ink, row-major, `width * height`. */
  mask: Uint8Array;
  width: number;
  height: number;
  /** Position in the source image. */
  x0: number;
  y0: number;
}

export interface GlyphMatch {
  char: string;
  score: number;
  /** Score gap to the best different character. */
  margin: number;
}

interface PreparedTemplate extends GlyphTemplate {
  vector: Float32Array;
  norm: number;
}

const TEMPLATES: PreparedTemplate[] = GLYPH_TEMPLATES.map((t) => {
  const vector = new Float32Array(t.grid.length);
  let norm = 0;
  for (let i = 0; i < t.grid.length; i++) {
    vector[i] = Number(t.grid[i]) / 9;
    norm += vector[i] * vector[i];
  }
  return { ...t, vector, norm: Math.sqrt(norm) };
});

/** Ink coverage of the glyph box-filtered onto the template grid. */
export function glyphGrid(glyph: BinaryGlyph): Float32Array {
  const out = new Float32Array(GLYPH_GRID_WIDTH * GLYPH_GRID_HEIGHT);
  const { mask, width, height } = glyph;
  for (let gy = 0; gy < GLYPH_GRID_HEIGHT; gy++) {
    const y0 = (gy * height) / GLYPH_GRID_HEIGHT;
    const y1 = ((gy + 1) * height) / GLYPH_GRID_HEIGHT;
    for (let gx = 0; gx < GLYPH_GRID_WIDTH; gx++) {
      const x0 = (gx * width) / GLYPH_GRID_WIDTH;
      const x1 = ((gx + 1) * width) / GLYPH_GRID_WIDTH;
      let ink = 0;
      let area = 0;
      // Fractional pixel overlap, so a 5-pixel-wide glyph still spreads over ten columns.
      for (let y = Math.floor(y0); y < Math.ceil(y1); y++) {
        const wy = Math.min(y + 1, y1) - Math.max(y, y0);
        if (wy <= 0) continue;
        for (let x = Math.floor(x0); x < Math.ceil(x1); x++) {
          const wx = Math.min(x + 1, x1) - Math.max(x, x0);
          if (wx <= 0) continue;
          const w = wx * wy;
          area += w;
          ink += mask[y * width + x] * w;
        }
      }
      out[gy * GLYPH_GRID_WIDTH + gx] = area > 0 ? ink / area : 0;
    }
  }
  return out;
}

/** Enclosed background regions (the counters of 0, 4, 6, 8, 9, D, %). */
export function countHoles(glyph: BinaryGlyph, minSize = 2): number {
  const { mask, width, height } = glyph;
  const W = width + 2;
  const H = height + 2;
  const seen = new Uint8Array(W * H);
  const ink = (x: number, y: number) =>
    x >= 1 && y >= 1 && x <= width && y <= height && mask[(y - 1) * width + (x - 1)] === 1;
  let holes = 0;
  const stack: number[] = [];
  for (let seed = 0; seed < W * H; seed++) {
    const sx = seed % W;
    const sy = (seed - sx) / W;
    if (seen[seed] || ink(sx, sy)) continue;
    seen[seed] = 1;
    stack.push(seed);
    let size = 0;
    let border = false;
    while (stack.length) {
      const i = stack.pop()!;
      const x = i % W;
      const y = (i - x) / W;
      size++;
      if (x === 0 || y === 0 || x === W - 1 || y === H - 1) border = true;
      // 4-connected background: a diagonal gap in the ink must not open a counter.
      const next = [i + 1, i - 1, i + W, i - W];
      const ok = [x < W - 1, x > 0, y < H - 1, y > 0];
      for (let k = 0; k < 4; k++) {
        const n = next[k];
        if (ok[k] && !seen[n] && !ink(n % W, (n - (n % W)) / W)) {
          seen[n] = 1;
          stack.push(n);
        }
      }
    }
    if (!border && size >= minSize) holes++;
  }
  return holes;
}

export function classifyGlyph(glyph: BinaryGlyph, allowed: string): GlyphMatch | null {
  if (glyph.height < 4) return null;
  const vector = glyphGrid(glyph);
  let norm = 0;
  for (const v of vector) norm += v * v;
  norm = Math.sqrt(norm);
  if (norm === 0) return null;
  const aspect = glyph.width / glyph.height;

  let candidates = TEMPLATES.filter((t) => allowed.includes(t.char));
  // Counters survive capture once a glyph is ~9 px tall; below that they close up and the
  // count would only mislead.
  if (glyph.height >= 9) {
    const holes = countHoles(glyph);
    const sameHoles = candidates.filter((t) => t.holes === holes);
    if (sameHoles.length > 0) candidates = sameHoles;
  }

  const scored = candidates
    .map((t) => {
      let dot = 0;
      for (let i = 0; i < vector.length; i++) dot += vector[i] * t.vector[i];
      const cosine = dot / (norm * t.norm);
      return { char: t.char, score: cosine - 0.5 * Math.abs(Math.log(aspect / t.aspect)) };
    })
    .sort((a, b) => b.score - a.score);
  if (scored.length === 0) return null;
  const runnerUp = scored.find((s) => s.char !== scored[0].char);
  return { char: scored[0].char, score: scored[0].score, margin: runnerUp ? scored[0].score - runnerUp.score : 1 };
}

/* ------------------------------------------------------------------ *
 * Segmentation
 * ------------------------------------------------------------------ */

interface Box {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

/**
 * Glyphs in a binary mask, left to right. Pieces of one character (the dot and slash of %,
 * a stroke broken by antialiasing) overlap horizontally, so overlapping boxes are merged.
 */
export function segmentGlyphs(mask: Uint8Array, width: number, height: number, x0 = 0, y0 = 0): BinaryGlyph[] {
  const seen = new Uint8Array(mask.length);
  const boxes: Box[] = [];
  const stack: number[] = [];
  for (let seed = 0; seed < mask.length; seed++) {
    if (!mask[seed] || seen[seed]) continue;
    seen[seed] = 1;
    stack.push(seed);
    const box = { minX: width, minY: height, maxX: -1, maxY: -1 };
    while (stack.length) {
      const i = stack.pop()!;
      const x = i % width;
      const y = (i - x) / width;
      if (x < box.minX) box.minX = x;
      if (x > box.maxX) box.maxX = x;
      if (y < box.minY) box.minY = y;
      if (y > box.maxY) box.maxY = y;
      for (let dy = -1; dy <= 1; dy++) {
        const ny = y + dy;
        if (ny < 0 || ny >= height) continue;
        for (let dx = -1; dx <= 1; dx++) {
          const nx = x + dx;
          if (nx < 0 || nx >= width) continue;
          const n = ny * width + nx;
          if (mask[n] && !seen[n]) {
            seen[n] = 1;
            stack.push(n);
          }
        }
      }
    }
    boxes.push(box);
  }

  boxes.sort((a, b) => a.minX - b.minX);
  const merged: Box[] = [];
  for (const box of boxes) {
    const last = merged[merged.length - 1];
    const overlap = last ? Math.min(last.maxX, box.maxX) - Math.max(last.minX, box.minX) + 1 : 0;
    const narrower = last ? Math.min(last.maxX - last.minX, box.maxX - box.minX) + 1 : 1;
    if (last && overlap >= narrower * 0.5) {
      last.minX = Math.min(last.minX, box.minX);
      last.minY = Math.min(last.minY, box.minY);
      last.maxX = Math.max(last.maxX, box.maxX);
      last.maxY = Math.max(last.maxY, box.maxY);
    } else {
      merged.push({ ...box });
    }
  }

  return merged.map((box) => {
    const w = box.maxX - box.minX + 1;
    const h = box.maxY - box.minY + 1;
    const out = new Uint8Array(w * h);
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) out[y * w + x] = mask[(box.minY + y) * width + box.minX + x];
    }
    return { mask: out, width: w, height: h, x0: x0 + box.minX, y0: y0 + box.minY };
  });
}

/* ------------------------------------------------------------------ *
 * Text regions
 * ------------------------------------------------------------------ */

function lum(data: Uint8ClampedArray, p: number): number {
  return 0.299 * data[p] + 0.587 * data[p + 1] + 0.114 * data[p + 2];
}

/**
 * White text in a rectangle, as glyphs. The threshold sits halfway between the background
 * (the median) and the text (the brightest pixels); a region with no clearly brighter ink
 * returns nothing rather than a guess.
 */
export function textGlyphs(
  image: RasterImage,
  rect: { x0: number; y0: number; x1: number; y1: number },
  minContrast = 45
): BinaryGlyph[] {
  const x0 = Math.max(0, Math.floor(rect.x0));
  const y0 = Math.max(0, Math.floor(rect.y0));
  const x1 = Math.min(image.width, Math.ceil(rect.x1));
  const y1 = Math.min(image.height, Math.ceil(rect.y1));
  const w = x1 - x0;
  const h = y1 - y0;
  if (w < 4 || h < 4) return [];

  const values = new Float32Array(w * h);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) values[y * w + x] = lum(image.data, ((y0 + y) * image.width + x0 + x) * 4);
  }
  const sorted = Array.from(values).sort((a, b) => a - b);
  const background = sorted[sorted.length >> 1];
  const peak = sorted[Math.floor(sorted.length * 0.995)];
  if (peak - background < minContrast) return [];
  const threshold = background + (peak - background) * 0.55;

  const mask = new Uint8Array(w * h);
  for (let i = 0; i < mask.length; i++) mask[i] = values[i] >= threshold ? 1 : 0;

  const glyphs = segmentGlyphs(mask, w, h, x0, y0);
  if (glyphs.length === 0) return [];
  // Drop specks: real characters are at least half the height of the tallest one.
  const tallest = Math.max(...glyphs.map((g) => g.height));
  return glyphs.filter((g) => g.height >= tallest * 0.5);
}

/** Glyphs split into words at gaps wider than ~half a character height. */
export function groupWords(glyphs: BinaryGlyph[]): BinaryGlyph[][] {
  if (glyphs.length === 0) return [];
  const height = Math.max(...glyphs.map((g) => g.height));
  const words: BinaryGlyph[][] = [[glyphs[0]]];
  for (let i = 1; i < glyphs.length; i++) {
    const prev = glyphs[i - 1];
    const gap = glyphs[i].x0 - (prev.x0 + prev.width);
    if (gap > height * 0.42) words.push([glyphs[i]]);
    else words[words.length - 1].push(glyphs[i]);
  }
  return words;
}

const DIGITS = '0123456789';

/** A run of digit glyphs as a number, or null when any of them is unconvincing. */
export function readDigits(glyphs: BinaryGlyph[], minScore = 0.6): number | null {
  if (glyphs.length === 0 || glyphs.length > 4) return null;
  let text = '';
  for (const glyph of glyphs) {
    const match = classifyGlyph(glyph, DIGITS);
    if (!match || match.score < minScore) return null;
    text += match.char;
  }
  return Number(text);
}

// English units by first letter, and the Russian client's: СЕКУНД, МИНУТ, ЧАС, ДНЕЙ.
const UNIT_SECONDS: Record<string, number> = {
  S: 1,
  M: 60,
  H: 3600,
  D: 86400,
  'С': 1,
  'М': 60,
  'Ч': 3600,
  'Д': 86400
};
const UNIT_LETTERS = Object.keys(UNIT_SECONDS).join('');

/**
 * "0 SECONDS", "12 MINUTES", "1 HOUR 5 MINUTES" ... as seconds. Each unit word is known from
 * its first letter alone, which is all that needs reading.
 */
export function readAgeSeconds(glyphs: BinaryGlyph[]): number | null {
  const words = groupWords(glyphs);
  let total = 0;
  let pending: number | null = null;
  let parts = 0;
  for (const word of words) {
    const first = classifyGlyph(word[0], DIGITS + UNIT_LETTERS);
    if (!first) return null;
    if (DIGITS.includes(first.char)) {
      if (pending !== null) return null;
      pending = readDigits(word);
      if (pending === null) return null;
    } else {
      if (pending === null) return null;
      total += pending * UNIT_SECONDS[first.char];
      pending = null;
      parts++;
    }
  }
  return parts > 0 && pending === null ? total : null;
}

/** "100%" -> 1, "99%" -> 0.99. The % sign is optional, in case it falls outside the crop. */
export function readPercent(glyphs: BinaryGlyph[]): number | null {
  if (glyphs.length === 0) return null;
  const last = classifyGlyph(glyphs[glyphs.length - 1], DIGITS + '%');
  const digits = last && last.char === '%' ? glyphs.slice(0, -1) : glyphs;
  const value = readDigits(digits);
  return value === null || value > 100 ? null : value / 100;
}

const LATIN_UPPER = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const CYRILLIC_UPPER = 'АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ';
const CYRILLIC_LOWER = 'абвгдеёжзийклмнопрстуфхцчшщъыьэюя';

/**
 * Latin lowercase grouped by how a letter sits on the line. Shape alone confuses i/j and
 * f/t at a dozen pixels; whether a letter rises to the capital height, or drops below the
 * baseline, does not.
 */
const LATIN_LOWER_BY_PLACE = {
  plain: 'acemnorstuvwxz',
  tall: 'bdfhkli',
  descender: 'gpqy',
  tallDescender: 'j'
};

export interface NameRead {
  text: string;
  /** Weakest letter's match score; low means at least one letter is a guess. */
  score: number;
}

function readInScript(glyphs: BinaryGlyph[], cyrillic: boolean): NameRead | null {
  const capital = glyphs[0];
  const baseline = [...glyphs.slice(1).map((g) => g.y0 + g.height)].sort((x, y) => x - y)[Math.floor((glyphs.length - 1) / 2)];
  const capTop = capital.y0;
  const capHeight = Math.max(1, baseline - capTop);
  let text = '';
  let total = 0;
  let worst = Infinity;
  for (let i = 0; i < glyphs.length; i++) {
    const g = glyphs[i];
    let allowed: string;
    if (i === 0) {
      allowed = cyrillic ? CYRILLIC_UPPER : LATIN_UPPER;
    } else if (cyrillic || capHeight < 12) {
      // Too small to judge placement (or a script without the table): shape only.
      allowed = cyrillic ? CYRILLIC_LOWER : Object.values(LATIN_LOWER_BY_PLACE).join('');
    } else {
      const tall = (baseline - g.y0) / capHeight >= 0.9;
      const descends = (g.y0 + g.height - baseline) / capHeight > 0.15;
      allowed = tall
        ? descends
          ? LATIN_LOWER_BY_PLACE.tallDescender
          : LATIN_LOWER_BY_PLACE.tall
        : descends
          ? LATIN_LOWER_BY_PLACE.descender
          : LATIN_LOWER_BY_PLACE.plain;
    }
    const match = classifyGlyph(g, allowed);
    if (!match) return null;
    text += match.char;
    total += match.score;
    worst = Math.min(worst, match.score);
  }
  return { text, score: Math.min(worst, total / glyphs.length) };
}

/**
 * An animal's name. The game writes it capitalised ("Naomi"), so the first letter is read
 * against capitals and the rest against lowercase. Both Latin and Cyrillic are tried, for
 * players on a Russian client, and the better-fitting reading wins.
 */
export function readName(glyphs: BinaryGlyph[]): NameRead | null {
  if (glyphs.length < 2 || glyphs.length > 16) return null;
  const latin = readInScript(glyphs, false);
  const cyrillic = readInScript(glyphs, true);
  const best = !cyrillic || (latin && latin.score >= cyrillic.score - 0.02) ? latin : cyrillic;
  return best && best.score >= 0.45 ? best : null;
}
