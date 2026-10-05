import { RasterImage } from '../scanner/scannerTypes.ts';
import { LivestockPanelRead } from './livestockPanelReader.ts';
import { PORTRAIT_REFERENCES, PORTRAIT_SIZE, PortraitKind } from './portraitData.ts';

export type { PortraitKind };

/**
 * Which animal the panel belongs to, from the portrait in its header.
 *
 * The header shows the game's art for the animal's kind: cow, bull, calf, sheep or lamb. It
 * sits at a fixed place relative to the gene badges (measured at UI scale 0.7 and 1.0:
 * centred 4.12 pitches left of the first badge and 1.86 above the row, 1.38 pitches across).
 * A small search around that spot absorbs capture scaling, and each reference is compared
 * only where it has the animal (its alpha), by correlation of brightness-normalised colour,
 * so the translucent panel and whatever shows through it do not count.
 *
 * Portraits show the animal's own coat, so each kind has several references (see
 * portraitData.ts) and the best one counts. Coats overlap between a cow and a calf, so when
 * the two best kinds are close and of the same species, only the species is reported.
 *
 * Cow and bull settle an adult's sex. Calves, lambs and sheep share one portrait per kind
 * for both sexes, so their sex is not on the panel at all.
 */

export interface PortraitMatch {
  /** The kind, or null when only the species is certain (a cow/calf look-alike). */
  kind: PortraitKind | null;
  species: 'cattle' | 'sheep';
  score: number;
  /** Score gap to the next-best kind. */
  margin: number;
}

/** Below this gap between the two best kinds, the kind is not trusted. */
const KIND_MARGIN = 0.12;

interface Reference {
  kind: PortraitKind;
  /** Normalised colour of the masked pixels, channel-major per pixel. */
  values: Float32Array;
  /** Pixel indices the reference covers. */
  mask: Uint16Array;
}

const N = PORTRAIT_SIZE;

function decodeBase64(text: string): Uint8Array {
  const binary = typeof atob === 'function' ? atob(text) : '';
  const out = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) out[i] = binary.charCodeAt(i);
  return out;
}

/** Zero-mean, unit-variance per channel over the given pixels. */
function normalise(rgb: Float32Array, mask: Uint16Array): Float32Array {
  const out = new Float32Array(mask.length * 3);
  for (let c = 0; c < 3; c++) {
    let mean = 0;
    for (const i of mask) mean += rgb[i * 3 + c];
    mean /= mask.length;
    let variance = 0;
    for (const i of mask) variance += (rgb[i * 3 + c] - mean) ** 2;
    const std = Math.sqrt(variance / mask.length) || 1;
    mask.forEach((i, k) => {
      out[k * 3 + c] = (rgb[i * 3 + c] - mean) / std;
    });
  }
  return out;
}

let references: Reference[] | null = null;

function getReferences(): Reference[] {
  if (references) return references;
  references = PORTRAIT_REFERENCES.map(({ kind, rgba: encoded }) => {
    const rgba = decodeBase64(encoded);
    const rgb = new Float32Array(N * N * 3);
    const covered: number[] = [];
    for (let i = 0; i < N * N; i++) {
      rgb[i * 3] = rgba[i * 4];
      rgb[i * 3 + 1] = rgba[i * 4 + 1];
      rgb[i * 3 + 2] = rgba[i * 4 + 2];
      if (rgba[i * 4 + 3] > 128) covered.push(i);
    }
    const mask = Uint16Array.from(covered);
    return { kind, mask, values: normalise(rgb, mask) };
  });
  return references;
}

/** A square of the frame box-filtered down to N x N RGB. */
function sampleSquare(image: RasterImage, cx: number, cy: number, size: number): Float32Array | null {
  const x0 = cx - size / 2;
  const y0 = cy - size / 2;
  if (x0 < 0 || y0 < 0 || x0 + size > image.width || y0 + size > image.height) return null;
  const out = new Float32Array(N * N * 3);
  const step = size / N;
  for (let gy = 0; gy < N; gy++) {
    for (let gx = 0; gx < N; gx++) {
      let r = 0;
      let g = 0;
      let b = 0;
      let count = 0;
      const sx0 = Math.floor(x0 + gx * step);
      const sx1 = Math.max(sx0 + 1, Math.floor(x0 + (gx + 1) * step));
      const sy0 = Math.floor(y0 + gy * step);
      const sy1 = Math.max(sy0 + 1, Math.floor(y0 + (gy + 1) * step));
      for (let y = sy0; y < sy1; y++) {
        for (let x = sx0; x < sx1; x++) {
          const p = (y * image.width + x) * 4;
          r += image.data[p];
          g += image.data[p + 1];
          b += image.data[p + 2];
          count++;
        }
      }
      const o = (gy * N + gx) * 3;
      out[o] = r / count;
      out[o + 1] = g / count;
      out[o + 2] = b / count;
    }
  }
  return out;
}

export const PORTRAIT_OFFSET = { dx: -4.12, dy: -1.86, size: 1.38 };

export function classifyPortrait(image: RasterImage, read: LivestockPanelRead): PortraitMatch | null {
  if (read.bottom) return null; // The older two-row panel has no portrait.
  const refs = getReferences();
  const pitch = read.top.pitch;
  const x1 = read.top.badges[0].cx;
  const y = read.top.cy;
  const best = new Map<PortraitKind, number>();

  for (const ds of [-0.08, 0, 0.08]) {
    const size = (PORTRAIT_OFFSET.size + ds) * pitch;
    for (const ox of [-0.1, 0, 0.1]) {
      for (const oy of [-0.1, 0, 0.1]) {
        const crop = sampleSquare(image, x1 + (PORTRAIT_OFFSET.dx + ox) * pitch, y + (PORTRAIT_OFFSET.dy + oy) * pitch, size);
        if (!crop) continue;
        for (const ref of refs) {
          const values = normalise(crop, ref.mask);
          let dot = 0;
          for (let i = 0; i < values.length; i++) dot += values[i] * ref.values[i];
          const score = dot / values.length;
          if (score > (best.get(ref.kind) ?? -Infinity)) best.set(ref.kind, score);
        }
      }
    }
  }

  const ranked = [...best.entries()].sort((a, b) => b[1] - a[1]);
  if (ranked.length < 2) return null;
  const [kind, score] = ranked[0];
  const [runnerUp, runnerScore] = ranked[1];
  const margin = score - runnerScore;
  if (score < 0.45) return null;
  const species = portraitSpecies(kind);
  if (margin >= KIND_MARGIN) return { kind, species, score, margin };
  // Too close to call the kind; the species still holds if both candidates share it.
  return portraitSpecies(runnerUp) === species ? { kind: null, species, score, margin } : null;
}

export function portraitSpecies(kind: PortraitKind): 'cattle' | 'sheep' {
  return kind === 'sheep' || kind === 'lamb' ? 'sheep' : 'cattle';
}

/** Sex when the portrait shows it: only adult cattle. */
export function portraitSex(kind: PortraitKind): 'male' | 'female' | null {
  if (kind === 'bull') return 'male';
  if (kind === 'cow') return 'female';
  return null;
}
