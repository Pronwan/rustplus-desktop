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
    /**
     * Confidence floor for a sample entering the confirmation window.
     *
     * Template matching reports how much better the winning glyph is than the runner-up, so
     * this rejects genuinely ambiguous reads while letting an ordinary correct one through.
     */
    minRowConfidence: 40,
    /** Height of one glyph cell fed to the classifier, in pixels. */
    cellHeight: 120,
    /**
     * Minimum gap between OCR passes.
     *
     * Template matching runs in microseconds and happens on every tracking frame. OCR costs
     * tens of milliseconds per slot, so it runs alongside on a throttle: often enough to
     * confirm a row within a glance, rarely enough to leave the tracking loop its budget.
     */
    ocrIntervalMs: 220
  },

  selection: {
    /** Two candidates closer than this in score are ambiguous, and neither is chosen. */
    ambiguityMargin: 0.08,
    /** Below this a row is not worth pursuing at all. */
    minCandidateScore: 0.38,
    /** How far a candidate may move between frames and still be the same target. */
    continuityRadiusFactor: 2.2
  },

  tracking: {
    /**
     * How long a momentarily undetected row keeps its lock.
     *
     * Detection misses a frame here and there on a hand-held phone. Dropping straight to
     * "searching" made the status thrash between modes several times a second and threw
     * away the confirmation window every time.
     */
    lostGraceMs: 400,
    /** Local search box around the last target, as a multiple of its own size. */
    searchExpansion: 1.8,
    /** Consecutive local-search misses before falling back to full-frame discovery. */
    maxTrackFailures: 3,
    /** Frames of continuous presence needed before persistence scores at full weight. */
    persistenceFrames: 8
  },

  quality: {
    minPixelsPerGene: 24,
    /**
     * Deliberately generous. Field testing showed a phone held at a natural, well-framed
     * distance produces around 200 px per gene and was being told to "move farther" while
     * looking at a perfectly readable row. Closer is better for OCR; the real upper limit is
     * the row no longer fitting, which `clipped` already reports.
     */
    maxPixelsPerGene: 600,
    /**
     * Handheld tolerance. A phone held in the hand never stops moving, so demanding a long
     * perfectly still window made the scanner feel broken. The window is short and the drift
     * allowance generous; wrong reads are caught by temporal agreement, not by stillness.
     */
    minStableMs: 150,
    maxDriftFraction: 0.4,
    maxPerspectiveDegrees: 35,
    minSharpness: 55,
    maxGlareRatio: 0.35,
    minMeanLuminance: 32
  },

  confirmation: {
    samples: 4,
    requiredMatches: 3,
    /** A target must be gone for this long before the same genes can be emitted again. */
    rearmAfterLossMs: 600,
    /**
     * How long the "Clone added" confirmation stays on screen. Without a hold the next
     * analysis frame overwrites it about 60ms later, so a successful scan looked identical
     * to nothing happening.
     */
    acceptedHoldMs: 1200
  }
} as const;
