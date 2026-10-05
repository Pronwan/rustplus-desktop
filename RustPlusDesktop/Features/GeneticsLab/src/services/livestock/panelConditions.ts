import { RasterImage } from '../scanner/scannerTypes.ts';
import { LivestockPanelRead } from './livestockPanelReader.ts';
import { NameRead, readAgeSeconds, readName, readPercent, textGlyphs } from './panelText.ts';

/**
 * The AGE and CONDITIONS > OVERALL values from the live animal panel.
 *
 * The panel is laid out on a fixed grid that scales with the UI, so once the gene badges are
 * found, everything else sits at a known offset in units of the badge pitch. Measured on
 * captures at UI scale 0.7 and 1.0, the offsets agree to within a few hundredths of a pitch:
 *
 *   AGE value       2.07 pitches below the badge row
 *   OVERALL value   7.20 pitches below
 *   value column    starts ~1.2 pitches right of the first badge's centre
 *
 * Only the live single-row panel has these rows; the earlier two-row panel does not.
 */

export interface PanelConditions {
  /** Age as shown, in seconds; null when it could not be read. */
  ageSeconds: number | null;
  /** CONDITIONS > OVERALL, 0..1; null when it could not be read. */
  overall: number | null;
}

/** The animal's name in the panel header, right of its portrait. */
export const NAME_BOX = { x0: -3.0, x1: 5.3, cy: -1.86, halfHeight: 0.34 };

export function readPanelName(image: RasterImage, read: LivestockPanelRead): NameRead | null {
  if (read.bottom) return null;
  const pitch = read.top.pitch;
  const x1 = read.top.badges[0].cx;
  const cy = read.top.cy + NAME_BOX.cy * pitch;
  const box = {
    x0: x1 + NAME_BOX.x0 * pitch,
    x1: x1 + NAME_BOX.x1 * pitch,
    y0: cy - NAME_BOX.halfHeight * pitch,
    y1: cy + NAME_BOX.halfHeight * pitch
  };
  if (box.y0 < 0 || box.x0 < 0) return null;
  return readName(textGlyphs(image, box));
}

export const PANEL_OFFSETS = {
  age: 2.07,
  overall: 7.2,
  valueStart: 1.0,
  valueEnd: 4.6,
  halfHeight: 0.3
};

function valueBox(read: LivestockPanelRead, rowOffset: number) {
  const pitch = read.top.pitch;
  const x1 = read.top.badges[0].cx;
  const cy = read.top.cy + rowOffset * pitch;
  return {
    x0: x1 + PANEL_OFFSETS.valueStart * pitch,
    x1: x1 + PANEL_OFFSETS.valueEnd * pitch,
    y0: cy - PANEL_OFFSETS.halfHeight * pitch,
    y1: cy + PANEL_OFFSETS.halfHeight * pitch
  };
}

export function readPanelConditions(image: RasterImage, read: LivestockPanelRead): PanelConditions {
  if (read.bottom) return { ageSeconds: null, overall: null };
  const ageBox = valueBox(read, PANEL_OFFSETS.age);
  const overallBox = valueBox(read, PANEL_OFFSETS.overall);
  const inFrame = (box: { y1: number }) => box.y1 <= image.height;
  return {
    ageSeconds: inFrame(ageBox) ? readAgeSeconds(textGlyphs(image, ageBox)) : null,
    overall: inFrame(overallBox) ? readPercent(textGlyphs(image, overallBox)) : null
  };
}
