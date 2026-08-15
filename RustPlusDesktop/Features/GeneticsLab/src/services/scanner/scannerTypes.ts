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
  recognizeRow(canvas: HTMLCanvasElement): Promise<GeneRecognitionResult | null>;
  recognizeSlots(canvases: HTMLCanvasElement[]): Promise<GeneRecognitionResult | null>;
  warmup(): Promise<void>;
  terminate(): Promise<void>;
  isWarm(): boolean;
}
