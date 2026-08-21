import { Quad, Vec2 } from './geometry.ts';

/**
 * Perspective normalisation.
 *
 * The canonical horizontal six-gene canvas produced here is the contract between camera
 * vision and the existing OCR modules: downstream code never needs to know the phone
 * position, camera roll, or viewing angle.
 */

export interface RasterImage {
  data: Uint8ClampedArray;
  width: number;
  height: number;
}

/** Homography coefficients for `x' = (a x + b y + c) / (g x + h y + 1)`. */
export type Homography = [number, number, number, number, number, number, number, number];

/**
 * Solves the 8x8 system for the homography mapping `from` onto `to`.
 * Returns null when the correspondences are degenerate.
 */
export function solveHomography(from: Quad, to: Quad): Homography | null {
  const matrix: number[][] = [];

  for (let i = 0; i < 4; i++) {
    const { x, y } = from[i];
    const { x: X, y: Y } = to[i];
    matrix.push([x, y, 1, 0, 0, 0, -x * X, -y * X, X]);
    matrix.push([0, 0, 0, x, y, 1, -x * Y, -y * Y, Y]);
  }

  // Gaussian elimination with partial pivoting.
  for (let col = 0; col < 8; col++) {
    let pivotRow = col;
    let best = Math.abs(matrix[col][col]);
    for (let row = col + 1; row < 8; row++) {
      const value = Math.abs(matrix[row][col]);
      if (value > best) {
        best = value;
        pivotRow = row;
      }
    }
    if (best < 1e-10) return null;

    if (pivotRow !== col) {
      const swap = matrix[col];
      matrix[col] = matrix[pivotRow];
      matrix[pivotRow] = swap;
    }

    const pivot = matrix[col][col];
    for (let k = col; k <= 8; k++) matrix[col][k] /= pivot;

    for (let row = 0; row < 8; row++) {
      if (row === col) continue;
      const factor = matrix[row][col];
      if (factor === 0) continue;
      for (let k = col; k <= 8; k++) {
        matrix[row][k] -= factor * matrix[col][k];
      }
    }
  }

  const h = matrix.map(row => row[8]) as unknown as Homography;
  return h.every(value => Number.isFinite(value)) ? h : null;
}

export function applyHomography(h: Homography, x: number, y: number): Vec2 {
  const denominator = h[6] * x + h[7] * y + 1;
  if (Math.abs(denominator) < 1e-12) return { x: NaN, y: NaN };
  return {
    x: (h[0] * x + h[1] * y + h[2]) / denominator,
    y: (h[3] * x + h[4] * y + h[5]) / denominator
  };
}

function sampleBilinear(source: RasterImage, x: number, y: number, out: Uint8ClampedArray, offset: number): void {
  // Outside the source the row is genuinely unknown. Black reads as background once the
  // existing preprocessor binarises the strip, so it never invents a letter stroke.
  if (x < 0 || y < 0 || x > source.width - 1 || y > source.height - 1) {
    out[offset] = 0;
    out[offset + 1] = 0;
    out[offset + 2] = 0;
    out[offset + 3] = 255;
    return;
  }

  const x0 = Math.floor(x);
  const y0 = Math.floor(y);
  const x1 = Math.min(x0 + 1, source.width - 1);
  const y1 = Math.min(y0 + 1, source.height - 1);
  const fx = x - x0;
  const fy = y - y0;

  const i00 = (y0 * source.width + x0) * 4;
  const i10 = (y0 * source.width + x1) * 4;
  const i01 = (y1 * source.width + x0) * 4;
  const i11 = (y1 * source.width + x1) * 4;

  const w00 = (1 - fx) * (1 - fy);
  const w10 = fx * (1 - fy);
  const w01 = (1 - fx) * fy;
  const w11 = fx * fy;

  for (let channel = 0; channel < 3; channel++) {
    out[offset + channel] =
      source.data[i00 + channel] * w00 +
      source.data[i10 + channel] * w10 +
      source.data[i01 + channel] * w01 +
      source.data[i11 + channel] * w11;
  }
  out[offset + 3] = 255;
}

/**
 * Maps `quad` (in source-image coordinates, ordered TL/TR/BR/BL) onto a
 * `outWidth x outHeight` upright rectangle.
 *
 * The homography is solved in the destination-to-source direction so each output pixel is
 * sampled exactly once, leaving no holes at oblique angles.
 */
export function warpQuadToRect(
  source: RasterImage,
  quad: Quad,
  outWidth: number,
  outHeight: number,
  reuse?: RasterImage
): RasterImage | null {
  if (outWidth <= 0 || outHeight <= 0) return null;

  const destination: Quad = [
    { x: 0, y: 0 },
    { x: outWidth, y: 0 },
    { x: outWidth, y: outHeight },
    { x: 0, y: outHeight }
  ];

  const h = solveHomography(destination, quad);
  if (!h) return null;

  const target =
    reuse && reuse.width === outWidth && reuse.height === outHeight
      ? reuse
      : { data: new Uint8ClampedArray(outWidth * outHeight * 4), width: outWidth, height: outHeight };

  let offset = 0;
  for (let y = 0; y < outHeight; y++) {
    const py = y + 0.5;
    for (let x = 0; x < outWidth; x++) {
      const source2d = applyHomography(h, x + 0.5, py);
      sampleBilinear(source, source2d.x, source2d.y, target.data, offset);
      offset += 4;
    }
  }

  return target;
}

/**
 * Foreshortening of the row, expressed as an approximate viewing angle away from
 * perpendicular. Derived from how much the near edge outruns the far edge.
 */
export function estimatePerspectiveDegrees(quad: Quad): number {
  const leftHeight = Math.hypot(quad[3].x - quad[0].x, quad[3].y - quad[0].y);
  const rightHeight = Math.hypot(quad[2].x - quad[1].x, quad[2].y - quad[1].y);
  const topWidth = Math.hypot(quad[1].x - quad[0].x, quad[1].y - quad[0].y);
  const bottomWidth = Math.hypot(quad[2].x - quad[3].x, quad[2].y - quad[3].y);

  const heightRatio = ratioOf(leftHeight, rightHeight);
  const widthRatio = ratioOf(topWidth, bottomWidth);
  const worst = Math.min(heightRatio, widthRatio);

  if (!Number.isFinite(worst) || worst <= 0) return 90;
  return (Math.acos(Math.min(1, worst)) * 180) / Math.PI;
}

function ratioOf(a: number, b: number): number {
  const max = Math.max(a, b);
  if (max <= 0) return 0;
  return Math.min(a, b) / max;
}

/** Axis-aligned bounds of a quad, expanded by `margin` and clamped to the given size. */
export function quadBounds(
  quad: Quad,
  margin: number,
  width: number,
  height: number
): { x: number; y: number; width: number; height: number } {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  for (const p of quad) {
    if (p.x < minX) minX = p.x;
    if (p.x > maxX) maxX = p.x;
    if (p.y < minY) minY = p.y;
    if (p.y > maxY) maxY = p.y;
  }

  const x = Math.max(0, Math.floor(minX - margin));
  const y = Math.max(0, Math.floor(minY - margin));
  const right = Math.min(width, Math.ceil(maxX + margin));
  const bottom = Math.min(height, Math.ceil(maxY + margin));

  return { x, y, width: Math.max(0, right - x), height: Math.max(0, bottom - y) };
}

export function translateQuad(quad: Quad, dx: number, dy: number, scale = 1): Quad {
  return quad.map(p => ({ x: (p.x + dx) * scale, y: (p.y + dy) * scale })) as Quad;
}

export function scaleQuad(quad: Quad, scale: number): Quad {
  return quad.map(p => ({ x: p.x * scale, y: p.y * scale })) as Quad;
}
