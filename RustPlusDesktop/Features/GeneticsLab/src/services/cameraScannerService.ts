import {
  CameraAnalysisResult,
  CameraFacingMode,
  CameraFrameAnalyzer,
  CameraReadSource,
  CameraScannerErrorCode,
  CameraScannerEvent,
  CameraScannerEventListener,
  CameraCaptureResult,
  CameraScannerState,
  CameraSlotReport,
  CameraTarget,
  CameraTrackCapabilities,
  GeneRecognitionResult,
  Point,
  RasterImage
} from './scanner/scannerTypes.ts';
import { CAMERA_SCANNER_CONFIG } from './scanner/cameraScannerConfig.ts';
import {
  createIdleCameraState,
  isCameraCaptureSupported,
  isCameraSecureContext
} from './scanner/cameraSupport.ts';
import { buildCameraGeneStrip } from './scanner/vision/cameraGeneStrip.ts';
import {
  GlyphRowRecognizer,
  inspectGeneRow,
  recognizeGenesByTemplate
} from './scanner/vision/templateGeneRecognizer.ts';
import { CameraSlotOcr, createTesseractSlotOcr } from './scanner/vision/cameraSlotOcr.ts';
import { rasterToCanvas, releaseRasterCanvases } from './scanner/vision/frameGrabber.ts';
import { TemporalVotingService } from './scanner/TemporalVotingService.ts';
import { CameraTargetRearm } from './scanner/CameraTargetRearm.ts';

export * from './scanner/cameraScannerConfig.ts';

/** Vote history key. The camera path has exactly one target at a time. */
const CAMERA_VOTE_KEY = 'camera';

export interface WakeLockHandle {
  release(): Promise<void> | void;
  addEventListener?(type: 'release', handler: () => void): void;
}

/**
 * Everything the camera scanner touches outside itself. Injected so the lifecycle can be
 * tested without a DOM, and so the service never reaches for a global at an awkward moment.
 */
export interface CameraScannerEnvironment {
  mediaDevices: MediaDevices | null;
  isSecureContext: boolean;
  isDocumentHidden: () => boolean;
  addVisibilityListener: (handler: () => void) => () => void;
  now: () => number;
  requestWakeLock: () => Promise<WakeLockHandle | null>;
}

export interface CameraScannerOptions {
  env?: Partial<CameraScannerEnvironment>;
  /** Overridable for tests. Defaults to template matching against the gene alphabet. */
  glyphRecognizer?: GlyphRowRecognizer;
  /**
   * Second-opinion OCR. Omit to lazily load Tesseract once the camera is running; pass null
   * to run on template matching alone, which is what the headless tests do.
   */
  slotOcr?: CameraSlotOcr | null;
}

function defaultEnvironment(): CameraScannerEnvironment {
  const nav = typeof navigator !== 'undefined' ? navigator : undefined;
  const doc = typeof document !== 'undefined' ? document : undefined;

  return {
    mediaDevices: nav?.mediaDevices ?? null,
    isSecureContext: isCameraSecureContext(),
    isDocumentHidden: () => doc?.visibilityState === 'hidden',
    addVisibilityListener: (handler: () => void) => {
      if (!doc) return () => {};
      doc.addEventListener('visibilitychange', handler);
      return () => doc.removeEventListener('visibilitychange', handler);
    },
    now: () => Date.now(),
    requestWakeLock: async () => {
      const wakeLock = (nav as unknown as { wakeLock?: { request(type: string): Promise<WakeLockHandle> } })
        ?.wakeLock;
      if (!wakeLock) return null;
      try {
        return await wakeLock.request('screen');
      } catch {
        return null;
      }
    }
  };
}

/**
 * Constraint ladder, tried in order. `exact` is never used: a phone that cannot deliver
 * 1080p should still be allowed to scan at whatever it can produce.
 */
export function buildConstraintTiers(facingMode: CameraFacingMode): MediaStreamConstraints[] {
  const { idealWidth, idealHeight, idealFrameRate, maxFrameRate } = CAMERA_SCANNER_CONFIG.capture;

  return [
    {
      audio: false,
      video: {
        facingMode: { ideal: facingMode },
        width: { ideal: idealWidth },
        height: { ideal: idealHeight },
        frameRate: { ideal: idealFrameRate, max: maxFrameRate }
      }
    },
    {
      audio: false,
      video: { facingMode: { ideal: facingMode } }
    },
    {
      audio: false,
      video: true
    }
  ];
}

export function classifyCameraError(err: unknown): CameraScannerErrorCode {
  const name = (err as { name?: string } | null)?.name;

  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      return 'permission-denied';
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      return 'no-camera';
    case 'NotReadableError':
    case 'TrackStartError':
    case 'OverconstrainedError':
    case 'ConstraintNotSatisfiedError':
    case 'AbortError':
      return 'stream-failed';
    default:
      return 'unknown';
  }
}

/**
 * Errors where retrying with looser constraints cannot help, and retrying would either
 * re-prompt the user for permission or waste time on a device that does not exist.
 */
function isTerminalRequestError(err: unknown): boolean {
  const code = classifyCameraError(err);
  return code === 'permission-denied' || code === 'no-camera';
}

const CAMERA_ERROR_MESSAGES: Record<CameraScannerErrorCode, string> = {
  'insecure-context': 'Camera scanning needs a secure (HTTPS) connection.',
  unsupported: 'This browser cannot open a camera. Add clones manually, or use the desktop scanner.',
  'permission-denied': 'Camera permission was denied. Allow camera access for this site, then try again.',
  'no-camera': 'No camera was found on this device.',
  'stream-failed': 'The camera could not be started. Close other apps using the camera and try again.',
  'stream-ended': 'The camera stopped. Restart the camera to keep scanning.',
  'vision-init-failed': 'Camera detection failed to start. Restart the camera to try again.',
  unknown: 'The camera could not be started.'
};

/**
 * Cadence and resolution steps used under thermal or performance pressure.
 *
 * Confidence gates are deliberately absent: the scanner may get slower, never less careful.
 */
const DEGRADATION_STEPS: Array<{ fps: number; discoveryWidth: number }> = [
  { fps: CAMERA_SCANNER_CONFIG.cadence.trackingFps, discoveryWidth: CAMERA_SCANNER_CONFIG.analysis.maxDiscoveryWidth },
  { fps: CAMERA_SCANNER_CONFIG.cadence.discoveryFps, discoveryWidth: CAMERA_SCANNER_CONFIG.analysis.maxDiscoveryWidth },
  { fps: CAMERA_SCANNER_CONFIG.cadence.discoveryFps, discoveryWidth: CAMERA_SCANNER_CONFIG.analysis.minDiscoveryWidth },
  { fps: CAMERA_SCANNER_CONFIG.cadence.minDiscoveryFps, discoveryWidth: CAMERA_SCANNER_CONFIG.analysis.minDiscoveryWidth }
];

const FRAME_COST_WINDOW = 20;

/**
 * Phone-camera scanner.
 *
 * Owns the camera stream lifecycle, the processing cadence, and the recognition gates. All
 * computer vision lives behind a `CameraFrameAnalyzer`; recognition reuses the same
 * preprocessor, Tesseract recogniser and temporal voting as the desktop path.
 */
export class CameraScannerService {
  private readonly env: CameraScannerEnvironment;
  private readonly recognizeGlyphs: GlyphRowRecognizer;
  // Camera confidence floor, not the desktop one. See CAMERA_SCANNER_CONFIG.recognition.
  private readonly voting = new TemporalVotingService(
    CAMERA_SCANNER_CONFIG.recognition.minRowConfidence
  );
  private readonly rearm = new CameraTargetRearm(CAMERA_SCANNER_CONFIG.confirmation.rearmAfterLossMs);

  private listeners: CameraScannerEventListener[] = [];
  private state: CameraScannerState = createIdleCameraState();

  private mediaStream: MediaStream | null = null;
  private videoElement: HTMLVideoElement | null = null;
  private trackEndedCleanup: (() => void) | null = null;
  private visibilityCleanup: (() => void) | null = null;
  private wakeLock: WakeLockHandle | null = null;

  private analyzerFactory: (() => CameraFrameAnalyzer) | null = null;
  private analyzer: CameraFrameAnalyzer | null = null;
  private analysisTimerId: ReturnType<typeof setInterval> | null = null;
  private isAnalyzing = false;
  private isRecognizing = false;
  private readAttempts = 0;
  private lastReadAt = 0;
  private acceptedUntil = 0;
  private lastComponentCount = 0;
  /** Most recent row with usable geometry, whether or not a quality gate let it through. */
  private captureCandidate: CameraTarget | null = null;
  private slotOcr: CameraSlotOcr | null = null;
  private readonly ocrInjected: boolean;
  private ocrLoading = false;
  private lastOcrAt = 0;
  /** Kept for the debug line only, never for confirming a later frame's template read. */
  private lastOcrSlots: string[] | null = null;
  private debugPreviewEnabled = false;
  private lastPreviewAt = 0;
  private lastStrip: { image: import('./scanner/scannerTypes.ts').RasterImage } | null = null;

  private degradationLevel = 0;
  private frameCosts: number[] = [];

  private isPaused = false;
  private isHidden = false;

  /** Invalidates in-flight async work (permission prompt, warm-up, OCR) after a stop. */
  private sessionToken = 0;
  private isSwitchingCamera = false;

  constructor(options: CameraScannerOptions = {}) {
    this.env = { ...defaultEnvironment(), ...options.env };
    this.recognizeGlyphs = options.glyphRecognizer ?? recognizeGenesByTemplate;
    this.ocrInjected = 'slotOcr' in options;
    this.slotOcr = options.slotOcr ?? null;
  }

  public static isSupported(env?: Partial<CameraScannerEnvironment>): boolean {
    return env && 'mediaDevices' in env
      ? isCameraCaptureSupported(env.mediaDevices)
      : isCameraCaptureSupported();
  }

  public static isSecureContext(env?: Partial<CameraScannerEnvironment>): boolean {
    if (env && 'isSecureContext' in env) return env.isSecureContext === true;
    return isCameraSecureContext();
  }

  public addEventListener(listener: CameraScannerEventListener): () => void {
    this.listeners.push(listener);
    return () => {
      this.listeners = this.listeners.filter(l => l !== listener);
    };
  }

  public getState(): CameraScannerState {
    return this.state;
  }

  public isRunning(): boolean {
    return this.mediaStream !== null;
  }

  /**
   * Installs the detector. The service creates one analyzer per camera session and disposes
   * it on stop, so vision resources never outlive the stream that needed them.
   */
  public setAnalyzerFactory(factory: (() => CameraFrameAnalyzer) | null): void {
    this.analyzerFactory = factory;
  }

  /** Called by the mobile UI when its <video> mounts, which may be before or after start(). */
  public attachVideo(element: HTMLVideoElement | null): void {
    if (this.videoElement && this.videoElement !== element) {
      this.videoElement.srcObject = null;
    }
    this.videoElement = element;
    if (element && this.mediaStream) {
      this.bindStreamToVideo(element, this.mediaStream);
    }
  }

  /**
   * Reads the row on screen right now, regardless of the quality gates.
   *
   * The automatic path is deliberately conservative and will refuse a row it is not certain
   * about. This is the manual escape hatch: it reads whatever geometry the locator last
   * found and hands the answer back for the user to confirm, so a visibly readable row is
   * never unreachable because a threshold disagrees.
   */
  public captureNow(): CameraCaptureResult {
    const target = this.captureCandidate;
    if (!target) return { status: 'no-row', geneString: null, confidence: null };

    const built = buildCameraGeneStrip(target.normalizedRow, target.slots, {
      cellHeight: CAMERA_SCANNER_CONFIG.recognition.cellHeight,
      padding: 16,
      gap: 16
    });
    if (!built) return { status: 'unreadable', geneString: null, confidence: null };

    // Deliberately ungated. Manual capture exists precisely for the rows the automatic
    // gates refuse, so it falls back to each slot's nearest template and lets the user fix
    // whatever is wrong. Only a slot with no ink at all has nothing to offer.
    const reports = inspectGeneRow(built.slotImages);
    if (reports.length !== 6 || reports.some(report => !report.gene)) {
      return { status: 'unreadable', geneString: null, confidence: null };
    }

    const geneString = reports.map(report => report.gene).join('');
    const meanMargin = reports.reduce((sum, report) => sum + report.margin, 0) / reports.length;

    return {
      status: 'read',
      geneString,
      confidence: Math.min(100, Math.round(45 + meanMargin * 60))
    };
  }

  /** Confirms a manually captured or corrected row, emitting it like any accepted read. */
  public commitManualRead(geneString: string): void {
    this.rearm.recordEmit(geneString);
    this.voting.reset(CAMERA_VOTE_KEY);
    this.acceptedUntil = this.env.now() + CAMERA_SCANNER_CONFIG.confirmation.acceptedHoldMs;

    this.setState({
      phase: 'accepted',
      acceptedCount: this.state.acceptedCount + 1,
      lastAcceptedGenes: geneString,
      qualityIssues: []
    });

    this.emit({ type: 'SAPLING-FOUND', geneString, confidence: 100 });
  }

  /** Resolves an ambiguous scene from a tap, in analysis frame coordinates. */
  public selectCandidateAt(point: Point): void {
    this.analyzer?.selectAt?.(point);
  }

  public async start(): Promise<boolean> {
    if (this.state.phase !== 'idle' && this.state.phase !== 'error') return false;

    if (!CameraScannerService.isSecureContext(this.env)) {
      this.failWith('insecure-context');
      return false;
    }

    if (!CameraScannerService.isSupported(this.env)) {
      this.failWith('unsupported');
      return false;
    }

    const token = ++this.sessionToken;
    this.isPaused = false;
    this.isHidden = this.env.isDocumentHidden();
    this.degradationLevel = 0;
    this.frameCosts = [];
    this.voting.reset();
    this.rearm.reset();
    this.setState({
      phase: 'requesting-permission',
      errorCode: undefined,
      errorMessage: undefined,
      qualityIssues: [],
      candidateCount: 0,
      overlay: null,
      // Template matching needs no worker and no asset download; it is ready immediately.
      isOcrReady: true,
      isOcrUnavailable: false
    });

    let stream: MediaStream;
    try {
      stream = await this.requestStream(this.state.facingMode);
    } catch (err) {
      if (token !== this.sessionToken) return false;
      this.failWith(classifyCameraError(err), (err as { message?: string } | null)?.message);
      return false;
    }

    // The user can close the scanner while the permission prompt is still open.
    if (token !== this.sessionToken) {
      stopStreamTracks(stream);
      return false;
    }

    const adopted = this.adoptStream(stream, token);
    if (adopted) void this.acquireWakeLock(token);
    return adopted;
  }

  private async requestStream(facingMode: CameraFacingMode): Promise<MediaStream> {
    const mediaDevices = this.env.mediaDevices;
    if (!mediaDevices) throw new DOMException('Camera unavailable', 'NotFoundError');

    const tiers = buildConstraintTiers(facingMode);
    let lastError: unknown = new DOMException('Camera unavailable', 'NotFoundError');

    for (const constraints of tiers) {
      try {
        return await mediaDevices.getUserMedia(constraints);
      } catch (err) {
        lastError = err;
        if (isTerminalRequestError(err)) throw err;
      }
    }

    throw lastError;
  }

  private adoptStream(stream: MediaStream, token: number): boolean {
    const track = stream.getVideoTracks()[0];
    if (!track) {
      stopStreamTracks(stream);
      this.failWith('stream-failed', 'No video track was returned by the camera.');
      return false;
    }

    this.mediaStream = stream;
    this.setState({ phase: 'starting' });

    const onEnded = () => this.handleStreamEnded(token);
    track.addEventListener('ended', onEnded);
    this.trackEndedCleanup = () => track.removeEventListener('ended', onEnded);

    if (this.videoElement) {
      this.bindStreamToVideo(this.videoElement, stream);
    }

    const settings = readTrackSettings(track);
    this.setState({
      phase: 'searching',
      facingMode: settings.facingMode ?? this.state.facingMode,
      captureResolution: settings.resolution,
      capabilities: readTrackCapabilities(track)
    });

    this.visibilityCleanup?.();
    this.visibilityCleanup = this.env.addVisibilityListener(() => this.handleVisibilityChange());

    void this.refreshCameraSwitchAvailability(token);
    // Loads in the background so the first frames are read by template matching alone
    // rather than waiting on a worker that may take seconds to appear on a cold cache.
    this.ensureSlotOcr();
    this.startAnalysisLoop();
    return true;
  }

  private bindStreamToVideo(element: HTMLVideoElement, stream: MediaStream): void {
    element.srcObject = stream;
    element.muted = true;
    element.playsInline = true;
    const played = element.play?.();
    if (played && typeof played.catch === 'function') {
      // Autoplay can be rejected until the user interacts; the visible controls recover it.
      played.catch(() => {});
    }
  }

  private async refreshCameraSwitchAvailability(token: number): Promise<void> {
    const mediaDevices = this.env.mediaDevices;
    if (!mediaDevices || typeof mediaDevices.enumerateDevices !== 'function') return;

    try {
      const devices = await mediaDevices.enumerateDevices();
      if (token !== this.sessionToken) return;
      const cameras = devices.filter(d => d.kind === 'videoinput');
      this.setState({ canSwitchCamera: cameras.length > 1 });
    } catch {
      // Enumeration can be blocked; camera switching simply stays hidden.
    }
  }

  public async switchCamera(): Promise<void> {
    if (!this.isRunning() || this.isSwitchingCamera) return;

    this.isSwitchingCamera = true;
    const nextFacing: CameraFacingMode = this.state.facingMode === 'environment' ? 'user' : 'environment';
    const token = ++this.sessionToken;

    this.stopAnalysisLoop();
    this.releaseStream();
    this.discardPendingRecognition();
    this.setState({
      phase: 'starting',
      facingMode: nextFacing,
      qualityIssues: [],
      candidateCount: 0,
      overlay: null
    });

    try {
      const stream = await this.requestStream(nextFacing);
      if (token !== this.sessionToken) {
        stopStreamTracks(stream);
        return;
      }
      this.adoptStream(stream, token);
    } catch (err) {
      if (token === this.sessionToken) {
        this.failWith(classifyCameraError(err), (err as { message?: string } | null)?.message);
      }
    } finally {
      this.isSwitchingCamera = false;
    }
  }

  public pause(): void {
    if (!this.isRunning() || this.isPaused) return;
    this.isPaused = true;
    this.stopAnalysisLoop();
    this.discardPendingRecognition();
    this.setState({ phase: 'paused', qualityIssues: [], candidateCount: 0, overlay: null });
  }

  public resume(): void {
    if (!this.isRunning() || !this.isPaused) return;
    this.isPaused = false;
    this.discardPendingRecognition();
    this.setState({ phase: 'searching', qualityIssues: [], candidateCount: 0 });
    this.startAnalysisLoop();
  }

  private handleVisibilityChange(): void {
    const hidden = this.env.isDocumentHidden();
    if (hidden === this.isHidden) return;
    this.isHidden = hidden;

    if (hidden) {
      this.stopAnalysisLoop();
      // A confirmation window that spanned a backgrounded app is not evidence of anything.
      this.discardPendingRecognition();
      void this.releaseWakeLock();
      return;
    }

    if (this.isRunning() && !this.isPaused) {
      this.setState({ phase: 'searching', qualityIssues: [], candidateCount: 0 });
      this.startAnalysisLoop();
      void this.acquireWakeLock(this.sessionToken);
    }
  }

  private handleStreamEnded(token: number): void {
    if (token !== this.sessionToken || !this.isRunning()) return;
    this.teardown();
    this.setState({
      ...createIdleCameraState(),
      phase: 'error',
      errorCode: 'stream-ended',
      errorMessage: CAMERA_ERROR_MESSAGES['stream-ended']
    });
  }

  /* ---------------------------------------------------------------- *
   * Wake lock
   * ---------------------------------------------------------------- */

  private async acquireWakeLock(token: number): Promise<void> {
    if (this.wakeLock) return;
    try {
      const lock = await this.env.requestWakeLock();
      if (!lock) return;
      if (token !== this.sessionToken || !this.isRunning()) {
        void lock.release();
        return;
      }
      this.wakeLock = lock;
      // Losing the lock (the OS dims the screen, the tab is backgrounded) is normal and must
      // never surface as a scanner failure.
      lock.addEventListener?.('release', () => {
        if (this.wakeLock === lock) this.wakeLock = null;
      });
    } catch {
      // A screen that dims is a worse experience, not a broken scanner.
    }
  }

  private async releaseWakeLock(): Promise<void> {
    const lock = this.wakeLock;
    this.wakeLock = null;
    if (!lock) return;
    try {
      await lock.release();
    } catch {
      // ignore
    }
  }

  /* ---------------------------------------------------------------- *
   * Analysis cadence
   * ---------------------------------------------------------------- */

  private startAnalysisLoop(): void {
    if (this.analysisTimerId !== null) return;
    if (!this.isRunning() || this.isPaused || this.isHidden) return;

    if (!this.analyzer && this.analyzerFactory) {
      try {
        this.analyzer = this.analyzerFactory();
      } catch (err) {
        this.analyzer = null;
        this.failWith('vision-init-failed', (err as { message?: string } | null)?.message);
        return;
      }
    }

    // Without a detector there is nothing to run per frame, and an empty loop would burn
    // phone battery for no reason.
    if (!this.analyzer) {
      this.setState({ isDetectionAvailable: false });
      return;
    }

    this.setState({ isDetectionAvailable: true });
    const intervalMs = Math.round(1000 / DEGRADATION_STEPS[this.degradationLevel].fps);
    this.analysisTimerId = setInterval(() => void this.analyzeFrame(), intervalMs);
  }

  private stopAnalysisLoop(): void {
    if (this.analysisTimerId !== null) {
      clearInterval(this.analysisTimerId);
      this.analysisTimerId = null;
    }
  }

  private async analyzeFrame(): Promise<void> {
    if (this.isAnalyzing) return;
    const analyzer = this.analyzer;
    const video = this.videoElement;
    if (!analyzer || !video || !this.isRunning() || this.isPaused || this.isHidden) return;
    if (!video.videoWidth) return;

    this.isAnalyzing = true;
    const token = this.sessionToken;
    const startedAt = this.env.now();

    try {
      const result = await analyzer.analyze(video, startedAt);
      if (token !== this.sessionToken) return;

      this.applyAnalysis(result);
      this.recordFrameCost(this.env.now() - startedAt);

      if (result.phase === 'tracking' && result.activeTarget) {
        await this.recognizeTarget(result.activeTarget, token);
      }
    } catch {
      if (token === this.sessionToken) {
        this.discardPendingRecognition();
        this.setState({ phase: 'searching', qualityIssues: [], candidateCount: 0 });
      }
    } finally {
      this.isAnalyzing = false;
    }
  }

  private applyAnalysis(result: CameraAnalysisResult): void {
    const now = this.env.now();
    const targetPresent = result.phase === 'tracking' || result.phase === 'quality-blocked';

    if (targetPresent) {
      this.rearm.markVisible();
    } else {
      this.rearm.markLost(now);
    }

    // Only genuine loss of the target invalidates the window. A single blurred or shaky
    // frame does not: the samples already collected were each captured on a frame that
    // passed every gate, and 3-of-4 agreement between them is the real safety property.
    // Discarding on every quality blip meant a hand-held phone never accumulated a window
    // at all, and re-read the same row from scratch indefinitely.
    if (!targetPresent) {
      this.voting.reset(CAMERA_VOTE_KEY);
      // Reported here as well as after a read, otherwise the sample count lingers on screen
      // after the row has gone and suggests progress that no longer exists.
      if (this.state.diagnostics.pendingSamples !== 0) {
        this.setState({ diagnostics: { ...this.state.diagnostics, pendingSamples: 0 } });
      }
    }

    // Let a successful scan stay readable. The overlay and diagnostics keep updating
    // underneath so tracking stays live; only the headline is held.
    if (now < this.acceptedUntil) {
      this.setState({ overlay: result.overlay, candidateCount: result.candidateCount });
      return;
    }

    this.lastComponentCount = result.componentCount;
    this.captureCandidate = result.activeTarget ?? result.fallbackTarget;

    this.setState({
      phase: result.phase,
      qualityIssues: result.qualityIssues,
      candidateCount: result.candidateCount,
      overlay: result.overlay
    });
  }

  /* ---------------------------------------------------------------- *
   * Recognition
   * ---------------------------------------------------------------- */

  private async recognizeTarget(target: CameraTarget, token: number): Promise<void> {
    if (this.isRecognizing) return;

    const now = this.env.now();
    // Nothing to gain from re-reading a row we just accepted.
    if (now < this.acceptedUntil) return;

    this.isRecognizing = true;
    this.readAttempts++;
    this.setState({ phase: 'reading' });

    try {
      const built = buildCameraGeneStrip(target.normalizedRow, target.slots, {
        cellHeight: CAMERA_SCANNER_CONFIG.recognition.cellHeight,
        padding: 16,
        gap: 16
      });
      if (!built) return;

      // Six slots in, six answers out. Runs in microseconds, so it happens on every
      // tracking frame rather than on a throttle.
      const reports = inspectGeneRow(built.slotImages);
      const templateResult = this.recognizeGlyphs(built.slotImages);

      // The slow half, on its own throttle. Null means it did not run this frame.
      const ocrSlots = await this.readSlotsWithOcr(built.slotImages, now);
      if (token !== this.sessionToken) return;

      const ocrGenes =
        ocrSlots && ocrSlots.length === 6 && ocrSlots.every(letter => letter.length === 1)
          ? ocrSlots.join('')
          : null;

      // The letters each slot matched best, before the margin and distance gates had a say.
      // Those gates exist to stop a lone uncertain reader from guessing; they have nothing
      // to add once a second, independent reader has produced the same six letters.
      const nearestGenes =
        reports.length === 6 && reports.every(report => report.gene)
          ? reports.map(report => report.gene).join('')
          : null;

      const { result, source } = this.combineReads(templateResult, nearestGenes, ocrGenes);
      this.publishReadDiagnostics(result, source, built, reports, ocrSlots ?? this.lastOcrSlots);
      if (!result) return;

      // Two independent recognisers landing on the same six letters is stronger evidence
      // than the same recogniser repeating itself, so it skips the confirmation window
      // entirely. Waiting four frames for a row both readers already agree on is what made
      // a single clone cost a hundred reads.
      if (source === 'agreed') {
        this.acceptRead(result.geneString, result.confidence);
        return;
      }

      // Otherwise fall back to 3-of-4 temporal confirmation, as the desktop scanner does.
      const confirmed = this.voting.addCandidate(CAMERA_VOTE_KEY, result);
      if (!confirmed) return;
      this.acceptRead(confirmed.geneString, confirmed.confidence);
    } catch {
      // A failed read is just a frame that produced nothing.
    } finally {
      this.isRecognizing = false;
    }
  }

  /**
   * Reconciles the two recognisers.
   *
   * Agreement is the fast path. Disagreement is treated as neither reader being trustworthy
   * on its own, so the row goes back through temporal confirmation rather than one of them
   * being arbitrarily preferred.
   */
  private combineReads(
    templateResult: GeneRecognitionResult | null,
    nearestGenes: string | null,
    ocrGenes: string | null
  ): { result: GeneRecognitionResult | null; source: CameraReadSource | null } {
    if (ocrGenes && nearestGenes && ocrGenes === nearestGenes) {
      return {
        result: {
          geneString: ocrGenes,
          confidence: Math.max(templateResult?.confidence ?? 0, 90),
          rawText: ocrGenes
        },
        source: 'agreed'
      };
    }

    if (templateResult) return { result: templateResult, source: 'template' };

    if (ocrGenes) {
      // OCR alone still has to earn its place through repeated agreement, so this sits just
      // above the confirmation floor rather than anywhere near certainty.
      return {
        result: { geneString: ocrGenes, confidence: 55, rawText: ocrGenes },
        source: 'ocr'
      };
    }

    return { result: null, source: null };
  }

  /**
   * Runs OCR at most once per interval, and returns null on the frames it skips.
   *
   * Returning the previous frame's letters would let a stale row confirm the current one,
   * which is exactly the class of mistake that saves the wrong clone.
   */
  private async readSlotsWithOcr(slotImages: RasterImage[], now: number): Promise<string[] | null> {
    const ocr = this.slotOcr;
    if (!ocr) return null;
    if (now - this.lastOcrAt < CAMERA_SCANNER_CONFIG.recognition.ocrIntervalMs) return null;

    this.lastOcrAt = now;
    try {
      const letters = await ocr.readSlots(slotImages);
      this.lastOcrSlots = letters;
      return letters;
    } catch {
      return null;
    }
  }

  /** Emits a confirmed row, subject to the rearm guard so one clone is not added twice. */
  private acceptRead(geneString: string, confidence: number): void {
    if (!this.rearm.shouldEmit(geneString)) return;
    this.rearm.recordEmit(geneString);
    this.voting.reset(CAMERA_VOTE_KEY);

    this.acceptedUntil = this.env.now() + CAMERA_SCANNER_CONFIG.confirmation.acceptedHoldMs;
    this.setState({
      phase: 'accepted',
      acceptedCount: this.state.acceptedCount + 1,
      lastAcceptedGenes: geneString,
      qualityIssues: []
    });

    this.emit({ type: 'SAPLING-FOUND', geneString, confidence });
  }

  /**
   * Loads Tesseract once the camera is live.
   *
   * Deferred to here rather than to module load so the OCR bundle never reaches a desktop
   * user who will not open the camera, and so a failure to load degrades to template-only
   * scanning instead of breaking the session.
   */
  private ensureSlotOcr(): void {
    if (this.ocrInjected || this.slotOcr || this.ocrLoading) return;
    // OCR draws each slot into a canvas, so without a DOM there is nothing it can read.
    // This is also what keeps the headless lifecycle tests off the Tesseract bundle.
    if (typeof document === 'undefined') return;
    this.ocrLoading = true;

    const token = this.sessionToken;
    void (async () => {
      try {
        const ocr = await createTesseractSlotOcr();
        if (token !== this.sessionToken) {
          void ocr.terminate();
          return;
        }
        await ocr.warmup();
        if (token !== this.sessionToken) {
          void ocr.terminate();
          return;
        }
        this.slotOcr = ocr;
      } catch {
        // Template matching alone still reads rows. OCR is a second opinion, not a dependency.
      } finally {
        this.ocrLoading = false;
      }
    })();
  }

  private releaseSlotOcr(): void {
    if (this.ocrInjected) return;
    const ocr = this.slotOcr;
    this.slotOcr = null;
    this.lastOcrSlots = null;
    this.lastOcrAt = 0;
    if (ocr) void ocr.terminate();
  }

  /**
   * Surfaces what the recogniser actually saw. A read that is correct but below the
   * confidence floor looks identical to a read that failed outright unless it is reported.
   */
  private publishReadDiagnostics(
    result: GeneRecognitionResult | null,
    source: CameraReadSource | null,
    built: { strip: RasterImage; slotInk: number[]; slotsWithinBounds: boolean },
    slotReports: CameraSlotReport[],
    ocrSlots: string[] | null
  ): void {

    let stripPreview = this.state.diagnostics.stripPreview;
    if (this.debugPreviewEnabled) {
      const now = this.env.now();
      if (now - this.lastPreviewAt >= 500) {
        this.lastPreviewAt = now;
        stripPreview = encodeRasterPreview(built.strip);
      }
    } else {
      stripPreview = null;
    }

    this.setState({
      diagnostics: {
        readAttempts: this.readAttempts,
        lastRawText: result?.geneString ?? null,
        lastConfidence: result?.confidence ?? null,
        lastSource: source,
        pendingSamples: this.voting.getSampleCount(CAMERA_VOTE_KEY),
        sampleWindow: CAMERA_SCANNER_CONFIG.confirmation.samples,
        componentCount: this.lastComponentCount,
        slotInk: built.slotInk.map(value => Math.round(value * 100) / 100),
        slotReports,
        ocrSlots,
        slotsWithinBounds: built.slotsWithinBounds,
        stripPreview
      }
    });
  }

  private discardPendingRecognition(): void {
    this.voting.reset(CAMERA_VOTE_KEY);
    this.analyzer?.reset();
  }

  /* ---------------------------------------------------------------- *
   * Adaptive degradation
   * ---------------------------------------------------------------- */

  private recordFrameCost(durationMs: number): void {
    this.frameCosts.push(durationMs);
    if (this.frameCosts.length < FRAME_COST_WINDOW) return;

    const sorted = [...this.frameCosts].sort((a, b) => a - b);
    const p95 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))];
    this.frameCosts = [];

    const budgetMs = 1000 / DEGRADATION_STEPS[this.degradationLevel].fps;

    if (p95 > budgetMs && this.degradationLevel < DEGRADATION_STEPS.length - 1) {
      this.setDegradationLevel(this.degradationLevel + 1);
    } else if (p95 < budgetMs * 0.4 && this.degradationLevel > 0) {
      this.setDegradationLevel(this.degradationLevel - 1);
    }
  }

  private setDegradationLevel(level: number): void {
    this.degradationLevel = level;
    const step = DEGRADATION_STEPS[level];

    this.analyzer?.setDiscoveryWidth?.(step.discoveryWidth);
    this.frameCosts = [];

    // Reschedule at the new cadence. Confidence gates are untouched.
    if (this.analysisTimerId !== null) {
      this.stopAnalysisLoop();
      this.startAnalysisLoop();
    }
  }

  /**
   * Turns on a preview of the exact image handed to the recogniser. Off by default: it costs
   * a PNG encode, and it is only useful while diagnosing a read failure.
   */
  public setDebugPreviewEnabled(enabled: boolean): void {
    this.debugPreviewEnabled = enabled;
    if (!enabled) {
      this.setState({ diagnostics: { ...this.state.diagnostics, stripPreview: null } });
    }
  }

  public isDebugPreviewEnabled(): boolean {
    return this.debugPreviewEnabled;
  }

  /** Current degradation step, for diagnostics and tests. */
  public getDegradationLevel(): number {
    return this.degradationLevel;
  }

  /* ---------------------------------------------------------------- *
   * Teardown
   * ---------------------------------------------------------------- */

  public stop(): void {
    if (this.state.phase === 'idle') return;
    this.teardown();
    this.setState(createIdleCameraState());
  }

  /** Releases every resource without deciding what the resulting phase should be. */
  private teardown(): void {
    this.sessionToken++;
    this.isPaused = false;
    this.isHidden = false;
    this.isSwitchingCamera = false;
    this.isRecognizing = false;
    this.readAttempts = 0;
    this.lastReadAt = 0;
    this.acceptedUntil = 0;
    releaseRasterCanvases();
    this.releaseSlotOcr();
    this.degradationLevel = 0;
    this.frameCosts = [];

    this.stopAnalysisLoop();
    this.voting.reset();
    this.rearm.reset();

    if (this.analyzer) {
      try {
        this.analyzer.dispose();
      } catch {
        // A failed dispose must not block the rest of the teardown.
      }
      this.analyzer = null;
    }

    this.visibilityCleanup?.();
    this.visibilityCleanup = null;

    void this.releaseWakeLock();
    this.releaseStream();
  }

  private releaseStream(): void {
    this.trackEndedCleanup?.();
    this.trackEndedCleanup = null;

    if (this.mediaStream) {
      stopStreamTracks(this.mediaStream);
      this.mediaStream = null;
    }

    if (this.videoElement) {
      this.videoElement.srcObject = null;
    }
  }

  public async dispose(): Promise<void> {
    this.stop();
    this.listeners = [];
  }

  private failWith(code: CameraScannerErrorCode, message?: string): void {
    this.teardown();
    this.setState({
      ...createIdleCameraState(),
      phase: 'error',
      errorCode: code,
      errorMessage: message && code === 'unknown' ? message : CAMERA_ERROR_MESSAGES[code]
    });
  }

  private setState(patch: Partial<CameraScannerState>): void {
    this.state = { ...this.state, ...patch };
    this.emit({ type: 'CAMERA_STATE', state: this.state });
  }

  private emit(event: CameraScannerEvent): void {
    for (const listener of this.listeners) {
      listener(event);
    }
  }
}

function clamp(value: number, min: number, max: number): number {
  return value < min ? min : value > max ? max : value;
}

function encodeRasterPreview(
  image: import('./scanner/scannerTypes.ts').RasterImage
): string | null {
  const canvas = rasterToCanvas(image, 7);
  if (!canvas) return null;
  try {
    return canvas.toDataURL('image/png');
  } catch {
    return null;
  }
}

function stopStreamTracks(stream: MediaStream): void {
  for (const track of stream.getTracks()) {
    try {
      track.stop();
    } catch {
      // ignore
    }
  }
}

function readTrackSettings(track: MediaStreamTrack): { resolution: string; facingMode?: CameraFacingMode } {
  let settings: MediaTrackSettings = {};
  try {
    settings = track.getSettings?.() ?? {};
  } catch {
    settings = {};
  }

  const width = settings.width ?? 0;
  const height = settings.height ?? 0;
  const facing = settings.facingMode;

  return {
    resolution: width && height ? `${width}x${height}` : '',
    facingMode: facing === 'environment' || facing === 'user' ? facing : undefined
  };
}

function readTrackCapabilities(track: MediaStreamTrack): CameraTrackCapabilities {
  let caps: Record<string, unknown> = {};
  try {
    caps = (track.getCapabilities?.() as Record<string, unknown>) ?? {};
  } catch {
    caps = {};
  }

  const focusModes = caps.focusMode;
  return {
    zoom: 'zoom' in caps,
    torch: caps.torch === true || (Array.isArray(caps.torch) && caps.torch.includes(true)),
    pointFocus:
      'pointsOfInterest' in caps ||
      (Array.isArray(focusModes) && (focusModes as string[]).includes('manual'))
  };
}
