import { describe, expect, it } from 'vitest';
import { DEFAULT_BADGE_FILTER_OPTIONS, detectBadgeCandidates } from '../services/scanner/vision/rasterOps.ts';
import { buildRowCandidates, DEFAULT_BUILD_CANDIDATES_OPTIONS } from '../services/scanner/vision/geometry.ts';
import { AutoCalibrator } from '../services/scanner/AutoCalibrator.ts';
import { DesktopTemplateRecognizer } from '../services/scanner/DesktopTemplateRecognizer.ts';
import { readPlantName } from '../services/scanner/plantNameReader.ts';

async function loadFixture(name: string, width: number, height: number) {
  const fsName = 'node:fs';
  const zlibName = 'node:zlib';
  const { readFileSync } = await import(fsName);
  const { gunzipSync } = await import(zlibName);
  const raw: Uint8Array = gunzipSync(readFileSync(new URL(`./fixtures/${name}.rgba.gz`, import.meta.url)));
  return { data: new Uint8ClampedArray(raw), width, height, scale: 1 };
}

/**
 * The crop framed in a few pixels of dirt. On screen the terrain carries on past the crop;
 * without the frame, the cut would open gaps in the ring of terrain around the panel.
 */
function framedInDirt(frame: { data: Uint8ClampedArray; width: number; height: number }, border = 4) {
  const width = frame.width + border * 2;
  const height = frame.height + border * 2;
  const data = new Uint8ClampedArray(width * height * 4);
  for (let i = 0; i < width * height; i++) data.set([149, 112, 84, 255], i * 4);
  for (let y = 0; y < frame.height; y++) {
    data.set(frame.data.subarray(y * frame.width * 4, (y + 1) * frame.width * 4), ((y + border) * width + border) * 4);
  }
  return { data, width, height, scale: 1, border };
}

// The options AutoCalibrator discovers rows with.
const CALIBRATION_ROW_OPTIONS = {
  ...DEFAULT_BUILD_CANDIDATES_OPTIONS,
  readingDirection: { x: 1, y: 0 },
  minDirectionalConfidence: 0.1,
  bandFactor: 0.9,
  maxSizeRatio: 2.2,
  minSpacingFactor: 0.45,
  maxSpacingFactor: 5.5,
  maxSpacingVariation: 0.55,
  paddingFactor: 0.05
};

// Planter panels from 1440p captures. Brown dirt and wood pass the red badge test, so the
// terrain around each panel is one masked ring around its dark body.
const CAPTURES = [
  // UI scale 0.7: small badges inside a panel the terrain encloses.
  { name: 'badges-planter-sunflower-ui07-terrain', width: 440, height: 240, center: { x: 253, y: 130 }, colors: 'rrgrgg' },
  { name: 'badges-planter-ui07-terrain', width: 400, height: 190, center: { x: 238, y: 114 }, colors: 'rggrrr' },
  // UI scale 1.0: red marks just above the row sit between badges in reading order.
  { name: 'badges-planter-ui10-marks', width: 676, height: 160, center: { x: 349, y: 80 }, colors: 'rggggr' }
];

describe('auto-calibration row discovery', () => {
  for (const capture of CAPTURES) {
    it(`finds the gene row in ${capture.name}`, async () => {
      const frame = framedInDirt(await loadFixture(capture.name, capture.width, capture.height));
      const components = detectBadgeCandidates(frame, undefined, { ...DEFAULT_BADGE_FILTER_OPTIONS, maxCandidates: 400 }, { width: 2560, height: 1440 });
      const [best] = buildRowCandidates(components, frame, frame.width, frame.height, CALIBRATION_ROW_OPTIONS);

      expect(best).toBeDefined();
      expect(Math.abs(best.center.x - frame.border - capture.center.x)).toBeLessThan(6);
      expect(Math.abs(best.center.y - frame.border - capture.center.y)).toBeLessThan(6);
      expect(best.members.map(m => m.color[0]).join('')).toBe(capture.colors);
    });
  }
});

// Calibrate, cut the region the way the scanner does, read the row, then the plant above it.
const PANELS = [
  { name: 'badges-planter-sunflower-ui07-terrain', width: 440, height: 240, genes: 'WXYXYY', plant: 'sunflower' },
  { name: 'badges-planter-ui07-terrain', width: 400, height: 190, genes: 'WGHXWW', plant: 'red-berry' },
  { name: 'badges-planter-yellow-berry-ui07-terrain', width: 380, height: 190, genes: 'XYHYWX', plant: 'yellow-berry' }
];

describe('auto-calibration end to end', () => {
  for (const panel of PANELS) {
    it(`calibrates on ${panel.name} and reads ${panel.genes} as ${panel.plant}`, async () => {
      // Wide enough that the row is no wider than half the frame, as on a real screen.
      const frame = framedInDirt(await loadFixture(panel.name, panel.width, panel.height), 200);
      const found = AutoCalibrator.findRegion(frame);
      if ('error' in found) throw new Error(found.error);

      const { region } = found;
      const W = frame.width;
      const H = frame.height;
      const xPx = Math.round(W * region.TOP_LEFT_X);
      const yPx = Math.round(H * region.TOP_LEFT_Y);
      const wPx = Math.round(W * region.WIDTH);
      const hPx = Math.ceil(H * region.WIDTH * region.HEIGHT_TO_WIDTH_RATIO);
      const geneWPx = Math.round(wPx * region.GENE_WIDTH_TO_WIDTH_RATIO);
      const roi = new Uint8ClampedArray(wPx * hPx * 4);
      for (let y = 0; y < hPx; y++) {
        roi.set(frame.data.subarray(((yPx + y) * W + xPx) * 4, ((yPx + y) * W + xPx + wPx) * 4), y * wPx * 4);
      }

      const read = DesktopTemplateRecognizer.recognizeFromRoi(roi, wPx, hPx, geneWPx, Math.max(0, (wPx - geneWPx * 6) / 5));
      expect(read?.geneString).toBe(panel.genes);

      const centers = read!.geometry!.centers;
      const row = {
        x: xPx + centers[0],
        y: yPx + read!.geometry!.centerY,
        pitch: (centers[centers.length - 1] - centers[0]) / (centers.length - 1)
      };
      expect(readPlantName(frame, row, 'planter')?.plant).toBe(panel.plant);
    });
  }
});
