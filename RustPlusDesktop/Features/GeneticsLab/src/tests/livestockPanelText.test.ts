import { describe, it, expect } from 'vitest';
import {
  BinaryGlyph,
  classifyGlyph,
  readAgeSeconds,
  readDigits,
  readName,
  readPercent
} from '../services/livestock/panelText.ts';
import { GLYPH_GRID_HEIGHT, GLYPH_GRID_WIDTH, GLYPH_TEMPLATES } from '../services/livestock/glyphTemplateData.ts';
import { readLivestockPanel } from '../services/livestock/livestockPanelReader.ts';
import { readMarkerDigit } from '../services/livestock/markerDigit.ts';
import { readPanelConditions, readPanelName } from '../services/livestock/panelConditions.ts';
import { classifyPortrait, portraitSex } from '../services/livestock/portraitClassifier.ts';
import { createAnimal, decodeGeneRow, namesMatch } from '../domain/livestock/animal.ts';
import { currentCondition, estimateAnimalPrice } from '../domain/livestock/pricing.ts';
import { RasterImage } from '../services/scanner/scannerTypes.ts';

/** A character drawn from its own reference grid at `height` pixels, placed at `x0`. */
function glyphOf(char: string, height: number, x0: number, y0 = 0): BinaryGlyph {
  const t = GLYPH_TEMPLATES.find((g) => g.char === char)!;
  const width = Math.max(2, Math.round(height * t.aspect));
  const mask = new Uint8Array(width * height);
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const gx = Math.min(GLYPH_GRID_WIDTH - 1, Math.floor((x / width) * GLYPH_GRID_WIDTH));
      const gy = Math.min(GLYPH_GRID_HEIGHT - 1, Math.floor((y / height) * GLYPH_GRID_HEIGHT));
      mask[y * width + x] = Number(t.grid[gy * GLYPH_GRID_WIDTH + gx]) >= 5 ? 1 : 0;
    }
  }
  return { mask, width, height, x0, y0 };
}

const TALL = 'bdfhklБб';
const DESCENDERS = 'gpqyjдрруфцщ';

/**
 * Text as the game draws it: capitals and digits full height, lowercase at x-height on the
 * same baseline, ascenders up to capital height, descenders below the baseline.
 */
function placed(text: string, cap = 20): BinaryGlyph[] {
  const glyphs: BinaryGlyph[] = [];
  let x = 0;
  for (const ch of text) {
    if (ch === ' ') {
      x += cap;
      continue;
    }
    const lower = ch !== ch.toUpperCase();
    const tall = TALL.includes(ch) || ch === 'i' || ch === 'j' || !lower;
    const descends = DESCENDERS.includes(ch);
    const top = tall ? 0 : ch === 't' ? Math.round(cap * 0.2) : Math.round(cap * 0.3);
    const bottom = descends ? Math.round(cap * 1.25) : cap;
    const glyph = glyphOf(ch, bottom - top, x, top);
    glyphs.push(glyph);
    x += glyph.width + Math.round(cap * 0.12);
  }
  return glyphs;
}

/** A line of text as glyphs; spaces become word gaps. Only the first letter of a word matters. */
function line(text: string, height = 14): BinaryGlyph[] {
  const glyphs: BinaryGlyph[] = [];
  let x = 0;
  for (const ch of text) {
    if (ch === ' ') {
      x += height;
      continue;
    }
    // Unit words are spelled with S/M/H/D stand-ins: the reader only looks at the first.
    const glyph = glyphOf(GLYPH_TEMPLATES.some((t) => t.char === ch) ? ch : 'S', height, x);
    glyphs.push(glyph);
    x += glyph.width + Math.round(height * 0.15);
  }
  return glyphs;
}

describe('panel glyph reading', () => {
  it('recognises every reference character within the set it is read against', () => {
    // The reader never compares across sets: digits, Latin capitals, Latin lowercase,
    // Cyrillic capitals and Cyrillic lowercase are each read on their own.
    const sets = [
      '0123456789%',
      'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
      'abcdefghijklmnopqrstuvwxyz',
      'АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ',
      'абвгдеёжзийклмнопрстуфхцчшщъыьэюя'
    ];
    for (const set of sets) {
      for (const ch of set) {
        if (ch === 'Ё' || ch === 'ё') continue; // differs from Е/е only by dots that merge at this size
        expect(classifyGlyph(glyphOf(ch, 14, 0), set)?.char, `char ${ch}`).toBe(ch);
      }
    }
  });

  it('reads multi-digit numbers', () => {
    expect(readDigits(line('14'))).toBe(14);
    expect(readDigits(line('10'))).toBe(10);
    expect(readDigits(line('2'))).toBe(2);
  });

  it('reads ages in every unit and combination', () => {
    expect(readAgeSeconds(line('0 SECONDS'))).toBe(0);
    expect(readAgeSeconds(line('45 SECONDS'))).toBe(45);
    expect(readAgeSeconds(line('12 MINUTES'))).toBe(720);
    expect(readAgeSeconds(line('1 HOUR'))).toBe(3600);
    expect(readAgeSeconds(line('3 HOURS 20 MINUTES'))).toBe(12000);
    expect(readAgeSeconds(line('2 DAYS'))).toBe(172800);
  });

  it('refuses an age with a number and no unit', () => {
    expect(readAgeSeconds(line('12'))).toBeNull();
  });

  it('reads condition percentages', () => {
    expect(readPercent(line('100%'))).toBe(1);
    expect(readPercent(line('99%'))).toBe(0.99);
    expect(readPercent(line('7%'))).toBe(0.07);
  });
});

async function loadFixture(name: string, width: number, height: number): Promise<RasterImage> {
  const fsName = 'node:fs';
  const zlibName = 'node:zlib';
  const { readFileSync } = await import(fsName);
  const { gunzipSync } = await import(zlibName);
  const raw: Uint8Array = gunzipSync(readFileSync(new URL(`./fixtures/${name}.rgba.gz`, import.meta.url)));
  return { data: new Uint8ClampedArray(raw), width, height };
}

describe('live panel, real capture', () => {
  it('reads genes, marker number, age and overall condition from one frame', async () => {
    // Desiree at UI scale 0.7: D red, L red, Y green, F neutral, H neutral, 6 on purple,
    // AGE "0 SECONDS", OVERALL "100%".
    const image = await loadFixture('livestock-desiree-panel-ui07', 355, 350);
    const read = readLivestockPanel(image, { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(read!.rows[0].levels).toEqual(['low', 'low', 'high', 'mid', 'mid']);
    expect(read!.rows[0].marker).toEqual({ value: 6, color: 'purple' });
    expect(readPanelConditions(image, read!)).toEqual({ ageSeconds: 0, overall: 1 });
  });
});

describe('age and condition in the sale estimate', () => {
  const row = decodeGeneRow('nnnnn|6v')!;

  it('counts age on from the reading and spots babies', () => {
    const at = 1_000_000;
    const calf = createAnimal({ rows: [row], observed: { ageSeconds: 600, overall: 1, at } });
    expect(currentCondition(calf, at + 60_000).ageSeconds).toBe(660);
    expect(currentCondition(calf, at).isBaby).toBe(true);
    expect(currentCondition(calf, at + 3_600_000).isBaby).toBe(false);
  });

  it('prices from the reading: old and run-down sells for less', () => {
    const at = 1_000_000;
    const fresh = createAnimal({ rows: [row], observed: { ageSeconds: 3600, overall: 1, at } });
    // 48h lifespan at an Ok gene: 36h lived is 75% -> age factor 0.75; 47.5% overall -> 0.7.
    const old = createAnimal({ rows: [row], observed: { ageSeconds: 36 * 3600, overall: 0.475, at } });
    expect(estimateAnimalPrice(fresh, {}, at).price).toBe(50);
    expect(estimateAnimalPrice(old, {}, at).exact).toBeCloseTo(50 * 0.75 * 0.7);
  });

  it('lets explicit what-if values override the reading', () => {
    const at = 1_000_000;
    const old = createAnimal({ rows: [row], observed: { ageSeconds: 36 * 3600, overall: 0.475, at } });
    expect(estimateAnimalPrice(old, { healthState: 1, ageLived: 0 }, at).price).toBe(50);
  });
});

describe('portrait: which animal the panel belongs to', () => {
  it('recognises a cow from the panel header', async () => {
    const image = await loadFixture('livestock-desiree-panel-ui07', 355, 350);
    const read = readLivestockPanel(image, { readMarkerDigit })!;
    const match = classifyPortrait(image, read);
    expect(match?.species).toBe('cattle');
    // Its own capture is among the references, so the kind is certain here.
    expect(match?.kind).toBe('cow');
  });

  it('recognises a bull from the panel header', async () => {
    const image = await loadFixture('livestock-nelson-header-ui10', 505, 180);
    const read = readLivestockPanel(image, { readMarkerDigit })!;
    expect(read).not.toBeNull();
    const match = classifyPortrait(image, read);
    expect(match?.kind).toBe('bull');
    expect(portraitSex(match!.kind!)).toBe('male');
  });

  it('gives no answer when the header is cut off', async () => {
    // The gene-bar crop has no header above the badges.
    const image = await loadFixture('livestock-desiree-ui10', 495, 85);
    const read = readLivestockPanel(image, { readMarkerDigit })!;
    expect(classifyPortrait(image, read)).toBeNull();
  });
});

describe('animal names', () => {
  it('reads names from real panels', async () => {
    const cases: Array<[string, number, number, string]> = [
      ['livestock-desiree-panel-ui07', 355, 350, 'Desiree'],
      ['livestock-nelson-header-ui10', 505, 180, 'Nelson'],
      ['livestock-naomi-header-ui10', 497, 160, 'Naomi']
    ];
    for (const [fixture, w, h, name] of cases) {
      const image = await loadFixture(fixture, w, h);
      const read = readLivestockPanel(image, { readMarkerDigit })!;
      expect(read, fixture).not.toBeNull();
      expect(readPanelName(image, read)?.text, fixture).toBe(name);
    }
  });

  it('keeps English names English and Russian names Russian', () => {
    for (const name of ['Naomi', 'Winifred', 'Elisabeth', 'Marshall', 'Dustin', 'Kathleen', 'Miriam', 'Nelson']) {
      expect(readName(placed(name))?.text, name).toBe(name);
    }
    for (const name of ['Наоми', 'Борис', 'Людмила', 'Зоя', 'Фёкла', 'Мирон', 'Олег']) {
      const read = readName(placed(name))?.text;
      expect(/^[А-яЁё]+$/.test(read ?? ''), `${name} read as ${read}`).toBe(true);
      expect(namesMatch(read ?? '', name), `${name} read as ${read}`).toBe(true);
    }
  });

  it('reads Russian age units', () => {
    expect(readAgeSeconds(line('12 МИНУТ'))).toBe(720);
    expect(readAgeSeconds(line('2 ЧАСА'))).toBe(7200);
  });

  it('matches the same animal across small misreads, scripts and case', () => {
    expect(namesMatch('Winifred', 'Winitred')).toBe(true);
    expect(namesMatch('Naomi', 'Naomj')).toBe(true);
    expect(namesMatch('Наоми', 'Haomi')).toBe(true);
    expect(namesMatch('Nelson', 'nelson')).toBe(true);
    expect(namesMatch('Nelson', 'Dustin')).toBe(false);
    // Short names must match exactly: one letter is a different animal.
    expect(namesMatch('Ada', 'Ida')).toBe(false);
  });
});
