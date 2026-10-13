import type { RasterImage, ScannerRegionType } from './scannerTypes.ts';
import { BinaryGlyph, classifyGlyph, groupWords, segmentGlyphs } from '../livestock/panelText.ts';

/**
 * Which plant a scanned gene row belongs to, read from the panel's own title.
 *
 * Both panels that show genes name the plant in capitals just above them: the planter view's
 * header ("HEMP", next to the plant icon) and the inventory tooltip's title ("GREEN BERRY
 * CLONE"). The name is read with the same glyph templates as the livestock panel (Rust's UI
 * font), so no OCR engine is involved.
 *
 * Where to look is measured in gene pitches (the distance between neighbouring gene slots)
 * from the first slot's centre and the row's centre line. In-game UI scale resizes the whole
 * panel, so these ratios hold at every scale and resolution; the search bands are wider than
 * the text to absorb capture scaling. Measured on captures of both panels:
 *
 *   planter header   x -0.76 .. +0.12 (HEMP), y -1.41 .. -1.15, letters 0.26 pitch tall
 *   inventory title  x -5.26 .. +3.28 (GREEN BERRY CLONE), y -7.34 .. -6.66, letters 0.68 tall
 */

export interface RowGeometry {
  /** First slot's centre, in image pixels. */
  x: number;
  /** The gene row's centre line, in image pixels. */
  y: number;
  /** Distance between neighbouring slot centres, in pixels. */
  pitch: number;
}

export interface NameBand {
  x0: number;
  x1: number;
  y0: number;
  y1: number;
  /** Capital-letter height, in pitches. */
  capHeight: number;
}

export const PLANT_NAME_BANDS: Record<ScannerRegionType, NameBand> = {
  // Starts right of the plant icon (which ends ~1.2 pitches left). Ends at 4.8, enough for
  // "YELLOW BERRY" (~4.1 pitches) and short of the panel's edge at ~5.6: past it the band
  // would see whatever terrain is behind the panel, often brighter than the text.
  planter: { x0: -0.95, x1: 4.8, y0: -1.7, y1: -0.95, capHeight: 0.27 },
  // Stops above the description box, whose sentence also names the plant in mixed case.
  inventory: { x0: -5.6, x1: 6.5, y0: -7.85, y1: -6.2, capHeight: 0.68 }
};

const shifted = (band: NameBand, dy: number, dx0 = 0): NameBand => ({ ...band, x0: band.x0 + dx0, y0: band.y0 + dy, y1: band.y1 + dy });

/**
 * Where else to look when the measured band reads nothing, best guess first. In-game UI
 * scale does not resize every part of a panel by the same factor, so the title can sit a
 * little higher or lower relative to the genes than in the captures the bands were measured
 * on; the inventory tooltip's title can also start further left. Only a known plant name
 * counts as a hit, so looking more widely cannot invent a plant.
 */
export const PLANT_NAME_SEARCH: Record<ScannerRegionType, NameBand[]> = {
  planter: [0, -0.3, 0.3, -0.6, 0.6].map((dy) => shifted(PLANT_NAME_BANDS.planter, dy)),
  inventory: [0, -0.6, 0.6, -1.2, 1.2, -1.8, 1.8].flatMap((dy) => [
    shifted(PLANT_NAME_BANDS.inventory, dy),
    shifted(PLANT_NAME_BANDS.inventory, dy, -2)
  ])
};

/** The image rectangle to read the plant name from, in pixels. */
export function plantNameRect(row: RowGeometry, type: ScannerRegionType, band: NameBand = PLANT_NAME_BANDS[type]) {
  return {
    x0: row.x + band.x0 * row.pitch,
    x1: row.x + band.x1 * row.pitch,
    y0: row.y + band.y0 * row.pitch,
    y1: row.y + band.y1 * row.pitch
  };
}

const UPPER = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';

const SINGLE_WORD_PLANTS: Record<string, string> = {
  HEMP: 'hemp',
  CORN: 'corn',
  POTATO: 'potato',
  PUMPKIN: 'pumpkin',
  WHEAT: 'wheat',
  SUNFLOWER: 'sunflower',
  ROSE: 'rose',
  ORCHID: 'orchid'
};

// Black berries exist in game but not in Genetics Lab, so they are recognised (and ignored)
// rather than misread as another colour.
const BERRY_COLOURS: Record<string, string | null> = {
  RED: 'red-berry',
  BLUE: 'blue-berry',
  YELLOW: 'yellow-berry',
  GREEN: 'green-berry',
  WHITE: 'white-berry',
  BLACK: null
};

// Capitals that small or soft text turns into each other. Swapping one for its look-alike
// costs half an edit, so "BEPPY" is still BERRY while "BEADY" is not.
const LOOK_ALIKE = ['PR', 'OD', 'OQ', 'DQ', 'CG', 'CO', 'EF', 'IL', 'IJ', 'IT', 'HN', 'MN', 'UV', 'VY', 'BE', 'KX'];
const lookAlike = new Set(LOOK_ALIKE.flatMap(([a, b]) => [a + b, b + a]));

function editDistance(a: string, b: string): number {
  const row = Array.from({ length: b.length + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    let previous = row[0];
    row[0] = i;
    for (let j = 1; j <= b.length; j++) {
      const current = row[j];
      const swap = a[i - 1] === b[j - 1] ? 0 : lookAlike.has(a[i - 1] + b[j - 1]) ? 0.5 : 1;
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, previous + swap);
      previous = current;
    }
  }
  return row[b.length];
}

/** Edits a word may be off by: one in longer words, only a look-alike in 4-letter ones. */
const allowedEdits = (word: string) => (word.length >= 5 ? 1 : word.length === 4 ? 0.5 : 0);

/** Where `word` occurs in `text` (allowing misreads): the best-matching window's start, or -1. */
function find(text: string, word: string, from = 0): number {
  let best = -1;
  let bestDistance = Infinity;
  for (let start = from; start + word.length <= text.length; start++) {
    const distance = editDistance(text.slice(start, start + word.length), word);
    if (distance < bestDistance) {
      best = start;
      bestDistance = distance;
    }
  }
  return bestDistance <= allowedEdits(word) ? best : -1;
}

/**
 * The plant a title names ("GREEN BERRY CLONE" -> "green-berry"), or null. Spaces are
 * ignored: narrow word gaps run words together ("BERRYCLONE") and wide letter gaps split
 * them, so the names are looked for anywhere in the run of letters.
 */
export function plantFromTitle(words: string[]): string | null {
  const text = words.join('').toUpperCase().replace(/[^A-Z]/g, '');
  if (text.length < 3) return null;

  const berryAt = find(text, 'BERRY');
  if (berryAt >= 0) {
    // The colour is the word right before BERRY.
    const before = text.slice(0, berryAt);
    let colour: string | null = null;
    let bestDistance = Infinity;
    for (const name of Object.keys(BERRY_COLOURS)) {
      if (before.length < name.length) continue;
      const distance = editDistance(before.slice(before.length - name.length), name);
      if (distance <= allowedEdits(name) && distance < bestDistance) {
        colour = name;
        bestDistance = distance;
      }
    }
    return colour ? BERRY_COLOURS[colour] : null;
  }

  for (const [name, plant] of Object.entries(SINGLE_WORD_PLANTS)) {
    if (find(text, name) >= 0) return plant;
  }
  return null;
}

/**
 * Capitals are at most about as wide as they are tall (M and W included). A glyph much wider
 * than that is two letters run together -- small UI scales and soft captures close the gap
 * between, say, E and M -- so it is cut at its thinnest column near the middle.
 */
function splitRunTogether(glyph: BinaryGlyph): BinaryGlyph[] {
  if (glyph.width <= glyph.height * 1.2 || glyph.width < 6) return [glyph];
  const { mask, width, height } = glyph;
  let cut = -1;
  let least = Infinity;
  for (let x = Math.floor(width * 0.3); x <= Math.ceil(width * 0.7); x++) {
    let ink = 0;
    for (let y = 0; y < height; y++) ink += mask[y * width + x];
    if (ink < least) {
      least = ink;
      cut = x;
    }
  }
  if (cut <= 0 || cut >= width - 1) return [glyph];
  const part = (from: number, to: number): BinaryGlyph => {
    const w = to - from;
    const sub = new Uint8Array(w * height);
    for (let y = 0; y < height; y++) for (let x = 0; x < w; x++) sub[y * w + x] = mask[y * width + from + x];
    return { mask: sub, width: w, height, x0: glyph.x0 + from, y0: glyph.y0 };
  };
  return [...splitRunTogether(part(0, cut)), ...splitRunTogether(part(cut + 1, width))];
}

/**
 * The title's letters in a band. The panels are translucent, so the band also holds whatever
 * the game draws behind them -- sand, grass, sky, a lit wall -- which can be as bright as the
 * text. Ink is therefore only near-white, unsaturated pixels clearly brighter than the band's
 * typical level, and a glyph has to be about as tall as a capital at this UI scale and sit on
 * the line the others share.
 */
function nameGlyphs(image: RasterImage, rect: { x0: number; y0: number; x1: number; y1: number }, capPx: number): BinaryGlyph[] {
  const x0 = Math.max(0, Math.floor(rect.x0));
  const y0 = Math.max(0, Math.floor(rect.y0));
  const x1 = Math.min(image.width, Math.ceil(rect.x1));
  const y1 = Math.min(image.height, Math.ceil(rect.y1));
  const w = x1 - x0;
  const h = y1 - y0;
  if (w < 8 || h < 6) return [];

  const lum = new Float32Array(w * h);
  const neutral = new Uint8Array(w * h);
  const neutralLums: number[] = [];
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const p = ((y0 + y) * image.width + x0 + x) * 4;
      const r = image.data[p];
      const g = image.data[p + 1];
      const b = image.data[p + 2];
      const i = y * w + x;
      lum[i] = 0.299 * r + 0.587 * g + 0.114 * b;
      if (Math.max(r, g, b) - Math.min(r, g, b) <= 48) {
        neutral[i] = 1;
        neutralLums.push(lum[i]);
      }
    }
  }
  if (neutralLums.length < 20) return [];
  const sortedAll = Array.from(lum).sort((a, b) => a - b);
  const background = sortedAll[sortedAll.length >> 1];
  neutralLums.sort((a, b) => a - b);
  const peak = neutralLums[Math.floor(neutralLums.length * 0.995)];
  if (peak - background < 40) return [];
  const threshold = background + (peak - background) * 0.55;

  const mask = new Uint8Array(w * h);
  for (let i = 0; i < mask.length; i++) mask[i] = neutral[i] && lum[i] >= threshold ? 1 : 0;

  const glyphs = segmentGlyphs(mask, w, h, x0, y0).filter(
    (g) => g.height >= capPx * 0.55 && g.height <= capPx * 1.8 && g.width <= capPx * 3
  );
  if (glyphs.length === 0) return [];
  // Keep the line most glyphs share; stray blobs above or below it are not letters.
  const centres = glyphs.map((g) => g.y0 + g.height / 2).sort((a, b) => a - b);
  const line = centres[centres.length >> 1];
  return glyphs.filter((g) => Math.abs(g.y0 + g.height / 2 - line) <= capPx * 0.45).sort((a, b) => a.x0 - b.x0);
}

/**
 * R and P share a bowl; only R has the leg in the lower right. At small UI scales the
 * template match calls most R's P ("BEPPY"), so a P with clear ink where the leg goes is an R.
 */
function settlePR(glyph: BinaryGlyph, char: string): string {
  if (char !== 'P' && char !== 'R') return char;
  const { mask, width, height } = glyph;
  let ink = 0;
  let area = 0;
  for (let y = Math.floor(height * 0.72); y < height; y++) {
    for (let x = Math.floor(width * 0.55); x < width; x++) {
      ink += mask[y * width + x];
      area++;
    }
  }
  return area > 0 && ink / area > 0.15 ? 'R' : 'P';
}

export interface PlantNameRead {
  plant: string;
  /** The words as read, for diagnostics. */
  text: string;
  /** Weakest letter's template score. */
  score: number;
}

/** What the title band holds as read, plant or not; for diagnostics. */
export interface PlantTitleRead {
  /** The words as read; empty when no letters were found. */
  text: string;
  /** The plant the words name, or null. */
  plant: string | null;
  /** Letter-height blobs found in the band. */
  glyphCount: number;
  /** Weakest letter's template score. */
  score: number;
}

/** Reads the title band above a gene row, whatever it says. */
export function readPlantTitle(
  image: RasterImage,
  row: RowGeometry,
  type: ScannerRegionType,
  band: NameBand = PLANT_NAME_BANDS[type]
): PlantTitleRead {
  const none = (glyphCount: number): PlantTitleRead => ({ text: '', plant: null, glyphCount, score: 0 });
  if (!(row.pitch > 4)) return none(0);
  const glyphs = nameGlyphs(image, plantNameRect(row, type, band), band.capHeight * row.pitch);
  if (glyphs.length < 3 || glyphs.length > 40) return none(glyphs.length);

  const words: string[] = [];
  let worst = Infinity;
  for (const word of groupWords(glyphs)) {
    let text = '';
    for (const glyph of word.flatMap(splitRunTogether)) {
      const match = classifyGlyph(glyph, UPPER);
      if (!match) continue;
      text += settlePR(glyph, match.char);
      worst = Math.min(worst, match.score);
    }
    if (text) words.push(text);
  }

  return { text: words.join(' '), plant: plantFromTitle(words), glyphCount: glyphs.length, score: worst === Infinity ? 0 : worst };
}

/**
 * Reads the title, trying the search bands in turn until one names a plant. Returns the hit,
 * or the read that found the most letters, with the band it came from.
 */
export function searchPlantTitle(
  image: RasterImage,
  row: RowGeometry,
  type: ScannerRegionType
): { read: PlantTitleRead; band: NameBand } {
  let best: { read: PlantTitleRead; band: NameBand } | null = null;
  for (const band of PLANT_NAME_SEARCH[type]) {
    const read = readPlantTitle(image, row, type, band);
    if (read.plant) return { read, band };
    if (!best || read.text.length > best.read.text.length) best = { read, band };
  }
  return best!;
}

/** Reads the plant named above a gene row, or null when the title is missing or unclear. */
export function readPlantName(image: RasterImage, row: RowGeometry, type: ScannerRegionType): PlantNameRead | null {
  const read = readPlantTitle(image, row, type);
  return read.plant ? { plant: read.plant, text: read.text, score: read.score } : null;
}
