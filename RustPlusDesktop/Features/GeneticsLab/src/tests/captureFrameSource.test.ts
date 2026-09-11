import { describe, it, expect, afterEach } from 'vitest';
import {
  createTrackFrameSource,
  createVideoFrameSource,
  isTrackProcessorSupported
} from '../services/scanner/vision/captureFrameSource.ts';

/* ------------------------------------------------------------------ *
 * A stand-in for the capture pipeline
 * ------------------------------------------------------------------ */

class FakeVideoFrame {
  public closed = false;
  constructor(
    public readonly displayWidth: number,
    public readonly displayHeight: number,
    public readonly id: number
  ) {}
  close() {
    this.closed = true;
  }
}

interface FakeProcessorHandle {
  push(frame: FakeVideoFrame): Promise<void>;
  end(): void;
}

let pending: FakeProcessorHandle | null = null;

function installFakeProcessor(): FakeProcessorHandle {
  let controller: ReadableStreamDefaultController<FakeVideoFrame> | null = null;

  (globalThis as any).MediaStreamTrackProcessor = class {
    public readable: ReadableStream<FakeVideoFrame>;
    constructor(_options: { track: unknown }) {
      this.readable = new ReadableStream<FakeVideoFrame>({
        start(c) {
          controller = c;
        }
      });
    }
  };

  const handle: FakeProcessorHandle = {
    async push(frame: FakeVideoFrame) {
      try {
        controller?.enqueue(frame);
      } catch {
        // Stopping the source cancels the reader, which closes the stream. A push after
        // that is exactly what the teardown test is checking, so it is not an error here.
      }
      // Let the source's read loop drain the queue before the assertions run.
      await new Promise(resolve => setTimeout(resolve, 0));
    },
    end() {
      try {
        controller?.close();
      } catch {
        // Already closed.
      }
    }
  };

  pending = handle;
  return handle;
}

function uninstallFakeProcessor(): void {
  pending?.end();
  pending = null;
  delete (globalThis as any).MediaStreamTrackProcessor;
}

const fakeTrack = {} as MediaStreamTrack;

afterEach(() => uninstallFakeProcessor());

describe('Capture frame source', () => {
  it('reads frames off the track when the platform supports it', async () => {
    const pipeline = installFakeProcessor();
    expect(isTrackProcessorSupported()).toBe(true);

    const source = createTrackFrameSource(fakeTrack)!;
    expect(source).not.toBeNull();
    expect(source.kind).toBe('track');
    expect(source.current()).toBeNull();

    await pipeline.push(new FakeVideoFrame(1920, 1080, 1));

    expect(source.width).toBe(1920);
    expect(source.height).toBe(1080);
    expect((source.current() as unknown as FakeVideoFrame).id).toBe(1);
    expect(source.frameCount).toBe(1);

    source.stop();
  });

  it('releases each frame as the next arrives', async () => {
    // VideoFrames come from a fixed pool of GPU buffers. Holding them makes the capturer run
    // out and stop producing, which presents exactly as the stall this class exists to
    // avoid -- so a leak here would silently undo the whole fix.
    const pipeline = installFakeProcessor();
    const source = createTrackFrameSource(fakeTrack)!;

    const frames = [
      new FakeVideoFrame(1920, 1080, 1),
      new FakeVideoFrame(1920, 1080, 2),
      new FakeVideoFrame(1920, 1080, 3)
    ];

    for (const frame of frames) await pipeline.push(frame);

    expect(frames[0].closed).toBe(true);
    expect(frames[1].closed).toBe(true);
    // The one still being read from stays open.
    expect(frames[2].closed).toBe(false);
    expect((source.current() as unknown as FakeVideoFrame).id).toBe(3);

    source.stop();
    expect(frames[2].closed).toBe(true);
    expect(source.current()).toBeNull();
  });

  it('notifies once per frame so scanning is driven by capture', async () => {
    const pipeline = installFakeProcessor();
    const source = createTrackFrameSource(fakeTrack)!;

    let notifications = 0;
    source.onFrame(() => notifications++);

    await pipeline.push(new FakeVideoFrame(800, 600, 1));
    await pipeline.push(new FakeVideoFrame(800, 600, 2));

    expect(notifications).toBe(2);
    expect(source.frameCount).toBe(2);

    // A stopped source must not keep calling back into a torn-down scanner.
    source.stop();
    await pipeline.push(new FakeVideoFrame(800, 600, 3));
    expect(notifications).toBe(2);
  });

  it('measures the gap between frames rather than between ticks', async () => {
    const pipeline = installFakeProcessor();
    const source = createTrackFrameSource(fakeTrack)!;

    await pipeline.push(new FakeVideoFrame(800, 600, 1));
    expect(source.lastFrameGapMs).toBe(0);
    const firstAt = source.lastFrameAt;
    expect(firstAt).toBeGreaterThan(0);

    await new Promise(resolve => setTimeout(resolve, 20));
    await pipeline.push(new FakeVideoFrame(800, 600, 2));

    expect(source.lastFrameGapMs).toBeGreaterThanOrEqual(15);
    expect(source.lastFrameAt).toBeGreaterThan(firstAt);

    source.stop();
  });

  it('declines the track path when the platform has no processor', () => {
    uninstallFakeProcessor();
    expect(isTrackProcessorSupported()).toBe(false);
    expect(createTrackFrameSource(fakeTrack)).toBeNull();
  });

  it('counts a video frame only when the capture actually advanced', async () => {
    let currentTime = 0;
    const video = {
      readyState: 4,
      videoWidth: 1280,
      videoHeight: 720,
      get currentTime() {
        return currentTime;
      }
    } as unknown as HTMLVideoElement;

    const source = createVideoFrameSource(video);
    expect(source.kind).toBe('video');

    // The element is decodable throughout; only currentTime says whether a frame is new.
    // Counting ticks instead is what made a frozen capture look healthy.
    await new Promise(resolve => setTimeout(resolve, 40));
    const afterStall = source.frameCount;

    currentTime = 0.033;
    await new Promise(resolve => setTimeout(resolve, 40));

    expect(source.frameCount).toBe(afterStall + 1);
    source.stop();
  });
});
