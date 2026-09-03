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
    requiredMatches: 3, // 3 consecutive reads of same gene string for 100% rock-solid accuracy
    activeRegionThreshold: 0.12, // Permissive activity score cutoff for small badges
    fastPathMinConfidence: 52, // Fast template matching threshold
    fastPathSingleFrameThreshold: 101 // Disable single-frame bypass: strictly require 3 reads
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
