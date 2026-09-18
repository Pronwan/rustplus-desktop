/**
 * How much the ROI's pixel signature must move before the same genotype counts as a new
 * item. Tooltip redraws shift it by far more than this; compositor dithering and the cursor
 * moving within a still tooltip shift it by far less.
 */
const NEW_ITEM_SIGNATURE_SHIFT = 0.05;

export class PlantScanDeduplicator {
  private lastAcceptedGenes: Record<string | number, string> = {};
  private lastAcceptedSignatures: Record<string | number, number> = {};
  private isPlantCurrentlyVisible: Record<string | number, boolean> = {};

  /**
   * Checks whether a candidate plant scan should be accepted or suppressed as a duplicate.
   *
   * @param key Region key ('inventory' | 'planter' | index)
   * @param candidateGeneString The 6-letter candidate genotype
   * @param currentRoiSignature The visual signature of the region
   * @returns true if this is a newly presented plant that should be emitted, false if it is the same visible plant
   */
  public shouldAccept(
    key: string | number,
    candidateGeneString: string,
    currentRoiSignature: number
  ): boolean {
    const lastGenes = this.lastAcceptedGenes[key];
    const lastSig = this.lastAcceptedSignatures[key];
    const isVisible = this.isPlantCurrentlyVisible[key];

    if (!lastGenes || !isVisible) {
      // First plant detected or previous plant was dismissed
      this.lastAcceptedGenes[key] = candidateGeneString;
      this.lastAcceptedSignatures[key] = currentRoiSignature;
      this.isPlantCurrentlyVisible[key] = true;
      return true;
    }

    // Any different genotype is accepted as a new plant. Duplicate mis-reads are prevented
    // upstream by the 3-of-4 temporal voting (a transient wrong read never reaches enough
    // matching frames to be emitted), so the deduplicator never suppresses a differing
    // read here — that avoids ever dropping a genuinely different plant.
    if (candidateGeneString !== lastGenes) {
      this.lastAcceptedGenes[key] = candidateGeneString;
      this.lastAcceptedSignatures[key] = currentRoiSignature;
      this.isPlantCurrentlyVisible[key] = true;
      return true;
    }

    // Identical genetics. Whether this is the same plant or a different one that happens to
    // share a genotype is not answerable from the letters, so the ROI's own appearance
    // decides: a tooltip that redrew for a different item looks materially different, while
    // one the cursor is still resting on does not. Without this, hovering two clones with the
    // same genes in a row silently drops the second -- and identical genotypes are common
    // enough in a breeding tray that the loss would not look like a bug.
    const signatureShift = Math.abs(currentRoiSignature - lastSig) / Math.max(1, Math.abs(lastSig));
    if (signatureShift > NEW_ITEM_SIGNATURE_SHIFT) {
      this.lastAcceptedSignatures[key] = currentRoiSignature;
      return true;
    }

    // Same plant, still on screen. Suppressing here is what stops background flicker from
    // re-triggering the accept sound on every frame.
    return false;
  }

  public isRegionCurrentlyVisible(key: string | number): boolean {
    return !!this.isPlantCurrentlyVisible[key];
  }

  /**
   * Signals that the region has transitioned to an empty/different state (e.g. tooltip closed).
   */
  public markRegionDismissed(key: string | number): void {
    this.isPlantCurrentlyVisible[key] = false;
  }

  public reset(key?: string | number): void {
    if (key !== undefined) {
      delete this.lastAcceptedGenes[key];
      delete this.lastAcceptedSignatures[key];
      delete this.isPlantCurrentlyVisible[key];
    } else {
      this.lastAcceptedGenes = {};
      this.lastAcceptedSignatures = {};
      this.isPlantCurrentlyVisible = {};
    }
  }
}
