export type ScannerRegionType = 'inventory' | 'planter';

export interface ScannerRegion {
  id?: string;
  type?: ScannerRegionType;
  TOP_LEFT_X: number;
  TOP_LEFT_Y: number;
  WIDTH: number;
  HEIGHT_TO_WIDTH_RATIO: number;
  GENE_WIDTH_TO_WIDTH_RATIO: number;
}

export type StarvationReason = 'CAPTURE_STALLED' | 'OCR_LATENCY_SPIKE' | 'TICK_DELAYED' | 'MULTIPLE';

export type ScannerEventType =
  | 'INITIALIZING'
  | 'STARTED'
  | 'STOPPED'
  | 'PREVIEW'
  | 'SAPLING-FOUND'
  | 'ERROR'
  | 'DIAGNOSTICS'
  | 'STARVATION_DETECTED'
  | 'STARVATION_RESOLVED';

export interface ScannerEvent {
  type: ScannerEventType;
  regionIndex?: number;
  regionType?: ScannerRegionType;
  previewDataUrl?: string;
  geneString?: string;
  confidence?: number;
  error?: string;
  diagnostics?: ScannerDiagnostics;
  isStarved?: boolean;
  starvationReason?: StarvationReason;
}

export type ScannerEventListener = (event: ScannerEvent) => void;

export interface GeneRecognitionResult {
  geneString: string;
  confidence: number;
  rawText?: string;
  slotConfidences?: number[];
}

export interface ScanCandidate {
  regionIndex: number;
  regionType: ScannerRegionType;
  genes: string;
  confidence: number;
  geneConfidences: number[];
  activityScore: number;
  valid: boolean;
}

export interface RegionActivity {
  regionIndex: number;
  regionType: ScannerRegionType;
  active: boolean;
  score: number;
}

export interface ScannerDiagnostics {
  fps: number;
  tickGapMs: number;
  videoFrameAgeMs: number;
  videoFrameGapMs: number;
  lastScanLatencyMs: number;
  lastOcrLatencyMs: number;
  rowOcrLatencyMs: number;
  slotOcrLatencyMs: number;
  pipelineStage: string;
  pipelineStageAgeMs: number;
  uiUpdateLatencyMs: number;
  pageVisibility: DocumentVisibilityState;
  captureResolution: string;
  confidence: number;
  totalScans: number;
  acceptedPlants: number;
  rejectedScans: number;
  activeRegion: ScannerRegionType | 'none';
  inventoryActivity?: number;
  planterActivity?: number;
  isStarved?: boolean;
  starvationReason?: StarvationReason;
}

export interface GeneRecognizer {
  /**
   * `minConfidence` defaults to the desktop floor. The camera path passes a lower one:
   * a photograph of a monitor never scores like a pixel-exact screen capture.
   */
  recognizeRow(canvas: HTMLCanvasElement, minConfidence?: number): Promise<GeneRecognitionResult | null>;
  recognizeSlots(
    canvases: HTMLCanvasElement[],
    minGeneConfidence?: number,
    minAverageConfidence?: number
  ): Promise<GeneRecognitionResult | null>;
  warmup(): Promise<void>;
  terminate(): Promise<void>;
  isWarm(): boolean;
  /** Last raw OCR attempt, including reads rejected by the confidence floor. */
  getLastRawRead?(): { text: string; confidence: number } | null;
}

/* ------------------------------------------------------------------ *
 * Mobile camera scanner
 *
 * These types are additive. They describe the phone-camera path only and
 * never change the meaning of the desktop scanner events above.
 * ------------------------------------------------------------------ */

export interface Point {
  x: number;
  y: number;
}

/** Plain RGBA pixel buffer. Keeps the vision stack independent of canvas and DOM. */
export interface RasterImage {
  data: Uint8ClampedArray;
  width: number;
  height: number;
}

export type CameraScannerPhase =
  | 'idle'
  | 'requesting-permission'
  | 'starting'
  | 'searching'
  | 'ambiguous'
  | 'tracking'
  | 'quality-blocked'
  | 'reading'
  | 'accepted'
  | 'paused'
  | 'error';

export type CameraQualityIssue =
  | 'too-far'
  | 'too-close'
  | 'blurred'
  | 'glare'
  | 'too-dark'
  | 'moving'
  | 'extreme-perspective'
  | 'clipped';

/**
 * Where the six genes sit inside the normalised row canvas.
 *
 * Perspective is already removed at this point, so the slots are uniform and can be
 * described the same way the desktop path describes its ROI.
 */
export interface CameraRowSlots {
  baseX: number;
  baseY: number;
  geneWidth: number;
  gapWidth: number;
  height: number;
}

export interface CameraTarget {
  corners: [Point, Point, Point, Point];
  candidateScore: number;
  trackingConfidence: number;
  qualityIssues: CameraQualityIssue[];
  /**
   * The perspective-corrected row. Pixels rather than a canvas so the OCR strip can be
   * built and tested without a DOM.
   *
   * Backed by a buffer the locator reuses between frames. It is valid only until the next
   * `analyze()` call, which the service guarantees by awaiting recognition inline.
   */
  normalizedRow: RasterImage;
  slots: CameraRowSlots;
}

export type CameraScannerErrorCode =
  | 'insecure-context'
  | 'unsupported'
  | 'permission-denied'
  | 'no-camera'
  | 'stream-failed'
  | 'stream-ended'
  | 'vision-init-failed'
  | 'unknown';

export type CameraFacingMode = 'environment' | 'user';

export interface CameraTrackCapabilities {
  zoom: boolean;
  torch: boolean;
  pointFocus: boolean;
}

/**
 * Everything the UI needs to draw borders over the camera preview. Corners are in analysis
 * frame coordinates; the surface maps them through the letterboxed video rectangle.
 */
export interface CameraOverlay {
  frameWidth: number;
  frameHeight: number;
  target: [Point, Point, Point, Point] | null;
  /** Which entry in `candidates` is the current target, so the UI can style it differently. */
  targetId: string | null;
  candidates: Array<{ id: string; corners: [Point, Point, Point, Point] }>;
}

export interface CameraScannerState {
  phase: CameraScannerPhase;
  qualityIssues: CameraQualityIssue[];
  overlay: CameraOverlay | null;
  errorCode?: CameraScannerErrorCode;
  errorMessage?: string;
  /** OCR worker is warm and reads can be attempted. */
  isOcrReady: boolean;
  /** OCR warm-up failed; the preview still runs and warm-up can be retried. */
  isOcrUnavailable: boolean;
  /** A frame analyzer is installed. Until Phase 2 lands this stays false. */
  isDetectionAvailable: boolean;
  facingMode: CameraFacingMode;
  canSwitchCamera: boolean;
  capabilities: CameraTrackCapabilities;
  captureResolution: string;
  acceptedCount: number;
  lastAcceptedGenes: string | null;
  /** Number of similarly scored candidates when phase is 'ambiguous'. */
  candidateCount: number;
  /**
   * Beta diagnostics. Without these a failing read is invisible: the surface just sits on
   * "Reading genetics..." forever with no way to tell whether OCR is returning nothing,
   * returning noise, or returning the right answer below the confidence floor.
   */
  diagnostics: {
    readAttempts: number;
    lastRawText: string | null;
    lastConfidence: number | null;
    /** Which recognition path produced the last text. */
    lastSource: 'row' | 'slots' | null;
    /** Samples currently held in the confirmation window. */
    pendingSamples: number;
    /** Samples needed before a result can be confirmed. */
    sampleWindow: number;
  };
}

export type CameraScannerEvent =
  | { type: 'CAMERA_STATE'; state: CameraScannerState }
  | { type: 'SAPLING-FOUND'; geneString: string; confidence: number };

export type CameraScannerEventListener = (event: CameraScannerEvent) => void;

/**
 * Result of analysing one camera frame. Phase 2 (`DynamicGeneLocator`) produces this;
 * the service only schedules the work and applies the recognition gates.
 */
export interface CameraAnalysisResult {
  phase: Extract<CameraScannerPhase, 'searching' | 'ambiguous' | 'tracking' | 'quality-blocked'>;
  qualityIssues: CameraQualityIssue[];
  candidateCount: number;
  /** Present only when geometry and quality both passed, i.e. when OCR is allowed to run. */
  activeTarget: CameraTarget | null;
  overlay: CameraOverlay;
}

/**
 * Seam between camera lifecycle (this phase) and dynamic detection (Phase 2).
 * The service owns scheduling; the analyzer owns all computer vision.
 */
export interface CameraFrameAnalyzer {
  analyze(video: HTMLVideoElement, nowMs: number): Promise<CameraAnalysisResult>;
  /** Drop tracking state, e.g. after the page was hidden or the target was lost. */
  reset(): void;
  /** Release native/WASM resources. Must be safe to call more than once. */
  dispose(): void;
  /**
   * Resolve an ambiguous scene from a user tap, in analysis frame coordinates.
   * A tap selects a candidate for this appearance only; it never saves a region.
   */
  selectAt?(point: Point): void;
  /** Reduce per-frame cost under thermal or performance pressure. */
  setDiscoveryWidth?(width: number): void;
}
