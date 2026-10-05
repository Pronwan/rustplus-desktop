/**
 * Livestock panel reader, off the main thread.
 *
 * A full 1080p frame costs ~100 ms to search; doing that on the UI thread several times a
 * second would make the page stutter while scanning. The worker first searches around where
 * the panel was last seen (a few milliseconds while the player keeps looking at the same
 * animal), and only falls back to the whole frame when the panel has moved or gone.
 */

import { readLivestockPanel, LivestockPanelRead, LivestockRowRead } from '../services/livestock/livestockPanelReader.ts';
import { readMarkerDigit } from '../services/livestock/markerDigit.ts';
import { PanelConditions, readPanelConditions, readPanelName } from '../services/livestock/panelConditions.ts';
import { PortraitMatch, classifyPortrait } from '../services/livestock/portraitClassifier.ts';

export interface LivestockReaderRequest {
  id: number;
  buffer: ArrayBuffer;
  width: number;
  height: number;
  /** Last known panel bounds in this frame's pixels, to search first. */
  hint?: { x0: number; y0: number; x1: number; y1: number } | null;
}

export interface LivestockReaderResponse {
  id: number;
  read: LivestockPanelRead | null;
  /** AGE and OVERALL from the same frame, when the panel has them. */
  conditions: PanelConditions | null;
  /** Which animal, from the header portrait. */
  portrait: PortraitMatch | null;
  /** The animal's in-game name, when it could be read. */
  name: string | null;
  elapsedMs: number;
  /** True when the hint region was enough. */
  fromHint: boolean;
}

function crop(
  data: Uint8ClampedArray,
  width: number,
  height: number,
  box: { x0: number; y0: number; x1: number; y1: number }
) {
  const x0 = Math.max(0, Math.floor(box.x0));
  const y0 = Math.max(0, Math.floor(box.y0));
  const x1 = Math.min(width, Math.ceil(box.x1));
  const y1 = Math.min(height, Math.ceil(box.y1));
  const w = x1 - x0;
  const h = y1 - y0;
  if (w < 40 || h < 20) return null;
  const out = new Uint8ClampedArray(w * h * 4);
  for (let y = 0; y < h; y++) {
    const start = ((y0 + y) * width + x0) * 4;
    out.set(data.subarray(start, start + w * 4), y * w * 4);
  }
  return { image: { data: out, width: w, height: h }, x0, y0 };
}

function shift(read: LivestockPanelRead, dx: number, dy: number): LivestockPanelRead {
  const move = <T extends { cx: number; cy: number }>(b: T): T => ({ ...b, cx: b.cx + dx, cy: b.cy + dy });
  const moveRow = (row: LivestockRowRead): LivestockRowRead => ({ ...row, cy: row.cy + dy, badges: row.badges.map(move) });
  return {
    ...read,
    top: moveRow(read.top),
    bottom: read.bottom ? moveRow(read.bottom) : null,
    bounds: {
      x0: read.bounds.x0 + dx,
      y0: read.bounds.y0 + dy,
      x1: read.bounds.x1 + dx,
      y1: read.bounds.y1 + dy
    }
  };
}

self.onmessage = (event: MessageEvent<LivestockReaderRequest>) => {
  const { id, buffer, width, height, hint } = event.data;
  const started = performance.now();
  const data = new Uint8ClampedArray(buffer);
  let read: LivestockPanelRead | null = null;
  let fromHint = false;

  if (hint) {
    // Generous margin: the player's view drifts between frames.
    const mw = (hint.x1 - hint.x0) * 0.6;
    const mh = (hint.y1 - hint.y0) * 1.2;
    const region = crop(data, width, height, {
      x0: hint.x0 - mw,
      y0: hint.y0 - mh,
      x1: hint.x1 + mw,
      y1: hint.y1 + mh
    });
    if (region) {
      const local = readLivestockPanel(region.image, { readMarkerDigit });
      if (local) {
        read = shift(local, region.x0, region.y0);
        fromHint = true;
      }
    }
  }

  if (!read) {
    read = readLivestockPanel({ data, width, height }, { readMarkerDigit });
  }

  const conditions = read ? readPanelConditions({ data, width, height }, read) : null;
  const portrait = read ? classifyPortrait({ data, width, height }, read) : null;
  const name = read ? readPanelName({ data, width, height }, read)?.text ?? null : null;
  const response: LivestockReaderResponse = { id, read, conditions, portrait, name, elapsedMs: performance.now() - started, fromHint };
  (self as unknown as Worker).postMessage(response);
};
