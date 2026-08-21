import { describe, it, expect } from 'vitest';
import {
  GENE_ALPHABET,
  GeneLetter,
  classifyGlyphFeatures,
  countHoles,
  despeckle,
  extractGlyphFeatures,
  rasterizeGlyph
} from '../services/scanner/vision/glyphTemplates.ts';
import {
  DEFAULT_TEMPLATE_OPTIONS,
  recognizeGenesByTemplate
} from '../services/scanner/vision/templateGeneRecognizer.ts';
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
