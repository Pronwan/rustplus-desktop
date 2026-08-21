import { GeneRecognitionResult, RasterImage } from '../scannerTypes.ts';
import { classifyGlyphImage } from './glyphTemplates.ts';

/**
 * Reads a six-gene row by classifying each slot against the five gene templates.
 *
 * This replaces Tesseract on the camera path. The locator already knows exactly where each
 * badge is, so there is nothing to segment: six slots in, six answers out. A text engine
 * had to infer the line structure itself and would return five characters for a six-gene
 * row, or none at all, and cost tens of milliseconds doing it.
 */

export interface TemplateRecognitionOptions {
  /** How much better the winning template must be than the runner-up, 0..1. */
  minMargin: number;
  /** Worst tolerated zoning distance to the winning template. */
  maxDistance: number;
}

export const DEFAULT_TEMPLATE_OPTIONS: TemplateRecognitionOptions = {
  // Testing on distorted glyphs put correct matches at 0.46 and above, and every incorrect
  // match at 0.02 or below, so this sits in an empty band between the two.
  minMargin: 0.2,
  maxDistance: 0.4
};

export type GlyphRowRecognizer = (slotImages: RasterImage[]) => GeneRecognitionResult | null;

export function recognizeGenesByTemplate(
  slotImages: RasterImage[],
  options: TemplateRecognitionOptions = DEFAULT_TEMPLATE_OPTIONS
): GeneRecognitionResult | null {
  if (slotImages.length !== 6) return null;

  const letters: string[] = [];
  let marginSum = 0;

  for (const image of slotImages) {
    const match = classifyGlyphImage(image);
    // Any unreadable slot fails the whole row. A five-gene answer is never useful, and
    // guessing the sixth is exactly how a wrong clone gets saved.
    if (!match) return null;
    if (match.margin < options.minMargin) return null;
    if (match.distance > options.maxDistance) return null;

    letters.push(match.gene);
    marginSum += match.margin;
  }

  const meanMargin = marginSum / slotImages.length;

  return {
    geneString: letters.join(''),
    // Mapped so a bare pass lands just above the camera confidence floor and a clean read
    // lands near the top. Certainty about the result still comes from repeated agreement.
    confidence: Math.min(100, Math.round(45 + meanMargin * 60)),
    rawText: letters.join('')
  };
}
