import { AnalysisFrame } from './rasterOps.ts';
import { RasterImage } from '../scannerTypes.ts';

/**
 * The only part of the camera vision stack that touches the DOM.
 *
 * Isolating it here keeps every detection, geometry and quality rule testable against plain
 * pixel buffers, and gives the locator a single seam to fake in tests.
 */
export interface CameraFrameGrabber {
  /** Whole frame, downscaled to at most `maxWidth`, for candidate discovery. */
  grabAnalysis(video: HTMLVideoElement, maxWidth: number): AnalysisFrame | null;
  /** A native-resolution crop, in camera pixels, for perspective normalisation and OCR. */
  grabRegion(video: HTMLVideoElement, x: number, y: number, width: number, height: number): RasterImage | null;
  /** Materialises a warped row as a canvas, which is what the OCR modules consume. */
  toCanvas(image: RasterImage): HTMLCanvasElement | null;
  dispose(): void;
}

function createCanvas(width: number, height: number): HTMLCanvasElement | null {
  if (typeof document === 'undefined') return null;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  return canvas;
}

export class CanvasFrameGrabber implements CameraFrameGrabber {
  private analysisCanvas: HTMLCanvasElement | null = null;
  private regionCanvas: HTMLCanvasElement | null = null;
  private outputCanvas: HTMLCanvasElement | null = null;

  grabAnalysis(video: HTMLVideoElement, maxWidth: number): AnalysisFrame | null {
    const sourceWidth = video.videoWidth;
    const sourceHeight = video.videoHeight;
    if (!sourceWidth || !sourceHeight) return null;

    const ratio = Math.min(1, maxWidth / sourceWidth);
    const width = Math.max(1, Math.round(sourceWidth * ratio));
    const height = Math.max(1, Math.round(sourceHeight * ratio));

    if (!this.analysisCanvas) this.analysisCanvas = createCanvas(width, height);
    const canvas = this.analysisCanvas;
    if (!canvas) return null;

    if (canvas.width !== width || canvas.height !== height) {
      canvas.width = width;
      canvas.height = height;
    }

    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;

    ctx.drawImage(video, 0, 0, width, height);
    const image = ctx.getImageData(0, 0, width, height);

    return { data: image.data, width, height, scale: width / sourceWidth };
  }

  grabRegion(
    video: HTMLVideoElement,
    x: number,
    y: number,
    width: number,
    height: number
  ): RasterImage | null {
    if (width <= 0 || height <= 0) return null;
    if (!video.videoWidth || !video.videoHeight) return null;

    const clampedWidth = Math.min(Math.round(width), video.videoWidth);
    const clampedHeight = Math.min(Math.round(height), video.videoHeight);

    if (!this.regionCanvas) this.regionCanvas = createCanvas(clampedWidth, clampedHeight);
    const canvas = this.regionCanvas;
    if (!canvas) return null;

    if (canvas.width !== clampedWidth || canvas.height !== clampedHeight) {
      canvas.width = clampedWidth;
      canvas.height = clampedHeight;
    }

    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;

    ctx.drawImage(
      video,
      Math.round(x),
      Math.round(y),
      clampedWidth,
      clampedHeight,
      0,
      0,
      clampedWidth,
      clampedHeight
    );
    const image = ctx.getImageData(0, 0, clampedWidth, clampedHeight);

    return { data: image.data, width: clampedWidth, height: clampedHeight };
  }

  toCanvas(image: RasterImage): HTMLCanvasElement | null {
    if (!this.outputCanvas) this.outputCanvas = createCanvas(image.width, image.height);
    const canvas = this.outputCanvas;
    if (!canvas) return null;

    if (canvas.width !== image.width || canvas.height !== image.height) {
      canvas.width = image.width;
      canvas.height = image.height;
    }

    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;

    const imageData = ctx.createImageData(image.width, image.height);
    imageData.data.set(image.data);
    ctx.putImageData(imageData, 0, 0);
    return canvas;
  }

  dispose(): void {
    // Collapsing to 0x0 releases the backing store immediately rather than waiting for the
    // canvases themselves to be collected.
    for (const canvas of [this.analysisCanvas, this.regionCanvas, this.outputCanvas]) {
      if (!canvas) continue;
      canvas.width = 0;
      canvas.height = 0;
    }
    this.analysisCanvas = null;
    this.regionCanvas = null;
    this.outputCanvas = null;
  }
}


/* ------------------------------------------------------------------ *
 * Raster -> canvas
 *
 * The OCR modules take canvases, but everything upstream works on pixel buffers. These
 * canvases are pooled by index and reused across frames, so a scanning session allocates a
 * fixed handful rather than one per read.
 * ------------------------------------------------------------------ */

const rasterCanvasPool: HTMLCanvasElement[] = [];

export function rasterToCanvas(image: RasterImage, poolIndex: number): HTMLCanvasElement | null {
  if (typeof document === 'undefined') return null;
  if (image.width <= 0 || image.height <= 0) return null;

  let canvas = rasterCanvasPool[poolIndex];
  if (!canvas) {
    canvas = document.createElement('canvas');
    rasterCanvasPool[poolIndex] = canvas;
  }

  if (canvas.width !== image.width || canvas.height !== image.height) {
    canvas.width = image.width;
    canvas.height = image.height;
  }

  const ctx = canvas.getContext('2d', { willReadFrequently: true });
  if (!ctx) return null;

  const imageData = ctx.createImageData(image.width, image.height);
  imageData.data.set(image.data);
  ctx.putImageData(imageData, 0, 0);
  return canvas;
}

/** Drops the pooled canvases so a stopped scanner holds no backing stores. */
export function releaseRasterCanvases(): void {
  for (const canvas of rasterCanvasPool) {
    if (!canvas) continue;
    canvas.width = 0;
    canvas.height = 0;
  }
  rasterCanvasPool.length = 0;
}
