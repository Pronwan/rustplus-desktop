import { AnalysisFrame, BadgeComponent, meanLuminance } from './rasterOps.ts';

/**
 * Row geometry for the phone camera locator.
 *
 * Groups badge components into six-gene rows, scores them, and turns the winning group into
 * a quadrilateral ready for perspective normalisation. Pure functions throughout: every rule
 * below is exercised directly from synthetic component lists in the tests.
 */

export interface Vec2 {
  x: number;
  y: number;
}

/** Canonical corner order: top-left, top-right, bottom-right, bottom-left in reading space. */
export type Quad = [Vec2, Vec2, Vec2, Vec2];

export interface RowScoreParts {
  sixItemGeometry: number;
  spacingConsistency: number;
  sizeConsistency: number;
  badgeShape: number;
  colorEvidence: number;
  contextEvidence: number;
  temporalPersistence: number;
}

export interface RowCandidate {
  /** Members ordered along the canonical reading direction, gene 1 first. */
  members: BadgeComponent[];
  axis: Vec2;
  normal: Vec2;
  quad: Quad;
  score: number;
  scoreParts: RowScoreParts;
  medianBadgeSide: number;
  meanSpacing: number;
  /** Row centre in analysis-frame coordinates. */
  center: Vec2;
}

export interface RowGroupingOptions {
  /**
   * Unit vector for "left to right" as the user reads it, expressed in analysis-frame
   * coordinates. Derived from the screen orientation so a rolled phone still yields the
   * genes in the right order.
   */
  readingDirection: Vec2;
  /**
   * A row axis must line up with the reading direction at least this well. Below it the
   * gene order cannot be established from geometry, and the row is dropped rather than
   * guessed at.
   */
  minDirectionalConfidence: number;
  /** Half-width of the band around the fitted axis, as a multiple of the badge side. */
  bandFactor: number;
  /** Allowed badge-size ratio between two members, absorbing perspective foreshortening. */
  maxSizeRatio: number;
  /** Adjacent-centre spacing bounds, as multiples of the badge side. */
  minSpacingFactor: number;
  maxSpacingFactor: number;
  /** Largest tolerated coefficient of variation across the five adjacent gaps. */
  maxSpacingVariation: number;
  /** Quadrilateral padding, as a fraction of the badge side, so letters are not clipped. */
  paddingFactor: number;
}

export const DEFAULT_ROW_GROUPING_OPTIONS: RowGroupingOptions = {
  readingDirection: { x: 1, y: 0 },
  minDirectionalConfidence: 0.15,
  bandFactor: 0.7,
  maxSizeRatio: 1.7,
  minSpacingFactor: 0.8,
  maxSpacingFactor: 3.4,
  maxSpacingVariation: 0.35,
  paddingFactor: 0.28
};

export const GENES_PER_ROW = 6;

/* ------------------------------------------------------------------ *
 * Vector helpers
 * ------------------------------------------------------------------ */

export function normalize(v: Vec2): Vec2 {
  const length = Math.hypot(v.x, v.y);
  if (length === 0) return { x: 0, y: 0 };
  return { x: v.x / length, y: v.y / length };
}

export function dot(a: Vec2, b: Vec2): number {
  return a.x * b.x + a.y * b.y;
}

/** Rotates by 90 degrees so that, with y pointing down, the result points "below" the row. */
export function perpendicular(axis: Vec2): Vec2 {
  return { x: -axis.y, y: axis.x };
}

function badgeSide(c: BadgeComponent): number {
  return (c.width + c.height) / 2;
}

function median(values: number[]): number {
  if (values.length === 0) return 0;
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 === 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
}

function mean(values: number[]): number {
  if (values.length === 0) return 0;
  let sum = 0;
  for (const v of values) sum += v;
  return sum / values.length;
}

/** Coefficient of variation. Returns 0 for an empty or zero-mean set. */
function variation(values: number[]): number {
  const m = mean(values);
  if (m === 0) return 0;
  let acc = 0;
  for (const v of values) acc += (v - m) * (v - m);
  return Math.sqrt(acc / values.length) / m;
}

function clamp01(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return value < 0 ? 0 : value > 1 ? 1 : value;
}

/** Least-squares fit of `v = slope * u + intercept`. Falls back to a flat line if degenerate. */
export function fitLine(points: Array<{ u: number; v: number }>): { slope: number; intercept: number } {
  const n = points.length;
  if (n === 0) return { slope: 0, intercept: 0 };

  let sumU = 0;
  let sumV = 0;
  for (const p of points) {
    sumU += p.u;
    sumV += p.v;
  }
  const meanU = sumU / n;
  const meanV = sumV / n;

  let numerator = 0;
  let denominator = 0;
  for (const p of points) {
    const du = p.u - meanU;
    numerator += du * (p.v - meanV);
    denominator += du * du;
  }

  if (denominator < 1e-9) return { slope: 0, intercept: meanV };
  const slope = numerator / denominator;
  return { slope, intercept: meanV - slope * meanU };
}

/* ------------------------------------------------------------------ *
 * Reading direction
 * ------------------------------------------------------------------ */

/**
 * The camera delivers frames in sensor orientation while the page rotates around them, so
 * "left to right" in the frame depends on the current screen orientation angle.
 */
export function readingDirectionForOrientation(angleDegrees: number): Vec2 {
  const normalized = ((Math.round(angleDegrees / 90) * 90) % 360 + 360) % 360;
  switch (normalized) {
    case 90:
      return { x: 0, y: -1 };
    case 180:
      return { x: -1, y: 0 };
    case 270:
      return { x: 0, y: 1 };
    default:
      return { x: 1, y: 0 };
  }
}

/* ------------------------------------------------------------------ *
 * Grouping
 * ------------------------------------------------------------------ */

function sizeCompatible(a: BadgeComponent, b: BadgeComponent, maxRatio: number): boolean {
  const sideA = badgeSide(a);
  const sideB = badgeSide(b);
  if (sideA <= 0 || sideB <= 0) return false;
  const ratio = sideA > sideB ? sideA / sideB : sideB / sideA;
  return ratio <= maxRatio;
}

/**
 * Finds every plausible run of six badges lying along a common axis.
 *
 * Pairs of components propose an axis; components inside a narrow band around that axis are
 * ordered along it, and every window of six consecutive members is tested for spacing
 * consistency. Duplicate member sets collapse into one candidate.
 */
export function groupSixBadgeRows(
  components: BadgeComponent[],
  options: RowGroupingOptions = DEFAULT_ROW_GROUPING_OPTIONS
): Array<{ members: BadgeComponent[]; axis: Vec2; normal: Vec2; medianBadgeSide: number; meanSpacing: number }> {
  const results: Array<{
    members: BadgeComponent[];
    axis: Vec2;
    normal: Vec2;
    medianBadgeSide: number;
    meanSpacing: number;
  }> = [];

  if (components.length < GENES_PER_ROW) return results;

  const reading = normalize(options.readingDirection);
  const seen = new Set<string>();

  for (const a of components) {
    for (const b of components) {
      if (a === b) continue;
      if (!sizeCompatible(a, b, options.maxSizeRatio)) continue;

      const delta = { x: b.cx - a.cx, y: b.cy - a.cy };
      const distance = Math.hypot(delta.x, delta.y);
      if (distance < 1e-6) continue;

      const referenceSide = (badgeSide(a) + badgeSide(b)) / 2;
      if (referenceSide <= 0) continue;
      if (distance < referenceSide * options.minSpacingFactor) continue;
      if (distance > referenceSide * options.maxSpacingFactor) continue;

      const axis = { x: delta.x / distance, y: delta.y / distance };
      // Only axes pointing the way the user reads are considered. This halves the search and,
      // more importantly, is what stops a rolled phone from emitting a reversed gene string.
      if (dot(axis, reading) < options.minDirectionalConfidence) continue;

      const normal = perpendicular(axis);
      const band = referenceSide * options.bandFactor;

      const inBand: Array<{ component: BadgeComponent; t: number }> = [];
      for (const c of components) {
        if (!sizeCompatible(a, c, options.maxSizeRatio)) continue;
        const offset = { x: c.cx - a.cx, y: c.cy - a.cy };
        if (Math.abs(dot(offset, normal)) > band) continue;
        inBand.push({ component: c, t: dot(offset, axis) });
      }

      if (inBand.length < GENES_PER_ROW) continue;
      inBand.sort((p, q) => p.t - q.t);

      for (let start = 0; start + GENES_PER_ROW <= inBand.length; start++) {
        const window = inBand.slice(start, start + GENES_PER_ROW);
        const key = window
          .map(w => w.component.id)
          .sort((p, q) => p - q)
          .join(',');
        if (seen.has(key)) continue;

        const spacings: number[] = [];
        for (let k = 1; k < window.length; k++) {
          spacings.push(window[k].t - window[k - 1].t);
        }
        if (spacings.some(s => s <= 0)) continue;
        if (variation(spacings) > options.maxSpacingVariation) continue;

        const members = window.map(w => w.component);
        const sides = members.map(badgeSide);
        const medianSide = median(sides);
        const meanSpacing = mean(spacings);
        if (meanSpacing < medianSide * options.minSpacingFactor) continue;
        if (meanSpacing > medianSide * options.maxSpacingFactor) continue;

        seen.add(key);
        results.push({ members, axis, normal, medianBadgeSide: medianSide, meanSpacing });
      }
    }
  }

  return results;
}

/* ------------------------------------------------------------------ *
 * Scoring
 * ------------------------------------------------------------------ */

/**
 * Weights from the implementation plan. These are beta starting values tuned against
 * labelled fixtures in Phase 6, not product constants.
 */
export const ROW_SCORE_WEIGHTS = {
  sixItemGeometry: 0.25,
  spacingConsistency: 0.2,
  sizeConsistency: 0.15,
  badgeShape: 0.15,
  colorEvidence: 0.1,
  contextEvidence: 0.1,
  temporalPersistence: 0.05
} as const;

function collinearityScore(members: BadgeComponent[], axis: Vec2, normal: Vec2, side: number): number {
  if (side <= 0) return 0;
  const origin = members[0];
  let maxOffset = 0;
  for (const m of members) {
    const offset = Math.abs(dot({ x: m.cx - origin.cx, y: m.cy - origin.cy }, normal));
    if (offset > maxOffset) maxOffset = offset;
  }
  void axis;
  return clamp01(1 - maxOffset / (side * 0.6));
}

function badgeShapeScore(members: BadgeComponent[]): number {
  const scores = members.map(m => {
    const aspectScore = clamp01(1 - Math.abs(m.aspect - 1) / 0.9);
    const fillScore = clamp01((m.fillRatio - 0.3) / 0.5);
    return aspectScore * 0.5 + fillScore * 0.5;
  });
  return mean(scores);
}

/**
 * Rust shows genetics inside a dark tooltip panel. Sampling the strip immediately above and
 * below the badges separates a real tooltip from six unrelated coloured blobs.
 */
function contextScore(
  frame: AnalysisFrame | null,
  members: BadgeComponent[],
  normal: Vec2,
  side: number
): number {
  if (!frame || side <= 0) return 0.5;

  const center = rowCenter(members);
  const offset = side * 1.3;
  const halfLength = (Math.hypot(
    members[members.length - 1].cx - members[0].cx,
    members[members.length - 1].cy - members[0].cy
  ) + side) / 2;
  const halfBand = side * 0.35;

  const above = meanLuminance(
    frame,
    center.x - halfLength,
    center.y - offset - halfBand,
    center.x + halfLength,
    center.y - offset + halfBand
  );
  const below = meanLuminance(
    frame,
    center.x - halfLength,
    center.y + offset - halfBand,
    center.x + halfLength,
    center.y + offset + halfBand
  );
  void normal;

  const darkness = 1 - Math.min(above, below) / 255;
  return clamp01((darkness - 0.35) / 0.45);
}

export function scoreRowCandidate(
  group: { members: BadgeComponent[]; axis: Vec2; normal: Vec2; medianBadgeSide: number; meanSpacing: number },
  frame: AnalysisFrame | null,
  temporalPersistence = 0
): { score: number; parts: RowScoreParts } {
  const { members, axis, normal, medianBadgeSide } = group;

  const spacings: number[] = [];
  for (let k = 1; k < members.length; k++) {
    spacings.push(Math.hypot(members[k].cx - members[k - 1].cx, members[k].cy - members[k - 1].cy));
  }

  const parts: RowScoreParts = {
    sixItemGeometry: collinearityScore(members, axis, normal, medianBadgeSide),
    spacingConsistency: clamp01(1 - variation(spacings) / 0.35),
    sizeConsistency: clamp01(1 - variation(members.map(badgeSide)) / 0.4),
    badgeShape: badgeShapeScore(members),
    colorEvidence: mean(members.map(m => m.colorStrength)),
    contextEvidence: contextScore(frame, members, normal, medianBadgeSide),
    temporalPersistence: clamp01(temporalPersistence)
  };

  let score = 0;
  for (const key of Object.keys(ROW_SCORE_WEIGHTS) as Array<keyof RowScoreParts>) {
    score += ROW_SCORE_WEIGHTS[key] * parts[key];
  }

  return { score, parts };
}

export function rowCenter(members: BadgeComponent[]): Vec2 {
  let x = 0;
  let y = 0;
  for (const m of members) {
    x += m.cx;
    y += m.cy;
  }
  return { x: x / members.length, y: y / members.length };
}

/* ------------------------------------------------------------------ *
 * Quadrilateral derivation and validation
 * ------------------------------------------------------------------ */

export type QuadRejectionReason =
  | 'non-finite'
  | 'degenerate'
  | 'self-intersecting'
  | 'aspect-out-of-range'
  | 'mostly-off-frame';

export type QuadResult =
  | { ok: true; quad: Quad; clipped: boolean }
  | { ok: false; reason: QuadRejectionReason };

/**
 * Builds the row quadrilateral from the six badges.
 *
 * The top and bottom edges are fitted through the badges' upper and lower extents, so a row
 * seen at an angle produces a trapezoid rather than a rotated rectangle. Left and right come
 * from the outer extents of the first and last badge, padded so letters are never clipped.
 */
export function deriveRowQuad(
  members: BadgeComponent[],
  axis: Vec2,
  normal: Vec2,
  medianBadgeSide: number,
  paddingFactor = DEFAULT_ROW_GROUPING_OPTIONS.paddingFactor
): Quad {
  const origin = rowCenter(members);

  const project = (x: number, y: number) => ({
    u: dot({ x: x - origin.x, y: y - origin.y }, axis),
    v: dot({ x: x - origin.x, y: y - origin.y }, normal)
  });

  const topPoints: Array<{ u: number; v: number }> = [];
  const bottomPoints: Array<{ u: number; v: number }> = [];
  const memberSpans: Array<{ uMin: number; uMax: number }> = [];

  for (const m of members) {
    const corners = [
      project(m.minX, m.minY),
      project(m.maxX, m.minY),
      project(m.maxX, m.maxY),
      project(m.minX, m.maxY)
    ];

    let uMin = Infinity;
    let uMax = -Infinity;
    let vMin = Infinity;
    let vMax = -Infinity;
    for (const c of corners) {
      if (c.u < uMin) uMin = c.u;
      if (c.u > uMax) uMax = c.u;
      if (c.v < vMin) vMin = c.v;
      if (c.v > vMax) vMax = c.v;
    }

    const uCenter = (uMin + uMax) / 2;
    topPoints.push({ u: uCenter, v: vMin });
    bottomPoints.push({ u: uCenter, v: vMax });
    memberSpans.push({ uMin, uMax });
  }

  const top = fitLine(topPoints);
  const bottom = fitLine(bottomPoints);

  const pad = medianBadgeSide * paddingFactor;
  const uLeft = memberSpans[0].uMin - pad;
  const uRight = memberSpans[memberSpans.length - 1].uMax + pad;

  const toFrame = (u: number, v: number): Vec2 => ({
    x: origin.x + axis.x * u + normal.x * v,
    y: origin.y + axis.y * u + normal.y * v
  });

  return [
    toFrame(uLeft, top.slope * uLeft + top.intercept - pad),
    toFrame(uRight, top.slope * uRight + top.intercept - pad),
    toFrame(uRight, bottom.slope * uRight + bottom.intercept + pad),
    toFrame(uLeft, bottom.slope * uLeft + bottom.intercept + pad)
  ];
}

function cross(o: Vec2, a: Vec2, b: Vec2): number {
  return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}

export function quadEdgeLengths(quad: Quad): { top: number; right: number; bottom: number; left: number } {
  return {
    top: Math.hypot(quad[1].x - quad[0].x, quad[1].y - quad[0].y),
    right: Math.hypot(quad[2].x - quad[1].x, quad[2].y - quad[1].y),
    bottom: Math.hypot(quad[2].x - quad[3].x, quad[2].y - quad[3].y),
    left: Math.hypot(quad[3].x - quad[0].x, quad[3].y - quad[0].y)
  };
}

export interface QuadValidationOptions {
  minEdgeLength: number;
  minAspect: number;
  maxAspect: number;
  /** How many corners may fall outside the frame before the quad is thrown away. */
  maxOutsideCorners: number;
}

export const DEFAULT_QUAD_VALIDATION: QuadValidationOptions = {
  minEdgeLength: 4,
  minAspect: 2.5,
  maxAspect: 14,
  maxOutsideCorners: 1
};

export function validateQuad(
  quad: Quad,
  frameWidth: number,
  frameHeight: number,
  options: QuadValidationOptions = DEFAULT_QUAD_VALIDATION
): QuadResult {
  for (const p of quad) {
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) {
      return { ok: false, reason: 'non-finite' };
    }
  }

  const edges = quadEdgeLengths(quad);
  if (Math.min(edges.top, edges.right, edges.bottom, edges.left) < options.minEdgeLength) {
    return { ok: false, reason: 'degenerate' };
  }

  // A convex quad in canonical order has the same turn direction at all four corners.
  // Any sign flip means the shape is inverted or self-intersecting.
  const turns = [
    cross(quad[0], quad[1], quad[2]),
    cross(quad[1], quad[2], quad[3]),
    cross(quad[2], quad[3], quad[0]),
    cross(quad[3], quad[0], quad[1])
  ];
  const positive = turns.filter(t => t > 0).length;
  const negative = turns.filter(t => t < 0).length;
  if (positive > 0 && negative > 0) {
    return { ok: false, reason: 'self-intersecting' };
  }
  if (positive === 0 && negative === 0) {
    return { ok: false, reason: 'degenerate' };
  }

  const width = (edges.top + edges.bottom) / 2;
  const height = (edges.left + edges.right) / 2;
  if (height <= 0) return { ok: false, reason: 'degenerate' };

  const aspect = width / height;
  if (aspect < options.minAspect || aspect > options.maxAspect) {
    return { ok: false, reason: 'aspect-out-of-range' };
  }

  const outside = quad.filter(
    p => p.x < 0 || p.y < 0 || p.x > frameWidth - 1 || p.y > frameHeight - 1
  ).length;
  if (outside > options.maxOutsideCorners) {
    return { ok: false, reason: 'mostly-off-frame' };
  }

  return { ok: true, quad, clipped: outside > 0 };
}

export function quadCenter(quad: Quad): Vec2 {
  let x = 0;
  let y = 0;
  for (const p of quad) {
    x += p.x;
    y += p.y;
  }
  return { x: x / 4, y: y / 4 };
}

/** Mean corner-to-corner distance between two quads. Used to judge tracking continuity. */
export function quadDrift(a: Quad, b: Quad): number {
  let total = 0;
  for (let i = 0; i < 4; i++) {
    total += Math.hypot(a[i].x - b[i].x, a[i].y - b[i].y);
  }
  return total / 4;
}

/* ------------------------------------------------------------------ *
 * Full candidate construction
 * ------------------------------------------------------------------ */

export interface BuildCandidatesOptions extends RowGroupingOptions {
  validation: QuadValidationOptions;
}

export const DEFAULT_BUILD_CANDIDATES_OPTIONS: BuildCandidatesOptions = {
  ...DEFAULT_ROW_GROUPING_OPTIONS,
  validation: DEFAULT_QUAD_VALIDATION
};

export interface BuiltCandidate extends RowCandidate {
  clipped: boolean;
}

/**
 * Grouping, quadrilateral derivation, validation and scoring in one pass, returned
 * highest-score first. Groups whose quadrilateral fails validation are dropped here rather
 * than being handed downstream in a broken state.
 */
export function buildRowCandidates(
  components: BadgeComponent[],
  frame: AnalysisFrame | null,
  frameWidth: number,
  frameHeight: number,
  options: BuildCandidatesOptions = DEFAULT_BUILD_CANDIDATES_OPTIONS,
  persistenceFor: (center: Vec2) => number = () => 0
): BuiltCandidate[] {
  const groups = groupSixBadgeRows(components, options);
  const candidates: BuiltCandidate[] = [];

  for (const group of groups) {
    const quad = deriveRowQuad(
      group.members,
      group.axis,
      group.normal,
      group.medianBadgeSide,
      options.paddingFactor
    );

    const validation = validateQuad(quad, frameWidth, frameHeight, options.validation);
    if (!validation.ok) continue;

    const center = rowCenter(group.members);
    const { score, parts } = scoreRowCandidate(group, frame, persistenceFor(center));

    candidates.push({
      members: group.members,
      axis: group.axis,
      normal: group.normal,
      quad: validation.quad,
      clipped: validation.clipped,
      score,
      scoreParts: parts,
      medianBadgeSide: group.medianBadgeSide,
      meanSpacing: group.meanSpacing,
      center
    });
  }

  candidates.sort((a, b) => b.score - a.score);
  return candidates;
}
