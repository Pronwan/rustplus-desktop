import { RasterImage } from '../scannerTypes.ts';
import { rasterToCanvas, releaseRasterCanvases } from './frameGrabber.ts';

/**
 * Per-slot OCR as a second opinion on the camera path.
 *
 * Template matching is fast and cannot drop a glyph, but its references are rasterised from
 * stroke geometry rather than from Rust's actual typeface, so it can be confidently wrong
 * about a letter it has never really seen. Tesseract has the opposite profile: it knows a
 * great many typefaces, and it is slow. Running both and requiring them to agree turns two
 * mediocre readers into one that can accept on the very first frame, which is the only way
 * scanning a tray of clones is faster than typing the genes by hand.
 *
 * Single-character page segmentation is what makes this usable here. Line mode had to find
 * and split the text itself and returned five letters for a six-badge row; one image in, one
 * character out cannot do that.
 */
export interface CameraSlotOcr {
  warmup(): Promise<void>;
  /** One letter per slot, empty string where the slot could not be read. */
  readSlots(slotImages: RasterImage[]): Promise<string[]>;
  terminate(): Promise<void>;
}

export async function createTesseractSlotOcr(): Promise<CameraSlotOcr> {
  const { TesseractGeneRecognizer } = await import('../TesseractGeneRecognizer.ts');
  const engine = new TesseractGeneRecognizer();

  return {
    // Only the single-character worker matters here; the line worker is never used.
    warmup: () => engine.warmupSlotWorker(),

    async readSlots(slotImages: RasterImage[]): Promise<string[]> {
      const canvases: HTMLCanvasElement[] = [];
      for (let i = 0; i < slotImages.length; i++) {
        const canvas = rasterToCanvas(slotImages[i], i);
        // A missing canvas means no DOM to draw into, so there is nothing to read.
        if (!canvas) return slotImages.map(() => '');
        canvases.push(canvas);
      }

      return engine.readSlotLetters(canvases);
    },

    async terminate(): Promise<void> {
      releaseRasterCanvases();
      await engine.terminate();
    }
  };
}
