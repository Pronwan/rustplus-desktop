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

    // Candidate has identical genetics to the last accepted plant.
    // Verify if the visual signature changed by > 5% (meaning the user hovered a different item with the same genes)
    if (lastSig !== undefined) {
      const sigDiff = Math.abs(currentRoiSignature - lastSig) / Math.max(1, lastSig);
      if (sigDiff > 0.05) {
        // Visual signature indicates a new item instance
        this.lastAcceptedSignatures[key] = currentRoiSignature;
        return true;
      }
    }

    // Same plant still continuously displayed
    return false;
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
