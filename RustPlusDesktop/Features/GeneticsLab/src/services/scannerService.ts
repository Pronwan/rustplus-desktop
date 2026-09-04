import { ScannerRegion, StorageService } from './storageService.ts';
import {
  ScannerEvent,
  ScannerEventListener,
  ScannerDiagnostics,
  GeneRecognizer,
  ScanCandidate,
  ScannerRegionType
} from './scanner/scannerTypes.ts';
import { SCANNER_CONFIG } from './scanner/scannerConfig.ts';
import { GeneImagePreprocessor } from './scanner/GeneImagePreprocessor.ts';
import { TesseractGeneRecognizer } from './scanner/TesseractGeneRecognizer.ts';
import { RegionChangeDetector } from './scanner/RegionChangeDetector.ts';
import { FrameStabilityDetector } from './scanner/FrameStabilityDetector.ts';
import { TemporalVotingService } from './scanner/TemporalVotingService.ts';
import { PlantScanDeduplicator } from './scanner/PlantScanDeduplicator.ts';
import { ScannerStarvationDetector } from './scanner/ScannerStarvationDetector.ts';
import { AutoCalibrator, AutoCalibrateResult } from './scanner/AutoCalibrator.ts';
import { DesktopTemplateRecognizer } from './scanner/DesktopTemplateRecognizer.ts';
import { DesktopSlotExtractor } from './scanner/DesktopSlotExtractor.ts';

export * from './scanner/scannerTypes.ts';
export * from './scanner/scannerConfig.ts';
export * from './scanner/AutoCalibrator.ts';
export * from './scanner/DesktopTemplateRecognizer.ts';
export * from './scanner/DesktopSlotExtractor.ts';

export class ScannerService {
  private listeners: ScannerEventListener[] = [];
  private mediaStream: MediaStream | null = null;
  private videoElement: HTMLVideoElement | null = null;
  private isScanning = false;
  private isInitializing = false;

  // Background Web Worker ticker
  private tickerWorker: Worker | null = null;
  private scanTimerId: any = null;

  // Modular Pipeline Components
  private regions: ScannerRegion[] = [];
  private recognizer: GeneRecognizer;
  private changeDetector: RegionChangeDetector;
  private stabilityDetector: FrameStabilityDetector;
  private votingService: TemporalVotingService;
  private deduplicator: PlantScanDeduplicator;
  private starvationDetector: ScannerStarvationDetector;

  // Performance & Diagnostics Tracking
  private lastPreviewEmitTime = 0;
  private isOcrInProgress = false;
  private lastOcrTimestamps: Record<number, number> = {};
  private scanCount = 0;
  private acceptedCount = 0;
  private rejectedCount = 0;
  private frameCount = 0;
  private lastFpsCalcTime = Date.now();
  private currentFps = 0;
  private lastScanLatency = 0;
  private lastOcrLatency = 0;
  private lastRowOcrLatency = 0;
  private lastSlotOcrLatency = 0;
  private lastTickTime = 0;
  private lastTickGap = 0;
  private lastVideoTime = -1;
  private lastVideoFrameTime = 0;
  private lastVideoFrameGap = 0;
  private pipelineStage = 'idle';
  private pipelineStageStartedAt = performance.now();
  private pendingUiGene = '';
  private pendingUiStartedAt = 0;
  private lastUiUpdateLatency = 0;
  private latestConfidence = 0;
  private activityScores: Record<number, number> = { 0: 0, 1: 0 };
  private activeRegionType: ScannerRegionType | 'none' = 'none';
  // Preview rendering is UI-only work. It runs independently of recognition so the OCR
  // pipeline keeps going while the app is in the background, but there is no point paying
  // for it when nobody is looking at the calibration/preview panel.
  private previewEnabled = true;

  // Reusable Canvases
  private previewCanvases: HTMLCanvasElement[] = [];
  private roiCanvases: HTMLCanvasElement[] = [];

  constructor(recognizer?: GeneRecognizer) {
    this.regions = StorageService.getScannerRegions();
    this.recognizer = recognizer || new TesseractGeneRecognizer();
    this.changeDetector = new RegionChangeDetector();
    this.stabilityDetector = new FrameStabilityDetector();
    this.votingService = new TemporalVotingService();
    this.deduplicator = new PlantScanDeduplicator();
    this.starvationDetector = new ScannerStarvationDetector();
  }

  public static isSupported(): boolean {
    return typeof navigator !== 'undefined' &&
      !!navigator.mediaDevices &&
      'getDisplayMedia' in navigator.mediaDevices;
  }

  /**
   * Enables/disables preview rendering. The calibration panel turns this on while mounted
   * and off when it unmounts, so the scanner does no preview work during normal scanning
   * when the panel isn't on screen. Recognition is never gated by this.
   */
  public setPreviewEnabled(enabled: boolean): void {
    this.previewEnabled = enabled;
  }

  public addEventListener(listener: ScannerEventListener): () => void {
    this.listeners.push(listener);
    return () => {
      this.listeners = this.listeners.filter(l => l !== listener);
    };
  }

  private emit(event: ScannerEvent): void {
    for (const listener of this.listeners) {
      listener(event);
    }
  }

  public async start(): Promise<boolean> {
    if (this.isScanning || this.isInitializing) return false;
    this.isInitializing = true;

    // Kick off the OCR warmup right away so its (potentially large, first-run)
    // asset download runs in parallel with the screen-picker prompt and video
    // setup below, rather than starting only after the user has picked a window.
    const warmupPromise: Promise<void> = this.recognizer.isWarm()
      ? Promise.resolve()
      : this.recognizer.warmup().catch(() => {});

    try {
      this.regions = StorageService.getScannerRegions();

      this.mediaStream = await navigator.mediaDevices.getDisplayMedia({
        video: {
          width: { ideal: SCANNER_CONFIG.capture.idealWidth },
          height: { ideal: SCANNER_CONFIG.capture.idealHeight },
          frameRate: { max: SCANNER_CONFIG.capture.maxFrameRate }
        },
        audio: false
      });

      this.emit({ type: 'INITIALIZING' });

      const videoTrack = this.mediaStream.getVideoTracks()[0];
      if (!videoTrack) {
        throw new Error('No video track received from screen capture');
      }

      videoTrack.addEventListener('ended', () => {
        this.stop();
      });

      // Mount video element to DOM to ensure Chromium never throttles frame decoding
      this.videoElement = document.createElement('video');
      this.videoElement.autoplay = true;
      this.videoElement.playsInline = true;
      this.videoElement.muted = true;
      this.videoElement.style.position = 'fixed';
      this.videoElement.style.top = '-9999px';
      this.videoElement.style.left = '-9999px';
      this.videoElement.style.width = '100px';
      this.videoElement.style.height = '100px';
      this.videoElement.style.opacity = '0.001';
      this.videoElement.style.pointerEvents = 'none';
      this.videoElement.style.zIndex = '-9999';
      document.body.appendChild(this.videoElement);

      const video = this.videoElement;

      // Robust video initialization that never hangs on readyState
      await new Promise<void>((resolve) => {
        let isDone = false;
        const done = () => {
          if (isDone) return;
          isDone = true;
          video.play().then(() => resolve()).catch(() => resolve());
        };

        video.onloadedmetadata = done;
        video.onloadeddata = done;
        video.oncanplay = done;

        video.srcObject = this.mediaStream;

        if (video.readyState >= 1 && video.videoWidth > 0) {
          done();
        }

        // Safety fallback timeout
        setTimeout(done, 1200);
      });

      // Fast-path template recognizer starts instantly without Tesseract warmup delay!

      this.isScanning = true;
      this.isInitializing = false;
      this.starvationDetector.start();
      this.emit({ type: 'STARTED' });

      this.startScanLoop();
      return true;
    } catch (err: any) {
      this.stop();
      this.isInitializing = false;
      if (err?.name !== 'NotAllowedError' && err?.name !== 'AbortError') {
        this.emit({ type: 'ERROR', error: err?.message || 'Failed to initialize screen capture' });
      }
      this.emit({ type: 'STOPPED' });
      return false;
    }
  }


  private lastScanExecuteTime = 0;

  private startScanLoop(): void {
    if (!this.isScanning) return;

    // Use requestVideoFrameCallback if available to synchronize directly with GPU compositor frames
    if (this.videoElement && 'requestVideoFrameCallback' in this.videoElement) {
      const onFrame = () => {
        if (!this.isScanning) return;
        const now = performance.now();
        if (now - this.lastScanExecuteTime >= 15) {
          this.lastScanExecuteTime = now;
          this.scanFrame();
        }
        if (this.isScanning && this.videoElement && 'requestVideoFrameCallback' in this.videoElement) {
          (this.videoElement as any).requestVideoFrameCallback(onFrame);
        }
      };
      (this.videoElement as any).requestVideoFrameCallback(onFrame);
    }

    try {
      const tickerBlob = new Blob([`
        let timer = null;
        self.onmessage = function(e) {
          if (e.data === 'START') {
            if (timer) clearInterval(timer);
            timer = setInterval(function() {
              self.postMessage('TICK');
            }, ${SCANNER_CONFIG.performance.scanIntervalMs});
          } else if (e.data === 'STOP') {
            if (timer) clearInterval(timer);
            timer = null;
          }
        };
      `], { type: 'application/javascript' });

      this.tickerWorker = new Worker(URL.createObjectURL(tickerBlob));
      this.tickerWorker.onmessage = () => {
        if (this.isScanning) {
          const now = performance.now();
          if (now - this.lastScanExecuteTime >= SCANNER_CONFIG.performance.scanIntervalMs - 3) {
            this.lastScanExecuteTime = now;
            this.scanFrame();
          }
        }
      };
      this.tickerWorker.postMessage('START');
    } catch {
      const runScan = async () => {
        if (!this.isScanning) return;
        await this.scanFrame();
        if (this.isScanning) {
          this.scanTimerId = setTimeout(runScan, SCANNER_CONFIG.performance.scanIntervalMs);
        }
      };
      runScan();
    }
  }

  private async scanFrame(): Promise<void> {
    if (!this.videoElement || this.videoElement.videoWidth === 0) return;

    const startTime = performance.now();
    if (this.lastTickTime > 0) {
      this.lastTickGap = startTime - this.lastTickTime;
    }
    this.lastTickTime = startTime;

    if (this.videoElement.readyState >= 2) {
      if (this.lastVideoFrameTime > 0) {
        this.lastVideoFrameGap = startTime - this.lastVideoFrameTime;
      }
      this.lastVideoTime = this.videoElement.currentTime;
      this.lastVideoFrameTime = startTime;
    }

    if (!this.isOcrInProgress) {
      this.setPipelineStage('capture');
    }

    const videoW = this.videoElement.videoWidth;
    const videoH = this.videoElement.videoHeight;
    const now = Date.now();

    // FPS Calculation
    this.frameCount++;
    if (now - this.lastFpsCalcTime >= 1000) {
      this.currentFps = Math.round((this.frameCount * 1000) / (now - this.lastFpsCalcTime));
      this.frameCount = 0;
      this.lastFpsCalcTime = now;
    }

    // Preview is emitted whenever previewEnabled is true, independent of document.visibilityState,
    // so background windows, overlays, and multi-monitor setups receive real-time preview streams.
    const shouldEmitPreview =
      this.previewEnabled && now - this.lastPreviewEmitTime >= SCANNER_CONFIG.performance.previewIntervalMs;
    if (shouldEmitPreview) {
      this.lastPreviewEmitTime = now;
    }

    // Step 1: Evaluate Active Regions and Previews
    const activeCandidates: {
      rIdx: number;
      type: ScannerRegionType;
      xPx: number;
      yPx: number;
      wPx: number;
      hPx: number;
      geneWPx: number;
      gapWPx: number;
      signature: number;
      activityScore: number;
      roiData?: Uint8ClampedArray;
    }[] = [];

    for (let rIdx = 0; rIdx < this.regions.length; rIdx++) {
      const reg = this.regions[rIdx];
      const xPx = Math.round(videoW * reg.TOP_LEFT_X);
      const yPx = Math.round(videoH * reg.TOP_LEFT_Y);
      const wPx = Math.round(videoW * reg.WIDTH);
      const normH = reg.WIDTH * reg.HEIGHT_TO_WIDTH_RATIO;
      const hPx = Math.ceil(videoH * normH);

      if (wPx <= 0 || hPx <= 0) continue;

      // Rust Breeder-style Preview Rendering (throttled)
      if (shouldEmitPreview) {
        this.renderPreview(rIdx, xPx, yPx, wPx, hPx, reg);
      }

      if (!this.roiCanvases[rIdx]) {
        this.roiCanvases[rIdx] = document.createElement('canvas');
      }
      const roiCanvas = this.roiCanvases[rIdx];
      roiCanvas.width = wPx;
      roiCanvas.height = hPx;
      const roiCtx = roiCanvas.getContext('2d', { willReadFrequently: true });
      if (!roiCtx) continue;

      roiCtx.drawImage(this.videoElement, xPx, yPx, wPx, hPx, 0, 0, wPx, hPx);
      const roiData = roiCtx.getImageData(0, 0, wPx, hPx).data;

      // Activity Score Calculation
      const score = GeneImagePreprocessor.computeRegionActivityScore(roiData);
      this.activityScores[rIdx] = score;

      if (score < SCANNER_CONFIG.recognition.activeRegionThreshold) {
        const wasVisible = this.deduplicator.isRegionCurrentlyVisible(rIdx);
        this.deduplicator.markRegionDismissed(rIdx);
        if (wasVisible) {
          this.emit({ type: 'IDLE', regionIndex: rIdx });
        }
        continue;
      }

      const signature = this.changeDetector.computeSignature(roiCtx, wPx, hPx);
      const hasChanged = this.changeDetector.hasChanged(rIdx, signature);
      // Fast-Path: when tooltip activity is present, use fastStableDurationMs (15ms)
      const isStable = this.stabilityDetector.registerFrame(
        rIdx,
        signature,
        SCANNER_CONFIG.performance.roiChangeThreshold,
        SCANNER_CONFIG.performance.fastStableDurationMs
      );
      const lastOcr = this.lastOcrTimestamps[rIdx] || 0;

      if ((hasChanged && isStable) || isStable || now - lastOcr > 120) {
        const geneWPx = Math.round(wPx * reg.GENE_WIDTH_TO_WIDTH_RATIO);
        const totalGeneW = geneWPx * 6;
        const gapWPx = Math.max(0, (wPx - totalGeneW) / 5);

        activeCandidates.push({
          rIdx,
          type: rIdx === 0 ? 'inventory' : 'planter',
          xPx,
          yPx,
          wPx,
          hPx,
          geneWPx,
          gapWPx,
          signature,
          activityScore: score,
          roiData
        });
      }
    }

    if (activeCandidates.length === 0 && this.activeRegionType !== 'none') {
      this.activeRegionType = 'none';
      this.emit({
        type: 'ACTIVE_REGION_CHANGED',
        activeRegion: null,
        regionIndex: undefined
      });
    }

    // Step 2: Arbitrated Multi-Region Recognition
    // Fast-path template recognition does not even need Tesseract to be warm!
    if (activeCandidates.length > 0 && !this.isOcrInProgress) {
      this.isOcrInProgress = true;
      this.scanCount++;

      this.processArbitratedScan(activeCandidates)
        .catch((error) => {
          console.error('Scanner pipeline failed', error);
          this.setPipelineStage('error');
        })
        .finally(() => {
          this.isOcrInProgress = false;
        });
    } else if (!this.isOcrInProgress) {
      this.setPipelineStage(
        activeCandidates.length === 0
          ? 'roi-idle'
          : 'ocr-cold'
      );
    }

    this.lastScanLatency = performance.now() - startTime;

    // Evaluate GPU/CPU starvation in real-time
    const starvationEval = this.starvationDetector.evaluate({
      videoFrameAgeMs: this.lastVideoFrameTime > 0 ? startTime - this.lastVideoFrameTime : 0,
      videoFrameGapMs: this.lastVideoFrameGap,
      lastOcrLatencyMs: this.lastOcrLatency,
      rowOcrLatencyMs: this.lastRowOcrLatency,
      tickGapMs: this.lastTickGap,
      pipelineStage: this.pipelineStage,
      pipelineStageAgeMs: startTime - this.pipelineStageStartedAt
    });

    if (starvationEval.stateChanged) {
      if (starvationEval.isStarved) {
        this.emit({
          type: 'STARVATION_DETECTED',
          isStarved: true,
          starvationReason: starvationEval.starvationReason,
          diagnostics: this.getDiagnostics()
        });
      } else {
        this.emit({
          type: 'STARVATION_RESOLVED',
          isStarved: false,
          diagnostics: this.getDiagnostics()
        });
      }
    }
  }

  private async processArbitratedScan(
    regionsToScan: {
      rIdx: number;
      type: ScannerRegionType;
      xPx: number;
      yPx: number;
      wPx: number;
      hPx: number;
      geneWPx: number;
      gapWPx: number;
      signature: number;
      activityScore: number;
      roiData?: Uint8ClampedArray;
    }[]
  ): Promise<void> {
    if (!this.videoElement) return;
    const ocrStartTime = performance.now();
    const candidates: ScanCandidate[] = [];
    let rowOcrLatency = 0;
    let slotOcrLatency = 0;

    for (const item of regionsToScan) {
      this.lastOcrTimestamps[item.rIdx] = Date.now();

      let result: { geneString: string; confidence: number; slotConfidences?: number[] } | null = null;
      let isFastPath = false;

      // FAST-PATH: Sub-0.2ms In-Memory Template Matcher & Chromatic Color Guard
      if (item.roiData) {
        this.setPipelineStage('template-match');
        const templateResult = DesktopTemplateRecognizer.recognizeFromRoi(
          item.roiData,
          item.wPx,
          item.hPx,
          item.geneWPx,
          item.gapWPx
        );

        if (
          templateResult &&
          templateResult.confidence >= SCANNER_CONFIG.recognition.minConfidence
        ) {
          result = {
            geneString: templateResult.geneString,
            confidence: templateResult.confidence,
            slotConfidences: templateResult.slotConfidences
          };
          rowOcrLatency += templateResult.latencyMs;
          isFastPath = true;
        }
      }

      if (result && result.geneString) {
        candidates.push({
          regionIndex: item.rIdx,
          regionType: item.type,
          genes: result.geneString,
          confidence: result.confidence,
          geneConfidences: result.slotConfidences || [],
          activityScore: item.activityScore,
          valid: true,
          isFastPath
        });
      }
    }

    this.lastOcrLatency = performance.now() - ocrStartTime;
    this.lastRowOcrLatency = rowOcrLatency;
    this.lastSlotOcrLatency = slotOcrLatency;
    this.setPipelineStage('vote');

    if (candidates.length === 0) {
      this.setPipelineStage('no-result');
      if (this.activeRegionType !== 'none') {
        this.activeRegionType = 'none';
        this.emit({
          type: 'ACTIVE_REGION_CHANGED',
          activeRegion: null,
          regionIndex: undefined
        });
      }
      return;
    }

    // Step 2b: Identify which region actually detected real gene slots
    const detectedCandidate = candidates.slice().sort((a, b) => b.confidence - a.confidence)[0];
    const detectedRegionType: 'none' | ScannerRegionType = detectedCandidate.regionType;
    const detectedRegionIndex = detectedCandidate.regionIndex;

    if (detectedRegionType !== this.activeRegionType) {
      this.activeRegionType = detectedRegionType;
      this.emit({
        type: 'ACTIVE_REGION_CHANGED',
        activeRegion: detectedRegionType,
        regionIndex: detectedRegionIndex
      });
    }

    // Step 3: Confirm and emit each region independently.
    let emittedAny = false;
    let confirmedAny = false;
    const emittedGenesThisCycle = new Set<string>();

    for (const candidate of candidates) {
      this.latestConfidence = candidate.confidence;

      // Step 4: Temporal confirmation (per-region history with instant fast-path)
      const votedResult = this.votingService.addCandidate(
        candidate.regionIndex,
        {
          geneString: candidate.genes,
          confidence: candidate.confidence
        },
        candidate.isFastPath
      );

      if (!votedResult) continue;
      confirmedAny = true;

      // Guard: the same genotype captured by two overlapping regions in one cycle
      // (e.g. a single tooltip) should only be emitted once.
      if (emittedGenesThisCycle.has(votedResult.geneString)) continue;

      // Step 5: Display-state lock (per-region dedup)
      const targetRegion = regionsToScan.find(r => r.rIdx === candidate.regionIndex);
      const signature = targetRegion ? targetRegion.signature : 0;

      const shouldEmit = this.deduplicator.shouldAccept(
        candidate.regionIndex,
        votedResult.geneString,
        signature
      );

      if (!shouldEmit) {
        continue;
      }

      console.log(`[Scanner EMIT] >>> SAPLING-FOUND: ${votedResult.geneString} (conf: ${Math.round(votedResult.confidence)}%)`);

      this.acceptedCount++;
      this.pendingUiGene = votedResult.geneString;
      this.pendingUiStartedAt = performance.now();
      emittedGenesThisCycle.add(votedResult.geneString);
      emittedAny = true;

      this.emit({
        type: 'SAPLING-FOUND',
        regionIndex: candidate.regionIndex,
        regionType: candidate.regionType,
        geneString: votedResult.geneString,
        confidence: votedResult.confidence
      });
    }

    if (emittedAny) {
      this.setPipelineStage('ui-pending');
    } else if (confirmedAny) {
      this.setPipelineStage('duplicate');
    } else {
      this.setPipelineStage('vote-wait');
    }
  }

  /**
   * Renders a zoomed surround preview with 6 gene slot guide stripes.
   * Includes surrounding context padding so users can easily align tooltips.
   */
  private renderPreview(
    rIdx: number,
    xPx: number,
    yPx: number,
    wPx: number,
    hPx: number,
    reg: ScannerRegion
  ): void {
    if (!this.videoElement || this.videoElement.videoWidth === 0) return;

    const videoW = this.videoElement.videoWidth;
    const videoH = this.videoElement.videoHeight;

    // Surrounding context padding
    const padX = Math.round(wPx * 0.15);
    const padY = Math.round(hPx * 0.75);

    const srcX = Math.max(0, xPx - padX);
    const srcY = Math.max(0, yPx - padY);
    const srcW = Math.min(videoW - srcX, wPx + padX * 2);
    const srcH = Math.min(videoH - srcY, hPx + padY * 2);

    if (srcW <= 0 || srcH <= 0) return;

    // Target preview resolution for crisp high-DPI display without CPU lag
    const targetW = 360;
    const scale = targetW / srcW;
    const targetH = Math.round(srcH * scale);

    if (!this.previewCanvases[rIdx]) {
      this.previewCanvases[rIdx] = document.createElement('canvas');
    }
    const pCanvas = this.previewCanvases[rIdx];
    pCanvas.width = targetW;
    pCanvas.height = targetH;
    const pCtx = pCanvas.getContext('2d');
    if (!pCtx) return;

    // 1. Draw zoomed surrounding video area
    pCtx.imageSmoothingEnabled = false;
    pCtx.drawImage(this.videoElement, srcX, srcY, srcW, srcH, 0, 0, targetW, targetH);

    // 2. Compute local coordinates of the exact capture bounding box inside the preview
    const localBoxX = (xPx - srcX) * scale;
    const localBoxY = (yPx - srcY) * scale;
    const localBoxW = wPx * scale;
    const localBoxH = hPx * scale;

    // 3. Darken the outside surrounding context slightly for focus
    pCtx.fillStyle = 'rgba(0, 0, 0, 0.45)';
    // Top
    pCtx.fillRect(0, 0, targetW, localBoxY);
    // Bottom
    pCtx.fillRect(0, localBoxY + localBoxH, targetW, targetH - (localBoxY + localBoxH));
    // Left
    pCtx.fillRect(0, localBoxY, localBoxX, localBoxH);
    // Right
    pCtx.fillRect(localBoxX + localBoxW, localBoxY, targetW - (localBoxX + localBoxW), localBoxH);

    // 4. Draw the 6 gene slot stripes (alternating white/shaded guide columns)
    const geneWPx = Math.round(wPx * reg.GENE_WIDTH_TO_WIDTH_RATIO);
    const totalGeneW = geneWPx * 6;
    const gapW = Math.max(0, (wPx - totalGeneW) / 5);

    for (let slot = 0; slot < 6; slot++) {
      const slotSrcX = xPx + slot * (geneWPx + gapW);
      const slotLocalX = (slotSrcX - srcX) * scale;
      const slotLocalW = geneWPx * scale;

      // Alternating stripe highlight
      pCtx.fillStyle = slot % 2 === 0 ? 'rgba(255, 255, 255, 0.28)' : 'rgba(200, 200, 200, 0.15)';
      pCtx.fillRect(slotLocalX, localBoxY, slotLocalW, localBoxH);

      // Slot divider border
      pCtx.strokeStyle = 'rgba(0, 229, 255, 0.6)';
      pCtx.lineWidth = 1;
      pCtx.strokeRect(slotLocalX, localBoxY, slotLocalW, localBoxH);

      // Slot number label above slot
      pCtx.fillStyle = '#00E5FF';
      pCtx.font = 'bold 9px monospace';
      pCtx.textAlign = 'center';
      pCtx.fillText(`${slot + 1}`, slotLocalX + slotLocalW / 2, Math.max(10, localBoxY - 3));
    }

    // 5. Draw the outer bounding box
    pCtx.strokeStyle = '#00E5FF';
    pCtx.lineWidth = 1.5;
    pCtx.strokeRect(localBoxX, localBoxY, localBoxW, localBoxH);

    this.emit({
      type: 'PREVIEW',
      regionIndex: rIdx,
      regionType: rIdx === 0 ? 'inventory' : 'planter',
      previewDataUrl: pCanvas.toDataURL('image/jpeg', 0.68)
    });
  }

  public getDiagnostics(): ScannerDiagnostics {
    const now = performance.now();
    return {
      fps: this.currentFps,
      tickGapMs: Math.round(this.lastTickGap * 10) / 10,
      videoFrameAgeMs: Math.round((this.lastVideoFrameTime > 0 ? now - this.lastVideoFrameTime : 0) * 10) / 10,
      videoFrameGapMs: Math.round(this.lastVideoFrameGap * 10) / 10,
      lastScanLatencyMs: Math.round(this.lastScanLatency * 10) / 10,
      lastOcrLatencyMs: Math.round(this.lastOcrLatency * 10) / 10,
      rowOcrLatencyMs: Math.round(this.lastRowOcrLatency * 10) / 10,
      slotOcrLatencyMs: Math.round(this.lastSlotOcrLatency * 10) / 10,
      pipelineStage: this.pipelineStage,
      pipelineStageAgeMs: Math.round((now - this.pipelineStageStartedAt) * 10) / 10,
      uiUpdateLatencyMs: Math.round((this.pendingUiStartedAt > 0 ? now - this.pendingUiStartedAt : this.lastUiUpdateLatency) * 10) / 10,
      pageVisibility: document.visibilityState,
      captureResolution: this.videoElement
        ? `${this.videoElement.videoWidth}x${this.videoElement.videoHeight}`
        : '0x0',
      confidence: Math.round(this.latestConfidence),
      totalScans: this.scanCount,
      acceptedPlants: this.acceptedCount,
      rejectedScans: this.rejectedCount,
      activeRegion: this.activeRegionType,
      inventoryActivity: this.activityScores[0] || 0,
      planterActivity: this.activityScores[1] || 0,
      isStarved: this.starvationDetector.getIsStarved(),
      starvationReason: this.starvationDetector.getStarvationReason()
    };
  }

  public acknowledgeGeneHandled(geneString: string): void {
    if (geneString !== this.pendingUiGene || this.pendingUiStartedAt === 0) return;
    this.lastUiUpdateLatency = performance.now() - this.pendingUiStartedAt;
    this.pendingUiGene = '';
    this.pendingUiStartedAt = 0;
    this.setPipelineStage('accepted');
  }

  private setPipelineStage(stage: string): void {
    if (stage === this.pipelineStage) return;
    this.pipelineStage = stage;
    this.pipelineStageStartedAt = performance.now();
  }

  public moveRegion(regionIndex: number, dx: number, dy: number, videoW = 1920, videoH = 1080): void {
    const reg = this.regions[regionIndex];
    if (!reg) return;

    // Support both normalized delta (< 0.5) and pixel delta (>= 1)
    const actualVideoW = this.videoElement?.videoWidth || videoW;
    const actualVideoH = this.videoElement?.videoHeight || videoH;
    const dxNorm = Math.abs(dx) < 0.5 ? dx : dx / actualVideoW;
    const dyNorm = Math.abs(dy) < 0.5 ? dy : dy / actualVideoH;

    reg.TOP_LEFT_X = Math.max(0, Math.min(1 - reg.WIDTH, reg.TOP_LEFT_X + dxNorm));
    const normH = reg.WIDTH * reg.HEIGHT_TO_WIDTH_RATIO;
    reg.TOP_LEFT_Y = Math.max(0, Math.min(1 - normH, reg.TOP_LEFT_Y + dyNorm));

    this.saveRegions();

    // Trigger instant preview frame re-render
    if (this.videoElement && this.videoElement.videoWidth > 0) {
      const xPx = Math.round(actualVideoW * reg.TOP_LEFT_X);
      const yPx = Math.round(actualVideoH * reg.TOP_LEFT_Y);
      const wPx = Math.round(actualVideoW * reg.WIDTH);
      const hPx = Math.ceil(actualVideoH * normH);
      this.renderPreview(regionIndex, xPx, yPx, wPx, hPx, reg);
    }
  }

  public scaleRegion(regionIndex: number, dw: number, videoW = 1920): void {
    const reg = this.regions[regionIndex];
    if (!reg) return;

    const actualVideoW = this.videoElement?.videoWidth || videoW;
    const actualVideoH = this.videoElement?.videoHeight || 1080;
    const dwNorm = Math.abs(dw) < 0.5 ? dw : dw / actualVideoW;
    const newWidth = Math.max(0.02, Math.min(0.5, reg.WIDTH + dwNorm));
    const normH = newWidth * reg.HEIGHT_TO_WIDTH_RATIO;

    if (reg.TOP_LEFT_X + newWidth <= 1.0 && reg.TOP_LEFT_Y + normH <= 1.0) {
      reg.WIDTH = newWidth;
    }

    this.saveRegions();

    // Trigger instant preview frame re-render
    if (this.videoElement && this.videoElement.videoWidth > 0) {
      const xPx = Math.round(actualVideoW * reg.TOP_LEFT_X);
      const yPx = Math.round(actualVideoH * reg.TOP_LEFT_Y);
      const wPx = Math.round(actualVideoW * reg.WIDTH);
      const hPx = Math.ceil(actualVideoH * normH);
      this.renderPreview(regionIndex, xPx, yPx, wPx, hPx, reg);
    }
  }

  public adjustHeightRatio(regionIndex: number, dRatio: number): void {
    const reg = this.regions[regionIndex];
    if (!reg) return;
    reg.HEIGHT_TO_WIDTH_RATIO = Math.max(0.05, Math.min(0.5, reg.HEIGHT_TO_WIDTH_RATIO + dRatio));
  }

  public adjustGeneWidthRatio(regionIndex: number, dRatio: number): void {
    const reg = this.regions[regionIndex];
    if (!reg) return;
    reg.GENE_WIDTH_TO_WIDTH_RATIO = Math.max(0.02, Math.min(0.25, reg.GENE_WIDTH_TO_WIDTH_RATIO + dRatio));
  }

  public setRegions(newRegions: ScannerRegion[]): void {
    this.regions = newRegions.map(r => ({ ...r }));
    this.saveRegions();

    // Trigger instant preview frame re-render for both regions
    if (this.videoElement && this.videoElement.videoWidth > 0) {
      const videoW = this.videoElement.videoWidth;
      const videoH = this.videoElement.videoHeight;
      for (let rIdx = 0; rIdx < this.regions.length; rIdx++) {
        const reg = this.regions[rIdx];
        const xPx = Math.round(videoW * reg.TOP_LEFT_X);
        const yPx = Math.round(videoH * reg.TOP_LEFT_Y);
        const wPx = Math.round(videoW * reg.WIDTH);
        const normH = reg.WIDTH * reg.HEIGHT_TO_WIDTH_RATIO;
        const hPx = Math.ceil(videoH * normH);
        this.renderPreview(rIdx, xPx, yPx, wPx, hPx, reg);
      }
    }
  }

  public saveRegions(): void {
    StorageService.saveScannerRegions(this.regions);
  }

  public resetRegions(): ScannerRegion[] {
    this.regions = StorageService.resetScannerRegions();
    if (this.videoElement && this.videoElement.videoWidth > 0) {
      this.setRegions(this.regions);
    }
    return this.regions;
  }

  public getRegions(): ScannerRegion[] {
    return this.regions;
  }

  public getVideoElement(): HTMLVideoElement | null {
    return this.videoElement;
  }

  /**
   * Runs 1-Click Auto Calibration using the live video stream from screen capture.
   */
  public async autoCalibrate(preferredRegionIndex?: number): Promise<AutoCalibrateResult> {
    if (!this.videoElement || this.videoElement.videoWidth === 0) {
      return {
        success: false,
        regionIndex: preferredRegionIndex ?? 0,
        message: 'Screen capture is not active. Please start the scanner first.'
      };
    }

    const res = await AutoCalibrator.calibrateFromVideo(
      this.videoElement,
      preferredRegionIndex,
      this.recognizer
    );

    if (res.success && res.region) {
      const updatedRegions = [...this.regions];
      updatedRegions[res.regionIndex] = res.region;
      this.setRegions(updatedRegions);
    }

    return res;
  }

  public stop(): void {
    this.isScanning = false;
    this.isInitializing = false;
    this.starvationDetector.reset();

    if (this.tickerWorker) {
      try {
        this.tickerWorker.postMessage('STOP');
        this.tickerWorker.terminate();
      } catch {
        // ignore
      }
      this.tickerWorker = null;
    }

    if (this.scanTimerId) {
      clearTimeout(this.scanTimerId);
      this.scanTimerId = null;
    }

    if (this.mediaStream) {
      for (const track of this.mediaStream.getTracks()) {
        track.stop();
      }
      this.mediaStream = null;
    }

    if (this.videoElement) {
      this.videoElement.srcObject = null;
      if (this.videoElement.parentNode) {
        this.videoElement.parentNode.removeChild(this.videoElement);
      }
      this.videoElement = null;
    }

    this.emit({ type: 'STOPPED' });
  }

  public async terminateAll(): Promise<void> {
    this.stop();
    await this.recognizer.terminate();
  }
}
