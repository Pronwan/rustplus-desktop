export const SCANNER_CONFIG = {
  capture: {
    idealWidth: 1920,
    idealHeight: 1080,
    maxFrameRate: 30
  },

  recognition: {
    allowedGenes: ['G', 'H', 'Y', 'W', 'X'] as const,
    whitelist: 'GHYWX7681VTKM',
    workerCount: 6, // 6 parallel workers for simultaneous sub-40ms single-glyph OCR
    geneScale: 4,
    paddingPx: 8,
    minConfidence: 50, // Permissive confidence floor for smaller in-game UI scales (0.5 - 1.0)
    minGeneConfidence: 50,
    minAverageConfidence: 52,
    temporalSamples: 5,
    requiredMatches: 3, // Frames that must name the same letter, counted per slot
    activeRegionThreshold: 0.1, // Cheap pre-filter only; the reader is the real row guard
    /**
     * Confidence every slot must clear for a row to be accepted from a single frame.
     *
     * Measured on rendered tooltips across the range of Rust UI scales: the worst slot in a
     * row scores 93 or better at a badge 18px across or larger, 78 at 14px, and 69 at 11px.
     * The bar sits between the last two, so every scale a player realistically uses reads in
     * one frame and only a genuinely tiny badge falls back to the confirmation window.
     *
     * What is being accepted here is not a bare guess. The badge colour has already reduced
     * the choice to three letters or two before matching, and at 14px the winner still beats
     * the runner-up by 60%. The cost of being wrong is a letter the user can see and correct
     * in the HUD; the cost of being slow is three frames per clone across a whole tray.
     */
    instantAcceptSlotConfidence: 75,
    /** How long before a tooltip that defeated template matching is offered to OCR again. */
    slotOcrRetryMs: 1200
  },

  performance: {
    scanIntervalMs: 16, // 60 FPS active scanning for lightning-fast cursor sweeps
    stableDurationMs: 20,
    fastStableDurationMs: 0, // 0ms delay for instant frame acceptance
    previewIntervalMs: 33, // 30 FPS live preview updates (no lag in HUD region preview)
    roiChangeThreshold: 0.003,
    idleWorkerTimeoutMs: 300000
  },

  starvation: {
    startupGracePeriodMs: 3000,
    frameGapThresholdMs: 450,
    frameAgeThresholdMs: 600,
    ocrLatencyThresholdMs: 140,
    tickGapThresholdMs: 150,
    sustainedDurationMs: 1500,
    recoveryDurationMs: 2500,
    recommendedFpsCap: 50
  },

  calibration: {
    normalStepPx: 1,
    amplifiedStepPx: 3,
    holdDelayMs: 100,
    holdRepeatMs: 16
  },

  defaults: {
    inventory: {
      TOP_LEFT_X: 0.198, // Rust Left Info Panel: Genetics W - Y - G - W - G - Y
      TOP_LEFT_Y: 0.272,
      WIDTH: 0.088,
      HEIGHT_TO_WIDTH_RATIO: 0.17,
      GENE_WIDTH_TO_WIDTH_RATIO: 0.11
    },
    planter: {
      TOP_LEFT_X: 0.6116,
      TOP_LEFT_Y: 0.3422,
      WIDTH: 0.131,
      HEIGHT_TO_WIDTH_RATIO: 0.125,
      GENE_WIDTH_TO_WIDTH_RATIO: 0.08
    }
  }
};
