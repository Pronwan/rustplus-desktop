import {
  CameraAnalysisResult,
  CameraFrameAnalyzer,
  CameraOverlay,
  CameraRowSlots,
  CameraTarget,
  Point
} from './scannerTypes.ts';
import { CAMERA_SCANNER_CONFIG } from './cameraScannerConfig.ts';
import {
  AnalysisFrame,
  cropAnalysisFrame,
  detectBadgeCandidates
} from './vision/rasterOps.ts';
import {
  BuiltCandidate,
  DEFAULT_BUILD_CANDIDATES_OPTIONS,
  DEFAULT_QUAD_VALIDATION,
  GENES_PER_ROW,
  Quad,
  Vec2,
  buildRowCandidates,
  quadCenter,
  quadDrift,
  quadEdgeLengths,
  readingDirectionForOrientation
} from './vision/geometry.ts';
import {
  RasterImage,
  applyHomography,
  estimatePerspectiveDegrees,
  quadBounds,
  scaleQuad,
  solveHomography,
  translateQuad,
  warpQuadToRect
} from './vision/perspective.ts';
import {
  DEFAULT_ROW_QUALITY_THRESHOLDS,
  RowQualityThresholds,
  RowStabilityTracker,
  assessRowQuality
} from './vision/quality.ts';
import { CameraFrameGrabber, CanvasFrameGrabber } from './vision/frameGrabber.ts';

/**
 * Finds, selects, tracks, straightens and grades the six-gene row in a camera frame.
 *
 * No saved coordinates and no calibration: every frame either produces a target the OCR
 * stage is allowed to read, or an explicit reason why it may not.
 */

export interface DynamicGeneLocatorOptions {
  grabber?: CameraFrameGrabber;
  qualityThresholds?: RowQualityThresholds;
  /** Screen orientation angle in degrees, used to establish the reading direction. */
  orientationAngle?: () => number;
  discoveryWidth?: number;
}

interface TrackedTarget {
  id: string;
  quad: Quad;
  center: Vec2;
  medianBadgeSide: number;
  score: number;
  /** Consecutive frames this target has been re-found. */
  presence: number;
  lastSeenAt: number;
}

function defaultOrientationAngle(): number {
  if (typeof window === 'undefined') return 0;
  const angle = window.screen?.orientation?.angle;
  return typeof angle === 'number' ? angle : 0;
}

function emptyOverlay(frameWidth: number, frameHeight: number): CameraOverlay {
  return { frameWidth, frameHeight, target: null, targetId: null, candidates: [] };
}

function toPointQuad(quad: Quad): [Point, Point, Point, Point] {
  return [
    { x: quad[0].x, y: quad[0].y },
    { x: quad[1].x, y: quad[1].y },
    { x: quad[2].x, y: quad[2].y },
    { x: quad[3].x, y: quad[3].y }
  ];
}

/** Convex point-in-quad test, used to resolve a tap onto a candidate. */
function quadContains(quad: Quad, point: Point): boolean {
  let positive = 0;
  let negative = 0;

  for (let i = 0; i < 4; i++) {
    const a = quad[i];
    const b = quad[(i + 1) % 4];
    const cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
    if (cross > 0) positive++;
    else if (cross < 0) negative++;
  }

  return positive === 0 || negative === 0;
}

function candidateId(candidate: BuiltCandidate): string {
  // Quantised so the identity survives sub-pixel jitter but changes when the user hovers a
  // genuinely different plant.
  const c = candidate.center;
  return `${Math.round(c.x / 8)}:${Math.round(c.y / 8)}`;
}

export class DynamicGeneLocator implements CameraFrameAnalyzer {
  private readonly grabber: CameraFrameGrabber;
  private readonly qualityThresholds: RowQualityThresholds;
  private readonly orientationAngle: () => number;
  private readonly stability = new RowStabilityTracker(
    CAMERA_SCANNER_CONFIG.quality.minStableMs,
    CAMERA_SCANNER_CONFIG.quality.maxDriftFraction
  );

  private discoveryWidth: number;
  private tracked: TrackedTarget | null = null;
  private lastDiscoveryAt = 0;
  private trackFailures = 0;
  private pendingSelection: Point | null = null;
  private warpBuffer: RasterImage | undefined;
  private disposed = false;

  constructor(options: DynamicGeneLocatorOptions = {}) {
    this.grabber = options.grabber ?? new CanvasFrameGrabber();
    this.qualityThresholds = options.qualityThresholds ?? DEFAULT_ROW_QUALITY_THRESHOLDS;
    this.orientationAngle = options.orientationAngle ?? defaultOrientationAngle;
    this.discoveryWidth = options.discoveryWidth ?? CAMERA_SCANNER_CONFIG.analysis.maxDiscoveryWidth;
  }

  async analyze(video: HTMLVideoElement, nowMs: number): Promise<CameraAnalysisResult> {
    if (this.disposed) return this.searching(emptyOverlay(0, 0));

    const frame = this.grabber.grabAnalysis(video, this.discoveryWidth);
    if (!frame) return this.searching(emptyOverlay(0, 0));

    const candidates = this.findCandidates(frame, nowMs);
    const overlay: CameraOverlay = {
      frameWidth: frame.width,
      frameHeight: frame.height,
      target: null,
      targetId: null,
      candidates: candidates.map(c => ({ id: candidateId(c), corners: toPointQuad(c.quad) }))
    };

    const selection = this.select(candidates, nowMs);

    if (selection.kind === 'none') {
      // Hold the lock briefly through a detection miss instead of thrashing to "searching"
      // and discarding the confirmation window.
      if (this.tracked && nowMs - this.tracked.lastSeenAt < CAMERA_SCANNER_CONFIG.tracking.lostGraceMs) {
        overlay.target = toPointQuad(this.tracked.quad);
        overlay.targetId = this.tracked.id;
        return {
          phase: 'quality-blocked',
          qualityIssues: ['moving'],
          candidateCount: 1,
          activeTarget: null,
          overlay
        };
      }
      this.loseTarget();
      return this.searching(overlay);
    }

    if (selection.kind === 'ambiguous') {
      // Two plausible rows. Fail closed and ask for a tap rather than picking one.
      this.loseTarget();
      return {
        phase: 'ambiguous',
        qualityIssues: [],
        candidateCount: selection.count,
        activeTarget: null,
        overlay
      };
    }

    const candidate = selection.candidate;
    overlay.target = toPointQuad(candidate.quad);
    overlay.targetId = candidateId(candidate);
    this.rememberTarget(candidate, nowMs);

    return this.evaluateTarget(video, frame, candidate, overlay, nowMs);
  }

  /* ---------------------------------------------------------------- *
   * Discovery and tracking
   * ---------------------------------------------------------------- */

  private findCandidates(frame: AnalysisFrame, nowMs: number): BuiltCandidate[] {
    const shouldRediscover =
      !this.tracked ||
      this.trackFailures > 0 ||
      nowMs - this.lastDiscoveryAt >= CAMERA_SCANNER_CONFIG.cadence.redetectIntervalMs;

    if (!shouldRediscover && this.tracked) {
      const local = this.searchNearTarget(frame, this.tracked);
      if (local.length > 0) return local;
      // A local miss is not yet a lost target; the next tick falls back to full discovery.
      this.trackFailures++;
      return [];
    }

    this.lastDiscoveryAt = nowMs;
    this.trackFailures = 0;
    return this.discover(frame, frame.width, frame.height, 0, 0, false);
  }

  /** Runs the full pipeline over a region, returning candidates in whole-frame coordinates. */
  private discover(
    frame: AnalysisFrame,
    frameWidth: number,
    frameHeight: number,
    offsetX: number,
    offsetY: number,
    isCropped: boolean
  ): BuiltCandidate[] {
    const components = detectBadgeCandidates(frame, undefined, undefined, {
      width: frameWidth,
      height: frameHeight
    });
    if (components.length < GENES_PER_ROW) return [];

    const options = {
      ...DEFAULT_BUILD_CANDIDATES_OPTIONS,
      readingDirection: readingDirectionForOrientation(this.orientationAngle()),
      // Inside a tracking crop the frame edge is an artefact of where we chose to look, not
      // a real boundary. Off-frame judgement is deferred until coordinates are lifted back.
      validation: isCropped
        ? { ...DEFAULT_QUAD_VALIDATION, maxOutsideCorners: 4 }
        : DEFAULT_QUAD_VALIDATION
    };

    const tracked = this.tracked;
    const persistenceFor = (center: Vec2): number => {
      if (!tracked) return 0;
      const radius = Math.max(12, tracked.medianBadgeSide * CAMERA_SCANNER_CONFIG.selection.continuityRadiusFactor);
      const distance = Math.hypot(center.x + offsetX - tracked.center.x, center.y + offsetY - tracked.center.y);
      if (distance > radius) return 0;
      return Math.min(1, tracked.presence / CAMERA_SCANNER_CONFIG.tracking.persistenceFrames);
    };

    const candidates = buildRowCandidates(
      components,
      frame,
      frame.width,
      frame.height,
      options,
      persistenceFor
    );

    if (!isCropped) return candidates;

    // Lift the cropped search back into whole-frame coordinates, then apply the real
    // off-frame rule against the actual frame bounds.
    const lifted: BuiltCandidate[] = [];
    for (const candidate of candidates) {
      const quad = translateQuad(candidate.quad, offsetX, offsetY) as Quad;
      const outside = quad.filter(
        p => p.x < 0 || p.y < 0 || p.x > frameWidth - 1 || p.y > frameHeight - 1
      ).length;
      if (outside > DEFAULT_QUAD_VALIDATION.maxOutsideCorners) continue;

      lifted.push({
        ...candidate,
        quad,
        center: { x: candidate.center.x + offsetX, y: candidate.center.y + offsetY },
        // The members travel with the quad: the OCR slot measurement projects their bounding
        // boxes through the warp, so they must be in the same space as everything else.
        members: candidate.members.map(member => ({
          ...member,
          minX: member.minX + offsetX,
          maxX: member.maxX + offsetX,
          minY: member.minY + offsetY,
          maxY: member.maxY + offsetY,
          cx: member.cx + offsetX,
          cy: member.cy + offsetY
        })),
        clipped: outside > 0
      });
    }
    return lifted;
  }

  /**
   * Local re-detection around the last known row.
   *
   * Cheaper than re-scanning the whole frame at the tracking rate, and more robust on this
   * content than sparse optical flow: the badges themselves are the features.
   */
  private searchNearTarget(frame: AnalysisFrame, tracked: TrackedTarget): BuiltCandidate[] {
    const edges = quadEdgeLengths(tracked.quad);
    const width = Math.max(edges.top, edges.bottom);
    const height = Math.max(edges.left, edges.right);
    const expansion = CAMERA_SCANNER_CONFIG.tracking.searchExpansion;

    const boxWidth = width * expansion;
    const boxHeight = Math.max(height * expansion * 1.5, height + width * 0.25);

    const crop = cropAnalysisFrame(
      frame,
      tracked.center.x - boxWidth / 2,
      tracked.center.y - boxHeight / 2,
      boxWidth,
      boxHeight
    );
    if (!crop) return [];

    return this.discover(crop.frame, frame.width, frame.height, crop.offsetX, crop.offsetY, true);
  }

  /* ---------------------------------------------------------------- *
   * Selection
   * ---------------------------------------------------------------- */

  private select(
    candidates: BuiltCandidate[],
    nowMs: number
  ):
    | { kind: 'none' }
    | { kind: 'ambiguous'; count: number }
    | { kind: 'target'; candidate: BuiltCandidate } {
    const viable = candidates.filter(c => c.score >= CAMERA_SCANNER_CONFIG.selection.minCandidateScore);
    if (viable.length === 0) return { kind: 'none' };

    // 1. An explicit tap wins, for this appearance only. Nothing is saved.
    if (this.pendingSelection) {
      const tap = this.pendingSelection;
      this.pendingSelection = null;
      const tapped =
        viable.find(c => quadContains(c.quad, tap)) ??
        viable
          .map(c => ({ c, d: Math.hypot(c.center.x - tap.x, c.center.y - tap.y) }))
          .sort((a, b) => a.d - b.d)[0]?.c;
      if (tapped) return { kind: 'target', candidate: tapped };
    }

    // 2. Continue tracking the row we already had, as long as it is still where we left it.
    if (this.tracked) {
      const radius = Math.max(
        12,
        this.tracked.medianBadgeSide * CAMERA_SCANNER_CONFIG.selection.continuityRadiusFactor
      );
      const continued = viable
        .map(c => ({ c, d: Math.hypot(c.center.x - this.tracked!.center.x, c.center.y - this.tracked!.center.y) }))
        .filter(entry => entry.d <= radius)
        .sort((a, b) => a.d - b.d)[0];
      if (continued) return { kind: 'target', candidate: continued.c };
    }

    // 3. Fresh acquisition. Two rows of similar quality are not resolvable from geometry, so
    //    neither is chosen.
    if (viable.length > 1) {
      const gap = viable[0].score - viable[1].score;
      if (gap < CAMERA_SCANNER_CONFIG.selection.ambiguityMargin) {
        return { kind: 'ambiguous', count: viable.length };
      }
    }

    void nowMs;
    return { kind: 'target', candidate: viable[0] };
  }

  private rememberTarget(candidate: BuiltCandidate, nowMs: number): void {
    const id = candidateId(candidate);
    const isSameTarget = this.tracked?.id === id;

    this.tracked = {
      id,
      quad: candidate.quad,
      center: candidate.center,
      medianBadgeSide: candidate.medianBadgeSide,
      score: candidate.score,
      presence: isSameTarget ? this.tracked!.presence + 1 : 1,
      lastSeenAt: nowMs
    };
    this.trackFailures = 0;
  }

  private loseTarget(): void {
    if (!this.tracked) return;
    this.tracked = null;
    this.stability.reset();
  }

  /* ---------------------------------------------------------------- *
   * Normalisation and quality
   * ---------------------------------------------------------------- */

  private evaluateTarget(
    video: HTMLVideoElement,
    frame: AnalysisFrame,
    candidate: BuiltCandidate,
    overlay: CameraOverlay,
    nowMs: number
  ): CameraAnalysisResult {
    const isStable = this.stability.update(candidate.quad, nowMs);

    // Analysis coordinates are the downscaled frame; the row is re-read at native resolution
    // so OCR never inherits the discovery downscale.
    const cameraQuad = scaleQuad(candidate.quad, 1 / frame.scale);
    const cameraEdges = quadEdgeLengths(cameraQuad);
    const rowWidth = (cameraEdges.top + cameraEdges.bottom) / 2;
    const rowHeight = (cameraEdges.left + cameraEdges.right) / 2;
    const pixelsPerGene = rowWidth / GENES_PER_ROW;
    const perspectiveDegrees = estimatePerspectiveDegrees(cameraQuad);

    const margin = rowHeight * CAMERA_SCANNER_CONFIG.analysis.regionMarginFactor;
    const bounds = quadBounds(cameraQuad, margin, video.videoWidth, video.videoHeight);
    const region = this.grabber.grabRegion(video, bounds.x, bounds.y, bounds.width, bounds.height);
    if (!region) return this.blocked(overlay, ['moving']);

    const localQuad = translateQuad(cameraQuad, -bounds.x, -bounds.y) as Quad;
    // Keep the row's own proportions. A fixed-height canvas would stretch the letters
    // sideways by however much this particular row differs from 6:1.
    const normalizedWidth = CAMERA_SCANNER_CONFIG.analysis.normalizedRowWidth;
    const normalizedHeight = Math.round(
      Math.min(
        CAMERA_SCANNER_CONFIG.analysis.maxNormalizedRowHeight,
        Math.max(
          CAMERA_SCANNER_CONFIG.analysis.minNormalizedRowHeight,
          (normalizedWidth * rowHeight) / Math.max(1, rowWidth)
        )
      )
    );

    const warped = warpQuadToRect(region, localQuad, normalizedWidth, normalizedHeight, this.warpBuffer);
    if (!warped) return this.blocked(overlay, ['extreme-perspective']);
    this.warpBuffer = warped;

    const quality = assessRowQuality(
      {
        normalized: warped,
        pixelsPerGene,
        perspectiveDegrees,
        clipped: candidate.clipped,
        isStable
      },
      this.qualityThresholds
    );

    if (quality.issues.length > 0) {
      return this.blocked(overlay, quality.issues);
    }

    const slots = this.measureSlots(
      candidate,
      frame.scale,
      bounds.x,
      bounds.y,
      localQuad,
      normalizedWidth,
      normalizedHeight
    );
    if (!slots) return this.blocked(overlay, ['extreme-perspective']);

    // Only now, with geometry and quality both satisfied, is a readable target produced.
    const target: CameraTarget = {
      corners: toPointQuad(candidate.quad),
      candidateScore: candidate.score,
      trackingConfidence: Math.min(
        1,
        (this.tracked?.presence ?? 1) / CAMERA_SCANNER_CONFIG.tracking.persistenceFrames
      ),
      qualityIssues: [],
      normalizedRow: warped,
      slots
    };

    return {
      phase: 'tracking',
      qualityIssues: [],
      candidateCount: 1,
      activeTarget: target,
      overlay
    };
  }

  /**
   * Projects each badge through the same homography used for the warp, so the OCR slots are
   * measured rather than assumed. Normalisation has already removed perspective, so the
   * result is a uniform pitch the existing preprocessor can consume directly.
   */
  private measureSlots(
    candidate: BuiltCandidate,
    frameScale: number,
    boundsX: number,
    boundsY: number,
    localQuad: Quad,
    width: number,
    height: number
  ): CameraRowSlots | null {
    const forward = solveHomography(localQuad, [
      { x: 0, y: 0 },
      { x: width, y: 0 },
      { x: width, y: height },
      { x: 0, y: height }
    ]);
    if (!forward) return null;

    const boxes: Array<{ x0: number; x1: number; y0: number; y1: number }> = [];
    for (const member of candidate.members) {
      const corners = [
        { x: member.minX, y: member.minY },
        { x: member.maxX, y: member.minY },
        { x: member.maxX, y: member.maxY },
        { x: member.minX, y: member.maxY }
      ];

      let x0 = Infinity;
      let x1 = -Infinity;
      let y0 = Infinity;
      let y1 = -Infinity;

      for (const corner of corners) {
        const mapped = applyHomography(
          forward,
          corner.x / frameScale - boundsX,
          corner.y / frameScale - boundsY
        );
        if (!Number.isFinite(mapped.x) || !Number.isFinite(mapped.y)) return null;
        if (mapped.x < x0) x0 = mapped.x;
        if (mapped.x > x1) x1 = mapped.x;
        if (mapped.y < y0) y0 = mapped.y;
        if (mapped.y > y1) y1 = mapped.y;
      }

      boxes.push({ x0, x1, y0, y1 });
    }

    if (boxes.length !== GENES_PER_ROW) return null;

    const geneWidth = boxes.reduce((sum, b) => sum + (b.x1 - b.x0), 0) / boxes.length;
    const pitch = (boxes[GENES_PER_ROW - 1].x0 - boxes[0].x0) / (GENES_PER_ROW - 1);
    const baseY = boxes.reduce((sum, b) => sum + b.y0, 0) / boxes.length;
    const slotHeight = boxes.reduce((sum, b) => sum + (b.y1 - b.y0), 0) / boxes.length;

    if (!(geneWidth > 0) || !(pitch > 0) || !(slotHeight > 0)) return null;

    return {
      baseX: boxes[0].x0,
      baseY,
      geneWidth,
      gapWidth: pitch - geneWidth,
      height: slotHeight
    };
  }

  private blocked(
    overlay: CameraOverlay,
    issues: CameraAnalysisResult['qualityIssues']
  ): CameraAnalysisResult {
    return {
      phase: 'quality-blocked',
      qualityIssues: issues,
      candidateCount: 1,
      activeTarget: null,
      overlay
    };
  }

  private searching(overlay: CameraOverlay): CameraAnalysisResult {
    return {
      phase: 'searching',
      qualityIssues: [],
      candidateCount: overlay.candidates.length,
      activeTarget: null,
      overlay
    };
  }

  /* ---------------------------------------------------------------- *
   * Lifecycle
   * ---------------------------------------------------------------- */

  selectAt(point: Point): void {
    this.pendingSelection = point;
  }

  setDiscoveryWidth(width: number): void {
    const clamped = Math.max(
      CAMERA_SCANNER_CONFIG.analysis.minDiscoveryWidth,
      Math.min(CAMERA_SCANNER_CONFIG.analysis.maxDiscoveryWidth, Math.round(width))
    );
    if (clamped === this.discoveryWidth) return;
    this.discoveryWidth = clamped;
    // The frame geometry changed, so nothing measured against the old scale still applies.
    this.reset();
  }

  reset(): void {
    this.tracked = null;
    this.trackFailures = 0;
    this.lastDiscoveryAt = 0;
    this.pendingSelection = null;
    this.stability.reset();
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    this.reset();
    this.warpBuffer = undefined;
    this.grabber.dispose();
  }
}

/** Drift between two consecutive target quads, exposed for tracking diagnostics. */
export { quadDrift, quadCenter };
