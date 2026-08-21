import { describe, it, expect } from 'vitest';
import {
  AnalysisFrame,
  BadgeComponent,
  DEFAULT_BADGE_FILTER_OPTIONS,
  buildBadgeMask,
  cleanBadgeMask,
  detectBadgeCandidates,
  filterBadgeCandidates,
  findComponents,
  MASK_GREEN,
  MASK_RED
} from '../services/scanner/vision/rasterOps.ts';
import {
  DEFAULT_BUILD_CANDIDATES_OPTIONS,
  Quad,
  buildRowCandidates,
  deriveRowQuad,
  groupSixBadgeRows,
  normalize,
  perpendicular,
  quadDrift,
  readingDirectionForOrientation,
  validateQuad
} from '../services/scanner/vision/geometry.ts';
import {
  applyHomography,
  estimatePerspectiveDegrees,
  quadBounds,
  solveHomography,
  warpQuadToRect
} from '../services/scanner/vision/perspective.ts';
import {
  buildCameraGeneStrip,
  inkCoverage
} from '../services/scanner/vision/cameraGeneStrip.ts';
import type { RasterImage } from '../services/scanner/scannerTypes.ts';
import {
  computeContainRect,
  elementToFrame,
  frameToElement,
  quadToSvgPoints
} from '../services/scanner/cameraOverlayGeometry.ts';
import {
  DEFAULT_ROW_QUALITY_THRESHOLDS,
  RowStabilityTracker,
  assessRowQuality,
  measureExposure,
  measureSharpness
} from '../services/scanner/vision/quality.ts';

/* ------------------------------------------------------------------ *
 * Synthetic frame helpers
 * ------------------------------------------------------------------ */

const GREEN: [number, number, number] = [58, 168, 72];
const RED: [number, number, number] = [198, 62, 48];
const TOOLTIP: [number, number, number] = [22, 22, 26];
const WHITE: [number, number, number] = [242, 242, 240];

function createFrame(
  width: number,
  height: number,
  background: [number, number, number] = [120, 120, 125]
): AnalysisFrame {
  const data = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < data.length; i += 4) {
    data[i] = background[0];
    data[i + 1] = background[1];
    data[i + 2] = background[2];
    data[i + 3] = 255;
  }
  return { data, width, height, scale: 0.5 };
}

function setPixel(frame: AnalysisFrame, x: number, y: number, color: [number, number, number]): void {
  if (x < 0 || y < 0 || x >= frame.width || y >= frame.height) return;
  const i = (Math.floor(y) * frame.width + Math.floor(x)) * 4;
  frame.data[i] = color[0];
  frame.data[i + 1] = color[1];
  frame.data[i + 2] = color[2];
  frame.data[i + 3] = 255;
}

function fillRect(
  frame: AnalysisFrame,
  x0: number,
  y0: number,
  x1: number,
  y1: number,
  color: [number, number, number]
): void {
  for (let y = Math.floor(y0); y <= Math.floor(y1); y++) {
    for (let x = Math.floor(x0); x <= Math.floor(x1); x++) {
      setPixel(frame, x, y, color);
    }
  }
}

/** Draws one gene badge: a coloured block with a white letter punched out of the middle. */
function drawBadge(
  frame: AnalysisFrame,
  cx: number,
  cy: number,
  w: number,
  h: number,
  color: [number, number, number],
  angle = 0
): void {
  const halfW = w / 2;
  const halfH = h / 2;
  const cos = Math.cos(-angle);
  const sin = Math.sin(-angle);
  const reach = Math.ceil(Math.hypot(halfW, halfH)) + 1;

  for (let y = Math.floor(cy - reach); y <= Math.ceil(cy + reach); y++) {
    for (let x = Math.floor(cx - reach); x <= Math.ceil(cx + reach); x++) {
      const dx = x - cx;
      const dy = y - cy;
      const lx = dx * cos - dy * sin;
      const ly = dx * sin + dy * cos;
      if (Math.abs(lx) > halfW || Math.abs(ly) > halfH) continue;
      const isLetter = Math.abs(lx) <= halfW * 0.22 && Math.abs(ly) <= halfH * 0.55;
      setPixel(frame, x, y, isLetter ? WHITE : color);
    }
  }
}

interface RowSpec {
  cx: number;
  cy: number;
  badgeWidth?: number;
  badgeHeight?: number;
  spacing?: number;
  angle?: number;
  colors?: Array<[number, number, number]>;
  withTooltip?: boolean;
  count?: number;
}

/** Draws a full genetics row, optionally rotated, on a dark tooltip panel. */
function drawGeneRow(frame: AnalysisFrame, spec: RowSpec): void {
  const {
    cx,
    cy,
    badgeWidth = 14,
    badgeHeight = 16,
    spacing = 18,
    angle = 0,
    colors,
    withTooltip = true,
    count = 6
  } = spec;

  const axis = { x: Math.cos(angle), y: Math.sin(angle) };
  const normal = perpendicular(axis);

  if (withTooltip) {
    const halfLength = (spacing * (count - 1)) / 2 + badgeWidth;
    const halfBand = badgeHeight * 2.2;
    for (let t = -halfLength; t <= halfLength; t += 0.5) {
      for (let n = -halfBand; n <= halfBand; n += 0.5) {
        setPixel(frame, cx + axis.x * t + normal.x * n, cy + axis.y * t + normal.y * n, TOOLTIP);
      }
    }
  }

  for (let i = 0; i < count; i++) {
    const offset = (i - (count - 1) / 2) * spacing;
    const color = colors ? colors[i % colors.length] : i % 3 === 0 ? RED : GREEN;
    drawBadge(frame, cx + axis.x * offset, cy + axis.y * offset, badgeWidth, badgeHeight, color, angle);
  }
}

function makeComponent(overrides: Partial<BadgeComponent> & { id: number; cx: number; cy: number }): BadgeComponent {
  const width = overrides.width ?? 14;
  const height = overrides.height ?? 16;
  return {
    minX: overrides.cx - width / 2,
    minY: overrides.cy - height / 2,
    maxX: overrides.cx + width / 2,
    maxY: overrides.cy + height / 2,
    width,
    height,
    pixelCount: Math.round(width * height * 0.7),
    fillRatio: 0.7,
    aspect: width / height,
    color: 'green',
    colorStrength: 0.9,
    touchesEdge: false,
    ...overrides
  };
}

/** Six evenly spaced badges along an axis, in analysis-frame coordinates. */
function makeRowComponents(options: {
  cx?: number;
  cy?: number;
  spacing?: number;
  angle?: number;
  count?: number;
} = {}): BadgeComponent[] {
  const { cx = 200, cy = 150, spacing = 18, angle = 0, count = 6 } = options;
  const axis = { x: Math.cos(angle), y: Math.sin(angle) };

  return Array.from({ length: count }, (_, i) => {
    const offset = (i - (count - 1) / 2) * spacing;
    return makeComponent({ id: i, cx: cx + axis.x * offset, cy: cy + axis.y * offset });
  });
}

/* ------------------------------------------------------------------ *
 * Raster operations
 * ------------------------------------------------------------------ */

describe('Badge masking', () => {
  it('separates green and red badges from neutral surroundings', () => {
    const frame = createFrame(60, 40, [128, 128, 130]);
    fillRect(frame, 5, 5, 20, 20, GREEN);
    fillRect(frame, 35, 5, 50, 20, RED);

    const mask = buildBadgeMask(frame);

    expect(mask.labels[12 * 60 + 12]).toBe(MASK_GREEN);
    expect(mask.labels[12 * 60 + 42]).toBe(MASK_RED);
    // Neutral grey carries no hue and must not register.
    expect(mask.labels[30 * 60 + 30]).toBe(0);
  });

  it('ignores near-black and unsaturated pixels regardless of channel order', () => {
    const frame = createFrame(20, 20, [10, 14, 11]);
    fillRect(frame, 2, 2, 8, 8, [200, 205, 202]);

    const mask = buildBadgeMask(frame);

    expect(Array.from(mask.labels).every(v => v === 0)).toBe(true);
  });

  it('closes the letter hole so a badge stays one component', () => {
    const frame = createFrame(40, 40, [120, 120, 125]);
    drawBadge(frame, 20, 20, 16, 18, GREEN);

    const mask = buildBadgeMask(frame);
    const rawComponents = findComponents(
      Uint8Array.from(mask.labels, v => (v === 0 ? 0 : 1)),
      mask
    );
    const cleanedComponents = findComponents(cleanBadgeMask(mask), mask);

    expect(rawComponents).toHaveLength(1);
    expect(cleanedComponents).toHaveLength(1);
    // The letter no longer removes pixels from the middle of the badge.
    expect(cleanedComponents[0].fillRatio).toBeGreaterThan(rawComponents[0].fillRatio);
  });

  it('rejects blobs outside the badge size and shape envelope', () => {
    const components = [
      makeComponent({ id: 0, cx: 50, cy: 50 }),
      makeComponent({ id: 1, cx: 80, cy: 50, width: 2, height: 2, pixelCount: 3 }),
      makeComponent({ id: 2, cx: 120, cy: 50, width: 60, height: 8, aspect: 7.5 }),
      makeComponent({ id: 3, cx: 160, cy: 50, colorStrength: 0.1 }),
      makeComponent({ id: 4, cx: 200, cy: 50, fillRatio: 0.12 })
    ];

    const kept = filterBadgeCandidates(components, 400, 300, DEFAULT_BADGE_FILTER_OPTIONS);

    expect(kept.map(c => c.id)).toEqual([0]);
  });

  it('finds exactly six badges in a rendered genetics row', () => {
    const frame = createFrame(320, 200);
    drawGeneRow(frame, { cx: 160, cy: 100 });

    const candidates = detectBadgeCandidates(frame);

    expect(candidates).toHaveLength(6);
    expect(candidates.filter(c => c.color === 'red').length).toBeGreaterThan(0);
    expect(candidates.filter(c => c.color === 'green').length).toBeGreaterThan(0);
  });
});

/* ------------------------------------------------------------------ *
 * Row grouping and scoring
 * ------------------------------------------------------------------ */

describe('Six-badge row grouping', () => {
  it('groups six aligned badges into one candidate row', () => {
    const groups = groupSixBadgeRows(makeRowComponents());

    expect(groups).toHaveLength(1);
    expect(groups[0].members).toHaveLength(6);
  });

  it('does not form a row from only five badges', () => {
    const groups = groupSixBadgeRows(makeRowComponents({ count: 5 }));
    expect(groups).toHaveLength(0);
  });

  it('produces competing windows rather than one confident row when seven badges align', () => {
    const groups = groupSixBadgeRows(makeRowComponents({ count: 7 }));

    // Two overlapping runs of six. Neither may be selected automatically; the caller
    // resolves this as ambiguity.
    expect(groups.length).toBeGreaterThan(1);
  });

  it('rejects badges scattered off the row axis', () => {
    const components = makeRowComponents();
    components[3] = makeComponent({ id: 3, cx: components[3].cx, cy: components[3].cy + 40 });

    const groups = groupSixBadgeRows(components);

    expect(groups).toHaveLength(0);
  });

  it('rejects a row whose gaps are wildly uneven', () => {
    const components = [
      makeComponent({ id: 0, cx: 100, cy: 100 }),
      makeComponent({ id: 1, cx: 118, cy: 100 }),
      makeComponent({ id: 2, cx: 136, cy: 100 }),
      makeComponent({ id: 3, cx: 190, cy: 100 }),
      makeComponent({ id: 4, cx: 208, cy: 100 }),
      makeComponent({ id: 5, cx: 226, cy: 100 })
    ];

    const groups = groupSixBadgeRows(components);

    expect(groups).toHaveLength(0);
  });

  it('returns members in canonical order however the components were supplied', () => {
    const ordered = makeRowComponents();
    const shuffled = [...ordered].reverse();

    const groups = groupSixBadgeRows(shuffled);

    expect(groups).toHaveLength(1);
    expect(groups[0].members.map(m => m.id)).toEqual([0, 1, 2, 3, 4, 5]);
  });

  it('keeps canonical order when the row is rotated', () => {
    for (const degrees of [-30, -15, 15, 30, 45]) {
      const angle = (degrees * Math.PI) / 180;
      const groups = groupSixBadgeRows(makeRowComponents({ angle }));

      expect(groups.length).toBeGreaterThan(0);
      expect(groups[0].members.map(m => m.id)).toEqual([0, 1, 2, 3, 4, 5]);
    }
  });

  it('refuses to guess gene order when the row runs across the reading direction', () => {
    const vertical = makeRowComponents({ angle: Math.PI / 2 });

    const withoutOrientation = groupSixBadgeRows(vertical);
    expect(withoutOrientation).toHaveLength(0);

    // With the page rotated, the same pixels are read in the right order instead.
    const withOrientation = groupSixBadgeRows(vertical, {
      ...DEFAULT_BUILD_CANDIDATES_OPTIONS,
      readingDirection: readingDirectionForOrientation(270)
    });
    expect(withOrientation).toHaveLength(1);
    expect(withOrientation[0].members.map(m => m.id)).toEqual([0, 1, 2, 3, 4, 5]);
  });

  it('maps every screen orientation onto a reading direction', () => {
    expect(readingDirectionForOrientation(0)).toEqual({ x: 1, y: 0 });
    expect(readingDirectionForOrientation(90)).toEqual({ x: 0, y: -1 });
    expect(readingDirectionForOrientation(180)).toEqual({ x: -1, y: 0 });
    expect(readingDirectionForOrientation(270)).toEqual({ x: 0, y: 1 });
    expect(readingDirectionForOrientation(360)).toEqual({ x: 1, y: 0 });
  });
});

/* ------------------------------------------------------------------ *
 * Quadrilateral derivation and validation
 * ------------------------------------------------------------------ */

describe('Row quadrilateral', () => {
  it('encloses an upright row with padding and canonical corner order', () => {
    const members = makeRowComponents({ cx: 200, cy: 150, spacing: 18 });
    const quad = deriveRowQuad(members, { x: 1, y: 0 }, { x: 0, y: 1 }, 15);

    const [tl, tr, br, bl] = quad;
    expect(tl.x).toBeLessThan(tr.x);
    expect(bl.x).toBeLessThan(br.x);
    expect(tl.y).toBeLessThan(bl.y);
    expect(tr.y).toBeLessThan(br.y);

    // Padded beyond the outermost badge edges so letters are never clipped.
    expect(tl.x).toBeLessThan(members[0].minX);
    expect(tr.x).toBeGreaterThan(members[5].maxX);

    expect(validateQuad(quad, 400, 300).ok).toBe(true);
  });

  it('follows a rotated row', () => {
    const angle = Math.PI / 8;
    const axis = { x: Math.cos(angle), y: Math.sin(angle) };
    const members = makeRowComponents({ angle });
    const quad = deriveRowQuad(members, axis, perpendicular(axis), 15);

    const result = validateQuad(quad, 400, 300);
    expect(result.ok).toBe(true);
    // Top-left stays the corner nearest the first badge.
    expect(quad[0].x).toBeLessThan(quad[1].x);
  });

  it('accepts a moderately foreshortened row', () => {
    const members = Array.from({ length: 6 }, (_, i) => {
      const shrink = 1 - i * 0.06;
      return makeComponent({
        id: i,
        cx: 120 + i * 18,
        cy: 150,
        width: 14 * shrink,
        height: 16 * shrink
      });
    });

    const quad = deriveRowQuad(members, { x: 1, y: 0 }, { x: 0, y: 1 }, 14);
    const result = validateQuad(quad, 400, 300);

    expect(result.ok).toBe(true);
    expect(estimatePerspectiveDegrees(quad)).toBeGreaterThan(0);
  });

  it('rejects degenerate, inverted and self-intersecting quadrilaterals', () => {
    const collapsed: Quad = [
      { x: 10, y: 10 },
      { x: 10.5, y: 10 },
      { x: 10.5, y: 10.5 },
      { x: 10, y: 10.5 }
    ];
    expect(validateQuad(collapsed, 400, 300)).toEqual({ ok: false, reason: 'degenerate' });

    const bowtie: Quad = [
      { x: 20, y: 20 },
      { x: 120, y: 60 },
      { x: 20, y: 60 },
      { x: 120, y: 20 }
    ];
    expect(validateQuad(bowtie, 400, 300).ok).toBe(false);

    const nonFinite: Quad = [
      { x: NaN, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 30 },
      { x: 0, y: 30 }
    ];
    expect(validateQuad(nonFinite, 400, 300)).toEqual({ ok: false, reason: 'non-finite' });
  });

  it('rejects a quadrilateral whose shape cannot hold six genes', () => {
    const tooTall: Quad = [
      { x: 20, y: 20 },
      { x: 80, y: 20 },
      { x: 80, y: 160 },
      { x: 20, y: 160 }
    ];
    expect(validateQuad(tooTall, 400, 300)).toEqual({ ok: false, reason: 'aspect-out-of-range' });
  });

  it('flags one corner off-frame as clipped and rejects two', () => {
    const oneOut: Quad = [
      { x: -8, y: 20 },
      { x: 200, y: 20 },
      { x: 200, y: 56 },
      { x: 10, y: 56 }
    ];
    const single = validateQuad(oneOut, 400, 300);
    expect(single.ok).toBe(true);
    expect(single.ok && single.clipped).toBe(true);

    const twoOut: Quad = [
      { x: -8, y: 20 },
      { x: 200, y: 20 },
      { x: 200, y: 56 },
      { x: -8, y: 56 }
    ];
    expect(validateQuad(twoOut, 400, 300)).toEqual({ ok: false, reason: 'mostly-off-frame' });
  });

  it('measures corner drift between frames', () => {
    const a: Quad = [
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 20 },
      { x: 0, y: 20 }
    ];
    const b: Quad = a.map(p => ({ x: p.x + 3, y: p.y + 4 })) as Quad;
    expect(quadDrift(a, b)).toBeCloseTo(5, 5);
  });
});

/* ------------------------------------------------------------------ *
 * Candidate selection
 * ------------------------------------------------------------------ */

describe('Row candidate scoring', () => {
  it('scores a clean tooltip row above a scattered lookalike', () => {
    const frame = createFrame(360, 240);
    drawGeneRow(frame, { cx: 180, cy: 120 });

    const components = detectBadgeCandidates(frame);
    const candidates = buildRowCandidates(components, frame, frame.width, frame.height);

    expect(candidates).toHaveLength(1);
    expect(candidates[0].members).toHaveLength(6);
    expect(candidates[0].score).toBeGreaterThan(0.6);
    expect(candidates[0].scoreParts.contextEvidence).toBeGreaterThan(0.5);
  });

  it('returns two closely scored candidates when two identical rows are visible', () => {
    const frame = createFrame(360, 260);
    drawGeneRow(frame, { cx: 180, cy: 80 });
    drawGeneRow(frame, { cx: 180, cy: 190 });

    const components = detectBadgeCandidates(frame);
    const candidates = buildRowCandidates(components, frame, frame.width, frame.height);

    expect(candidates).toHaveLength(2);
    expect(Math.abs(candidates[0].score - candidates[1].score)).toBeLessThan(0.08);
  });

  it('gives no candidate when the row is incomplete', () => {
    const frame = createFrame(360, 240);
    drawGeneRow(frame, { cx: 180, cy: 120, count: 5 });

    const components = detectBadgeCandidates(frame);
    const candidates = buildRowCandidates(components, frame, frame.width, frame.height);

    expect(candidates).toHaveLength(0);
  });

  it('rewards persistence supplied by the tracker', () => {
    const frame = createFrame(360, 240);
    drawGeneRow(frame, { cx: 180, cy: 120 });
    const components = detectBadgeCandidates(frame);

    const cold = buildRowCandidates(components, frame, frame.width, frame.height);
    const warm = buildRowCandidates(
      components,
      frame,
      frame.width,
      frame.height,
      DEFAULT_BUILD_CANDIDATES_OPTIONS,
      () => 1
    );

    expect(warm[0].score).toBeGreaterThan(cold[0].score);
  });
});

/* ------------------------------------------------------------------ *
 * Perspective normalisation
 * ------------------------------------------------------------------ */

describe('Perspective normalisation', () => {
  it('solves a homography that maps the corners exactly', () => {
    const from: Quad = [
      { x: 0, y: 0 },
      { x: 100, y: 0 },
      { x: 100, y: 50 },
      { x: 0, y: 50 }
    ];
    const to: Quad = [
      { x: 12, y: 20 },
      { x: 180, y: 8 },
      { x: 172, y: 74 },
      { x: 20, y: 66 }
    ];

    const h = solveHomography(from, to);
    expect(h).not.toBeNull();

    for (let i = 0; i < 4; i++) {
      const mapped = applyHomography(h!, from[i].x, from[i].y);
      expect(mapped.x).toBeCloseTo(to[i].x, 6);
      expect(mapped.y).toBeCloseTo(to[i].y, 6);
    }
  });

  it('returns null for degenerate correspondences', () => {
    const collapsed: Quad = [
      { x: 5, y: 5 },
      { x: 5, y: 5 },
      { x: 5, y: 5 },
      { x: 5, y: 5 }
    ];
    const rect: Quad = [
      { x: 0, y: 0 },
      { x: 10, y: 0 },
      { x: 10, y: 10 },
      { x: 0, y: 10 }
    ];
    expect(solveHomography(collapsed, rect)).toBeNull();
  });

  it('straightens a rotated row into left-to-right order', () => {
    const frame = createFrame(200, 200, [0, 0, 0]);
    const angle = Math.PI / 6;
    const axis = { x: Math.cos(angle), y: Math.sin(angle) };
    const normal = perpendicular(axis);
    const center = { x: 100, y: 100 };

    // Left half red, right half green, drawn along a rotated strip.
    for (let t = -60; t <= 60; t += 0.25) {
      for (let n = -12; n <= 12; n += 0.25) {
        const color = t < 0 ? RED : GREEN;
        setPixel(frame, center.x + axis.x * t + normal.x * n, center.y + axis.y * t + normal.y * n, color);
      }
    }

    const corner = (t: number, n: number) => ({
      x: center.x + axis.x * t + normal.x * n,
      y: center.y + axis.y * t + normal.y * n
    });
    const quad: Quad = [corner(-60, -12), corner(60, -12), corner(60, 12), corner(-60, 12)];

    const warped = warpQuadToRect(frame, quad, 120, 24);
    expect(warped).not.toBeNull();

    const sampleAt = (x: number, y: number) => {
      const i = (y * warped!.width + x) * 4;
      return [warped!.data[i], warped!.data[i + 1], warped!.data[i + 2]];
    };

    const leftSample = sampleAt(30, 12);
    const rightSample = sampleAt(90, 12);

    expect(leftSample[0]).toBeGreaterThan(leftSample[1]);
    expect(rightSample[1]).toBeGreaterThan(rightSample[0]);
  });

  it('fills outside the source with black rather than inventing content', () => {
    const frame = createFrame(40, 40, [200, 30, 30]);
    const quad: Quad = [
      { x: -60, y: -60 },
      { x: -20, y: -60 },
      { x: -20, y: -50 },
      { x: -60, y: -50 }
    ];

    const warped = warpQuadToRect(frame, quad, 20, 8);

    expect(warped).not.toBeNull();
    expect(Array.from(warped!.data.slice(0, 3))).toEqual([0, 0, 0]);
  });

  it('reports no foreshortening for a rectangle and more for a trapezoid', () => {
    const rect: Quad = [
      { x: 0, y: 0 },
      { x: 120, y: 0 },
      { x: 120, y: 24 },
      { x: 0, y: 24 }
    ];
    const trapezoid: Quad = [
      { x: 0, y: 0 },
      { x: 120, y: 6 },
      { x: 120, y: 22 },
      { x: 0, y: 30 }
    ];

    expect(estimatePerspectiveDegrees(rect)).toBeCloseTo(0, 4);
    expect(estimatePerspectiveDegrees(trapezoid)).toBeGreaterThan(10);
  });

  it('clamps expanded quad bounds to the source image', () => {
    const quad: Quad = [
      { x: 5, y: 5 },
      { x: 95, y: 5 },
      { x: 95, y: 25 },
      { x: 5, y: 25 }
    ];
    expect(quadBounds(quad, 20, 100, 60)).toEqual({ x: 0, y: 0, width: 100, height: 45 });
  });
});

/* ------------------------------------------------------------------ *
 * Quality gates
 * ------------------------------------------------------------------ */

describe('Row quality measurement', () => {
  function makeRow(width: number, height: number, draw: (frame: AnalysisFrame) => void): AnalysisFrame {
    const frame = createFrame(width, height, TOOLTIP);
    draw(frame);
    return frame;
  }

  it('scores crisp letters far above a blurred version of the same row', () => {
    const sharp = makeRow(120, 24, frame => {
      for (let i = 0; i < 6; i++) fillRect(frame, 8 + i * 18, 6, 14 + i * 18, 18, WHITE);
    });

    const blurred = makeRow(120, 24, frame => {
      for (let i = 0; i < 6; i++) {
        for (let x = 5 + i * 18; x <= 17 + i * 18; x++) {
          for (let y = 3; y <= 21; y++) {
            const dx = Math.abs(x - (11 + i * 18)) / 6;
            const dy = Math.abs(y - 12) / 9;
            const falloff = Math.max(0, 1 - Math.hypot(dx, dy));
            const value = 22 + falloff * 200;
            setPixel(frame, x, y, [value, value, value]);
          }
        }
      }
    });

    expect(measureSharpness(sharp)).toBeGreaterThan(measureSharpness(blurred) * 3);
  });

  it('reports exposure ratios for a glared row', () => {
    const glared = makeRow(120, 24, frame => fillRect(frame, 0, 0, 90, 23, [252, 252, 252]));
    const report = measureExposure(glared);

    expect(report.nearWhiteRatio).toBeGreaterThan(0.6);
    expect(report.meanLuminance).toBeGreaterThan(150);
  });

  it('names the distance problem rather than a generic failure', () => {
    const row = makeRow(120, 24, frame => {
      for (let i = 0; i < 6; i++) fillRect(frame, 8 + i * 18, 6, 14 + i * 18, 18, WHITE);
    });

    const tooFar = assessRowQuality({
      normalized: row,
      pixelsPerGene: 12,
      perspectiveDegrees: 5,
      clipped: false,
      isStable: true
    });
    expect(tooFar.issues).toContain('too-far');
    expect(tooFar.issues).not.toContain('too-close');

    const tooClose = assessRowQuality({
      normalized: row,
      pixelsPerGene: 180,
      perspectiveDegrees: 5,
      clipped: false,
      isStable: true
    });
    expect(tooClose.issues).toContain('too-close');
  });

  it('blocks on glare, extreme angle, clipping and movement', () => {
    const glared = makeRow(120, 24, frame => fillRect(frame, 0, 0, 110, 23, [252, 252, 252]));

    const report = assessRowQuality({
      normalized: glared,
      pixelsPerGene: 40,
      perspectiveDegrees: 52,
      clipped: true,
      isStable: false
    });

    expect(report.issues).toEqual(
      expect.arrayContaining(['glare', 'extreme-perspective', 'clipped', 'moving'])
    );
  });

  it('does not call a dark row blurred; it asks for light first', () => {
    const dark = makeRow(120, 24, frame => fillRect(frame, 0, 0, 119, 23, [6, 6, 8]));

    const report = assessRowQuality({
      normalized: dark,
      pixelsPerGene: 40,
      perspectiveDegrees: 5,
      clipped: false,
      isStable: true
    });

    expect(report.issues).toContain('too-dark');
    expect(report.issues).not.toContain('blurred');
  });

  it('passes a clean, steady, well-sized row with no issues at all', () => {
    const row = makeRow(120, 24, frame => {
      for (let i = 0; i < 6; i++) fillRect(frame, 8 + i * 18, 6, 14 + i * 18, 18, WHITE);
    });

    const report = assessRowQuality({
      normalized: row,
      pixelsPerGene: 46,
      perspectiveDegrees: 8,
      clipped: false,
      isStable: true
    });

    expect(report.issues).toEqual([]);
    expect(report.sharpness).toBeGreaterThan(DEFAULT_ROW_QUALITY_THRESHOLDS.minSharpness);
  });
});

describe('Row stability tracking', () => {
  const quadAt = (dx: number, dy: number): Quad => [
    { x: 10 + dx, y: 10 + dy },
    { x: 130 + dx, y: 10 + dy },
    { x: 130 + dx, y: 34 + dy },
    { x: 10 + dx, y: 34 + dy }
  ];

  it('requires a steady quadrilateral for the full window', () => {
    const tracker = new RowStabilityTracker(400);

    expect(tracker.update(quadAt(0, 0), 0)).toBe(false);
    expect(tracker.update(quadAt(0, 0), 200)).toBe(false);
    expect(tracker.update(quadAt(0, 0), 500)).toBe(true);
  });

  it('restarts the window when the row jumps', () => {
    const tracker = new RowStabilityTracker(400);

    tracker.update(quadAt(0, 0), 0);
    expect(tracker.update(quadAt(0, 0), 500)).toBe(true);

    expect(tracker.update(quadAt(30, 0), 600)).toBe(false);
    expect(tracker.update(quadAt(30, 0), 700)).toBe(false);
    expect(tracker.update(quadAt(30, 0), 1100)).toBe(true);
  });

  it('forgets everything on reset', () => {
    const tracker = new RowStabilityTracker(400);
    tracker.update(quadAt(0, 0), 0);
    tracker.update(quadAt(0, 0), 500);

    tracker.reset();

    expect(tracker.update(quadAt(0, 0), 600)).toBe(false);
  });
});

describe('Vector helpers', () => {
  it('normalizes and keeps the perpendicular pointing below the row', () => {
    expect(normalize({ x: 3, y: 4 })).toEqual({ x: 0.6, y: 0.8 });
    expect(normalize({ x: 0, y: 0 })).toEqual({ x: 0, y: 0 });
    expect(perpendicular({ x: 1, y: 0 })).toEqual({ x: -0, y: 1 });
  });
});

/* ------------------------------------------------------------------ *
 * Overlay mapping
 * ------------------------------------------------------------------ */

describe('Overlay mapping onto the letterboxed video', () => {
  it('centres a wide frame inside a taller element with vertical bars', () => {
    const rect = computeContainRect(1920, 1080, 400, 800);

    expect(rect).not.toBeNull();
    expect(rect!.scale).toBeCloseTo(400 / 1920, 6);
    expect(rect!.width).toBeCloseTo(400, 6);
    expect(rect!.height).toBeCloseTo(225, 6);
    expect(rect!.offsetX).toBeCloseTo(0, 6);
    expect(rect!.offsetY).toBeCloseTo((800 - 225) / 2, 6);
  });

  it('centres a wide frame inside a wider element with side bars', () => {
    const rect = computeContainRect(1920, 1080, 1600, 400)!;

    expect(rect.scale).toBeCloseTo(400 / 1080, 6);
    expect(rect.offsetY).toBeCloseTo(0, 6);
    expect(rect.offsetX).toBeGreaterThan(0);
  });

  it('rejects degenerate sizes instead of dividing by zero', () => {
    expect(computeContainRect(0, 1080, 400, 800)).toBeNull();
    expect(computeContainRect(1920, 1080, 0, 800)).toBeNull();
    expect(computeContainRect(1920, 1080, 400, 0)).toBeNull();
  });

  it('round-trips a point through the letterbox', () => {
    const rect = computeContainRect(1920, 1080, 400, 800)!;
    const original = { x: 960, y: 540 };

    const onScreen = frameToElement(original, rect);
    expect(onScreen.x).toBeCloseTo(200, 6);
    expect(onScreen.y).toBeCloseTo(400, 6);

    const back = elementToFrame(onScreen, rect);
    expect(back.x).toBeCloseTo(original.x, 6);
    expect(back.y).toBeCloseTo(original.y, 6);
  });

  it('maps a tap in the letterbox bar outside the frame', () => {
    const rect = computeContainRect(1920, 1080, 400, 800)!;
    // Well above the top of the displayed image.
    const point = elementToFrame({ x: 200, y: 10 }, rect);
    expect(point.y).toBeLessThan(0);
  });

  it('renders a quad as SVG points in element space', () => {
    const rect = computeContainRect(100, 100, 200, 200)!;
    const points = quadToSvgPoints(
      [
        { x: 0, y: 0 },
        { x: 100, y: 0 },
        { x: 100, y: 50 },
        { x: 0, y: 50 }
      ],
      rect
    );

    expect(points).toBe('0.0,0.0 200.0,0.0 200.0,100.0 0.0,100.0');
  });
});

/* ------------------------------------------------------------------ *
 * Camera OCR strip
 * ------------------------------------------------------------------ */

describe('Camera gene strip', () => {
  const SLOTS = { baseX: 10, baseY: 8, geneWidth: 40, gapWidth: 12, height: 48 };

  /** A normalised row: dark panel, six green badges, a white bar glyph in each. */
  function normalizedRow(options: { withLetters?: boolean } = {}): RasterImage {
    const { withLetters = true } = options;
    const width = SLOTS.baseX + 6 * (SLOTS.geneWidth + SLOTS.gapWidth);
    const height = SLOTS.baseY * 2 + SLOTS.height;
    const image: RasterImage = {
      data: new Uint8ClampedArray(width * height * 4),
      width,
      height
    };

    const put = (x: number, y: number, c: [number, number, number]) => {
      const i = (y * width + x) * 4;
      image.data[i] = c[0];
      image.data[i + 1] = c[1];
      image.data[i + 2] = c[2];
      image.data[i + 3] = 255;
    };

    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) put(x, y, TOOLTIP);
    }

    for (let slot = 0; slot < 6; slot++) {
      const x0 = SLOTS.baseX + slot * (SLOTS.geneWidth + SLOTS.gapWidth);
      for (let y = SLOTS.baseY; y < SLOTS.baseY + SLOTS.height; y++) {
        for (let x = x0; x < x0 + SLOTS.geneWidth; x++) {
          const isGlyph =
            withLetters &&
            Math.abs(x - (x0 + SLOTS.geneWidth / 2)) < SLOTS.geneWidth * 0.14 &&
            Math.abs(y - (SLOTS.baseY + SLOTS.height / 2)) < SLOTS.height * 0.3;
          put(x, y, isGlyph ? WHITE : GREEN);
        }
      }
    }

    return image;
  }

  it('puts black ink only where the glyphs are, never in the gaps', () => {
    const built = buildCameraGeneStrip(normalizedRow(), SLOTS);
    expect(built).not.toBeNull();

    const { strip } = built!;
    const at = (x: number, y: number) => strip.data[(y * strip.width + x) * 4];

    // Padding corners and the strip's outer border stay white.
    expect(at(2, 2)).toBe(255);
    expect(at(strip.width - 3, strip.height - 3)).toBe(255);

    // This is the defect that produced empty reads: the desktop stitcher's global threshold
    // flipped the white background and every inter-glyph gap to black.
    const cellWidth = Math.round((SLOTS.geneWidth / SLOTS.height) * 120);
    const firstGapX = 16 + cellWidth + 8;
    expect(at(firstGapX, Math.round(strip.height / 2))).toBe(255);
  });

  it('produces ink in a plausible range for six glyphs', () => {
    const built = buildCameraGeneStrip(normalizedRow(), SLOTS)!;
    const coverage = inkCoverage(built.strip);

    expect(coverage).toBeGreaterThan(0.01);
    expect(coverage).toBeLessThan(0.35);
  });

  it('emits six separately usable slot images', () => {
    const built = buildCameraGeneStrip(normalizedRow(), SLOTS)!;

    expect(built.slotImages).toHaveLength(6);
    for (const slot of built.slotImages) {
      expect(slot.width).toBeGreaterThan(0);
      expect(inkCoverage(slot)).toBeGreaterThan(0.01);
    }
  });

  it('stays blank rather than inventing strokes when a badge has no glyph', () => {
    const built = buildCameraGeneStrip(normalizedRow({ withLetters: false }), SLOTS)!;
    // A flat badge has no real contrast, so the fixed fallback cut must not turn the badge
    // body itself into a letter.
    expect(inkCoverage(built.strip)).toBeLessThan(0.02);
  });

  it('scales the cell to the badge aspect ratio', () => {
    const wide = buildCameraGeneStrip(normalizedRow(), { ...SLOTS, geneWidth: 80 })!;
    const narrow = buildCameraGeneStrip(normalizedRow(), { ...SLOTS, geneWidth: 20 })!;

    expect(wide.strip.width).toBeGreaterThan(narrow.strip.width);
    expect(wide.strip.height).toBe(narrow.strip.height);
  });

  it('rejects degenerate slot geometry', () => {
    expect(buildCameraGeneStrip(normalizedRow(), { ...SLOTS, geneWidth: 0 })).toBeNull();
    expect(buildCameraGeneStrip(normalizedRow(), { ...SLOTS, height: 0 })).toBeNull();
  });
});
