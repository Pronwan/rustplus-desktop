import { maskFromImage, extractGlyphFeatures, classifyGlyphFeatures } from './vision/glyphTemplates.ts';
import { DesktopSlotExtractor } from './DesktopSlotExtractor.ts';
import { SCANNER_CONFIG } from './scannerConfig.ts';

export interface DesktopTemplateResult {
  success: boolean;
  geneString: string;
  confidence: number;
  slotConfidences: number[];
  chromaticVerified: boolean;
  latencyMs: number;
}

const GREEN_GENES = new Set(['G', 'Y', 'H']);
const RED_GENES = new Set(['W', 'X']);

export class DesktopTemplateRecognizer {
  /**
   * High-speed in-memory template matching tailored for crisp digital desktop frames.
   * Runs in ~0.15ms to 0.3ms for all 6 slots combined.
   */
  public static recognizeFromRoi(
    roiData: Uint8ClampedArray,
    roiW: number,
    roiH: number,
    geneWPx: number,
    gapWPx: number
  ): DesktopTemplateResult | null {
    const startTime = performance.now();
    const slots = DesktopSlotExtractor.extractSlots(roiData, roiW, roiH, geneWPx, gapWPx);

    if (slots.length !== 6) return null;

    // Strict Anti-Terrain Guard:
    // In Rust, ALL 6 slots of a plant gene string sit inside distinct circular badges (Green or Red).
    // Terrain, rocks, cliffs, clouds, or ground NEVER contain 5+ distinct authentic badges.
    const confirmedBadges = slots.filter(s => s.dominantColor === 'green' || s.dominantColor === 'red').length;
    if (confirmedBadges < 5) {
      return null;
    }

    // Every plant slot must have confirmed white letter text strokes
    const slotsWithWhite = slots.filter(s => s.hasWhiteText).length;
    if (slotsWithWhite < 5) {
      return null;
    }

    const detectedLetters: string[] = [];
    const slotConfidences: number[] = [];
    let chromaticViolations = 0;

    for (let i = 0; i < 6; i++) {
      const slot = slots[i];
      if (!slot.hasWhiteText) {
        return null; // A real plant slot MUST contain white letter text
      }

      // Direct binary mask (bypasses maskFromImage reallocation)
      const mask = slot.mask || maskFromImage(slot.image);
      const features = extractGlyphFeatures(mask, slot.image.width, slot.image.height);

      if (!features || features.density < 0.05 || features.density > 0.85) {
        return null; // Slot empty, solid, or random terrain speck
      }

      // Fast Chromatic Pruning: In Rust, Green badges ONLY contain G, Y, H. Red badges ONLY contain W, X.
      // Filtering candidate templates by badge color speeds up matching and makes cross-color confusion impossible!
      const allowedGenes =
        slot.dominantColor === 'green'
          ? (['G', 'Y', 'H'] as const)
          : slot.dominantColor === 'red'
            ? (['W', 'X'] as const)
            : undefined;

      const match = classifyGlyphFeatures(features, allowedGenes);
      if (!match) {
        return null;
      }

      const gene = match.gene;
      const margin = match.margin;
      const distance = match.distance;

      // Distance gate for digital font matching (tolerant to subpixel antialiasing at 0.5 - 1.0 UI scale)
      if (distance > 0.47) {
        return null;
      }

      // Chromatic Guard: Red genes (W, X) cannot be in a confirmed green badge,
      // and Green genes (G, Y, H) cannot be in a confirmed red badge!
      if (slot.dominantColor === 'green' && RED_GENES.has(gene)) {
        return null;
      } else if (slot.dominantColor === 'red' && GREEN_GENES.has(gene)) {
        return null;
      }

      const slotConf = Math.min(
        100,
        Math.max(50, Math.round(100 - distance * 120 + margin * 30))
      );

      // Every single slot must meet minimum individual gene confidence
      if (slotConf < SCANNER_CONFIG.recognition.minGeneConfidence) {
        return null;
      }

      detectedLetters.push(gene);
      slotConfidences.push(slotConf);
    }

    const meanConfidence = Math.round(
      slotConfidences.reduce((a, b) => a + b, 0) / 6
    );

    const latencyMs = performance.now() - startTime;
    const geneString = detectedLetters.join('');

    console.log(`[Scanner FastPath] Recognized: ${geneString} (${meanConfidence}% conf in ${latencyMs.toFixed(2)}ms)`);

    return {
      success: true,
      geneString,
      confidence: meanConfidence,
      slotConfidences,
      chromaticVerified: chromaticViolations === 0,
      latencyMs
    };
  }
}
