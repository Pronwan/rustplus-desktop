import { CameraSlotReport, GeneRecognitionResult, RasterImage } from '../scannerTypes.ts';
import { inspectGlyphImage } from './glyphTemplates.ts';

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

/**
 * Both gates are needed; each catches what the other lets through.
 *
 * Measured against glyphs put through every distortion a photographed monitor applies --
 * two rounds of dilation, erosion, speckle, and a blur stand-in -- correct matches stay
 * inside 0.09 distance and 0.60 margin. Pure noise lands at 0.27 distance but only 0.06
 * margin, so margin rejects it. Half a glyph, which is what a bad crop produces, can reach
 * 0.47 margin because the surviving half really does resemble one letter best; distance
 * rejects that at 0.25. The band between the two populations is empty.
 */
export const DEFAULT_TEMPLATE_OPTIONS: TemplateRecognitionOptions = {
  minMargin: 0.35,
  maxDistance: 0.15
};

export type GlyphRowRecognizer = (slotImages: RasterImage[]) => GeneRecognitionResult | null;

const round2 = (value: number): number => Math.round(value * 100) / 100;

/**
 * Classifies every slot and reports each one, including the ones that fail a gate.
 *
 * Recognition returns a single null for a row that any one slot spoiled, which says nothing
 * about which slot or which gate. This runs the same classification and keeps the evidence,
 * so the failing stage can be read off a device rather than inferred.
 */
export function inspectGeneRow(
  slotImages: RasterImage[],
  options: TemplateRecognitionOptions = DEFAULT_TEMPLATE_OPTIONS
): CameraSlotReport[] {
  return slotImages.map(image => {
    const { density, match, reject } = inspectGlyphImage(image);

    if (!match) {
      return { gene: null, density: round2(density), margin: 0, distance: 1, reject: reject ?? 'blank' };
    }

    const gated =
      reject ??
      (match.margin < options.minMargin
        ? 'margin'
        : match.distance > options.maxDistance
          ? 'distance'
          : null);

    return {
      gene: match.gene,
      density: round2(density),
      margin: round2(match.margin),
      distance: round2(match.distance),
      reject: gated
    };
  });
}

/** Builds a row result from slot reports, or null when any slot failed a gate. */
export function resultFromSlotReports(reports: CameraSlotReport[]): GeneRecognitionResult | null {
  if (reports.length !== 6) return null;

  // Any unreadable slot fails the whole row. A five-gene answer is never useful, and
  // guessing the sixth is exactly how a wrong clone gets saved.
  if (reports.some(report => report.reject !== null || !report.gene)) return null;

  const letters = reports.map(report => report.gene).join('');
  const meanMargin = reports.reduce((sum, report) => sum + report.margin, 0) / reports.length;

  return {
    geneString: letters,
    // Mapped so a bare pass lands just above the camera confidence floor and a clean read
    // lands near the top. Certainty about the result still comes from repeated agreement.
    confidence: Math.min(100, Math.round(45 + meanMargin * 60)),
    rawText: letters
  };
}

export function recognizeGenesByTemplate(
  slotImages: RasterImage[],
  options: TemplateRecognitionOptions = DEFAULT_TEMPLATE_OPTIONS
): GeneRecognitionResult | null {
  if (slotImages.length !== 6) return null;
  return resultFromSlotReports(inspectGeneRow(slotImages, options));
}
