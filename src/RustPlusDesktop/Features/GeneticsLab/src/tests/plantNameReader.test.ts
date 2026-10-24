import { describe, expect, it } from 'vitest';
import type { RasterImage } from '../services/scanner/scannerTypes.ts';
import { plantFromTitle, plantNameRect, readPlantName, searchPlantTitle } from '../services/scanner/plantNameReader.ts';

async function loadFixture(name: string, width: number, height: number): Promise<RasterImage> {
  const fsName = 'node:fs';
  const zlibName = 'node:zlib';
  const { readFileSync } = await import(fsName);
  const { gunzipSync } = await import(zlibName);
  const raw: Uint8Array = gunzipSync(readFileSync(new URL(`./fixtures/${name}.rgba.gz`, import.meta.url)));
  return { data: new Uint8ClampedArray(raw), width, height };
}

describe('plantFromTitle', () => {
  it('reads single-word plants and berry colours', () => {
    expect(plantFromTitle(['HEMP'])).toBe('hemp');
    expect(plantFromTitle(['GREEN', 'BERRY', 'CLONE'])).toBe('green-berry');
    expect(plantFromTitle(['YELLOW', 'BERRY', 'SEED'])).toBe('yellow-berry');
    expect(plantFromTitle(['PUMPKIN', 'CLONE'])).toBe('pumpkin');
    expect(plantFromTitle(['SUNFLOWER'])).toBe('sunflower');
  });

  it('forgives one misread letter in longer words', () => {
    expect(plantFromTitle(['HEMR'])).toBe('hemp');
    expect(plantFromTitle(['GREEN', 'BERPY'])).toBe('green-berry');
    expect(plantFromTitle(['P0TATO'.replace('0', 'O')])).toBe('potato');
  });

  it('reads through small-text look-alikes and run-together words', () => {
    expect(plantFromTitle(['YELLOW', 'BEPPY'])).toBe('yellow-berry'); // R read as P
    expect(plantFromTitle(['BLUE', 'BERRYCLDNE'])).toBe('blue-berry'); // "BERRY CLONE" merged
    expect(plantFromTitle(['PUMPKINCLONE'])).toBe('pumpkin');
    expect(plantFromTitle(['HE', 'MP'])).toBe('hemp'); // a wide letter gap split the word
  });

  it('refuses what it cannot place', () => {
    expect(plantFromTitle(['BLACK', 'BERRY', 'CLONE'])).toBeNull();
    expect(plantFromTitle(['BERRY'])).toBeNull();
    expect(plantFromTitle(['GENETICS'])).toBeNull();
    expect(plantFromTitle([])).toBeNull();
  });
});

describe('plantNameRect', () => {
  it('scales with the gene pitch, so it follows the in-game UI scale', () => {
    const small = plantNameRect({ x: 100, y: 100, pitch: 30 }, 'planter');
    const large = plantNameRect({ x: 100, y: 100, pitch: 60 }, 'planter');
    expect(large.x1 - large.x0).toBeCloseTo(2 * (small.x1 - small.x0));
    expect(100 - large.y0).toBeCloseTo(2 * (100 - small.y0));
  });
});

// Real captures at different resolutions and in-game UI scales (gene pitch 27-73 px),
// cropped to the panel. `row` is the first gene slot's centre and the slot pitch.
const CAPTURES = [
  { name: 'plant-planter-hemp-a', width: 628, height: 200, row: { x: 187, y: 153, pitch: 72.8 }, type: 'planter', plant: 'hemp' },
  { name: 'plant-planter-hemp-b', width: 500, height: 160, row: { x: 149, y: 125, pitch: 61.5 }, type: 'planter', plant: 'hemp' },
  { name: 'plant-planter-hemp-c', width: 380, height: 120, row: { x: 113.5, y: 96, pitch: 46.1 }, type: 'planter', plant: 'hemp' },
  { name: 'plant-planter-yellow-berry', width: 320, height: 110, row: { x: 96, y: 85, pitch: 38.6 }, type: 'planter', plant: 'yellow-berry' },
  { name: 'plant-inventory-green-berry', width: 640, height: 250, row: { x: 171, y: 234, pitch: 29.3 }, type: 'inventory', plant: 'green-berry' },
  { name: 'plant-inventory-blue-berry', width: 640, height: 240, row: { x: 205.5, y: 215.5, pitch: 27.3 }, type: 'inventory', plant: 'blue-berry' },
  // Planter panels over bright terrain seen through the translucent panel, UI scale 1.0 and 0.7.
  { name: 'plant-planter-hemp-ui10-12', width: 480, height: 185, row: { x: 99, y: 142, pitch: 61.6 }, type: 'planter', plant: 'hemp' },
  { name: 'plant-planter-orchid-ui10-13', width: 480, height: 185, row: { x: 99, y: 142, pitch: 61.6 }, type: 'planter', plant: 'orchid' },
  { name: 'plant-planter-wheat-ui10-14', width: 480, height: 185, row: { x: 99, y: 142, pitch: 61.6 }, type: 'planter', plant: 'wheat' },
  { name: 'plant-planter-green-berry-ui10-15', width: 480, height: 185, row: { x: 99, y: 142, pitch: 61.6 }, type: 'planter', plant: 'green-berry' },
  { name: 'plant-planter-blue-berry-ui10-16', width: 480, height: 185, row: { x: 99, y: 142, pitch: 61.6 }, type: 'planter', plant: 'blue-berry' },
  { name: 'plant-planter-rose-ui07-22', width: 337, height: 130, row: { x: 70, y: 100, pitch: 43.2 }, type: 'planter', plant: 'rose' },
  { name: 'plant-planter-hemp-ui07-23', width: 337, height: 130, row: { x: 70, y: 100, pitch: 43.2 }, type: 'planter', plant: 'hemp' },
  { name: 'plant-planter-red-berry-ui07-24', width: 337, height: 130, row: { x: 70, y: 100, pitch: 43.2 }, type: 'planter', plant: 'red-berry' },
  { name: 'plant-planter-red-berry-ui07-25', width: 337, height: 130, row: { x: 70, y: 100, pitch: 43.2 }, type: 'planter', plant: 'red-berry' },
  { name: 'plant-planter-sunflower-ui07-26', width: 337, height: 130, row: { x: 70, y: 100, pitch: 43.2 }, type: 'planter', plant: 'sunflower' }
] as const;

describe('readPlantName on captures', () => {
  for (const capture of CAPTURES) {
    it(`reads ${capture.plant} from ${capture.name} (pitch ${capture.row.pitch})`, async () => {
      const image = await loadFixture(capture.name, capture.width, capture.height);
      const read = readPlantName(image, capture.row, capture.type);
      expect(read?.plant, read?.text).toBe(capture.plant);
    });
  }

  it('tells R from P (the leg), even at UI scale 0.7', async () => {
    for (const [name, width, height, row, expected] of [
      ['plant-planter-green-berry-ui10-15', 480, 185, { x: 99, y: 142, pitch: 61.6 }, 'GREEN BERRY'],
      ['plant-planter-sunflower-ui07-26', 337, 130, { x: 70, y: 100, pitch: 43.2 }, 'SUNFLOWER'],
      ['plant-planter-red-berry-ui07-24', 337, 130, { x: 70, y: 100, pitch: 43.2 }, 'BERRY']
    ] as const) {
      const read = readPlantName(await loadFixture(name, width, height), row, 'planter');
      expect(read?.text ?? '').toContain(expected);
    }
  });

  it('still reads a capture shrunk to half size (a smaller UI scale, blurrier text)', async () => {
    const source = await loadFixture('plant-planter-hemp-a', 628, 200);
    const w = 314, h = 100;
    const data = new Uint8ClampedArray(w * h * 4);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) for (let c = 0; c < 4; c++) {
      let sum = 0;
      for (let dy = 0; dy < 2; dy++) for (let dx = 0; dx < 2; dx++) sum += source.data[((y * 2 + dy) * 628 + x * 2 + dx) * 4 + c];
      data[(y * w + x) * 4 + c] = sum / 4;
    }
    const read = readPlantName({ data, width: w, height: h }, { x: 93.5, y: 76.5, pitch: 36.4 }, 'planter');
    expect(read?.plant).toBe('hemp');
  });

  it('reads nothing where there is no title', async () => {
    const image = await loadFixture('plant-planter-hemp-a', 628, 200);
    // Pretend the row is far lower: the band above it is empty panel and gene badges.
    expect(readPlantName(image, { x: 187, y: 195, pitch: 30 }, 'planter')).toBeNull();
  });

  it('finds a title that sits off the measured band (UI scale moves it relative to the genes)', async () => {
    const inventory = await loadFixture('plant-inventory-green-berry', 640, 250);
    const row = { x: 171, y: 234, pitch: 29.3 };
    for (const dy of [-2, -1, 1, 2]) {
      expect(searchPlantTitle(inventory, { ...row, y: row.y + dy * row.pitch }, 'inventory').read.plant).toBe('green-berry');
    }
    // The title starting further left than the band.
    expect(searchPlantTitle(inventory, { ...row, x: row.x + 1.5 * row.pitch }, 'inventory').read.plant).toBe('green-berry');

    const planter = await loadFixture('plant-planter-hemp-ui07-23', 337, 130);
    for (const dy of [-0.5, 0.5]) {
      expect(searchPlantTitle(planter, { x: 70, y: 100 + dy * 43.2, pitch: 43.2 }, 'planter').read.plant).toBe('hemp');
    }
  });

  it('searching more widely still reads nothing where there is no title', async () => {
    const image = await loadFixture('plant-planter-hemp-a', 628, 200);
    expect(searchPlantTitle(image, { x: 187, y: 195, pitch: 30 }, 'planter').read.plant).toBeNull();
  });
});
