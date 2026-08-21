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
    /**
     * Height bounds only. The actual height follows the detected row's aspect ratio, because
     * squeezing a 4.6:1 row into a fixed 6:1 canvas stretches the letters sideways and the
     * recogniser reads the distortion, not the gene.
     */
    minNormalizedRowHeight: 60,
    maxNormalizedRowHeight: 200,
    /** Extra margin around the row when re-grabbing it at native resolution, as a row-height fraction. */
    regionMarginFactor: 0.35
  },

  recognition: {
    /**
     * Confidence floors for the camera path, deliberately below the desktop values.
     *
     * The desktop scanner reads pixel-exact screen captures and can demand 75%. A phone
     * photographing a monitor fights defocus, moire and rolling shutter, so a correct read
     * routinely scores in the 40s and 50s. Holding it to the desktop floor rejected every
     * single read. Confidence in the *result* comes from 3-of-4 temporal agreement instead.
     */
    minRowConfidence: 40,
    minSlotConfidence: 35,
    minAverageSlotConfidence: 45,
    /** Target glyph height handed to Tesseract, in pixels. */
    targetGlyphHeight: 84
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
    /**
     * Handheld tolerance. A phone held in the hand never stops moving, so demanding a long
     * perfectly still window made the scanner feel broken. The window is short and the drift
     * allowance generous; wrong reads are caught by temporal agreement, not by stillness.
     */
    minStableMs: 150,
    maxDriftFraction: 0.4,
    maxPerspectiveDegrees: 35
  },

  confirmation: {
    samples: 4,
    requiredMatches: 3,
    /** A target must be gone for this long before the same genes can be emitted again. */
    rearmAfterLossMs: 600
  }
} as const;
