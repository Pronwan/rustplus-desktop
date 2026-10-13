import type { RasterImage, ScannerRegionType } from './scannerTypes.ts';
import { BinaryGlyph, classifyGlyph, groupWords, textGlyphs } from '../livestock/panelText.ts';

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

interface NameBand {
  x0: number;
  x1: number;
  y0: number;
  y1: number;
}

export const PLANT_NAME_BANDS: Record<ScannerRegionType, NameBand> = {
  // Starts right of the plant icon (which ends ~1.2 pitches left) and stops at the panel edge.
  planter: { x0: -0.95, x1: 5.7, y0: -1.75, y1: -0.85 },
  // Stops above the description box, whose sentence also names the plant in mixed case.
  inventory: { x0: -5.6, x1: 14, y0: -7.85, y1: -6.2 }
};

/** The image rectangle to read the plant name from, in pixels. */
export function plantNameRect(row: RowGeometry, type: ScannerRegionType) {
  const band = PLANT_NAME_BANDS[type];
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

export interface PlantNameRead {
  plant: string;
  /** The words as read, for diagnostics. */
  text: string;
  /** Weakest letter's template score. */
  score: number;
}

/** Reads the plant named above a gene row, or null when the title is missing or unclear. */
export function readPlantName(image: RasterImage, row: RowGeometry, type: ScannerRegionType): PlantNameRead | null {
  if (!(row.pitch > 4)) return null;
  const glyphs = textGlyphs(image, plantNameRect(row, type));
  if (glyphs.length < 3 || glyphs.length > 40) return null;

  const words: string[] = [];
  let worst = Infinity;
  for (const word of groupWords(glyphs)) {
    let text = '';
    for (const glyph of word.flatMap(splitRunTogether)) {
      const match = classifyGlyph(glyph, UPPER);
      if (!match) continue;
      text += match.char;
      worst = Math.min(worst, match.score);
    }
    if (text) words.push(text);
  }

  const plant = plantFromTitle(words);
  return plant ? { plant, text: words.join(' '), score: worst } : null;
}
