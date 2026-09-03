import { RasterImage } from './scannerTypes.ts';

export interface ExtractedSlot {
  image: RasterImage;
  mask: Uint8Array;
  dominantColor: 'green' | 'red' | 'unknown';
  hasWhiteText: boolean;
  whitePixelCount: number;
  totalPixels: number;
}

export class DesktopSlotExtractor {
  /**
   * Directly extracts and dynamically upscales gene badge slots in memory.
   * Uses blue-channel contrast to preserve thin font strokes and antialiasing at small UI scales (0.5 to 1.0).
   */
  public static extractSlots(
    roiData: Uint8ClampedArray,
    roiW: number,
    roiH: number,
    geneWPx: number,
    gapWPx: number
  ): ExtractedSlot[] {
    const slots: ExtractedSlot[] = [];
    const stride = roiW * 4;

    for (let slotIdx = 0; slotIdx < 6; slotIdx++) {
      const startX = Math.round(slotIdx * (geneWPx + gapWPx));
      const slotW = Math.max(1, Math.min(geneWPx, roiW - startX));
      const slotH = roiH;

      if (slotW <= 0 || slotH <= 0) continue;

      let greenCount = 0;
      let redCount = 0;
      let whiteCount = 0;
      let minScore = 255;
      let maxScore = 0;

      // Pass 1: Measure contrast range and chromatic colors
      for (let y = 0; y < slotH; y++) {
        const srcRowOffset = y * stride + startX * 4;
        for (let x = 0; x < slotW; x++) {
          const srcIdx = srcRowOffset + x * 4;
          const r = roiData[srcIdx];
          const g = roiData[srcIdx + 1];
          const b = roiData[srcIdx + 2];
          const lum = (r * 299 + g * 587 + b * 114) / 1000;

          // Blue channel contrast: Rust red/green badges and swamp terrain have low blue (b < 65),
          // while white gene letters have high blue (b > 90) and high luminance.
          const textScore = b * 1.4 + lum * 0.6;

          if (textScore < minScore) minScore = textScore;
          if (textScore > maxScore) maxScore = textScore;

          // Rust badge is a circle centered in the slot!
          // ONLY count badge colors inside the circular badge zone
          const relX = (x + 0.5) / slotW - 0.5;
          const relY = (y + 0.5) / slotH - 0.5;
          const distFromCenter = Math.hypot(relX, relY);

          if (distFromCenter <= 0.58) {
            // Authentic Green badge: #659A2B / olive-green planter badge
            if (g > 68 && g >= r + 8 && g > b + 16) {
              greenCount++;
            }
            // Authentic Red badge: #B44437 / reddish planter badge
            else if (r > 88 && r >= g + 20 && r >= b + 20) {
              redCount++;
            }
          }

          const minVal = Math.min(r, g, b);
          const maxVal = Math.max(r, g, b);
          if (b > 85 && lum > 120 && (maxVal - minVal) < 55) {
            whiteCount++;
          }
        }
      }

      const totalPixels = slotW * slotH;
      const minBadgePixels = Math.max(6, Math.round(totalPixels * 0.08));
      const dominantColor: 'green' | 'red' | 'unknown' =
        greenCount > redCount && greenCount >= minBadgePixels
          ? 'green'
          : redCount > greenCount && redCount >= minBadgePixels
            ? 'red'
            : 'unknown';

      // Dynamic threshold for white text binarization
      const scoreRange = maxScore - minScore;
      const textThreshold = scoreRange > 18
        ? minScore + scoreRange * 0.38
        : 65;

      // Pass 2: Scale up to at least ~52px height for accurate feature zoning
      const targetSize = 52;
      const scale = Math.max(2, Math.min(5, Math.round(targetSize / Math.max(1, slotH))));
      const destW = slotW * scale;
      const destH = slotH * scale;
      const slotBuffer = new Uint8ClampedArray(destW * destH * 4);
      const maskBuffer = new Uint8Array(destW * destH);

      for (let y = 0; y < slotH; y++) {
        const srcRowOffset = y * stride + startX * 4;
        for (let x = 0; x < slotW; x++) {
          const srcIdx = srcRowOffset + x * 4;
          const r = roiData[srcIdx];
          const g = roiData[srcIdx + 1];
          const b = roiData[srcIdx + 2];

          const lum = (r * 299 + g * 587 + b * 114) / 1000;
          const textScore = b * 1.4 + lum * 0.6;

          // Ignore outer 8% margins to discard any hyphen/dash between badges
          const isMargin = x < slotW * 0.08 || x > slotW * 0.92;
          // Clean white letter extraction preserving antialiased strokes
          const isText = !isMargin && textScore >= textThreshold && (b > 75 || lum > 135);
          const val = isText ? 0 : 255;
          const maskVal = isText ? 1 : 0;

          // Duplicate into scale x scale block
          for (let dy = 0; dy < scale; dy++) {
            const destRowOffset = (y * scale + dy) * destW;
            for (let dx = 0; dx < scale; dx++) {
              const pIdx = destRowOffset + (x * scale + dx);
              maskBuffer[pIdx] = maskVal;
              const destIdx = pIdx * 4;
              slotBuffer[destIdx] = val;
              slotBuffer[destIdx + 1] = val;
              slotBuffer[destIdx + 2] = val;
              slotBuffer[destIdx + 3] = 255;
            }
          }
        }
      }

      slots.push({
        image: {
          data: slotBuffer,
          width: destW,
          height: destH
        },
        mask: maskBuffer,
        dominantColor,
        hasWhiteText: whiteCount > totalPixels * 0.02,
        whitePixelCount: whiteCount,
        totalPixels
      });
    }

    return slots;
  }
}
