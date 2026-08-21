/**
 * Configuration for the phone-camera scanner.
 *
 * Deliberately separate from SCANNER_CONFIG: the desktop screen-capture path is a
 * frozen compatibility surface, and none of the values below may leak into it.
 *
 * Every threshold here is a beta starting value. They are tuned against labelled
 * device fixtures in Phase 6, not chosen by assumption.
 */
export const CAMERA_SCANNER_CONFIG = {
  capture: {
    idealWidth: 1920,
    idealHeight: 1080,
    idealFrameRate: 15,
    maxFrameRate: 30
  },

  cadence: {
    /** Full-frame candidate discovery. Degrades toward minDiscoveryFps under load. */
    discoveryFps: 8,
    minDiscoveryFps: 5,
    /** Local tracking of an acquired target between discovery passes. */
    trackingFps: 15,
    /** Force a fresh discovery pass this often even while tracking looks healthy. */
    redetectIntervalMs: 750
  },

  analysis: {
    maxDiscoveryWidth: 960,
    minDiscoveryWidth: 720,
    normalizedRowWidth: 600,
    normalizedRowHeight: 100,
    /** Extra margin around the row when re-grabbing it at native resolution, as a row-height fraction. */
    regionMarginFactor: 0.35
  },

  selection: {
    /** Two candidates closer than this in score are ambiguous, and neither is chosen. */
    ambiguityMargin: 0.08,
    /** Below this a row is not worth pursuing at all. */
    minCandidateScore: 0.45,
    /** How far a candidate may move between frames and still be the same target. */
    continuityRadiusFactor: 2.2
  },

  tracking: {
    /** Local search box around the last target, as a multiple of its own size. */
    searchExpansion: 1.8,
    /** Consecutive local-search misses before falling back to full-frame discovery. */
    maxTrackFailures: 3,
    /** Frames of continuous presence needed before persistence scores at full weight. */
    persistenceFrames: 8
  },

  quality: {
    minPixelsPerGene: 24,
    maxPixelsPerGene: 110,
    minStableMs: 400,
    maxPerspectiveDegrees: 35
  },

  confirmation: {
    samples: 4,
    requiredMatches: 3,
    /** A target must be gone for this long before the same genes can be emitted again. */
    rearmAfterLossMs: 600
  }
} as const;
