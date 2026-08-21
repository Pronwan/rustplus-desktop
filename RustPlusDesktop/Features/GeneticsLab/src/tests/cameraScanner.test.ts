import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import {
  CameraScannerService,
  buildConstraintTiers,
  classifyCameraError
} from '../services/cameraScannerService.ts';
import { describeCameraStatus } from '../services/scanner/cameraStatusMessages.ts';
import { createIdleCameraState } from '../services/scanner/cameraSupport.ts';
import { CameraTargetRearm } from '../services/scanner/CameraTargetRearm.ts';
import { TemporalVotingService } from '../services/scanner/TemporalVotingService.ts';
import { CAMERA_SCANNER_CONFIG } from '../services/scanner/cameraScannerConfig.ts';
import {
  DEFAULT_ROW_QUALITY_THRESHOLDS,
  assessRowQuality
} from '../services/scanner/vision/quality.ts';
import type { RasterImage } from '../services/scanner/scannerTypes.ts';
import type { GlyphRowRecognizer } from '../services/scanner/vision/templateGeneRecognizer.ts';
import type {
  CameraAnalysisResult,
  CameraFrameAnalyzer,
  CameraScannerState,
  CameraTarget,
  GeneRecognitionResult
} from '../services/scanner/scannerTypes.ts';

/* ------------------------------------------------------------------ *
 * Fakes
 * ------------------------------------------------------------------ */

function mediaError(name: string): Error {
  return Object.assign(new Error(name), { name });
}

class FakeTrack {
  public kind = 'video';
  public stopped = false;
  private listeners: Record<string, Array<() => void>> = {};

  constructor(
    private settings: MediaTrackSettings = { width: 1920, height: 1080, facingMode: 'environment' },
    private capabilities: Record<string, unknown> = {}
  ) {}

  stop(): void {
    this.stopped = true;
  }

  addEventListener(type: string, handler: () => void): void {
    (this.listeners[type] ||= []).push(handler);
  }

  removeEventListener(type: string, handler: () => void): void {
    this.listeners[type] = (this.listeners[type] || []).filter(l => l !== handler);
  }

  listenerCount(type: string): number {
    return (this.listeners[type] || []).length;
  }

  emitEnded(): void {
    for (const handler of [...(this.listeners.ended || [])]) handler();
  }

  getSettings(): MediaTrackSettings {
    return this.settings;
  }

  getCapabilities(): Record<string, unknown> {
    return this.capabilities;
  }
}

class FakeStream {
  constructor(public tracks: FakeTrack[] = [new FakeTrack()]) {}
  getTracks(): FakeTrack[] {
    return this.tracks;
  }
  getVideoTracks(): FakeTrack[] {
    return this.tracks.filter(t => t.kind === 'video');
  }
}

interface FakeMediaDevices {
  getUserMedia: ReturnType<typeof vi.fn>;
  enumerateDevices: ReturnType<typeof vi.fn>;
  calls: MediaStreamConstraints[];
}

/**
 * @param outcomes One entry per getUserMedia attempt: a stream to resolve, or an error to reject.
 *                 The final entry repeats if more attempts are made than outcomes provided.
 */
function createMediaDevices(outcomes: Array<FakeStream | Error>, cameraCount = 1): FakeMediaDevices {
  const calls: MediaStreamConstraints[] = [];
  let index = 0;

  const getUserMedia = vi.fn(async (constraints: MediaStreamConstraints) => {
    calls.push(constraints);
    const outcome = outcomes[Math.min(index, outcomes.length - 1)];
    index++;
    if (outcome instanceof Error) throw outcome;
    return outcome as unknown as MediaStream;
  });

  const enumerateDevices = vi.fn(async () =>
    Array.from({ length: cameraCount }, (_, i) => ({ kind: 'videoinput', deviceId: `cam-${i}` }))
  );

  return { getUserMedia, enumerateDevices, calls };
}

/** Scripted stand-in for template matching, so recognition behaviour can be driven exactly. */
class FakeRecognizer {
  public rowCalls = 0;
  /** Consumed in order; the last entry repeats once exhausted. */
  public rowResults: Array<GeneRecognitionResult | null> = [null];

  readonly recognize: GlyphRowRecognizer = () => {
    const result = this.rowResults[Math.min(this.rowCalls, this.rowResults.length - 1)] ?? null;
    this.rowCalls++;
    return result;
  };
}

function geneResult(geneString: string, confidence = 92): GeneRecognitionResult {
  return { geneString, confidence };
}

/**
 * Minimal canvas host so the real `GeneImagePreprocessor` can run under the node test
 * environment. Only the operations the preprocessor actually performs are implemented.
 */
function installFakeCanvasHost(): () => void {
  const previous = (globalThis as { document?: unknown }).document;

  (globalThis as { document?: unknown }).document = {
    createElement(tag: string) {
      if (tag !== 'canvas') return {};
      return {
        width: 0,
        height: 0,
        getContext: () => ({
          fillStyle: '',
          imageSmoothingEnabled: false,
          imageSmoothingQuality: 'high',
          fillRect: () => {},
          drawImage: () => {},
          getImageData: (_x: number, _y: number, w: number, h: number) => ({
            data: new Uint8ClampedArray(Math.max(0, w * h * 4)),
            width: w,
            height: h
          }),
          putImageData: () => {},
          createImageData: (w: number, h: number) => ({
            data: new Uint8ClampedArray(Math.max(0, w * h * 4)),
            width: w,
            height: h
          })
        }),
        toDataURL: () => 'data:image/png;base64,'
      };
    }
  };

  return () => {
    (globalThis as { document?: unknown }).document = previous;
  };
}

function trackingTarget(): CameraTarget {
  return {
    corners: [
      { x: 100, y: 100 },
      { x: 400, y: 100 },
      { x: 400, y: 150 },
      { x: 100, y: 150 }
    ],
    candidateScore: 0.82,
    trackingConfidence: 0.9,
    qualityIssues: [],
    normalizedRow: {
      data: new Uint8ClampedArray(600 * 100 * 4),
      width: 600,
      height: 100
    },
    slots: { baseX: 10, baseY: 12, geneWidth: 84, gapWidth: 12, height: 76 }
  };
}

/** Analyzer whose result is chosen per call, so a whole scanning sequence can be scripted. */
class ScriptedAnalyzer implements CameraFrameAnalyzer {
  public analyzeCalls = 0;
  public resetCalls = 0;
  public disposeCalls = 0;

  constructor(private readonly script: (call: number) => FakeAnalysis) {}

  async analyze(): Promise<CameraAnalysisResult> {
    const step = this.script(this.analyzeCalls);
    this.analyzeCalls++;
    return {
      ...step,
      overlay: { frameWidth: 960, frameHeight: 540, target: null, targetId: null, candidates: [] }
    };
  }
  reset(): void {
    this.resetCalls++;
  }
  dispose(): void {
    this.disposeCalls++;
  }
}

const TRACKING: FakeAnalysis = {
  phase: 'tracking',
  qualityIssues: [],
  candidateCount: 1,
  activeTarget: null
};
const SEARCHING: FakeAnalysis = {
  phase: 'searching',
  qualityIssues: [],
  candidateCount: 0,
  activeTarget: null
};

type FakeAnalysis = Pick<CameraAnalysisResult, 'phase' | 'qualityIssues' | 'candidateCount' | 'activeTarget'>;

class FakeAnalyzer implements CameraFrameAnalyzer {
  public analyzeCalls = 0;
  public resetCalls = 0;
  public disposeCalls = 0;
  private readonly result: CameraAnalysisResult;

  constructor(result: FakeAnalysis) {
    this.result = {
      ...result,
      overlay: { frameWidth: 960, frameHeight: 540, target: null, targetId: null, candidates: [] }
    };
  }

  async analyze(): Promise<CameraAnalysisResult> {
    this.analyzeCalls++;
    return this.result;
  }
  reset(): void {
    this.resetCalls++;
  }
  dispose(): void {
    this.disposeCalls++;
  }
}

function createVideoElement(): HTMLVideoElement {
  return {
    srcObject: null,
    videoWidth: 1920,
    videoHeight: 1080,
    muted: false,
    playsInline: false,
    play: () => Promise.resolve()
  } as unknown as HTMLVideoElement;
}

interface Harness {
  service: CameraScannerService;
  devices: FakeMediaDevices;
  recognizer: FakeRecognizer;
  states: CameraScannerState[];
  saplings: Array<{ geneString: string; confidence: number }>;
  setHidden: (hidden: boolean) => void;
  visibilityListenerCount: () => number;
  /** Advances the scanner clock and the fake timers together. */
  runFor: (ms: number) => Promise<void>;
}

function createHarness(options: {
  outcomes?: Array<FakeStream | Error>;
  cameraCount?: number;
  isSecureContext?: boolean;
  mediaDevices?: FakeMediaDevices | null;
  recognizer?: FakeRecognizer;
} = {}): Harness {
  const devices = options.mediaDevices !== undefined
    ? options.mediaDevices
    : createMediaDevices(options.outcomes ?? [new FakeStream()], options.cameraCount ?? 1);

  const recognizer = options.recognizer ?? new FakeRecognizer();

  let hidden = false;
  let visibilityHandlers: Array<() => void> = [];
  let clock = 0;

  const service = new CameraScannerService({
    glyphRecognizer: (...args) => recognizer.recognize(...args),
    env: {
      mediaDevices: devices as unknown as MediaDevices,
      isSecureContext: options.isSecureContext ?? true,
      isDocumentHidden: () => hidden,
      addVisibilityListener: handler => {
        visibilityHandlers.push(handler);
        return () => {
          visibilityHandlers = visibilityHandlers.filter(h => h !== handler);
        };
      },
      now: () => clock,
      requestWakeLock: async () => null
    }
  });

  const states: CameraScannerState[] = [];
  const saplings: Array<{ geneString: string; confidence: number }> = [];
  service.addEventListener(event => {
    if (event.type === 'CAMERA_STATE') states.push(event.state);
    else if (event.type === 'SAPLING-FOUND') {
      saplings.push({ geneString: event.geneString, confidence: event.confidence });
    }
  });

  return {
    service,
    devices: devices as FakeMediaDevices,
    recognizer,
    states,
    saplings,
    setHidden: (value: boolean) => {
      hidden = value;
      for (const handler of [...visibilityHandlers]) handler();
    },
    visibilityListenerCount: () => visibilityHandlers.length,
    runFor: async (ms: number) => {
      const step = 20;
      for (let elapsed = 0; elapsed < ms; elapsed += step) {
        clock += step;
        await vi.advanceTimersByTimeAsync(step);
      }
    }
  };
}

/** Boots a camera session with a scripted analyzer and scripted OCR results. */
async function startCameraWith(
  script: (call: number) => FakeAnalysis,
  rowResults: Array<GeneRecognitionResult | null>
): Promise<{ harness: Harness; analyzer: ScriptedAnalyzer }> {
  const recognizer = new FakeRecognizer();
  recognizer.rowResults = rowResults;

  const harness = createHarness({ recognizer });
  const analyzer = new ScriptedAnalyzer(script);
  harness.service.attachVideo(createVideoElement());
  harness.service.setAnalyzerFactory(() => analyzer);

  await harness.service.start();
  // The recogniser warms up alongside the permission prompt.
  await vi.advanceTimersByTimeAsync(0);

  return { harness, analyzer };
}

/* ------------------------------------------------------------------ *
 * Tests
 * ------------------------------------------------------------------ */

describe('Camera scanner constraints', () => {
  it('asks for the rear camera at 1080p/15fps first and never uses exact constraints', () => {
    const tiers = buildConstraintTiers('environment');
    const serialized = JSON.stringify(tiers);

    expect(tiers).toHaveLength(3);
    expect(tiers[0].video).toMatchObject({
      facingMode: { ideal: 'environment' },
      width: { ideal: 1920 },
      height: { ideal: 1080 },
      frameRate: { ideal: 15, max: 30 }
    });
    expect(tiers[1].video).toEqual({ facingMode: { ideal: 'environment' } });
    expect(tiers[2].video).toBe(true);
    expect(serialized).not.toContain('exact');
    expect(tiers.every(t => t.audio === false)).toBe(true);
  });

  it('maps getUserMedia failures onto recoverable error codes', () => {
    expect(classifyCameraError(mediaError('NotAllowedError'))).toBe('permission-denied');
    expect(classifyCameraError(mediaError('SecurityError'))).toBe('permission-denied');
    expect(classifyCameraError(mediaError('NotFoundError'))).toBe('no-camera');
    expect(classifyCameraError(mediaError('OverconstrainedError'))).toBe('stream-failed');
    expect(classifyCameraError(mediaError('NotReadableError'))).toBe('stream-failed');
    expect(classifyCameraError(new Error('boom'))).toBe('unknown');
  });
});

describe('Camera scanner lifecycle', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('attempts the preferred constraints first and reaches searching', async () => {
    const harness = createHarness();

    const started = await harness.service.start();

    expect(started).toBe(true);
    expect(harness.devices.getUserMedia).toHaveBeenCalledTimes(1);
    expect(harness.devices.calls[0].video).toMatchObject({ width: { ideal: 1920 } });
    expect(harness.service.getState().phase).toBe('searching');
    expect(harness.service.getState().captureResolution).toBe('1920x1080');
    expect(harness.service.getState().facingMode).toBe('environment');
  });

  it('falls back to looser constraints when the device cannot satisfy them', async () => {
    const harness = createHarness({
      outcomes: [mediaError('OverconstrainedError'), new FakeStream()]
    });

    const started = await harness.service.start();

    expect(started).toBe(true);
    expect(harness.devices.getUserMedia).toHaveBeenCalledTimes(2);
    expect(harness.devices.calls[1].video).toEqual({ facingMode: { ideal: 'environment' } });
    expect(harness.service.getState().phase).toBe('searching');
  });

  it('reports stream-failed only after every tier has been tried', async () => {
    const harness = createHarness({ outcomes: [mediaError('NotReadableError')] });

    const started = await harness.service.start();

    expect(started).toBe(false);
    expect(harness.devices.getUserMedia).toHaveBeenCalledTimes(3);
    expect(harness.service.getState().errorCode).toBe('stream-failed');
  });

  it('does not re-prompt after a permission denial', async () => {
    const harness = createHarness({ outcomes: [mediaError('NotAllowedError')] });

    const started = await harness.service.start();

    expect(started).toBe(false);
    expect(harness.devices.getUserMedia).toHaveBeenCalledTimes(1);
    expect(harness.service.getState().phase).toBe('error');
    expect(harness.service.getState().errorCode).toBe('permission-denied');
    expect(harness.service.isRunning()).toBe(false);
  });

  it('stops immediately when no camera exists', async () => {
    const harness = createHarness({ outcomes: [mediaError('NotFoundError')] });

    await harness.service.start();

    expect(harness.devices.getUserMedia).toHaveBeenCalledTimes(1);
    expect(harness.service.getState().errorCode).toBe('no-camera');
  });

  it('refuses to open a permission flow on an insecure origin', async () => {
    const harness = createHarness({ isSecureContext: false });

    const started = await harness.service.start();

    expect(started).toBe(false);
    expect(harness.devices.getUserMedia).not.toHaveBeenCalled();
    expect(harness.service.getState().errorCode).toBe('insecure-context');
  });

  it('reports an unsupported browser without touching the camera API', async () => {
    const harness = createHarness({ mediaDevices: null });

    const started = await harness.service.start();

    expect(started).toBe(false);
    expect(harness.service.getState().errorCode).toBe('unsupported');
  });

  it('offers camera switching only when more than one camera exists', async () => {
    const single = createHarness({ cameraCount: 1 });
    await single.service.start();
    expect(single.service.getState().canSwitchCamera).toBe(false);

    const dual = createHarness({ outcomes: [new FakeStream()], cameraCount: 2 });
    await dual.service.start();
    expect(dual.service.getState().canSwitchCamera).toBe(true);
  });

  it('reads optional hardware capabilities from the track', async () => {
    const track = new FakeTrack({ width: 1280, height: 720, facingMode: 'environment' }, {
      zoom: { min: 1, max: 4 },
      torch: true,
      focusMode: ['continuous', 'manual']
    });
    const harness = createHarness({ outcomes: [new FakeStream([track])] });

    await harness.service.start();

    expect(harness.service.getState().capabilities).toEqual({ zoom: true, torch: true, pointFocus: true });
    expect(harness.service.getState().captureResolution).toBe('1280x720');
  });

  it('assumes no optional hardware controls when the track reports none', async () => {
    const harness = createHarness({ outcomes: [new FakeStream([new FakeTrack(undefined, {})])] });

    await harness.service.start();

    expect(harness.service.getState().capabilities).toEqual({ zoom: false, torch: false, pointFocus: false });
  });
});

describe('Camera scanner cleanup', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('stops every track, detaches the video, and returns to idle', async () => {
    const track = new FakeTrack();
    const harness = createHarness({ outcomes: [new FakeStream([track])] });
    const video = createVideoElement();
    harness.service.attachVideo(video);

    await harness.service.start();
    expect(video.srcObject).not.toBeNull();

    harness.service.stop();

    expect(track.stopped).toBe(true);
    expect(track.listenerCount('ended')).toBe(0);
    expect(video.srcObject).toBeNull();
    expect(harness.service.isRunning()).toBe(false);
    expect(harness.service.getState().phase).toBe('idle');
    expect(harness.visibilityListenerCount()).toBe(0);
  });

  it('leaves no live track, timer, or listener behind after ten start/stop cycles', async () => {
    const tracks: FakeTrack[] = [];
    const devices = createMediaDevices([]);
    devices.getUserMedia.mockImplementation(async () => {
      const track = new FakeTrack();
      tracks.push(track);
      return new FakeStream([track]) as unknown as MediaStream;
    });

    const harness = createHarness({ mediaDevices: devices });
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => new FakeAnalyzer({
      phase: 'searching',
      qualityIssues: [],
      candidateCount: 0,
      activeTarget: null
    }));

    for (let i = 0; i < 10; i++) {
      await harness.service.start();
      expect(harness.service.isRunning()).toBe(true);
      harness.service.stop();
    }

    expect(tracks).toHaveLength(10);
    expect(tracks.every(t => t.stopped)).toBe(true);
    expect(vi.getTimerCount()).toBe(0);
    expect(harness.visibilityListenerCount()).toBe(0);
  });

  it('surfaces a recoverable error when the stream ends on its own', async () => {
    const track = new FakeTrack();
    const harness = createHarness({ outcomes: [new FakeStream([track])] });

    await harness.service.start();
    track.emitEnded();

    expect(harness.service.getState().phase).toBe('error');
    expect(harness.service.getState().errorCode).toBe('stream-ended');
    expect(harness.service.isRunning()).toBe(false);
    expect(track.stopped).toBe(true);
  });

  it('can restart after an error without a fresh instance', async () => {
    const harness = createHarness({ outcomes: [mediaError('NotAllowedError'), new FakeStream()] });

    await harness.service.start();
    expect(harness.service.getState().phase).toBe('error');

    const restarted = await harness.service.start();
    expect(restarted).toBe(true);
    expect(harness.service.getState().phase).toBe('searching');
  });

  it('discards a stream that arrives after the user closed the scanner', async () => {
    const track = new FakeTrack();
    let release: (stream: FakeStream) => void = () => {};
    const devices = createMediaDevices([]);
    devices.getUserMedia.mockImplementation(
      () => new Promise(resolve => {
        release = resolve as unknown as (stream: FakeStream) => void;
      })
    );

    const harness = createHarness({ mediaDevices: devices });
    const pending = harness.service.start();

    // The user closes the surface while the permission prompt is still open.
    harness.service.stop();
    release(new FakeStream([track]));

    await expect(pending).resolves.toBe(false);
    expect(track.stopped).toBe(true);
    expect(harness.service.isRunning()).toBe(false);
  });
});

describe('Camera scanner processing cadence', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('runs no frame loop until a detector is installed', async () => {
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());

    await harness.service.start();

    expect(vi.getTimerCount()).toBe(0);
    expect(harness.service.getState().isDetectionAvailable).toBe(false);
  });

  it('creates one analyzer per session and disposes it on stop', async () => {
    const analyzers: FakeAnalyzer[] = [];
    const harness = createHarness({ outcomes: [new FakeStream(), new FakeStream()] });
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => {
      const analyzer = new FakeAnalyzer({
        phase: 'tracking',
        qualityIssues: [],
        candidateCount: 1,
        activeTarget: null
      });
      analyzers.push(analyzer);
      return analyzer;
    });

    await harness.service.start();
    expect(harness.service.getState().isDetectionAvailable).toBe(true);
    harness.service.stop();
    await harness.service.start();
    harness.service.stop();

    expect(analyzers).toHaveLength(2);
    expect(analyzers.every(a => a.disposeCalls === 1)).toBe(true);
  });

  it('drives guidance from the analyzer result', async () => {
    const analyzer = new FakeAnalyzer({
      phase: 'quality-blocked',
      qualityIssues: ['too-far'],
      candidateCount: 1,
      activeTarget: null
    });
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => analyzer);

    await harness.service.start();
    await vi.advanceTimersByTimeAsync(200);

    expect(analyzer.analyzeCalls).toBeGreaterThan(0);
    expect(harness.service.getState().phase).toBe('quality-blocked');
    expect(harness.service.getState().qualityIssues).toEqual(['too-far']);
  });

  it('halts analysis and clears tracking state while the page is hidden', async () => {
    const analyzer = new FakeAnalyzer({
      phase: 'tracking',
      qualityIssues: [],
      candidateCount: 1,
      activeTarget: null
    });
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => analyzer);

    await harness.service.start();
    await vi.advanceTimersByTimeAsync(200);
    const callsWhileVisible = analyzer.analyzeCalls;

    harness.setHidden(true);
    await vi.advanceTimersByTimeAsync(500);

    expect(vi.getTimerCount()).toBe(0);
    expect(analyzer.analyzeCalls).toBe(callsWhileVisible);
    expect(analyzer.resetCalls).toBe(1);

    harness.setHidden(false);
    await vi.advanceTimersByTimeAsync(200);

    expect(analyzer.analyzeCalls).toBeGreaterThan(callsWhileVisible);
  });

  it('stops analysis while paused and resumes cleanly', async () => {
    const analyzer = new FakeAnalyzer({
      phase: 'tracking',
      qualityIssues: [],
      candidateCount: 1,
      activeTarget: null
    });
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => analyzer);

    await harness.service.start();
    harness.service.pause();

    expect(harness.service.getState().phase).toBe('paused');
    expect(vi.getTimerCount()).toBe(0);

    const callsWhilePaused = analyzer.analyzeCalls;
    await vi.advanceTimersByTimeAsync(500);
    expect(analyzer.analyzeCalls).toBe(callsWhilePaused);

    harness.service.resume();
    await vi.advanceTimersByTimeAsync(200);

    expect(harness.service.getState().phase).not.toBe('paused');
    expect(analyzer.analyzeCalls).toBeGreaterThan(callsWhilePaused);
  });
});

describe('Camera recognition readiness', () => {
  it('is ready to read the moment the camera opens', async () => {
    // Template matching needs no worker and no model download, so there is no warm-up
    // window during which reads are impossible.
    const harness = createHarness();
    await harness.service.start();

    expect(harness.service.getState().isOcrReady).toBe(true);
    expect(harness.service.getState().isOcrUnavailable).toBe(false);
  });
});

describe('Camera recognition safety', () => {
  let restoreDom: () => void;

  beforeEach(() => {
    restoreDom = installFakeCanvasHost();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    restoreDom();
  });

  const startWith = startCameraWith;

  it('never runs OCR while quality is blocking', async () => {
    const { harness, analyzer } = await startWith(
      () => ({
        phase: 'quality-blocked',
        qualityIssues: ['too-far'],
        candidateCount: 1,
        activeTarget: null
      }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(600);

    expect(analyzer.analyzeCalls).toBeGreaterThan(4);
    expect(harness.recognizer.rowCalls).toBe(0);
    expect(harness.saplings).toHaveLength(0);
  });

  it('never runs OCR while two candidates compete', async () => {
    const { harness } = await startWith(
      () => ({
        phase: 'ambiguous',
        qualityIssues: [],
        candidateCount: 2,
        activeTarget: null
      }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(600);

    expect(harness.recognizer.rowCalls).toBe(0);
    expect(harness.saplings).toHaveLength(0);
    expect(harness.service.getState().phase).toBe('ambiguous');
  });

  it('accepts once three of four samples agree', async () => {
    const { harness } = await startWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(600);

    expect(harness.saplings).toHaveLength(1);
    expect(harness.saplings[0].geneString).toBe('GGYHYX');
    expect(harness.service.getState().lastAcceptedGenes).toBe('GGYHYX');
    expect(harness.service.getState().acceptedCount).toBe(1);
  });

  it('does not accept when only two of four samples agree', async () => {
    // Four reads, split two and two, then nothing further to tip the vote either way.
    const { harness } = await startWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGYHYX'), geneResult('GGYHYX'), geneResult('GGXHYW'), geneResult('GGXHYW'), null]
    );

    await harness.runFor(1200);

    expect(harness.recognizer.rowCalls).toBeGreaterThanOrEqual(4);
    expect(harness.saplings).toHaveLength(0);
  });

  it('discards the pending window when the target is genuinely lost', async () => {
    let visible = true;
    const recognizer = new FakeRecognizer();
    // Alternating reads never confirm on their own, so the window fills without emptying.
    recognizer.rowResults = [geneResult('GGYHYX'), geneResult('WWXXYY')];

    const harness = createHarness({ recognizer });
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(
      () => new ScriptedAnalyzer(() => (visible ? { ...TRACKING, activeTarget: trackingTarget() } : SEARCHING))
    );
    await harness.service.start();
    await vi.advanceTimersByTimeAsync(0);

    await harness.runFor(200);
    expect(harness.service.getState().diagnostics.pendingSamples).toBeGreaterThan(0);
    expect(harness.saplings).toHaveLength(0);

    visible = false;
    await harness.runFor(200);

    // The row left the frame, so nothing collected before it counts as evidence any more.
    expect(harness.service.getState().diagnostics.pendingSamples).toBe(0);
  });

  it('keeps the pending window through a shaky frame', async () => {
    // The row stays present but one frame fails a quality gate. The samples already banked
    // are still good evidence, so acceptance still happens.
    let shaky = false;
    const recognizer = new FakeRecognizer();
    recognizer.rowResults = [geneResult('GGYHYX')];

    const harness = createHarness({ recognizer });
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(
      () =>
        new ScriptedAnalyzer(() =>
          shaky
            ? { phase: 'quality-blocked', qualityIssues: ['moving'], candidateCount: 1, activeTarget: null }
            : { ...TRACKING, activeTarget: trackingTarget() }
        )
    );
    await harness.service.start();
    await vi.advanceTimersByTimeAsync(0);

    await harness.runFor(340);
    shaky = true;
    await harness.runFor(140);
    shaky = false;
    await harness.runFor(600);

    expect(harness.saplings).toHaveLength(1);
  });

  it('emits once for a target that stays continuously visible', async () => {
    const { harness } = await startWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(3000);

    expect(harness.recognizer.rowCalls).toBeGreaterThan(10);
    expect(harness.saplings).toHaveLength(1);
  });

  it('rearms after the row disappears long enough, then emits the same genes again', async () => {
    // Tracking, a long gap with no row, then tracking again.
    const { harness } = await startWith(
      call => (call >= 6 && call < 30 ? SEARCHING : { ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(4000);

    expect(harness.saplings).toHaveLength(2);
    expect(harness.saplings.every(s => s.geneString === 'GGYHYX')).toBe(true);
  });

  it('emits immediately when a clearly different genotype replaces the last one', async () => {
    const { harness } = await startWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [
        geneResult('GGYHYX'),
        geneResult('GGYHYX'),
        geneResult('GGYHYX'),
        geneResult('WWXXYY'),
        geneResult('WWXXYY'),
        geneResult('WWXXYY')
      ]
    );

    await harness.runFor(2400);

    expect(harness.saplings.map(s => s.geneString)).toEqual(['GGYHYX', 'WWXXYY']);
  });

  it('produces nothing when the glyphs cannot be classified confidently', async () => {
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [null]
    );

    await harness.runFor(400);

    expect(harness.recognizer.rowCalls).toBeGreaterThan(0);
    expect(harness.saplings).toHaveLength(0);
  });

  it('clears the pending window when the page is hidden', async () => {
    const { harness, analyzer } = await startWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGYHYX')]
    );

    await harness.runFor(120);
    harness.setHidden(true);
    await harness.runFor(200);
    harness.setHidden(false);
    await harness.runFor(120);

    expect(analyzer.resetCalls).toBeGreaterThan(0);
    expect(harness.saplings).toHaveLength(0);
  });
});

describe('Camera OCR confidence is independent of the desktop scanner', () => {
  let restoreDom: () => void;

  beforeEach(() => {
    restoreDom = installFakeCanvasHost();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    restoreDom();
  });

  it('keeps the desktop voting floor at its original value', () => {
    const desktop = new TemporalVotingService();
    for (let i = 0; i < 4; i++) {
      expect(desktop.addCandidate('inventory', geneResult('GGYHYX', 50))).toBeNull();
    }
  });

  it('accepts camera-grade confidence that the desktop floor would reject', () => {
    const camera = new TemporalVotingService(CAMERA_SCANNER_CONFIG.recognition.minRowConfidence);

    expect(camera.addCandidate('camera', geneResult('GGYHYX', 50))).toBeNull();
    expect(camera.addCandidate('camera', geneResult('GGYHYX', 47))).toBeNull();
    const confirmed = camera.addCandidate('camera', geneResult('GGYHYX', 52));

    expect(confirmed?.geneString).toBe('GGYHYX');
  });

  it('still rejects reads below even the camera floor', () => {
    const camera = new TemporalVotingService(CAMERA_SCANNER_CONFIG.recognition.minRowConfidence);
    for (let i = 0; i < 4; i++) {
      expect(camera.addCandidate('camera', geneResult('GGYHYX', 20))).toBeNull();
    }
  });

  it('adds a clone from reads a monitor photograph actually produces', async () => {
    // 52% is a realistic camera score for a correct read, and is well under the desktop
    // floor of 75 that previously discarded every one of them.
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGGYYY', 52)]
    );

    await harness.runFor(600);

    expect(harness.saplings.map(s => s.geneString)).toEqual(['GGGYYY']);
  });

  it('reports what OCR saw so a failing read is diagnosable', async () => {
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [null]
    );

    await harness.runFor(400);
    const diagnostics = harness.service.getState().diagnostics;

    expect(diagnostics.readAttempts).toBeGreaterThan(0);
    expect(diagnostics.pendingSamples).toBe(0);
  });

  it('stops claiming to be reading once attempts are clearly going nowhere', () => {
    const base = createIdleCameraState();
    const stalled = describeCameraStatus({
      ...base,
      phase: 'reading',
      isDetectionAvailable: true,
      diagnostics: {
        readAttempts: 40,
        lastRawText: '',
        lastConfidence: 12,
        lastSource: null,
        pendingSamples: 0,
        sampleWindow: 4,
        slotInk: [],
        slotsWithinBounds: true,
        stripPreview: null
      }
    });

    expect(stalled.headline).toBe('Cannot read the letters');
    expect(stalled.tone).toBe('warn');
  });
});

describe('Camera scanner adaptive degradation', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it('slows the cadence when frames cost more than their budget', async () => {
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());

    // Each analyze call consumes 200ms of scanner clock, far beyond the 15fps budget.
    const analyzer: CameraFrameAnalyzer = {
      analyze: async () => {
        await harness.runFor(200);
        return {
          ...SEARCHING,
          overlay: { frameWidth: 960, frameHeight: 540, target: null, targetId: null, candidates: [] }
        };
      },
      reset: () => {},
      dispose: () => {}
    };
    harness.service.setAnalyzerFactory(() => analyzer);

    await harness.service.start();
    expect(harness.service.getDegradationLevel()).toBe(0);

    await harness.runFor(6000);

    expect(harness.service.getDegradationLevel()).toBeGreaterThan(0);
  });

  it('resets the degradation level on stop', async () => {
    const harness = createHarness();
    harness.service.attachVideo(createVideoElement());
    harness.service.setAnalyzerFactory(() => new ScriptedAnalyzer(() => SEARCHING));

    await harness.service.start();
    harness.service.stop();

    expect(harness.service.getDegradationLevel()).toBe(0);
  });
});

describe('Camera target rearm', () => {
  it('holds a continuously visible target to one emission', () => {
    const rearm = new CameraTargetRearm(600);

    expect(rearm.shouldEmit('GGYHYX')).toBe(true);
    rearm.recordEmit('GGYHYX');

    for (let t = 0; t < 5000; t += 100) rearm.markVisible();
    expect(rearm.shouldEmit('GGYHYX')).toBe(false);
  });

  it('rearms after a sustained absence', () => {
    const rearm = new CameraTargetRearm(600);
    rearm.recordEmit('GGYHYX');

    rearm.markLost(0);
    rearm.markLost(300);
    expect(rearm.shouldEmit('GGYHYX')).toBe(false);

    rearm.markLost(700);
    expect(rearm.shouldEmit('GGYHYX')).toBe(true);
  });

  it('does not rearm on a brief flicker', () => {
    const rearm = new CameraTargetRearm(600);
    rearm.recordEmit('GGYHYX');

    rearm.markLost(0);
    rearm.markLost(200);
    rearm.markVisible();
    rearm.markLost(400);
    rearm.markLost(700);

    // The clock moved past 600ms overall, but the row was never absent that long at once.
    expect(rearm.shouldEmit('GGYHYX')).toBe(false);
  });

  it('always allows a clearly different genotype through', () => {
    const rearm = new CameraTargetRearm(600);
    rearm.recordEmit('GGYHYX');
    rearm.markVisible();

    expect(rearm.shouldEmit('WWXXYY')).toBe(true);
  });
});

describe('Camera status vocabulary', () => {
  const base = createIdleCameraState();

  it('gives one explicit instruction per quality problem', () => {
    const blocked = describeCameraStatus({
      ...base,
      phase: 'quality-blocked',
      isDetectionAvailable: true,
      qualityIssues: ['too-far']
    });
    expect(blocked.tone).toBe('warn');
    expect(blocked.instruction).toBe('Move closer');

    const tooClose = describeCameraStatus({
      ...base,
      phase: 'quality-blocked',
      isDetectionAvailable: true,
      qualityIssues: ['too-close']
    });
    expect(tooClose.instruction).toBe('Move farther so all six genes fit');
  });

  it('shows the most blocking issue when several are reported at once', () => {
    const status = describeCameraStatus({
      ...base,
      phase: 'quality-blocked',
      isDetectionAvailable: true,
      qualityIssues: ['moving', 'blurred', 'too-close']
    });
    expect(status.headline).toBe('Too close');
  });

  it('asks for a tap instead of guessing between candidates', () => {
    const status = describeCameraStatus({ ...base, phase: 'ambiguous', isDetectionAvailable: true, candidateCount: 2 });
    expect(status.instruction).toBe('Tap the intended row');
    expect(status.announce).toBe(true);
  });

  it('distinguishes an added clone from one already in the inventory', () => {
    const added = describeCameraStatus(
      { ...base, phase: 'accepted', isDetectionAvailable: true, lastAcceptedGenes: 'GYGHYY' },
      { lastResultKind: 'added' }
    );
    expect(added.tone).toBe('success');
    expect(added.headline).toBe('Clone added: GYGHYY');

    const duplicate = describeCameraStatus(
      { ...base, phase: 'accepted', isDetectionAvailable: true, lastAcceptedGenes: 'GYGHYY' },
      { lastResultKind: 'duplicate' }
    );
    expect(duplicate.headline).toBe('Already in clone inventory');
  });

  it('reports a lost target separately from a cold search', () => {
    const searching = describeCameraStatus({ ...base, phase: 'searching', isDetectionAvailable: true });
    expect(searching.headline).toBe('Searching');

    const lost = describeCameraStatus(
      { ...base, phase: 'searching', isDetectionAvailable: true },
      { recentlyLostTarget: true }
    );
    expect(lost.headline).toBe('Genetics lost');
  });

  it('never relies on colour alone: every status carries text', () => {
    const phases: CameraScannerState['phase'][] = [
      'idle', 'requesting-permission', 'starting', 'searching', 'ambiguous',
      'tracking', 'quality-blocked', 'reading', 'accepted', 'paused', 'error'
    ];

    for (const phase of phases) {
      const status = describeCameraStatus({ ...base, phase, isDetectionAvailable: true });
      expect(status.headline.length).toBeGreaterThan(0);
      expect(status.instruction.length).toBeGreaterThan(0);
    }
  });

  it('reports a missing detector instead of pretending to search', () => {
    const status = describeCameraStatus({ ...base, phase: 'searching', isDetectionAvailable: false });
    expect(status.headline).toBe('Detection unavailable');
    expect(status.tone).toBe('warn');
    expect(status.announce).toBe(true);
  });
});

describe('Camera quality thresholds have one source of truth', () => {
  it('uses the camera config values, not a stale private copy', () => {
    expect(DEFAULT_ROW_QUALITY_THRESHOLDS.maxPixelsPerGene).toBe(
      CAMERA_SCANNER_CONFIG.quality.maxPixelsPerGene
    );
    expect(DEFAULT_ROW_QUALITY_THRESHOLDS.minPixelsPerGene).toBe(
      CAMERA_SCANNER_CONFIG.quality.minPixelsPerGene
    );
    expect(DEFAULT_ROW_QUALITY_THRESHOLDS.maxPerspectiveDegrees).toBe(
      CAMERA_SCANNER_CONFIG.quality.maxPerspectiveDegrees
    );
  });

  it('accepts the pixel density a hand-held phone actually produces', () => {
    const row: RasterImage = { data: new Uint8ClampedArray(40 * 10 * 4), width: 40, height: 10 };

    // Around 200 px per gene is normal, well-framed hand-held distance. It was being
    // reported as "too close" and blocking every read.
    const report = assessRowQuality({
      normalized: row,
      pixelsPerGene: 208,
      perspectiveDegrees: 6,
      clipped: false,
      isStable: true
    });

    expect(report.issues).not.toContain('too-close');
  });
});

describe('Camera accepted confirmation', () => {
  let restoreDom: () => void;

  beforeEach(() => {
    restoreDom = installFakeCanvasHost();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.clearAllTimers();
    vi.useRealTimers();
    restoreDom();
  });

  it('keeps the accepted state on screen long enough to be seen', async () => {
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGGYYY', 88)]
    );

    await harness.runFor(700);
    expect(harness.saplings).toHaveLength(1);

    // Several analysis frames later the confirmation is still the headline.
    await harness.runFor(200);
    expect(harness.service.getState().phase).toBe('accepted');
    expect(harness.service.getState().lastAcceptedGenes).toBe('GGGYYY');
  });

  it('returns to tracking once the hold expires', async () => {
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGGYYY', 88)]
    );

    await harness.runFor(700);
    expect(harness.service.getState().phase).toBe('accepted');

    await harness.runFor(CAMERA_SCANNER_CONFIG.confirmation.acceptedHoldMs + 300);
    expect(harness.service.getState().phase).not.toBe('accepted');
  });

  it('keeps the overlay live while the confirmation is held', async () => {
    const { harness } = await startCameraWith(
      () => ({ ...TRACKING, activeTarget: trackingTarget() }),
      [geneResult('GGGYYY', 88)]
    );

    await harness.runFor(700);
    await harness.runFor(200);

    expect(harness.service.getState().phase).toBe('accepted');
    expect(harness.service.getState().overlay).not.toBeNull();
  });
});
