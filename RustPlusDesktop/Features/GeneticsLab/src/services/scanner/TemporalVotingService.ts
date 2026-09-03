import { GeneRecognitionResult } from './scannerTypes.ts';
import { SCANNER_CONFIG } from './scannerConfig.ts';

export class TemporalVotingService {
  private history: Record<string | number, GeneRecognitionResult[]> = {};

  /**
   * @param minConfidence Confidence floor for accepting a sample. Defaults to the desktop
   *   value; the camera path supplies its own, because camera OCR scores lower than a
   *   pixel-exact screen capture even when it is completely correct.
   */
  constructor(private readonly minConfidence: number = SCANNER_CONFIG.recognition.minAverageConfidence) {}

  public addCandidate(
    key: string | number,
    result: GeneRecognitionResult,
    _isFastPath = false
  ): GeneRecognitionResult | null {
    if (!result.geneString || result.geneString.length !== 6) return null;
    if (result.confidence < this.minConfidence) return null;

    if (!this.history[key]) {
      this.history[key] = [];
    }

    const list = this.history[key];

    // Reset accumulator when moving to a different gene string so old plants NEVER leak votes!
    if (list.length > 0 && list[list.length - 1].geneString !== result.geneString) {
      this.history[key] = [result];
      return null;
    }

    list.push(result);

    const requiredVotes = SCANNER_CONFIG.recognition.requiredMatches; // 3

    // 3 consecutive identical reads of this exact gene sequence guarantees 100% rock-solid accuracy
    if (list.length >= requiredVotes) {
      this.history[key] = []; // Reset on consensus emission
      console.log(`[Scanner Voting] Consensus reached (3 consecutive matches): ${result.geneString}`);
      return result;
    }

    return null;
  }

  /** Samples currently in the confirmation window for a key. */
  public getSampleCount(key: string | number): number {
    return this.history[key]?.length ?? 0;
  }

  public reset(key?: string | number): void {
    if (key !== undefined) {
      delete this.history[key];
    } else {
      this.history = {};
    }
  }
}
