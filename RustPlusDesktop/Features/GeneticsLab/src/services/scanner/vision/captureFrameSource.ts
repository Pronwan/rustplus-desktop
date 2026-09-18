/**
 * Where the scanner gets its pixels from.
 *
 * The screen capture arrives as a `MediaStreamTrack` either way. What differs is how the
 * frames reach the code that reads them, and behind an exclusive-fullscreen game that turns
 * out to be the whole problem.
 *
 * Routing the track through an `HTMLVideoElement` means a frame is only readable once the
 * renderer has *presented* it, and presentation rides on the same compositor the game is
 * starving. `drawImage(video, ...)` never fails or throws when that happens -- it quietly
 * hands back the last frame it managed to present, so the scanner reads a tooltip that is no
 * longer on screen, or reads the same one forever. Every knob that could have prevented this
 * is already turned: the host passes `--disable-renderer-backgrounding`,
 * `--disable-backgrounding-occluded-windows` and `--disable-features=CalculateNativeWinOcclusion`,
 * forces the web lifecycle state to active over CDP, opts the WebView2 processes out of
 * EcoQoS and raises their priority, and the page holds a silent AudioContext open. None of
 * that helps, because none of it is about throttling -- the compositor is busy, not asleep.
 *
 * `MediaStreamTrackProcessor` exposes the same track as a stream of `VideoFrame`s straight
 * from the capture pipeline. There is no element, no presentation step and no compositor in
 * the path, and a `VideoFrame` is itself a `CanvasImageSource`, so the drawing code does not
 * change. Where it is unavailable the video element still works exactly as before.
 */

export type CaptureFrameSourceKind = 'track' | 'video';

export interface CaptureFrameSource {
  readonly kind: CaptureFrameSourceKind;
  /** Captured surface size. Zero until the first frame lands. */
  readonly width: number;
  readonly height: number;
  /** `performance.now()` of the most recent genuinely new frame, or 0. */
  readonly lastFrameAt: number;
  /** Milliseconds between the last two new frames. */
  readonly lastFrameGapMs: number;
  readonly frameCount: number;
  /** The current frame as something `drawImage` accepts, or null before the first one. */
  current(): CanvasImageSource | null;
  /** Called once per new frame. Used to drive scanning off capture rather than off a timer. */
  onFrame(listener: () => void): void;
  stop(): void;
}

export function isTrackProcessorSupported(): boolean {
  return typeof (globalThis as any).MediaStreamTrackProcessor === 'function';
}

abstract class BaseFrameSource {
  protected listener: (() => void) | null = null;
  protected _lastFrameAt = 0;
  protected _lastFrameGapMs = 0;
  protected _frameCount = 0;

  public get lastFrameAt(): number {
    return this._lastFrameAt;
  }

  public get lastFrameGapMs(): number {
    return this._lastFrameGapMs;
  }

  public get frameCount(): number {
    return this._frameCount;
  }

  public onFrame(listener: () => void): void {
    this.listener = listener;
  }

  protected markFrame(): void {
    const now = performance.now();
    if (this._lastFrameAt > 0) this._lastFrameGapMs = now - this._lastFrameAt;
    this._lastFrameAt = now;
    this._frameCount++;
    this.listener?.();
  }
}

class TrackFrameSource extends BaseFrameSource implements CaptureFrameSource {
  public readonly kind = 'track' as const;

  private reader: ReadableStreamDefaultReader<any> | null = null;
  private frame: any = null;
  private running = true;
  private _width = 0;
  private _height = 0;

  constructor(track: MediaStreamTrack) {
    super();
    const Processor = (globalThis as any).MediaStreamTrackProcessor;
    const processor = new Processor({ track });
    this.reader = (processor.readable as ReadableStream).getReader();
    void this.pump();
  }

  public get width(): number {
    return this._width;
  }

  public get height(): number {
    return this._height;
  }

  public current(): CanvasImageSource | null {
    return this.frame;
  }

  private async pump(): Promise<void> {
    const reader = this.reader;
    if (!reader) return;

    try {
      while (this.running) {
        const { value, done } = await reader.read();
        if (done || !this.running) {
          value?.close?.();
          break;
        }

        // Exactly one frame is held at a time and the outgoing one is closed immediately.
        // VideoFrames are backed by a fixed pool of GPU buffers; holding on to them starves
        // the capturer and it simply stops producing, which looks identical to the stall
        // this class exists to avoid.
        this.frame?.close?.();
        this.frame = value;
        this._width = value.displayWidth || value.codedWidth || this._width;
        this._height = value.displayHeight || value.codedHeight || this._height;
        this.markFrame();
      }
    } catch {
      // The track ended or the reader was cancelled. `stop()` handles the teardown.
    }
  }

  public stop(): void {
    this.running = false;
    this.listener = null;

    try {
      this.reader?.cancel();
    } catch {
      // Already closed.
    }
    this.reader = null;

    try {
      this.frame?.close?.();
    } catch {
      // Already closed.
    }
    this.frame = null;
  }
}

class VideoFrameSource extends BaseFrameSource implements CaptureFrameSource {
  public readonly kind = 'video' as const;

  private running = true;
  private lastMediaTime = -1;
  private pollTimer: any = null;

  constructor(private readonly video: HTMLVideoElement) {
    super();

    if ('requestVideoFrameCallback' in video) {
      const onPresented = (_now: number, metadata: any) => {
        if (!this.running) return;
        // `mediaTime` identifies the frame itself, so a repeated presentation of a frame the
        // capture never replaced is not counted as a new one.
        if (metadata && metadata.mediaTime !== this.lastMediaTime) {
          this.lastMediaTime = metadata.mediaTime;
          this.markFrame();
        }
        (this.video as any).requestVideoFrameCallback(onPresented);
      };
      (video as any).requestVideoFrameCallback(onPresented);
      return;
    }

    // Without the presentation callback there is nothing to subscribe to, so currentTime is
    // polled instead. It moves only when a new frame was decoded.
    this.pollTimer = setInterval(() => {
      if (!this.running) return;
      if (this.video.readyState >= 2 && this.video.currentTime !== this.lastMediaTime) {
        this.lastMediaTime = this.video.currentTime;
        this.markFrame();
      }
    }, 8);
  }

  public get width(): number {
    return this.video.videoWidth;
  }

  public get height(): number {
    return this.video.videoHeight;
  }

  public current(): CanvasImageSource | null {
    return this.video.videoWidth > 0 ? this.video : null;
  }

  public stop(): void {
    this.running = false;
    this.listener = null;
    if (this.pollTimer) {
      clearInterval(this.pollTimer);
      this.pollTimer = null;
    }
  }
}

/**
 * Frames straight off the capture track, or null where the platform cannot do it.
 *
 * Returning null rather than falling back here lets the caller skip creating the video
 * element altogether when this path works -- the element is not merely unused at that point,
 * it is a second sink on the same track doing exactly the decode-and-present work being
 * avoided.
 */
export function createTrackFrameSource(track: MediaStreamTrack): CaptureFrameSource | null {
  if (!isTrackProcessorSupported()) return null;

  try {
    return new TrackFrameSource(track);
  } catch (error) {
    console.warn('[Scanner] MediaStreamTrackProcessor failed, falling back to the video element', error);
    return null;
  }
}

/**
 * Frames as the renderer presents them.
 *
 * Kept because `MediaStreamTrackProcessor` is Chromium-only and this app also ships as a
 * plain web build.
 */
export function createVideoFrameSource(video: HTMLVideoElement): CaptureFrameSource {
  return new VideoFrameSource(video);
}
