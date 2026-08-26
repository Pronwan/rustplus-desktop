import { SCANNER_CONFIG } from './scannerConfig.ts';

export class GeneImagePreprocessor {
  private static stitchedCanvas: HTMLCanvasElement | null = null;
  private static slotCanvases: HTMLCanvasElement[] = [];

  private static getStitchedCanvas(): HTMLCanvasElement {
    if (!this.stitchedCanvas) {
      this.stitchedCanvas = document.createElement('canvas');
    }
    return this.stitchedCanvas;
  }

  private static getSlotCanvas(index: number): HTMLCanvasElement {
    if (!this.slotCanvases[index]) {
      this.slotCanvases[index] = document.createElement('canvas');
    }
    return this.slotCanvases[index];
  }

  /**
   * Adaptive binarization that extracts bright text from colored badges or dark backgrounds.
   */
  public static binarizeBuffer(data: Uint8ClampedArray | number[]): void {
    let minLum = 255;
    let maxLum = 0;

    for (let i = 0; i < data.length; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const minVal = Math.min(r, g, b); // White text has high min(R,G,B)
      if (minVal < minLum) minLum = minVal;
      if (minVal > maxLum) maxLum = minVal;
    }

    const dynamicThreshold = maxLum - minLum > 35
      ? minLum + (maxLum - minLum) * 0.45
      : 110;

    for (let i = 0; i < data.length; i += 4) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];
      const minVal = Math.min(r, g, b);

      const isText = minVal >= dynamicThreshold;
      const val = isText ? 0 : 255;

      data[i] = val;
      data[i + 1] = val;
      data[i + 2] = val;
      data[i + 3] = 255;
    }
  }

  public static binarize(ctx: CanvasRenderingContext2D, width: number, height: number): void {
    const imgData = ctx.getImageData(0, 0, width, height);
    this.binarizeBuffer(imgData.data);
    ctx.putImageData(imgData, 0, 0);
  }

  /**
   * Stitches 6 clean, preprocessed, binarized gene glyphs into ONE single horizontal row image.
   * This allows Tesseract in PSM.SINGLE_LINE mode to recognize all 6 letters in ONE single pass in ~20ms!
   */
  public static prepareStitchedGeneStrip(
    source: CanvasImageSource,
    baseX: number,
    baseY: number,
    geneWidthPx: number,
    gapWidthPx: number,
    heightPx: number,
    scale = 3,
    glyphGap = 12,
    padding = 10
  ): HTMLCanvasElement {
    const canvas = this.getStitchedCanvas();
    const glyphW = Math.max(1, Math.round(geneWidthPx * scale));
    const glyphH = Math.max(1, Math.round(heightPx * scale));
    const totalW = padding * 2 + glyphW * 6 + glyphGap * 5;
    const totalH = padding * 2 + glyphH;

    canvas.width = totalW;
    canvas.height = totalH;
    const ctx = canvas.getContext('2d');
    if (!ctx) return canvas;

    // Pure white background
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(0, 0, totalW, totalH);
    ctx.imageSmoothingEnabled = true;
    ctx.imageSmoothingQuality = 'high';

    // Draw the 6 individual slot crops side-by-side with uniform clean spacing
    for (let slot = 0; slot < 6; slot++) {
      const srcSlotX = baseX + slot * (geneWidthPx + gapWidthPx);
      const destX = padding + slot * (glyphW + glyphGap);
      ctx.drawImage(source, srcSlotX, baseY, geneWidthPx, heightPx, destX, padding, glyphW, glyphH);
    }

    // Binarize the entire stitched row
    this.binarize(ctx, totalW, totalH);
    return canvas;
  }

  /**
   * Extracts and scales the 6 individual character slot crops.
   */
  public static prepareSlotCrops(
    source: CanvasImageSource,
    baseX: number,
    baseY: number,
    geneWidthPx: number,
    gapWidthPx: number,
    heightPx: number,
    scale = SCANNER_CONFIG.recognition.geneScale,
    padding = SCANNER_CONFIG.recognition.paddingPx
  ): HTMLCanvasElement[] {
    const results: HTMLCanvasElement[] = [];

    for (let slot = 0; slot < 6; slot++) {
      const slotCanvas = this.getSlotCanvas(slot);
      const slotX = baseX + slot * (geneWidthPx + gapWidthPx);
      const scaledW = Math.max(1, Math.round(geneWidthPx * scale));
      const scaledH = Math.max(1, Math.round(heightPx * scale));
      const paddedW = scaledW + padding * 2;
      const paddedH = scaledH + padding * 2;

      slotCanvas.width = paddedW;
      slotCanvas.height = paddedH;
      const ctx = slotCanvas.getContext('2d');
      if (ctx) {
        ctx.fillStyle = '#FFFFFF';
        ctx.fillRect(0, 0, paddedW, paddedH);
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(source, slotX, baseY, geneWidthPx, heightPx, padding, padding, scaledW, scaledH);
        this.binarize(ctx, paddedW, paddedH);
        results.push(slotCanvas);
      }
    }

    return results;
  }

  /**
   * Calculates a cheap 0.1ms activity score to determine if an ROI contains an active plant UI.
   */
  public static computeRegionActivityScore(data: Uint8ClampedArray): number {
    let whiteTextPixels = 0;
    let badgeColorPixels = 0;
    let darkBackgroundPixels = 0;
    let totalSamples = 0;

    for (let i = 0; i < data.length; i += 16) {
      totalSamples++;
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];

      const minVal = Math.min(r, g, b);
      const maxVal = Math.max(r, g, b);
      const sat = maxVal - minVal;

      if (minVal > 140) {
        whiteTextPixels++;
      } else if (sat > 45 && (r > 110 || g > 110)) {
        badgeColorPixels++;
      } else if (maxVal < 65) {
        darkBackgroundPixels++;
      }
    }

    if (totalSamples === 0) return 0;

    const whiteTextRatio = whiteTextPixels / totalSamples;
    const badgeRatio = badgeColorPixels / totalSamples;
    const darkRatio = darkBackgroundPixels / totalSamples;

    let score = 0;
    if (whiteTextRatio > 0.015 && (badgeRatio > 0.08 || darkRatio > 0.12)) {
      score = Math.min(1.0, (whiteTextRatio * 15) + (badgeRatio * 2) + (darkRatio * 0.5));
    }

    return Math.round(score * 100) / 100;
  }
}
