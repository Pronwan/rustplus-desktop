import { LivestockPanelRead, readLivestockPanel } from './livestockPanelReader.ts';
import { readMarkerDigit } from './markerDigit.ts';
import { LivestockReadStabilizer, StableRead } from './livestockReadStabilizer.ts';
import type { ObservedCondition } from '../../domain/livestock/animal.ts';
import {
  CaptureFrameSource,
  createTrackFrameSource,
  createVideoFrameSource
} from '../scanner/vision/captureFrameSource.ts';
import type { LivestockReaderRequest, LivestockReaderResponse } from '../../workers/livestockReader.worker.ts';
import { PanelConditions, readPanelConditions, readPanelName } from './panelConditions.ts';
import { PortraitMatch, classifyPortrait } from './portraitClassifier.ts';

/**
 * One livestock scanning session: a frame source (the player's screen, or a phone camera
 * pointed at it), the reader worker, and the stabiliser that turns frames into animals.
 *
 * Both sources feed the same reader. The panel is found anywhere in the frame, so there is
 * nothing to calibrate: look at an animal, and its genes arrive.
 */

export type LivestockScanSource = 'desktop' | 'camera';
export type LivestockScanStatus = 'idle' | 'starting' | 'scanning' | 'error';

export interface LivestockScanState {
  status: LivestockScanStatus;
  source: LivestockScanSource | null;
  error: string | null;
  /** Most recent per-frame read, confirmed or not; null while no panel is in view. */
  live: LivestockPanelRead | null;
  /** AGE and OVERALL from the same frame. */
  liveConditions: PanelConditions | null;
  /** Which animal the header portrait shows, when it could tell. */
  livePortrait: PortraitMatch | null;
  /** The animal's in-game name, when it could be read. */
  liveName: string | null;
  /** Size of the analysed frame, so overlays can map read coordinates onto a preview. */
  frameSize: { width: number; height: number } | null;
  /** Milliseconds the reader spent on the last frame. */
  lastReadMs: number;
}

export type LivestockScanEvent =
  | { type: 'state'; state: LivestockScanState }
  | { type: 'confirmed'; read: StableRead; observed: ObservedCondition | null; portrait: PortraitMatch | null; name: string | null }
  | { type: 'lost' };

export const IDLE_LIVESTOCK_SCAN_STATE: LivestockScanState = {
  status: 'idle',
  source: null,
  error: null,
  live: null,
  liveConditions: null,
  livePortrait: null,
  liveName: null,
  frameSize: null,
  lastReadMs: 0
};

const DESKTOP_MAX_WIDTH = 1920;
const CAMERA_MAX_WIDTH = 1280;
const TICK_MS = 160;

export function isDesktopCaptureSupported(): boolean {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices && 'getDisplayMedia' in navigator.mediaDevices;
}

export function isCameraSupported(): boolean {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices && 'getUserMedia' in navigator.mediaDevices;
}

/**
 * Rear camera, ideally 1080p. Never `exact`: a phone that cannot deliver the ideal should
 * still scan at whatever it can produce. (Mirrors the clone camera scanner, which is loaded
 * lazily and so is not imported here.)
 */
const CAMERA_CONSTRAINT_TIERS: MediaStreamConstraints[] = [
  {
    audio: false,
    video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 }, frameRate: { ideal: 15, max: 30 } }
  },
  { audio: false, video: { facingMode: { ideal: 'environment' } } },
  { audio: false, video: true }
];

function classifyCameraError(err: unknown): 'permission-denied' | 'no-camera' | 'stream-failed' | 'unknown' {
  switch ((err as { name?: string } | null)?.name) {
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
    case 'AbortError':
      return 'stream-failed';
    default:
      return 'unknown';
  }
}

const CAMERA_ERRORS: Record<string, string> = {
  'permission-denied': 'Camera permission was denied. Allow camera access for this site, then try again.',
  'no-camera': 'No camera was found on this device.',
  'stream-failed': 'The camera could not be started. Close other apps using it and try again.',
  unknown: 'The camera could not be started.'
};

export class LivestockScanSession {
  private state: LivestockScanState = { ...IDLE_LIVESTOCK_SCAN_STATE };
  private stream: MediaStream | null = null;
  private frameSource: CaptureFrameSource | null = null;
  private ownedVideo: HTMLVideoElement | null = null;
  private cameraVideo: HTMLVideoElement | null = null;
  private canvas: HTMLCanvasElement | null = null;
  private worker: Worker | null = null;
  private workerBusy = false;
  private requestId = 0;
  private timer: ReturnType<typeof setInterval> | null = null;
  private hint: LivestockReaderRequest['hint'] = null;
  private readonly stabilizer = new LivestockReadStabilizer();

  constructor(private readonly onEvent: (event: LivestockScanEvent) => void) {}

  getState(): LivestockScanState {
    return this.state;
  }

  private setState(patch: Partial<LivestockScanState>): void {
    this.state = { ...this.state, ...patch };
    this.onEvent({ type: 'state', state: this.state });
  }

  /** Read the player's own screen. Works in the browser and inside RustPlus Desktop. */
  async startDesktop(): Promise<boolean> {
    if (this.state.status === 'starting' || this.state.status === 'scanning') return false;
    if (!isDesktopCaptureSupported()) {
      this.setState({ status: 'error', source: 'desktop', error: 'Screen capture is not available in this browser.' });
      return false;
    }
    this.setState({ status: 'starting', source: 'desktop', error: null, live: null });
    try {
      this.stream = await navigator.mediaDevices.getDisplayMedia({
        video: { width: { ideal: 1920 }, height: { ideal: 1080 }, frameRate: { max: 30 } },
        audio: false
      });
      const track = this.stream.getVideoTracks()[0];
      if (!track) throw new Error('No video track received from screen capture');
      track.addEventListener('ended', () => this.stop());

      this.frameSource = createTrackFrameSource(track);
      if (!this.frameSource) {
        const video = document.createElement('video');
        video.muted = true;
        video.playsInline = true;
        video.autoplay = true;
        // Kept in the DOM, off screen, so the browser keeps decoding frames.
        Object.assign(video.style, { position: 'fixed', left: '-9999px', top: '-9999px', width: '64px', opacity: '0.001' });
        document.body.appendChild(video);
        video.srcObject = this.stream;
        await video.play().catch(() => undefined);
        this.ownedVideo = video;
        this.frameSource = createVideoFrameSource(video);
      }
      this.begin('desktop');
      return true;
    } catch (err: any) {
      const cancelled = err?.name === 'NotAllowedError' || err?.name === 'AbortError';
      this.teardown();
      this.setState({
        status: cancelled ? 'idle' : 'error',
        source: null,
        error: cancelled ? null : err?.message || 'Screen capture failed to start.'
      });
      return false;
    }
  }

  /** Read through a camera pointed at the screen. `video` is the visible preview. */
  async startCamera(video: HTMLVideoElement): Promise<boolean> {
    if (this.state.status === 'starting' || this.state.status === 'scanning') return false;
    if (!isCameraSupported()) {
      this.setState({ status: 'error', source: 'camera', error: 'This browser cannot open a camera.' });
      return false;
    }
    this.setState({ status: 'starting', source: 'camera', error: null, live: null });

    let lastError: unknown = null;
    for (const constraints of CAMERA_CONSTRAINT_TIERS) {
      try {
        this.stream = await navigator.mediaDevices.getUserMedia(constraints);
        break;
      } catch (err) {
        lastError = err;
        const code = classifyCameraError(err);
        if (code === 'permission-denied' || code === 'no-camera') break;
      }
    }
    if (!this.stream) {
      this.teardown();
      this.setState({ status: 'error', error: CAMERA_ERRORS[classifyCameraError(lastError)] ?? CAMERA_ERRORS.unknown });
      return false;
    }

    video.muted = true;
    video.playsInline = true;
    video.srcObject = this.stream;
    await video.play().catch(() => undefined);
    this.cameraVideo = video;
    this.stream.getVideoTracks()[0]?.addEventListener('ended', () => this.stop());
    this.begin('camera');
    return true;
  }

  /** Allow the animal currently in view to be reported again. */
  rearm(): void {
    this.stabilizer.rearm();
  }

  stop(): void {
    this.teardown();
    this.setState({ ...IDLE_LIVESTOCK_SCAN_STATE });
  }

  private begin(source: LivestockScanSource): void {
    this.stabilizer.reset();
    this.hint = null;
    this.ensureWorker();
    this.setState({ status: 'scanning', source, error: null });
    this.timer = setInterval(() => this.tick(), TICK_MS);
  }

  private ensureWorker(): void {
    if (this.worker) return;
    try {
      this.worker = new Worker(new URL('../../workers/livestockReader.worker.ts', import.meta.url), {
        type: 'module'
      });
      this.worker.onmessage = (event: MessageEvent<LivestockReaderResponse>) => {
        this.workerBusy = false;
        this.handleRead(event.data.read, event.data.conditions, event.data.portrait, event.data.name, event.data.elapsedMs);
      };
      this.worker.onerror = () => {
        // Fall back to reading on the main thread, at the same cadence.
        this.worker?.terminate();
        this.worker = null;
        this.workerBusy = false;
      };
    } catch {
      this.worker = null;
    }
  }

  private grabFrame(): ImageData | null {
    const isCamera = this.state.source === 'camera';
    let drawable: CanvasImageSource | null;
    let sourceWidth: number;
    let sourceHeight: number;
    if (isCamera) {
      const video = this.cameraVideo;
      if (!video || !video.videoWidth) return null;
      drawable = video;
      sourceWidth = video.videoWidth;
      sourceHeight = video.videoHeight;
    } else {
      const source = this.frameSource;
      drawable = source?.current() ?? null;
      if (!source || !drawable || !source.width) return null;
      sourceWidth = source.width;
      sourceHeight = source.height;
    }

    const maxWidth = isCamera ? CAMERA_MAX_WIDTH : DESKTOP_MAX_WIDTH;
    const scale = Math.min(1, maxWidth / sourceWidth);
    const width = Math.max(1, Math.round(sourceWidth * scale));
    const height = Math.max(1, Math.round(sourceHeight * scale));

    if (!this.canvas) this.canvas = document.createElement('canvas');
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
      this.hint = null;
    }
    const ctx = this.canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    ctx.drawImage(drawable, 0, 0, width, height);
    return ctx.getImageData(0, 0, width, height);
  }

  private tick(): void {
    if (this.state.status !== 'scanning' || this.workerBusy) return;
    const frame = this.grabFrame();
    if (!frame) return;
    if (!this.state.frameSize || this.state.frameSize.width !== frame.width || this.state.frameSize.height !== frame.height) {
      this.state = { ...this.state, frameSize: { width: frame.width, height: frame.height } };
    }

    if (this.worker) {
      this.workerBusy = true;
      const request: LivestockReaderRequest = {
        id: ++this.requestId,
        buffer: frame.data.buffer,
        width: frame.width,
        height: frame.height,
        hint: this.hint
      };
      this.worker.postMessage(request, [frame.data.buffer]);
    } else {
      const started = performance.now();
      const read = readLivestockPanel(
        { data: frame.data, width: frame.width, height: frame.height },
        { readMarkerDigit }
      );
      const raster = { data: frame.data, width: frame.width, height: frame.height };
      const conditions = read ? readPanelConditions(raster, read) : null;
      const portrait = read ? classifyPortrait(raster, read) : null;
      const name = read ? readPanelName(raster, read)?.text ?? null : null;
      this.handleRead(read, conditions, portrait, name, performance.now() - started);
    }
  }

  private handleRead(
    read: LivestockPanelRead | null,
    conditions: PanelConditions | null,
    portrait: PortraitMatch | null,
    name: string | null,
    elapsedMs: number
  ): void {
    if (this.state.status !== 'scanning') return;
    this.hint = read ? read.bounds : null;
    // Keep the last confident portrait while the same panel stays in view: one blurred frame
    // should not forget what the animal is.
    const keptPortrait = portrait ?? (read ? this.state.livePortrait : null);
    this.setState({ live: read, liveConditions: conditions, livePortrait: keptPortrait, liveName: name, lastReadMs: elapsedMs });

    const event = this.stabilizer.push(read ? { rows: read.rows, confidence: read.confidence, name } : null, performance.now());
    if (!event) return;
    if (event.type === 'confirmed') {
      this.onEvent({ ...event, observed: conditions ? { ...conditions, at: Date.now() } : null, portrait: keptPortrait, name });
    } else {
      this.onEvent(event);
    }
  }

  private teardown(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    this.worker?.terminate();
    this.worker = null;
    this.workerBusy = false;
    this.frameSource?.stop();
    this.frameSource = null;
    this.stream?.getTracks().forEach((track) => track.stop());
    this.stream = null;
    if (this.ownedVideo) {
      this.ownedVideo.srcObject = null;
      this.ownedVideo.remove();
      this.ownedVideo = null;
    }
    if (this.cameraVideo) {
      this.cameraVideo.srcObject = null;
      this.cameraVideo = null;
    }
    this.hint = null;
  }
}
