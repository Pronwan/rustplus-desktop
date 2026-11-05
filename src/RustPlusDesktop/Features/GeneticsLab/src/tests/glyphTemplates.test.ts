import { describe, it, expect } from 'vitest';
import {
  GENE_ALPHABET,
  GeneLetter,
  classifyGlyphFeatures,
  countHoles,
  despeckle,
  isolateGlyphInk,
  extractGlyphFeatures,
  rasterizeGlyph
} from '../services/scanner/vision/glyphTemplates.ts';
import {
  DEFAULT_TEMPLATE_OPTIONS,
  inspectGeneRow,
  recognizeGenesByTemplate
} from '../services/scanner/vision/templateGeneRecognizer.ts';
import { formatSlotDiagnostics } from '../services/scanner/cameraStatusMessages.ts';
import type { RasterImage } from '../services/scanner/scannerTypes.ts';

const SIZE = 40;

/* ------------------------------------------------------------------ *
 * Distortions standing in for a photographed monitor
 * ------------------------------------------------------------------ */

function dilate(mask: Uint8Array, w: number, h: number): Uint8Array {
  const out = new Uint8Array(mask.length);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      let value = 0;
      for (let dy = -1; dy <= 1 && !value; dy++) {
        for (let dx = -1; dx <= 1 && !value; dx++) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx >= 0 && ny >= 0 && nx < w && ny < h && mask[ny * w + nx]) value = 1;
        }
      }
      out[y * w + x] = value;
    }
  }
  return out;
}

function erode(mask: Uint8Array, w: number, h: number): Uint8Array {
  const out = new Uint8Array(mask.length);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      let value = 1;
      for (let dy = -1; dy <= 1 && value; dy++) {
        for (let dx = -1; dx <= 1 && value; dx++) {
          const nx = x + dx;
          const ny = y + dy;
          if (nx < 0 || ny < 0 || nx >= w || ny >= h || !mask[ny * w + nx]) value = 0;
        }
      }
      out[y * w + x] = value;
    }
  }
  return out;
}

function speckle(mask: Uint8Array, rate: number, seed: number): Uint8Array {
  const out = Uint8Array.from(mask);
  let state = seed;
  for (let i = 0; i < out.length; i++) {
    state = (state * 1103515245 + 12345) & 0x7fffffff;
    if (state / 0x7fffffff < rate) out[i] = out[i] ? 0 : 1;
  }
  return out;
}

/** Renders a mask as a glyph cell: black strokes on white, as the strip builder emits. */
function toImage(mask: Uint8Array, size = SIZE): RasterImage {
  const image: RasterImage = {
    data: new Uint8ClampedArray(size * size * 4),
    width: size,
    height: size
  };
  for (let p = 0, i = 0; p < mask.length; p++, i += 4) {
    const value = mask[p] ? 0 : 255;
    image.data[i] = value;
    image.data[i + 1] = value;
    image.data[i + 2] = value;
    image.data[i + 3] = 255;
  }
  return image;
}

function classify(mask: Uint8Array): { gene: string; margin: number } | null {
  const features = extractGlyphFeatures(despeckle(mask, SIZE, SIZE), SIZE, SIZE);
  if (!features) return null;
  const match = classifyGlyphFeatures(features);
  return match ? { gene: match.gene, margin: match.margin } : null;
}

/* ------------------------------------------------------------------ *
 * Tests
 * ------------------------------------------------------------------ */

describe('Gene glyph templates', () => {
  it('rasterises a distinct shape for every letter in the alphabet', () => {
    const signatures = new Set<string>();
    for (const gene of GENE_ALPHABET) {
      const mask = rasterizeGlyph(gene, SIZE);
      expect(mask.some(v => v === 1)).toBe(true);
      signatures.add(mask.join(''));
    }
    expect(signatures.size).toBe(GENE_ALPHABET.length);
  });

  it('counts an enclosed pocket as a hole and ignores an open shape', () => {
    const size = 20;
    const ring = new Uint8Array(size * size);
    for (let y = 4; y < 16; y++) {
      for (let x = 4; x < 16; x++) {
        const onEdge = y === 4 || y === 15 || x === 4 || x === 15;
        if (onEdge) ring[y * size + x] = 1;
      }
    }
    expect(countHoles(ring, size, size)).toBe(1);

    // Break the ring: the interior now drains to the border.
    for (let x = 6; x < 14; x++) ring[15 * size + x] = 0;
    expect(countHoles(ring, size, size)).toBe(0);
  });
});

describe('Gene glyph classification', () => {
  it('identifies every letter at the reference weight', () => {
    for (const gene of GENE_ALPHABET) {
      const match = classify(rasterizeGlyph(gene, SIZE));
      expect(match?.gene).toBe(gene);
    }
  });

  it('survives heavier and lighter stroke weights', () => {
    for (const gene of GENE_ALPHABET) {
      const base = rasterizeGlyph(gene, SIZE);

      const bold = classify(dilate(base, SIZE, SIZE));
      expect(bold?.gene, `bold ${gene}`).toBe(gene);
      expect(bold!.margin).toBeGreaterThan(DEFAULT_TEMPLATE_OPTIONS.minMargin);

      const thin = classify(erode(base, SIZE, SIZE));
      expect(thin?.gene, `thin ${gene}`).toBe(gene);
      expect(thin!.margin).toBeGreaterThan(DEFAULT_TEMPLATE_OPTIONS.minMargin);
    }
  });

  it('survives sensor speckle, which is what despeckling is for', () => {
    for (const gene of GENE_ALPHABET) {
      const noisy = speckle(rasterizeGlyph(gene, SIZE), 0.03, 7);
      const match = classify(noisy);
      expect(match?.gene, `noisy ${gene}`).toBe(gene);
    }
  });

  it('reports no glyph for a blank or solid cell', () => {
    const blank = new Uint8Array(SIZE * SIZE);
    expect(classify(blank)).toBeNull();

    const solid = new Uint8Array(SIZE * SIZE).fill(1);
    const features = extractGlyphFeatures(solid, SIZE, SIZE)!;
    // Density alone disqualifies it before any template is consulted.
    expect(features.density).toBeGreaterThan(0.85);
  });
});

describe('Template row recognition', () => {
  function slotsFor(genes: string): RasterImage[] {
    return genes.split('').map(letter => toImage(rasterizeGlyph(letter as GeneLetter, SIZE)));
  }

  it('reads a full six-gene row', () => {
    const result = recognizeGenesByTemplate(slotsFor('GHYWXG'));

    expect(result).not.toBeNull();
    expect(result!.geneString).toBe('GHYWXG');
    expect(result!.confidence).toBeGreaterThan(DEFAULT_TEMPLATE_OPTIONS.minMargin * 100);
  });

  it('always returns six characters or nothing, never a partial row', () => {
    // The failure that made the OCR path unusable was a five-character answer for a
    // six-gene row, which then got discarded and restarted the confirmation window.
    const withOneBlank = slotsFor('GHYWXG');
    withOneBlank[3] = toImage(new Uint8Array(SIZE * SIZE));

    expect(recognizeGenesByTemplate(withOneBlank)).toBeNull();
    expect(recognizeGenesByTemplate(slotsFor('GHYWX'))).toBeNull();
  });

  it('refuses a slot it cannot tell apart rather than guessing', () => {
    const ambiguous = slotsFor('GHYWXG');
    // A near-solid block is closest to something, but not convincingly.
    const blob = new Uint8Array(SIZE * SIZE);
    for (let y = 10; y < 30; y++) for (let x = 10; x < 30; x++) blob[y * SIZE + x] = 1;
    ambiguous[2] = toImage(blob);

    expect(recognizeGenesByTemplate(ambiguous)).toBeNull();
  });

  it('reads every letter of the alphabet in row position', () => {
    const result = recognizeGenesByTemplate(slotsFor('GHYWXH'));
    expect(result?.geneString).toBe('GHYWXH');
  });
});

describe('Slot-level rejection reporting', () => {
  const blankCell = (): RasterImage => toImage(new Uint8Array(SIZE * SIZE));
  const solidCell = (): RasterImage => toImage(new Uint8Array(SIZE * SIZE).fill(1));

  it('names the gate that rejected each slot instead of collapsing the row to null', () => {
    const good = toImage(rasterizeGlyph('H', SIZE));
    const reports = inspectGeneRow([good, blankCell(), solidCell(), good, good, good]);

    expect(reports).toHaveLength(6);
    expect(reports[0].reject).toBeNull();
    expect(reports[0].gene).toBe('H');
    expect(reports[1].reject).toBe('blank');
    expect(reports[2].reject).toBe('solid');

    // The row itself still fails, which is the behaviour the reporting explains.
    expect(recognizeGenesByTemplate([good, blankCell(), solidCell(), good, good, good])).toBeNull();
  });

  it('keeps the nearest template and its scores on a slot that failed a gate', () => {
    // A cell whose ink is below the density floor: it has a nearest match, but the match
    // means nothing, and reporting both is what distinguishes the two cases on a device.
    const faint = new Uint8Array(SIZE * SIZE);
    faint[10 * SIZE + 10] = 1;
    faint[10 * SIZE + 11] = 1;

    const [report] = inspectGeneRow([toImage(faint)]);
    expect(report.reject).not.toBeNull();
    expect(report.distance).toBeGreaterThanOrEqual(0);
  });

  it('reports every slot on one line each, with the OCR letter beside the template letter', () => {
    const reports = inspectGeneRow([toImage(rasterizeGlyph('W', SIZE)), blankCell()]);
    const lines = formatSlotDiagnostics(reports, ['W', '']);

    expect(lines).toHaveLength(2);
    expect(lines[0]).toContain('tpl W');
    expect(lines[0]).toContain('ocr W');
    expect(lines[0]).toContain('ok');
    expect(lines[1]).toContain('tpl -');
    expect(lines[1]).toContain('ocr -');
    expect(lines[1]).toContain('blank');
  });

  it('says so explicitly when OCR has not run yet', () => {
    const lines = formatSlotDiagnostics(inspectGeneRow([toImage(rasterizeGlyph('X', SIZE))]), null);
    expect(lines[lines.length - 1]).toBe('ocr not run yet');
  });
});

describe('Stroke-weight invariance', () => {
  /**
   * The defect this locks down: per-slot thresholding on a photographed monitor binarised
   * one six-letter row at ink shares from 0.12 to 0.74. Comparing raw zone fills made that
   * scale difference dominate the distance, so every template scored about the same, the
   * margin collapsed, and the winner was whichever template carried a similar amount of ink
   * rather than a similar shape. On device that read a whole row as the same letter.
   */
  const weights: Array<[string, (m: Uint8Array) => Uint8Array]> = [
    ['unchanged', mask => mask],
    ['once dilated', mask => dilate(mask, SIZE, SIZE)],
    ['twice dilated', mask => dilate(dilate(mask, SIZE, SIZE), SIZE, SIZE)],
    ['eroded', mask => erode(mask, SIZE, SIZE)],
    ['dilated then speckled', mask => speckle(dilate(dilate(mask, SIZE, SIZE), SIZE, SIZE), 0.06, 7)]
  ];

  for (const [label, distort] of weights) {
    it(`reads every letter when the strokes come out ${label}`, () => {
      for (const gene of GENE_ALPHABET) {
        const mask = distort(rasterizeGlyph(gene as GeneLetter, SIZE));
        const match = classify(mask);

        expect(match, `${gene} ${label}`).not.toBeNull();
        expect(match!.gene, `${gene} ${label}`).toBe(gene);
        expect(match!.margin, `${gene} ${label} margin`).toBeGreaterThan(
          DEFAULT_TEMPLATE_OPTIONS.minMargin
        );
      }
    });
  }

  it('still refuses a cell that is only noise', () => {
    const noise = speckle(new Uint8Array(SIZE * SIZE), 0.3, 3);
    const features = extractGlyphFeatures(despeckle(noise, SIZE, SIZE), SIZE, SIZE);
    const match = features ? classifyGlyphFeatures(features) : null;

    // Noise resembles nothing in particular, so the margin is what rejects it.
    expect(match!.margin).toBeLessThan(DEFAULT_TEMPLATE_OPTIONS.minMargin);
  });

  it('still refuses half a glyph, which a bad crop produces', () => {
    for (const gene of GENE_ALPHABET) {
      const mask = rasterizeGlyph(gene as GeneLetter, SIZE);
      for (let y = 0; y < SIZE; y++) {
        for (let x = SIZE / 2; x < SIZE; x++) mask[y * SIZE + x] = 0;
      }

      const features = extractGlyphFeatures(despeckle(mask, SIZE, SIZE), SIZE, SIZE);
      const match = features ? classifyGlyphFeatures(features) : null;

      // Half a letter can genuinely resemble one template best, so margin alone would let
      // some of these through. Distance is the gate that catches them.
      expect(match!.distance, `half ${gene}`).toBeGreaterThan(DEFAULT_TEMPLATE_OPTIONS.maxDistance);
    }
  });
});

describe('Detached ink', () => {
  /**
   * Real strip-cell geometry. This matters: at template size the glyph fills its box, so a
   * corner speck barely moves the bounding box and the defect is invisible. In an actual
   * cell the letter sits in the middle with padding around it, and one speck in the corner
   * stretches the box across the whole cell.
   */
  const CELL_W = 165;
  const CELL_H = 152;
  const GLYPH = 80;

  function cellWithGlyph(gene: GeneLetter): Uint8Array {
    const glyph = rasterizeGlyph(gene, GLYPH);
    const cell = new Uint8Array(CELL_W * CELL_H);
    const ox = Math.round((CELL_W - GLYPH) / 2);
    const oy = Math.round((CELL_H - GLYPH) / 2);
    for (let y = 0; y < GLYPH; y++) {
      for (let x = 0; x < GLYPH; x++) {
        if (glyph[y * GLYPH + x]) cell[(oy + y) * CELL_W + (ox + x)] = 1;
      }
    }
    return cell;
  }

  function withSpeck(mask: Uint8Array, cx: number, cy: number, r: number): Uint8Array {
    const out = Uint8Array.from(mask);
    for (let y = cy - r; y <= cy + r; y++) {
      for (let x = cx - r; x <= cx + r; x++) {
        if (x < 0 || y < 0 || x >= CELL_W || y >= CELL_H) continue;
        out[y * CELL_W + x] = 1;
      }
    }
    return out;
  }

  function read(mask: Uint8Array) {
    const cleaned = isolateGlyphInk(despeckle(mask, CELL_W, CELL_H), CELL_W, CELL_H);
    const features = extractGlyphFeatures(cleaned, CELL_W, CELL_H);
    return features ? { match: classifyGlyphFeatures(features), density: features.density } : null;
  }

  it('reads every letter identically with or without specks in the cell', () => {
    for (const gene of GENE_ALPHABET) {
      const clean = cellWithGlyph(gene as GeneLetter);
      const specked = withSpeck(withSpeck(clean, 6, 6, 2), CELL_W - 7, CELL_H - 7, 2);

      const before = read(clean)!;
      const after = read(specked)!;

      expect(after.match!.gene, gene).toBe(gene);
      expect(after.match!.distance, gene).toBeCloseTo(before.match!.distance, 5);
      expect(after.density, gene).toBeCloseTo(before.density, 5);
    }
  });

  it('keeps a glyph that blur has broken into pieces', () => {
    // Components are kept in proportion to the largest, so an H whose crossbar has faded
    // keeps both uprights instead of being reduced to one.
    const mask = new Uint8Array(CELL_W * CELL_H);
    for (let y = 40; y < 110; y++) {
      for (let x = 60; x < 70; x++) mask[y * CELL_W + x] = 1;
      for (let x = 95; x < 105; x++) mask[y * CELL_W + x] = 1;
    }

    const kept = isolateGlyphInk(mask, CELL_W, CELL_H);
    let total = 0;
    for (const value of kept) total += value;

    expect(total).toBe(70 * 20);
  });

  it('leaves a single-component mask untouched', () => {
    const mask = cellWithGlyph('X');
    expect(isolateGlyphInk(mask, CELL_W, CELL_H)).toBe(mask);
  });
});
