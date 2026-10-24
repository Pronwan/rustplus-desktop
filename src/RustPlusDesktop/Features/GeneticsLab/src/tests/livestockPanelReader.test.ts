import { describe, it, expect } from 'vitest';
import { classifyBadgeColor, readLivestockPanel } from '../services/livestock/livestockPanelReader.ts';
import { readMarkerDigit } from '../services/livestock/markerDigit.ts';
import { encodeGeneRow } from '../domain/livestock/animal.ts';
import { RasterImage } from '../services/scanner/scannerTypes.ts';

/* ------------------------------------------------------------------ *
 * A stand-in for Rust's livestock gene panel
 *
 * Drawn at 4x and box-filtered down so badge edges and letters carry real antialiasing.
 * Letters are crude strokes on purpose: the reader must not depend on the typeface.
 * ------------------------------------------------------------------ */

type RGB = [number, number, number];

const PANEL: RGB = [55, 50, 40];
const COLOURS: Record<string, RGB> = {
  r: [190, 70, 46], // red gene badge
  g: [108, 152, 44], // green gene badge
  n: [128, 127, 126], // neutral gene badge: light grey in game
  p: [206, 104, 104], // pink marker
  b: [70, 118, 214] // blue marker
};
const WHITE: RGB = [240, 240, 240];

const SS = 4;

class Canvas {
  readonly w: number;
  readonly h: number;
  readonly px: Float32Array;
  constructor(w: number, h: number, fill: RGB) {
    this.w = w * SS;
    this.h = h * SS;
    this.px = new Float32Array(this.w * this.h * 3);
    for (let i = 0; i < this.w * this.h; i++) this.px.set(fill, i * 3);
  }
  set(x: number, y: number, c: RGB) {
    if (x < 0 || y < 0 || x >= this.w || y >= this.h) return;
    this.px.set(c, (y * this.w + x) * 3);
  }
  /** Coordinates in final pixels. */
  disc(cx: number, cy: number, r: number, c: RGB) {
    const R = r * SS;
    const X = cx * SS;
    const Y = cy * SS;
    for (let y = Math.floor(Y - R); y <= Math.ceil(Y + R); y++) {
      for (let x = Math.floor(X - R); x <= Math.ceil(X + R); x++) {
        if ((x + 0.5 - X) ** 2 + (y + 0.5 - Y) ** 2 <= R * R) this.set(x, y, c);
      }
    }
  }
  rect(x0: number, y0: number, x1: number, y1: number, c: RGB) {
    for (let y = Math.round(y0 * SS); y < Math.round(y1 * SS); y++) {
      for (let x = Math.round(x0 * SS); x < Math.round(x1 * SS); x++) this.set(x, y, c);
    }
  }
  ring(cx: number, cy: number, rx: number, ry: number, t: number, c: RGB) {
    for (let y = Math.floor((cy - ry - t) * SS); y <= Math.ceil((cy + ry + t) * SS); y++) {
      for (let x = Math.floor((cx - rx - t) * SS); x <= Math.ceil((cx + rx + t) * SS); x++) {
        const dx = (x / SS - cx) / rx;
        const dy = (y / SS - cy) / ry;
        const d = Math.sqrt(dx * dx + dy * dy);
        if (Math.abs(d - 1) * Math.min(rx, ry) <= t / 2) this.set(x, y, c);
      }
    }
  }
  toRaster(): RasterImage {
    const W = this.w / SS;
    const H = this.h / SS;
    const data = new Uint8ClampedArray(W * H * 4);
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const acc = [0, 0, 0];
        for (let sy = 0; sy < SS; sy++) {
          for (let sx = 0; sx < SS; sx++) {
            const i = ((y * SS + sy) * this.w + x * SS + sx) * 3;
            acc[0] += this.px[i];
            acc[1] += this.px[i + 1];
            acc[2] += this.px[i + 2];
          }
        }
        const o = (y * W + x) * 4;
        data[o] = acc[0] / (SS * SS);
        data[o + 1] = acc[1] / (SS * SS);
        data[o + 2] = acc[2] / (SS * SS);
        data[o + 3] = 255;
      }
    }
    return { data, width: W, height: H };
  }
}

/** Crude letter strokes inside a box of height `h` centred on (cx, cy). */
function drawMark(c: Canvas, mark: string, cx: number, cy: number, h: number) {
  const t = Math.max(1, h * 0.18);
  const w = h * 0.6;
  const x0 = cx - w / 2;
  const x1 = cx + w / 2;
  const y0 = cy - h / 2;
  const y1 = cy + h / 2;
  switch (mark) {
    case '0':
      c.ring(cx, cy, w / 2 - t / 2, h / 2 - t / 2, t, WHITE);
      break;
    case '1':
      c.rect(cx - t / 2, y0, cx + t / 2, y1, WHITE);
      break;
    case 'L':
      c.rect(x0, y0, x0 + t, y1, WHITE);
      c.rect(x0, y1 - t, x1, y1, WHITE);
      break;
    case 'F':
      c.rect(x0, y0, x0 + t, y1, WHITE);
      c.rect(x0, y0, x1, y0 + t, WHITE);
      c.rect(x0, cy - t / 2, x1 - t, cy + t / 2, WHITE);
      break;
    case 'H':
      c.rect(x0, y0, x0 + t, y1, WHITE);
      c.rect(x1 - t, y0, x1, y1, WHITE);
      c.rect(x0, cy - t / 2, x1, cy + t / 2, WHITE);
      break;
    case 'Y':
      c.rect(cx - t / 2, cy, cx + t / 2, y1, WHITE);
      c.rect(x0, y0, x0 + t, cy, WHITE);
      c.rect(x1 - t, y0, x1, cy, WHITE);
      c.rect(x0, cy - t / 2, x1, cy + t / 2, WHITE);
      break;
    case 'D':
    default:
      c.rect(x0, y0, x0 + t, y1, WHITE);
      c.ring(cx - w * 0.1, cy, w / 2, h / 2 - t / 2, t, WHITE);
      break;
  }
}

interface PanelSpec {
  /** Five gene codes (r/n/g) + marker colour (p/b) for each row. */
  top: string;
  bottom: string;
  topMark?: string;
  bottomMark?: string;
  /** Top-row badge diameter in final pixels. */
  diameter?: number;
  /** Frame size and panel origin. */
  frame?: { width: number; height: number; x: number; y: number; background?: RGB };
  clutter?: boolean;
}

function renderPanel(spec: PanelSpec): RasterImage {
  const D = spec.diameter ?? 18;
  const pitch = D * 1.3;
  const d2 = D * 0.66;
  const frame = spec.frame ?? { width: Math.round(pitch * 6 + D * 2), height: Math.round(D * 3.6), x: 0, y: 0 };
  const c = new Canvas(frame.width, frame.height, frame.background ?? PANEL);

  if (spec.clutter) {
    // Grass behind the panel and a line of HUD text: saturated green, and white letters on
    // an even pitch, the two things most likely to be mistaken for badges.
    c.rect(0, frame.height * 0.7, frame.width, frame.height, [70, 120, 30]);
    for (let i = 0; i < 14; i++) drawMark(c, 'H', 12 + i * D * 0.55, frame.height * 0.85, D * 0.35);
  }

  const panelW = pitch * 6 + D * 0.6;
  const panelH = D * 2.7;
  c.rect(frame.x, frame.y, frame.x + panelW, frame.y + panelH, PANEL);

  const left = frame.x + D * 0.8;
  const topY = frame.y + D * 0.85;
  const bottomY = topY + D * 1.15;
  const letters = ['D', 'L', 'Y', 'F', 'H'];
  for (let i = 0; i < 6; i++) {
    const x = left + i * pitch;
    c.disc(x, topY, D / 2, COLOURS[spec.top[i]]);
    drawMark(c, i < 5 ? letters[i] : spec.topMark ?? '0', x, topY, D * 0.44);
    c.disc(x, bottomY, d2 / 2, COLOURS[spec.bottom[i]]);
    drawMark(c, i < 5 ? letters[i] : spec.bottomMark ?? '1', x, bottomY, d2 * 0.44);
  }
  return c.toRaster();
}

/** The live panel: a label, then one row of six badges. */
function renderSingleRow(genes: string, marker: RGB, D = 26): RasterImage {
  const pitch = D * 1.25;
  const W = Math.round(D * 5 + pitch * 6);
  const H = Math.round(D * 2.2);
  const c = new Canvas(W, H, [64, 58, 50]);
  // "GENETICS" in small white caps, the thing most likely to be mistaken for a row.
  for (let i = 0; i < 8; i++) drawMark(c, 'H', D * 0.4 + i * D * 0.32, H / 2 - D * 0.15, D * 0.3);
  const fill: Record<string, RGB> = { r: [170, 71, 46], g: [138, 174, 72], n: [128, 127, 126] };
  const letters = ['D', 'L', 'Y', 'F', 'H'];
  const left = D * 4.2;
  for (let i = 0; i < 6; i++) {
    const x = left + i * pitch;
    c.disc(x, H / 2, D / 2, i < 5 ? fill[genes[i]] : marker);
    drawMark(c, i < 5 ? letters[i] : '1', x, H / 2, D * 0.46);
  }
  return c.toRaster();
}

function codes(spec: PanelSpec) {
  const read = readLivestockPanel(renderPanel(spec), { readMarkerDigit });
  expect(read).not.toBeNull();
  return [encodeGeneRow(read!.rows[0]), encodeGeneRow(read!.rows[1])];
}

describe('badge colour classification', () => {
  it('separates the gene colours from each other and from the marker colours', () => {
    expect(classifyBadgeColor(...COLOURS.r)).toBe('red');
    expect(classifyBadgeColor(...COLOURS.g)).toBe('green');
    expect(classifyBadgeColor(...COLOURS.n)).toBe('grey');
    expect(classifyBadgeColor(...COLOURS.p)).toBe('pink');
    expect(classifyBadgeColor(...COLOURS.b)).toBe('blue');
    expect(classifyBadgeColor(...PANEL)).toBe('grey');
  });

  it('matches colours sampled from the live game', () => {
    expect(classifyBadgeColor(128, 127, 126)).toBe('grey'); // neutral gene
    expect(classifyBadgeColor(138, 173, 75)).toBe('green');
    expect(classifyBadgeColor(170, 71, 46)).toBe('red');
    expect(classifyBadgeColor(93, 99, 174)).toBe('blue'); // marker 14
    expect(classifyBadgeColor(114, 90, 181)).toBe('purple'); // marker 6
    expect(classifyBadgeColor(93, 176, 162)).toBe('teal'); // marker 4
    expect(classifyBadgeColor(180, 89, 92)).toBe('pink'); // marker 0
  });
});

describe('livestock panel reader (synthetic)', () => {
  it('reads an all-red panel with its markers', () => {
    expect(codes({ top: 'rrrrrp', bottom: 'rrrrrb' })).toEqual(['rrrrr|0p', 'rrrrr|1b']);
  });

  it('reads mixed green, red and neutral badges', () => {
    expect(codes({ top: 'gnrgnp', bottom: 'rgnngb' })).toEqual(['gnrgn|0p', 'rgnng|1b']);
  });

  it('finds a row whose first badges are neutral and invisible to the colour pass', () => {
    expect(codes({ top: 'nnggrp', bottom: 'nnrrgb' })).toEqual(['nnggr|0p', 'nnrrg|1b']);
  });

  it('reads an all-neutral panel from the letters alone', () => {
    expect(codes({ top: 'nnnnnp', bottom: 'nnnnnb', diameter: 26 })).toEqual(['nnnnn|0p', 'nnnnn|1b']);
  });

  for (const diameter of [12, 16, 24, 36]) {
    it(`reads the panel with ${diameter}px badges`, () => {
      const [top, bottom] = codes({ top: 'grgrgp', bottom: 'rgrgrb', diameter });
      expect(top.slice(0, 5)).toBe('grgrg');
      expect(bottom.slice(0, 5)).toBe('rgrgr');
      expect(top[7]).toBe('p');
      expect(bottom[7]).toBe('b');
    });
  }

  it('finds the panel in a larger frame next to grass and HUD text', () => {
    const image = renderPanel({
      top: 'ggrnrp',
      bottom: 'rnggnb',
      diameter: 20,
      frame: { width: 420, height: 260, x: 150, y: 40, background: [92, 104, 118] },
      clutter: true
    });
    const read = readLivestockPanel(image, { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(encodeGeneRow(read!.rows[0])).toBe('ggrnr|0p');
    expect(encodeGeneRow(read!.rows[1])).toBe('rnggn|1b');
    expect(read!.bounds.x0).toBeGreaterThan(140);
    expect(read!.bounds.x0).toBeLessThan(175);
  });

  it('reads the live single-row panel, label and all', () => {
    const read = readLivestockPanel(renderSingleRow('nrgng', [114, 90, 181]), { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(read!.rows).toHaveLength(1);
    expect(encodeGeneRow(read!.rows[0])).toBe('nrgng|1v');
  });

  it('reads an all-neutral single row from its letters', () => {
    const read = readLivestockPanel(renderSingleRow('nnnnn', [93, 176, 162], 30));
    expect(read).not.toBeNull();
    expect(encodeGeneRow(read!.rows[0]).slice(0, 5)).toBe('nnnnn');
    expect(read!.rows[0].marker.color).toBe('teal');
  });

  it('does not mistake a plant genetics row for a livestock panel', () => {
    // One row of six badges, no second row: what a clone tooltip looks like.
    const c = new Canvas(200, 40, [26, 26, 26]);
    'GGYYWX'.split('').forEach((gene, i) => {
      c.disc(20 + i * 30, 20, 11, gene === 'W' || gene === 'X' ? COLOURS.r : COLOURS.g);
      drawMark(c, 'H', 20 + i * 30, 20, 9);
    });
    expect(readLivestockPanel(c.toRaster())).toBeNull();
  });

  it('returns null on an empty frame', () => {
    expect(readLivestockPanel(new Canvas(300, 200, [60, 60, 60]).toRaster())).toBeNull();
  });
});

/**
 * Real in-game pixels, cropped from players' screenshots and stored as gzipped RGBA. Loaded
 * through non-literal specifiers because the project carries no Node type definitions.
 */
async function loadFixture(name: string, width: number, height: number): Promise<RasterImage> {
  const fsName = 'node:fs';
  const zlibName = 'node:zlib';
  const { readFileSync } = await import(fsName);
  const { gunzipSync } = await import(zlibName);
  const raw: Uint8Array = gunzipSync(readFileSync(new URL(`./fixtures/${name}.rgba.gz`, import.meta.url)));
  return { data: new Uint8ClampedArray(raw), width, height };
}

describe('livestock panel reader (real capture)', () => {
  it('reads the live single-row panel at UI scale 0.7', async () => {
    // Winifred: D neutral, L green, Y neutral, F green, H neutral, marker 14 on blue.
    const read = readLivestockPanel(await loadFixture('livestock-winifred-ui07', 345, 68), { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(read!.rows).toHaveLength(1);
    expect(encodeGeneRow(read!.rows[0]).slice(0, 5)).toBe('ngngn');
    expect(read!.rows[0].marker.color).toBe('blue');
  });

  it('reads the live single-row panel at UI scale 1.0', async () => {
    // Desiree: D red, L red, Y green, F neutral, H neutral, marker 6 on purple.
    const read = readLivestockPanel(await loadFixture('livestock-desiree-ui10', 495, 85), { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(read!.rows).toHaveLength(1);
    expect(encodeGeneRow(read!.rows[0]).slice(0, 5)).toBe('rrgnn');
    expect(read!.rows[0].marker.color).toBe('purple');
  });

  it('reads a bull with a teal marker over open sky', async () => {
    // Nelson: D green, L neutral, Y green, F neutral, H red, marker 4 on teal.
    const read = readLivestockPanel(await loadFixture('livestock-nelson-ui10', 492, 82), { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(encodeGeneRow(read!.rows[0]).slice(0, 5)).toBe('gngnr');
    expect(read!.rows[0].marker.color).toBe('teal');
  });

  it('reads the earlier two-row panel, markers included', async () => {
    // A cow's two-row panel, every gene red, pink "0" over blue "1". Badges ~15px across.
    const read = readLivestockPanel(await loadFixture('livestock-panel-cow', 250, 85), { readMarkerDigit });
    expect(read).not.toBeNull();
    expect(read!.rows.map(encodeGeneRow)).toEqual(['rrrrr|0p', 'rrrrr|1b']);
  });
});

describe('marker digits', () => {
  function glyph(rows: string[]) {
    const height = rows.length;
    const width = rows[0].length;
    const mask = new Uint8Array(width * height);
    rows.forEach((row, y) => row.split('').forEach((ch, x) => (mask[y * width + x] = ch === '#' ? 1 : 0)));
    return { mask, width, height };
  }

  it('reads a thin and a bold zero, and a one', async () => {
    const { classifyMarkerGlyph } = await import('../services/livestock/markerDigit.ts');
    expect(classifyMarkerGlyph(glyph(['####', '#..#', '#..#', '#..#', '#..#', '#..#', '####', '.##.']))).toBe(0);
    expect(classifyMarkerGlyph(glyph(['.###.', '.###.', '#####', '##.##', '##.##', '##.##', '#####', '.###.', '.###.']))).toBe(0);
    expect(classifyMarkerGlyph(glyph(['##', '.#', '##', '##', '.#']))).toBe(1);
  });

  it('refuses a six and a nine rather than calling them zero', async () => {
    const { classifyMarkerGlyph } = await import('../services/livestock/markerDigit.ts');
    const six = ['.###.', '#....', '#....', '####.', '#...#', '#...#', '#...#', '.###.'];
    const nine = ['.###.', '#...#', '#...#', '#...#', '.####', '....#', '....#', '.###.'];
    expect(classifyMarkerGlyph(glyph(six))).toBeNull();
    expect(classifyMarkerGlyph(glyph(nine))).toBeNull();
  });
});
