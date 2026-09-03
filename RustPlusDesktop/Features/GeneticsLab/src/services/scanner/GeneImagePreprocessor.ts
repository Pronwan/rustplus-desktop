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

  public static binarizeBuffer(data: Uint8ClampedArray | number[]): void {
    this.binarizeSlotBuffer(data as any);
  }

  /**
   * Binarizes slot pixel buffer into pure black glyph text on pure white background.
   * White text glyphs -> 0 (Black)
   * Colored badge circle or dark background -> 255 (White)
   * Uses luminance vs saturation analysis that functions accurately across all UI scales (0.5 to 1.0+).
   */
  public static binarizeSlotBuffer(data: Uint8ClampedArray): void {
    let minScore = 255;
    let maxScore = 0;
    const pixelScores = new Float32Array(data.length / 4);

    // Pass 1: Compute White Ink Score = Luminance - (Saturation * 0.7)
    // White text glyphs: high luminance, near-zero saturation -> score 160-255
    // Green/Red badges: high saturation -> score 20-75
    // Dark background: low luminance -> score 10-40
    for (let i = 0, p = 0; i < data.length; i += 4, p++) {
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];

      const lum = (r * 299 + g * 587 + b * 114) / 1000;
      const sat = Math.max(r, g, b) - Math.min(r, g, b);
      const score = Math.max(0, lum - sat * 0.72);

      pixelScores[p] = score;
      if (score < minScore) minScore = score;
      if (score > maxScore) maxScore = score;
    }

    const scoreRange = maxScore - minScore;
    const threshold = scoreRange > 20
      ? minScore + scoreRange * 0.44
      : 70;

    for (let i = 0, p = 0; i < data.length; i += 4, p++) {
      const score = pixelScores[p];
      const isText = score >= threshold && score > 50;
      const val = isText ? 0 : 255; // 0 = Black text, 255 = White page background

      data[i] = val;
      data[i + 1] = val;
      data[i + 2] = val;
      data[i + 3] = 255;
    }
  }

  /**
   * Stitches 6 clean, preprocessed, binarized gene glyphs into ONE single horizontal row image.
   * Produces a clean white strip with 6 black characters, perfectly formatted for Tesseract PSM.SINGLE_LINE.
   *
   * KEY INNOVATION FOR SMALL UI SCALES (0.5 to 0.8):
   * Binarizes each slot at 1x native resolution FIRST to protect 1-pixel strokes against bilinear blur,
   * then upscales the crisp binarized glyph to Tesseract's optimal font height (~72px slot).
   */
  public static prepareStitchedGeneStrip(
    source: CanvasImageSource,
    baseX: number,
    baseY: number,
    geneWidthPx: number,
    gapWidthPx: number,
    heightPx: number,
    scale?: number,
    glyphGap = 16,
    padding = 14
  ): HTMLCanvasElement {
    const canvas = this.getStitchedCanvas();
    const targetSlotH = 72;
    const computedScale = scale ?? Math.max(3, Math.min(6, Math.round(targetSlotH / Math.max(1, heightPx))));
    const glyphW = Math.max(1, Math.round(geneWidthPx * computedScale));
    const glyphH = Math.max(1, Math.round(heightPx * computedScale));
    const totalW = padding * 2 + glyphW * 6 + glyphGap * 5;
    const totalH = padding * 2 + glyphH;

    canvas.width = totalW;
    canvas.height = totalH;
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) return canvas;

    // Fill entire strip with pure white paper background
    ctx.fillStyle = '#FFFFFF';
    ctx.fillRect(0, 0, totalW, totalH);

    // Slot canvases for 1x native crop and binarization
    const nativeCanvas = this.getSlotCanvas(0);
    nativeCanvas.width = geneWidthPx;
    nativeCanvas.height = heightPx;
    const nativeCtx = nativeCanvas.getContext('2d', { willReadFrequently: true });

    for (let slot = 0; slot < 6; slot++) {
      const srcSlotX = baseX + slot * (geneWidthPx + gapWidthPx);
      const destX = padding + slot * (glyphW + glyphGap);

      if (nativeCtx) {
        // Step 1: Capture native 1x crop (zero blur)
        nativeCtx.fillStyle = '#FFFFFF';
        nativeCtx.fillRect(0, 0, geneWidthPx, heightPx);
        nativeCtx.drawImage(source, srcSlotX, baseY, geneWidthPx, heightPx, 0, 0, geneWidthPx, heightPx);
        
        // Step 2: Binarize at 1x native resolution so 1px strokes are completely preserved
        const slotImgData = nativeCtx.getImageData(0, 0, geneWidthPx, heightPx);
        this.binarizeSlotBuffer(slotImgData.data);
        nativeCtx.putImageData(slotImgData, 0, 0);

        // Step 3: Upscale the crisp black-on-white glyph onto master strip
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'medium';
        ctx.drawImage(nativeCanvas, 0, 0, geneWidthPx, heightPx, destX, padding, glyphW, glyphH);
      }
    }

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
    const targetH = 64;
    const computedScale = Math.max(3, Math.min(6, Math.round(targetH / Math.max(1, heightPx))));
    const scaledW = Math.max(1, Math.round(geneWidthPx * computedScale));
    const scaledH = Math.max(1, Math.round(heightPx * computedScale));

    const nativeCanvas = this.getSlotCanvas(1);
    nativeCanvas.width = geneWidthPx;
    nativeCanvas.height = heightPx;
    const nativeCtx = nativeCanvas.getContext('2d', { willReadFrequently: true });

    for (let slot = 0; slot < 6; slot++) {
      const slotCanvas = this.getSlotCanvas(slot + 2);
      const slotX = baseX + slot * (geneWidthPx + gapWidthPx);
      const paddedW = scaledW + padding * 2;
      const paddedH = scaledH + padding * 2;

      slotCanvas.width = paddedW;
      slotCanvas.height = paddedH;
      const ctx = slotCanvas.getContext('2d', { willReadFrequently: true });
      if (ctx && nativeCtx) {
        ctx.fillStyle = '#FFFFFF';
        ctx.fillRect(0, 0, paddedW, paddedH);

        // 1x crop and binarize
        nativeCtx.drawImage(source, slotX, baseY, geneWidthPx, heightPx, 0, 0, geneWidthPx, heightPx);
        const imgData = nativeCtx.getImageData(0, 0, geneWidthPx, heightPx);
        this.binarizeSlotBuffer(imgData.data);
        nativeCtx.putImageData(imgData, 0, 0);

        // Scale up
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'medium';
        ctx.drawImage(nativeCanvas, 0, 0, geneWidthPx, heightPx, padding, padding, scaledW, scaledH);

        results.push(slotCanvas);
      }
    }

    return results;
  }

  /**
   * Fast, reliable activity score calculator tailored for all UI scales (0.5 to 1.0+).
   */
  public static computeRegionActivityScore(data: Uint8ClampedArray): number {
    let whiteTextPixels = 0;
    let badgeColorPixels = 0;
    let darkBackgroundPixels = 0;
    let totalSamples = 0;

    // Sample every pixel for small ROIs (ensures 1px text at 0.5/0.7 scale is never missed)
    for (let i = 0; i < data.length; i += 4) {
      totalSamples++;
      const r = data[i];
      const g = data[i + 1];
      const b = data[i + 2];

      const minVal = Math.min(r, g, b);
      const maxVal = Math.max(r, g, b);
      const sat = maxVal - minVal;
      const lum = (r * 299 + g * 587 + b * 114) / 1000;
      const whiteScore = lum - sat * 0.75;

      const isGreenBadge = g > 70 && g >= r + 10 && g >= b + 18;
      const isRedBadge = r > 90 && r >= g + 25 && r >= b + 25;

      if (whiteScore > 105 || (b > 90 && lum > 125 && sat < 45)) {
        whiteTextPixels++;
      } else if (isGreenBadge || isRedBadge) {
        badgeColorPixels++;
      } else if (maxVal < 65) {
        darkBackgroundPixels++;
      }
    }

    if (totalSamples === 0) return 0;

    const whiteTextRatio = whiteTextPixels / totalSamples;
    const badgeRatio = badgeColorPixels / totalSamples;
    const darkRatio = darkBackgroundPixels / totalSamples;

    // Authentic gene tooltip requires both vibrant circular badges and white text
    let score = 0;
    if (badgeRatio >= 0.05 && whiteTextRatio >= 0.015) {
      score = Math.min(1.0, (whiteTextRatio * 16) + (badgeRatio * 4) + (darkRatio * 0.4));
    }

    return Math.round(score * 100) / 100;
  }
}
