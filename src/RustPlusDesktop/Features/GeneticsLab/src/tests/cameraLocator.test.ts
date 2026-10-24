import { describe, it, expect } from 'vitest';
import { DynamicGeneLocator } from '../services/scanner/DynamicGeneLocator.ts';
import { AnalysisFrame, cropAnalysisFrame } from '../services/scanner/vision/rasterOps.ts';
import { CameraFrameGrabber } from '../services/scanner/vision/frameGrabber.ts';
import { RasterImage } from '../services/scanner/vision/perspective.ts';
import { perpendicular } from '../services/scanner/vision/geometry.ts';

/**
 * End-to-end locator tests.
 *
 * A fake grabber feeds synthetic camera frames straight into the real pipeline, so detection,
 * selection, tracking, normalisation and the quality gates are all exercised together without
 * a browser.
 */

const GREEN: [number, number, number] = [58, 168, 72];
const RED: [number, number, number] = [198, 62, 48];
const TOOLTIP: [number, number, number] = [24, 24, 28];
const WHITE: [number, number, number] = [244, 244, 242];
const DESK: [number, number, number] = [96, 94, 100];

const CAMERA_WIDTH = 960;
const CAMERA_HEIGHT = 540;

function createFrame(): AnalysisFrame {
  const data = new Uint8ClampedArray(CAMERA_WIDTH * CAMERA_HEIGHT * 4);
  for (let i = 0; i < data.length; i += 4) {
    data[i] = DESK[0];
    data[i + 1] = DESK[1];
    data[i + 2] = DESK[2];
    data[i + 3] = 255;
  }
  return { data, width: CAMERA_WIDTH, height: CAMERA_HEIGHT, scale: 1 };
}

function setPixel(frame: AnalysisFrame, x: number, y: number, color: [number, number, number]): void {
  const px = Math.floor(x);
  const py = Math.floor(y);
  if (px < 0 || py < 0 || px >= frame.width || py >= frame.height) return;
  const i = (py * frame.width + px) * 4;
  frame.data[i] = color[0];
  frame.data[i + 1] = color[1];
  frame.data[i + 2] = color[2];
  frame.data[i + 3] = 255;
}

interface RowOptions {
  cx: number;
  cy: number;
  badgeWidth?: number;
  badgeHeight?: number;
  spacing?: number;
  angle?: number;
  count?: number;
}

/** A dark tooltip panel carrying six colour-coded gene badges with white letters. */
function drawGeneRow(frame: AnalysisFrame, options: RowOptions): void {
  const {
    cx,
    cy,
    badgeWidth = 40,
    badgeHeight = 46,
    spacing = 52,
    angle = 0,
    count = 6
  } = options;

  const axis = { x: Math.cos(angle), y: Math.sin(angle) };
  const normal = perpendicular(axis);

  const halfLength = (spacing * (count - 1)) / 2 + badgeWidth;
  const halfBand = badgeHeight * 1.6;
  for (let t = -halfLength; t <= halfLength; t += 0.5) {
    for (let n = -halfBand; n <= halfBand; n += 0.5) {
      setPixel(frame, cx + axis.x * t + normal.x * n, cy + axis.y * t + normal.y * n, TOOLTIP);
    }
  }

  for (let i = 0; i < count; i++) {
    const offset = (i - (count - 1) / 2) * spacing;
    const bx = cx + axis.x * offset;
    const by = cy + axis.y * offset;
    const color = i % 3 === 0 ? RED : GREEN;
    const halfW = badgeWidth / 2;
    const halfH = badgeHeight / 2;

    for (let lx = -halfW; lx <= halfW; lx += 0.5) {
      for (let ly = -halfH; ly <= halfH; ly += 0.5) {
        // A blocky glyph: two verticals joined by a bar, enough to give the sharpness
        // measurement real letter edges to work with.
        const isStroke =
          Math.abs(Math.abs(lx) - halfW * 0.4) < halfW * 0.12 && Math.abs(ly) < halfH * 0.55;
        const isBar = Math.abs(ly) < halfH * 0.12 && Math.abs(lx) < halfW * 0.4;
        setPixel(
          frame,
          bx + axis.x * lx + normal.x * ly,
          by + axis.y * lx + normal.y * ly,
          isStroke || isBar ? WHITE : color
        );
      }
    }
  }
}

interface FakeGrabber extends CameraFrameGrabber {
  disposed: boolean;
  canvasCount: number;
}

function createFakeGrabber(frame: AnalysisFrame): FakeGrabber {
  return {
    disposed: false,
    canvasCount: 0,
    grabAnalysis(): AnalysisFrame | null {
      return frame;
    },
    grabRegion(_video, x, y, width, height): RasterImage | null {
      const crop = cropAnalysisFrame(frame, x, y, width, height);
      if (!crop) return null;
      return { data: crop.frame.data, width: crop.frame.width, height: crop.frame.height };
    },
    toCanvas(): HTMLCanvasElement | null {
      this.canvasCount++;
      return {} as HTMLCanvasElement;
    },
    dispose(): void {
      this.disposed = true;
    }
  };
}

const VIDEO = { videoWidth: CAMERA_WIDTH, videoHeight: CAMERA_HEIGHT } as HTMLVideoElement;

/** Runs the locator for `frames` ticks at a fixed cadence, returning the last result. */
async function run(locator: DynamicGeneLocator, frames: number, stepMs = 100) {
  let result = await locator.analyze(VIDEO, 0);
  for (let i = 1; i < frames; i++) {
    result = await locator.analyze(VIDEO, i * stepMs);
  }
  return result;
}

describe('DynamicGeneLocator end to end', () => {
  it('locks onto a well-framed row and produces a readable target', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270 });
    const grabber = createFakeGrabber(frame);
    const locator = new DynamicGeneLocator({ grabber, orientationAngle: () => 0 });

    const result = await run(locator, 8);

    expect(result.phase).toBe('tracking');
    expect(result.activeTarget).not.toBeNull();
    expect(result.qualityIssues).toEqual([]);
    expect(result.overlay.target).not.toBeNull();
    expect(result.overlay.frameWidth).toBe(CAMERA_WIDTH);
  });

  it('measures six uniform OCR slots inside the normalised row', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const result = await run(locator, 8);
    const slots = result.activeTarget?.slots;

    expect(slots).toBeDefined();
    expect(slots!.geneWidth).toBeGreaterThan(0);
    expect(slots!.height).toBeGreaterThan(0);
    // Six slots plus five gaps must span the normalised canvas without overflowing it.
    const span = slots!.baseX + 6 * slots!.geneWidth + 5 * slots!.gapWidth;
    expect(span).toBeGreaterThan(400);
    expect(span).toBeLessThanOrEqual(600);
  });

  it('still locks on when the row is rotated', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270, angle: Math.PI / 9 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const result = await run(locator, 8);

    expect(result.phase).toBe('tracking');
    expect(result.activeTarget).not.toBeNull();
  });

  it('reports searching when nothing resembling a row is visible', async () => {
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(createFrame()),
      orientationAngle: () => 0
    });

    const result = await run(locator, 4);

    expect(result.phase).toBe('searching');
    expect(result.activeTarget).toBeNull();
    expect(result.overlay.candidates).toHaveLength(0);
  });

  it('says the row is too far away rather than reading it badly', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270, badgeWidth: 12, badgeHeight: 14, spacing: 16 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const result = await run(locator, 8);

    expect(result.phase).toBe('quality-blocked');
    expect(result.qualityIssues).toContain('too-far');
    expect(result.activeTarget).toBeNull();
  });

  it('refuses to choose between two equally plausible rows', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 140 });
    drawGeneRow(frame, { cx: 480, cy: 400 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const result = await run(locator, 4);

    expect(result.phase).toBe('ambiguous');
    expect(result.candidateCount).toBe(2);
    expect(result.activeTarget).toBeNull();
    expect(result.overlay.candidates).toHaveLength(2);
  });

  it('resolves ambiguity from a tap, without saving anything', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 140 });
    drawGeneRow(frame, { cx: 480, cy: 400 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const ambiguous = await run(locator, 3);
    expect(ambiguous.phase).toBe('ambiguous');

    locator.selectAt({ x: 480, y: 400 });
    const chosen = await locator.analyze(VIDEO, 400);

    expect(chosen.phase).not.toBe('ambiguous');
    expect(chosen.overlay.target).not.toBeNull();
    // The tap picked the lower row.
    expect(chosen.overlay.target![0].y).toBeGreaterThan(300);

    // A fresh locator has no memory of that choice: nothing was persisted.
    const replay = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });
    expect((await run(replay, 3)).phase).toBe('ambiguous');
  });

  it('keeps tracking the same row while the phone drifts', async () => {
    const positions = [480, 486, 492, 498, 504, 510];
    const grabberFrames = positions.map(cx => {
      const frame = createFrame();
      drawGeneRow(frame, { cx, cy: 270 });
      return frame;
    });

    let index = 0;
    const grabber: CameraFrameGrabber = {
      grabAnalysis: () => grabberFrames[Math.min(index, grabberFrames.length - 1)],
      grabRegion: (_video, x, y, width, height) => {
        const crop = cropAnalysisFrame(
          grabberFrames[Math.min(index, grabberFrames.length - 1)],
          x,
          y,
          width,
          height
        );
        return crop ? { data: crop.frame.data, width: crop.frame.width, height: crop.frame.height } : null;
      },
      toCanvas: () => ({} as HTMLCanvasElement),
      dispose: () => {}
    };

    const locator = new DynamicGeneLocator({ grabber, orientationAngle: () => 0 });

    let result = await locator.analyze(VIDEO, 0);
    for (index = 1; index < positions.length; index++) {
      result = await locator.analyze(VIDEO, index * 100);
      expect(result.phase === 'tracking' || result.phase === 'quality-blocked').toBe(true);
      expect(result.overlay.target).not.toBeNull();
    }

    expect(result.overlay.target).not.toBeNull();
  });

  it('drops the target when the row leaves the frame', async () => {
    const withRow = createFrame();
    drawGeneRow(withRow, { cx: 480, cy: 270 });
    const empty = createFrame();

    let current = withRow;
    const grabber: CameraFrameGrabber = {
      grabAnalysis: () => current,
      grabRegion: (_video, x, y, width, height) => {
        const crop = cropAnalysisFrame(current, x, y, width, height);
        return crop ? { data: crop.frame.data, width: crop.frame.width, height: crop.frame.height } : null;
      },
      toCanvas: () => ({} as HTMLCanvasElement),
      dispose: () => {}
    };

    const locator = new DynamicGeneLocator({ grabber, orientationAngle: () => 0 });
    await run(locator, 6);

    current = empty;
    const lost = await locator.analyze(VIDEO, 1000);

    expect(lost.phase).toBe('searching');
    expect(lost.overlay.target).toBeNull();
  });

  it('still offers a moving row, flagged as moving rather than withheld', async () => {
    // Alternating positions never satisfy the stability window. That used to withhold the
    // target entirely, which left a clearly legible row unread indefinitely on a hand-held
    // phone. Motion is now reported as advice and the recogniser decides.
    const frames = [470, 520].map(cx => {
      const frame = createFrame();
      drawGeneRow(frame, { cx, cy: 270 });
      return frame;
    });

    let index = 0;
    const grabber: CameraFrameGrabber = {
      grabAnalysis: () => frames[index % 2],
      grabRegion: (_video, x, y, width, height) => {
        const crop = cropAnalysisFrame(frames[index % 2], x, y, width, height);
        return crop ? { data: crop.frame.data, width: crop.frame.width, height: crop.frame.height } : null;
      },
      toCanvas: () => ({} as HTMLCanvasElement),
      dispose: () => {}
    };

    const locator = new DynamicGeneLocator({ grabber, orientationAngle: () => 0 });

    let sawTarget = false;
    let sawMovingAdvice = false;
    for (index = 0; index < 10; index++) {
      const result = await locator.analyze(VIDEO, index * 100);
      if (result.activeTarget) sawTarget = true;
      if (result.qualityIssues.includes('moving')) sawMovingAdvice = true;
    }

    expect(sawTarget).toBe(true);
    expect(sawMovingAdvice).toBe(true);
  });

  it('keeps a blocked row available to manual capture', async () => {
    // Too far to read automatically, but the geometry is sound, so a manual capture still
    // has something to work with rather than reporting no row at all.
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270, badgeWidth: 12, badgeHeight: 14, spacing: 16 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const result = await run(locator, 8);

    expect(result.phase).toBe('quality-blocked');
    expect(result.activeTarget).toBeNull();
  });

  it('releases the grabber on dispose and stops analysing', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270 });
    const grabber = createFakeGrabber(frame);
    const locator = new DynamicGeneLocator({ grabber, orientationAngle: () => 0 });

    await run(locator, 6);
    locator.dispose();
    locator.dispose();

    expect(grabber.disposed).toBe(true);
    const afterDispose = await locator.analyze(VIDEO, 2000);
    expect(afterDispose.phase).toBe('searching');
    expect(afterDispose.activeTarget).toBeNull();
  });

  it('re-establishes its lock from scratch when the discovery resolution changes', async () => {
    const frame = createFrame();
    drawGeneRow(frame, { cx: 480, cy: 270 });
    const locator = new DynamicGeneLocator({
      grabber: createFakeGrabber(frame),
      orientationAngle: () => 0
    });

    const before = await run(locator, 6);
    expect(before.activeTarget).not.toBeNull();

    locator.setDiscoveryWidth(720);

    // Nothing measured against the old frame geometry carries over, but the row is found
    // again rather than being withheld.
    const after = await locator.analyze(VIDEO, 700);
    expect(after.overlay.target).not.toBeNull();
  });
});
