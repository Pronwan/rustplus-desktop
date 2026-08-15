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
    minGeneConfidence: 60, // Minimum confidence for each individual glyph
    minAverageConfidence: 75, // Minimum average confidence across all 6 glyphs
    temporalSamples: 4,
    requiredMatches: 3, // 3-of-4 temporal confirmation (fails safe on torn frames instead of confirming a mis-read)
    activeRegionThreshold: 0.25 // Activity score cutoff to skip inactive background
  },

  performance: {
    scanIntervalMs: 50,
    stableDurationMs: 60,
    previewIntervalMs: 200,
    roiChangeThreshold: 0.005,
    idleWorkerTimeoutMs: 300000
  },

  calibration: {
    normalStepPx: 1,
    amplifiedStepPx: 3,
    holdDelayMs: 100,
    holdRepeatMs: 16
  },

  defaults: {
    inventory: {
      TOP_LEFT_X: 0.4156,
      TOP_LEFT_Y: 0.2772,
      WIDTH: 0.079,
      HEIGHT_TO_WIDTH_RATIO: 0.18,
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
