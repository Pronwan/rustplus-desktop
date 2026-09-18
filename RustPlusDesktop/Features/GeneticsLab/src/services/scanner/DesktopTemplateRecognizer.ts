import {
  DesktopRowRead,
  DesktopSlotRead,
  readDesktopGeneRow
} from './vision/desktopRowReader.ts';

export type { DesktopRowRead, DesktopSlotRead } from './vision/desktopRowReader.ts';

export interface DesktopTemplateResult {
  success: boolean;
  geneString: string;
  confidence: number;
  slotConfidences: number[];
  /** Lowest confidence among the slots that produced a letter. */
  minSlotConfidence: number;
  /** How many of the six slots produced a letter. */
  resolvedCount: number;
  /** Letters for the slots that resolved, with a dot where one did not. */
  partial: string;
  /** Indices of the slots that abstained, for a second-opinion pass or a correction prompt. */
  unresolvedSlots: number[];
  chromaticVerified: boolean;
  /** Where the slot geometry came from. `calibration` means the badges were not found. */
  layoutSource: DesktopRowRead['layoutSource'];
  slots: DesktopSlotRead[];
  latencyMs: number;
}

/**
 * Recognises the six gene letters in a desktop ROI.
 *
 * The interesting change from the previous version is what happens when a slot is unclear.
 * That version had eight separate early returns, each of which discarded the whole row --
 * so a single marginal letter meant the scanner produced nothing at all, silently, and
 * because a tooltip is a still image it went on producing nothing for as long as the cursor
 * stayed put. This one always reports six verdicts. A row with one abstention still carries
 * five known letters and the index of the one that failed, which is enough for the service
 * to ask a second recogniser about that slot alone.
 */
export class DesktopTemplateRecognizer {
  public static recognizeFromRoi(
    roiData: Uint8ClampedArray,
    roiW: number,
    roiH: number,
    geneWPx: number,
    gapWPx: number,
    includeImages = false
  ): DesktopTemplateResult | null {
    const read = readDesktopGeneRow(
      roiData,
      roiW,
      roiH,
      { geneWidthPx: geneWPx, gapWidthPx: gapWPx },
      { includeImages }
    );

    // No badges means no row. Everything short of that is reported rather than discarded.
    if (read.reject) return null;

    const slotConfidences = read.slots.map(slot => slot.confidence);
    const unresolvedSlots = read.slots.filter(slot => !slot.gene || slot.reject).map(slot => slot.index);

    return {
      success: read.geneString !== null,
      geneString: read.geneString ?? '',
      confidence: read.confidence,
      slotConfidences,
      minSlotConfidence: read.minSlotConfidence,
      resolvedCount: read.resolvedCount,
      partial: read.slots.map(slot => (slot.reject === null && slot.gene ? slot.gene : '.')).join(''),
      unresolvedSlots,
      // Cross-colour confusion is impossible by construction: the badge colour selects the
      // candidate templates before matching, so a red badge cannot yield G, Y or H.
      chromaticVerified: read.slots.every(slot => slot.color !== 'unknown'),
      layoutSource: read.layoutSource,
      slots: read.slots,
      latencyMs: read.latencyMs
    };
  }
}
