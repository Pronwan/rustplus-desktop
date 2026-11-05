import { describe, it, expect } from 'vitest';
import { locateBadgeColumns } from '../services/scanner/vision/badgeColumns.ts';
import { readDesktopGeneRow } from '../services/scanner/vision/desktopRowReader.ts';
import { rasterizeGlyph, GeneLetter } from '../services/scanner/vision/glyphTemplates.ts';
import { DesktopTemplateRecognizer } from '../services/scanner/DesktopTemplateRecognizer.ts';
import { SCANNER_CONFIG } from '../services/scanner/scannerConfig.ts';

/* ------------------------------------------------------------------ *
 * A stand-in for a Rust genetics tooltip
 *
 * Rendered at 4x and box-filtered down, so every fixture carries the antialiasing a real
 * capture has. That is the part that matters: at a 0.5 UI scale a stroke is one blended
 * pixel, and a pipeline that only ever sees hard edges will pass tests and fail on a screen.
 * ------------------------------------------------------------------ */

const SUPERSAMPLE = 4;

const BADGE_GREEN: [number, number, number] = [101, 154, 43]; // #659A2B
const BADGE_RED: [number, number, number] = [180, 68, 55]; // #B44437
const PANEL: [number, number, number] = [26, 26, 26];
const LETTER: [number, number, number] = [242, 242, 242];
const DASH: [number, number, number] = [138, 138, 138];

const GREEN_SET = new Set(['G', 'Y', 'H']);

interface RenderOptions {
  /** Badge diameter in final (not supersampled) pixels. */
  badgeDiameter?: number;
  /** Centre-to-centre spacing as a multiple of the badge diameter. */
  pitchFactor?: number;
  /** Letter height as a fraction of the badge diameter. */
  letterScale?: number;
  /** Letter width as a fraction of its height. Real type is not square. */
  letterAspect?: number;
  /** Vertical padding above and below the badges. Negative crops into them. */
  padY?: number;
  /** Horizontal padding before the first badge and after the last. */
  padX?: number;
  /** Sub-pixel shift of the whole row, in final pixels. */
  offsetX?: number;
  offsetY?: number;
  /** Panel colour behind the badges. */
  background?: [number, number, number];
  /** Draw the separator dashes Rust puts between the badges. */
  dashes?: boolean;
  /** Thicken (positive) or thin (negative) the strokes, in supersampled pixels. */
  strokeBias?: number;
}

interface Fixture {
  data: Uint8ClampedArray;
  width: number;
  height: number;
  /** The geometry a correctly calibrated region would have supplied. */
  hint: { geneWidthPx: number; gapWidthPx: number };
}

function renderRow(genes: string, options: RenderOptions = {}): Fixture {
  const {
    badgeDiameter = 20,
    pitchFactor = 1.45,
    letterScale = 0.56,
    letterAspect = 0.82,
    padY = 3,
    padX = 4,
    offsetX = 0,
    offsetY = 0,
    background = PANEL,
    dashes = true,
    strokeBias = 0
  } = options;

  const pitch = badgeDiameter * pitchFactor;
  const width = Math.round(padX * 2 + pitch * 5 + badgeDiameter);
  const height = Math.round(badgeDiameter + padY * 2);

  const sw = width * SUPERSAMPLE;
  const sh = height * SUPERSAMPLE;
  const buffer = new Float64Array(sw * sh * 3);

  const paint = (x: number, y: number, color: [number, number, number]) => {
    if (x < 0 || y < 0 || x >= sw || y >= sh) return;
    const i = (y * sw + x) * 3;
    buffer[i] = color[0];
    buffer[i + 1] = color[1];
    buffer[i + 2] = color[2];
  };

  for (let y = 0; y < sh; y++) for (let x = 0; x < sw; x++) paint(x, y, background);

  const radius = (badgeDiameter / 2) * SUPERSAMPLE;
  const centerY = (height / 2 + offsetY) * SUPERSAMPLE;
  const centers = genes.split('').map((_, i) => (padX + badgeDiameter / 2 + i * pitch + offsetX) * SUPERSAMPLE);

  if (dashes) {
    const thickness = Math.max(1, Math.round(0.1 * badgeDiameter * SUPERSAMPLE));
    for (let i = 1; i < centers.length; i++) {
      const from = Math.round(centers[i - 1] + radius * 1.2);
      const to = Math.round(centers[i] - radius * 1.2);
      for (let y = Math.round(centerY - thickness / 2); y < Math.round(centerY + thickness / 2); y++) {
        for (let x = from; x < to; x++) paint(x, y, DASH);
      }
    }
  }

  const templateSize = 96;
  const letterH = letterScale * badgeDiameter * SUPERSAMPLE;
  const letterW = letterH * letterAspect;

  genes.split('').forEach((gene, slot) => {
    const badge = GREEN_SET.has(gene) ? BADGE_GREEN : BADGE_RED;
    const cx = centers[slot];

    for (let y = Math.floor(centerY - radius); y <= Math.ceil(centerY + radius); y++) {
      for (let x = Math.floor(cx - radius); x <= Math.ceil(cx + radius); x++) {
        if (Math.hypot(x + 0.5 - cx, y + 0.5 - centerY) <= radius) paint(x, y, badge);
      }
    }

    const mask = rasterizeGlyph(gene as GeneLetter, templateSize);
    const left = cx - letterW / 2;
    const top = centerY - letterH / 2;

    for (let y = Math.floor(top); y <= Math.ceil(top + letterH); y++) {
      for (let x = Math.floor(left); x <= Math.ceil(left + letterW); x++) {
        let hit = false;
        // A bias radius thickens or thins the stroke the way a different type weight would.
        const reach = Math.max(0, strokeBias);
        for (let dy = -reach; dy <= reach && !hit; dy++) {
          for (let dx = -reach; dx <= reach && !hit; dx++) {
            const u = (x + 0.5 + dx - left) / letterW;
            const v = (y + 0.5 + dy - top) / letterH;
            if (u < 0 || v < 0 || u >= 1 || v >= 1) continue;
            const tx = Math.min(templateSize - 1, Math.floor(u * templateSize));
            const ty = Math.min(templateSize - 1, Math.floor(v * templateSize));
            if (mask[ty * templateSize + tx]) hit = true;
          }
        }
        if (hit) paint(x, y, LETTER);
      }
    }
  });

  if (strokeBias < 0) {
    // Thinning is an erosion of the letter colour, applied after the glyphs are laid down.
    const copy = Float64Array.from(buffer);
    const isLetter = (x: number, y: number) => {
      if (x < 0 || y < 0 || x >= sw || y >= sh) return false;
      const i = (y * sw + x) * 3;
      return copy[i] === LETTER[0] && copy[i + 1] === LETTER[1] && copy[i + 2] === LETTER[2];
    };
    for (let y = 0; y < sh; y++) {
      for (let x = 0; x < sw; x++) {
        if (!isLetter(x, y)) continue;
        let keep = true;
        for (let dy = strokeBias; dy <= -strokeBias && keep; dy++) {
          for (let dx = strokeBias; dx <= -strokeBias && keep; dx++) {
            if (!isLetter(x + dx, y + dy)) keep = false;
          }
        }
        if (!keep) {
          const cx = centers.reduce((best, c) => (Math.abs(c - x) < Math.abs(best - x) ? c : best), centers[0]);
          const slot = centers.indexOf(cx);
          paint(x, y, GREEN_SET.has(genes[slot]) ? BADGE_GREEN : BADGE_RED);
        }
      }
    }
  }

  const data = new Uint8ClampedArray(width * height * 4);
  const area = SUPERSAMPLE * SUPERSAMPLE;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      let r = 0;
      let g = 0;
      let b = 0;
      for (let sy = 0; sy < SUPERSAMPLE; sy++) {
        for (let sx = 0; sx < SUPERSAMPLE; sx++) {
          const i = ((y * SUPERSAMPLE + sy) * sw + (x * SUPERSAMPLE + sx)) * 3;
          r += buffer[i];
          g += buffer[i + 1];
          b += buffer[i + 2];
        }
      }
      const p = (y * width + x) * 4;
      data[p] = Math.round(r / area);
      data[p + 1] = Math.round(g / area);
      data[p + 2] = Math.round(b / area);
      data[p + 3] = 255;
    }
  }

  return {
    data,
    width,
    height,
    hint: { geneWidthPx: badgeDiameter, gapWidthPx: pitch - badgeDiameter }
  };
}

/** Every combination the game can show, condensed into rows that cover each letter everywhere. */
const ROWS = [
  'GGGGGG',
  'HHHHHH',
  'YYYYYY',
  'WWWWWW',
  'XXXXXX',
  'GHYWXG',
  'XWGYHX',
  'YHGXWY',
  'WXHYGH',
  'GYHWXW',
  'HXWGYG',
  'XGWHYW'
];

function read(fixture: Fixture, hintOverride?: { geneWidthPx: number; gapWidthPx: number }) {
  return readDesktopGeneRow(fixture.data, fixture.width, fixture.height, hintOverride ?? fixture.hint);
}

describe('Desktop badge localisation', () => {
  it('finds all six badges directly and reports a detected layout', () => {
    const fixture = renderRow('GHYWXG');
    const layout = locateBadgeColumns(fixture.data, fixture.width, fixture.height, fixture.hint);

    expect(layout).not.toBeNull();
    expect(layout!.source).toBe('detected');
    expect(layout!.detectedCount).toBe(6);
    expect(layout!.columns.map(c => c.color)).toEqual(['green', 'green', 'green', 'red', 'red', 'green']);
  });

  it('places the slots from the measured pitch, not the calibrated one', () => {
    const fixture = renderRow('GHYWXG', { badgeDiameter: 22, pitchFactor: 1.5 });
    // A region calibrated for a different UI scale: pitch understated by a third.
    const layout = locateBadgeColumns(fixture.data, fixture.width, fixture.height, {
      geneWidthPx: 14,
      gapWidthPx: 4
    })!;

    const centers = layout.columns.map(c => (c.x0 + c.x1) / 2);
    const pitches = centers.slice(1).map((c, i) => c - centers[i]);
    for (const pitch of pitches) expect(pitch).toBeGreaterThan(28);
    expect(layout.source).toBe('detected');
  });

  it('reconstructs a badge the ROI clipped off the end of the row', () => {
    const fixture = renderRow('GHYWXG');
    // Crop the last badge away entirely.
    const cropW = fixture.width - Math.round(fixture.hint.geneWidthPx * 1.2);
    const cropped = new Uint8ClampedArray(cropW * fixture.height * 4);
    for (let y = 0; y < fixture.height; y++) {
      for (let x = 0; x < cropW; x++) {
        const src = (y * fixture.width + x) * 4;
        const dst = (y * cropW + x) * 4;
        cropped[dst] = fixture.data[src];
        cropped[dst + 1] = fixture.data[src + 1];
        cropped[dst + 2] = fixture.data[src + 2];
        cropped[dst + 3] = 255;
      }
    }

    const layout = locateBadgeColumns(cropped, cropW, fixture.height, fixture.hint)!;
    expect(layout.source).toBe('fitted');
    expect(layout.columns).toHaveLength(6);
    expect(layout.columns[5].detected).toBe(false);
  });

  it('reports nothing row-shaped for a flat panel', () => {
    const width = 160;
    const height = 24;
    const data = new Uint8ClampedArray(width * height * 4);
    for (let p = 0; p < width * height; p++) {
      data[p * 4] = 30;
      data[p * 4 + 1] = 30;
      data[p * 4 + 2] = 32;
      data[p * 4 + 3] = 255;
    }

    const layout = locateBadgeColumns(data, width, height, { geneWidthPx: 18, gapWidthPx: 8 })!;
    expect(layout.detectedCount).toBe(0);
    expect(layout.columns.every(c => c.color === 'unknown')).toBe(true);
  });
});

describe('Desktop gene row reader', () => {
  it('reads every letter in every position at the default scale', () => {
    for (const genes of ROWS) {
      const result = read(renderRow(genes));
      expect(result.geneString, `row ${genes}`).toBe(genes);
      expect(result.confidence).toBeGreaterThanOrEqual(70);
    }
  });

  it('reads across the whole range of Rust UI scales', () => {
    for (const diameter of [11, 14, 18, 24, 30, 40]) {
      for (const genes of ROWS) {
        const result = read(renderRow(genes, { badgeDiameter: diameter }));
        expect(result.geneString, `row ${genes} at ${diameter}px`).toBe(genes);
      }
    }
  });

  it('reads when calibration drifted horizontally', () => {
    for (const offset of [-4, -2, -1, 1, 2, 4]) {
      for (const genes of ROWS) {
        const fixture = renderRow(genes, { offsetX: offset });
        // The hint still describes the un-shifted row; the badges are what moved.
        expect(read(fixture).geneString, `row ${genes} at dx ${offset}`).toBe(genes);
      }
    }
  });

  it('reads when the calibrated slot width and pitch are wrong', () => {
    for (const genes of ROWS) {
      const fixture = renderRow(genes, { badgeDiameter: 24, pitchFactor: 1.5 });
      const wrong = { geneWidthPx: 12, gapWidthPx: 3 };
      expect(read(fixture, wrong).geneString, `row ${genes}`).toBe(genes);
    }
  });

  it('reads when the region is cropped tight to the letters', () => {
    for (const genes of ROWS) {
      const fixture = renderRow(genes, { badgeDiameter: 22, padY: -5 });
      expect(read(fixture).geneString, `row ${genes}`).toBe(genes);
    }
  });

  it('reads through stroke weight differences', () => {
    for (const bias of [-1, 0, 1, 2]) {
      for (const genes of ROWS) {
        const fixture = renderRow(genes, { badgeDiameter: 24, strokeBias: bias });
        expect(read(fixture).geneString, `row ${genes} at bias ${bias}`).toBe(genes);
      }
    }
  });

  it('is not fooled by the separator dashes between badges', () => {
    for (const genes of ROWS) {
      const withDashes = read(renderRow(genes, { dashes: true }));
      const without = read(renderRow(genes, { dashes: false }));
      expect(withDashes.geneString, `row ${genes}`).toBe(genes);
      expect(without.geneString, `row ${genes}`).toBe(genes);
    }
  });

  it('reads over a bright background behind the tooltip', () => {
    for (const genes of ROWS) {
      const fixture = renderRow(genes, { background: [196, 188, 170] });
      expect(read(fixture).geneString, `row ${genes}`).toBe(genes);
    }
  });

  it('reads a row whose letters sit high or low in their badges', () => {
    for (const offset of [-1.5, 1.5]) {
      for (const genes of ROWS) {
        const fixture = renderRow(genes, { badgeDiameter: 24, offsetY: offset });
        expect(read(fixture).geneString, `row ${genes} at dy ${offset}`).toBe(genes);
      }
    }
  });

  it('never invents a row from a panel with no badges', () => {
    const width = 180;
    const height = 26;
    const data = new Uint8ClampedArray(width * height * 4);
    for (let p = 0; p < width * height; p++) {
      const noise = (p * 37) % 23;
      data[p * 4] = 40 + noise;
      data[p * 4 + 1] = 42 + noise;
      data[p * 4 + 2] = 45 + noise;
      data[p * 4 + 3] = 255;
    }

    const result = readDesktopGeneRow(data, width, height, { geneWidthPx: 20, gapWidthPx: 8 });
    expect(result.geneString).toBeNull();
    expect(result.reject).toBe('not-a-row');
  });

  it('never invents a row from empty badges', () => {
    const fixture = renderRow('GHYWXG', { letterScale: 0 });
    const result = read(fixture);
    expect(result.geneString).toBeNull();
    expect(result.reject).toBe('not-a-row');
  });

  it('never puts a red-badge letter in a green badge', () => {
    for (const genes of ROWS) {
      const result = read(renderRow(genes));
      for (const slot of result.slots) {
        if (!slot.gene) continue;
        if (slot.color === 'green') expect(['G', 'Y', 'H']).toContain(slot.gene);
        if (slot.color === 'red') expect(['W', 'X']).toContain(slot.gene);
      }
    }
  });

  it('names the slot that failed instead of dropping the row', () => {
    const fixture = renderRow('GHYWXG', { badgeDiameter: 24 });
    // Paint over slot 3's letter with its own badge colour: one blank badge in a real row.
    const pitch = fixture.hint.geneWidthPx + fixture.hint.gapWidthPx;
    const cx = 4 + fixture.hint.geneWidthPx / 2 + 3 * pitch;
    const radius = fixture.hint.geneWidthPx / 2;
    for (let y = 0; y < fixture.height; y++) {
      for (let x = Math.floor(cx - radius); x <= Math.ceil(cx + radius); x++) {
        if (x < 0 || x >= fixture.width) continue;
        if (Math.hypot(x + 0.5 - cx, y + 0.5 - fixture.height / 2) > radius) continue;
        const p = (y * fixture.width + x) * 4;
        fixture.data[p] = BADGE_RED[0];
        fixture.data[p + 1] = BADGE_RED[1];
        fixture.data[p + 2] = BADGE_RED[2];
      }
    }

    const result = read(fixture);
    expect(result.geneString).toBeNull();
    expect(result.reject).toBeNull();
    expect(result.resolvedCount).toBe(5);
    expect(result.slots[3].reject).toBe('no-ink');
    expect(result.slots.filter(s => s.gene).map(s => s.gene).join('')).toBe('GHYXG');
  });

  it('stays well inside the frame budget', () => {
    const fixture = renderRow('GHYWXG', { badgeDiameter: 24 });
    // Warm the template cache so the measurement is of the read, not of first-call setup.
    read(fixture);

    const started = performance.now();
    const runs = 200;
    for (let i = 0; i < runs; i++) read(fixture);
    const perRead = (performance.now() - started) / runs;

    expect(perRead).toBeLessThan(4);
  });
});

describe('Desktop template recogniser contract', () => {
  const recognise = (fixture: Fixture, includeImages = false) =>
    DesktopTemplateRecognizer.recognizeFromRoi(
      fixture.data,
      fixture.width,
      fixture.height,
      fixture.hint.geneWidthPx,
      fixture.hint.gapWidthPx,
      includeImages
    );

  it('clears the single-frame bar at every UI scale the game offers', () => {
    // This is what makes a scan feel instant rather than costing three frames plus the wait
    // for them. If a threshold change ever pushes an ordinary read below the bar, the
    // scanner still works but quietly gets slower, which is exactly the kind of regression
    // that goes unnoticed -- so it is asserted rather than assumed.
    for (const diameter of [14, 18, 24, 30, 40]) {
      for (const genes of ROWS) {
        const result = recognise(renderRow(genes, { badgeDiameter: diameter }))!;
        expect(result.success, `row ${genes} at ${diameter}px`).toBe(true);
        expect(result.minSlotConfidence, `row ${genes} at ${diameter}px`).toBeGreaterThanOrEqual(
          SCANNER_CONFIG.recognition.instantAcceptSlotConfidence
        );
        expect(result.layoutSource).toBe('detected');
      }
    }
  });

  it('reports five letters and the failing index rather than nothing', () => {
    const fixture = renderRow('GHYWXG', { badgeDiameter: 24 });
    const pitch = fixture.hint.geneWidthPx + fixture.hint.gapWidthPx;
    const cx = 4 + fixture.hint.geneWidthPx / 2 + 2 * pitch;
    const radius = fixture.hint.geneWidthPx / 2;
    for (let y = 0; y < fixture.height; y++) {
      for (let x = Math.floor(cx - radius); x <= Math.ceil(cx + radius); x++) {
        if (x < 0 || x >= fixture.width) continue;
        if (Math.hypot(x + 0.5 - cx, y + 0.5 - fixture.height / 2) > radius) continue;
        const p = (y * fixture.width + x) * 4;
        fixture.data[p] = 101;
        fixture.data[p + 1] = 154;
        fixture.data[p + 2] = 43;
      }
    }

    const result = recognise(fixture)!;
    expect(result.success).toBe(false);
    expect(result.partial).toBe('GH.WXG');
    expect(result.unresolvedSlots).toEqual([2]);
    expect(result.resolvedCount).toBe(5);
  });

  it('hands out a raster only for the slots that need a second opinion', () => {
    const plain = recognise(renderRow('GHYWXG'))!;
    expect(plain.slots.every(slot => slot.image === undefined)).toBe(true);

    const detailed = recognise(renderRow('GHYWXG'), true)!;
    for (const slot of detailed.slots) {
      expect(slot.image).toBeDefined();
      expect(slot.image!.width).toBeGreaterThan(0);
      // Black ink on white, which is what an OCR engine expects to be handed.
      const values = new Set(Array.from(slot.image!.data.filter((_, i) => i % 4 === 0)));
      for (const value of values) expect([0, 255]).toContain(value);
    }
  });

  it('returns nothing at all when the region is not looking at a row', () => {
    const width = 160;
    const height = 24;
    const data = new Uint8ClampedArray(width * height * 4);
    for (let p = 0; p < width * height; p++) {
      data[p * 4] = 22;
      data[p * 4 + 1] = 24;
      data[p * 4 + 2] = 26;
      data[p * 4 + 3] = 255;
    }
    expect(DesktopTemplateRecognizer.recognizeFromRoi(data, width, height, 18, 8)).toBeNull();
  });
});
