import { Point } from './scannerTypes.ts';

/**
 * Maps between camera-frame coordinates and the on-screen video rectangle.
 *
 * The preview uses `object-fit: contain` so the whole camera image stays visible, which means
 * the video is letterboxed inside its element. Overlays and taps have to travel through that
 * same rectangle or the border will sit somewhere the row is not.
 */

export interface ContainRect {
  /** Offset of the displayed image inside the element, in CSS pixels. */
  offsetX: number;
  offsetY: number;
  /** Displayed size of the image, in CSS pixels. */
  width: number;
  height: number;
  /** CSS pixels per frame pixel. */
  scale: number;
}

export function computeContainRect(
  frameWidth: number,
  frameHeight: number,
  elementWidth: number,
  elementHeight: number
): ContainRect | null {
  if (frameWidth <= 0 || frameHeight <= 0 || elementWidth <= 0 || elementHeight <= 0) return null;

  const scale = Math.min(elementWidth / frameWidth, elementHeight / frameHeight);
  const width = frameWidth * scale;
  const height = frameHeight * scale;

  return {
    offsetX: (elementWidth - width) / 2,
    offsetY: (elementHeight - height) / 2,
    width,
    height,
    scale
  };
}

export function frameToElement(point: Point, rect: ContainRect): Point {
  return {
    x: rect.offsetX + point.x * rect.scale,
    y: rect.offsetY + point.y * rect.scale
  };
}

export function elementToFrame(point: Point, rect: ContainRect): Point {
  return {
    x: (point.x - rect.offsetX) / rect.scale,
    y: (point.y - rect.offsetY) / rect.scale
  };
}

/** SVG `points` attribute for a quadrilateral given in frame coordinates. */
export function quadToSvgPoints(corners: readonly Point[], rect: ContainRect): string {
  return corners
    .map(corner => {
      const mapped = frameToElement(corner, rect);
      return `${mapped.x.toFixed(1)},${mapped.y.toFixed(1)}`;
    })
    .join(' ');
}
