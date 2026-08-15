import { GeneRecognitionResult } from './scannerTypes.ts';
import { SCANNER_CONFIG } from './scannerConfig.ts';

export class TemporalVotingService {
  private history: Record<string | number, GeneRecognitionResult[]> = {};

  public addCandidate(key: string | number, result: GeneRecognitionResult): GeneRecognitionResult | null {
    if (!result.geneString || result.geneString.length !== 6) return null;
    if (result.confidence < SCANNER_CONFIG.recognition.minAverageConfidence) return null;

    if (!this.history[key]) {
      this.history[key] = [];
    }

    const list = this.history[key];
    list.push(result);

    const maxSamples = SCANNER_CONFIG.recognition.temporalSamples;
    if (list.length > maxSamples) {
      list.shift();
    }

    // 1. Exact Match Check (e.g. 2 out of 3 match exactly)
    const counts: Record<string, number> = {};
    for (const item of list) {
      counts[item.geneString] = (counts[item.geneString] || 0) + 1;
      if (counts[item.geneString] >= SCANNER_CONFIG.recognition.requiredMatches) {
        return item;
      }
    }

    // 2. Position-by-position majority voting across 3 samples
    if (list.length >= maxSamples) {
      const votedChars: string[] = [];
      let totalConfidence = 0;

      for (let pos = 0; pos < 6; pos++) {
        const charCounts: Record<string, number> = {};
        for (const item of list) {
          const char = item.geneString[pos];
          charCounts[char] = (charCounts[char] || 0) + 1;
        }

        let bestChar = '';
        let bestCount = 0;
        for (const [char, count] of Object.entries(charCounts)) {
          if (count > bestCount) {
            bestCount = count;
            bestChar = char;
          }
        }

        if (bestCount >= SCANNER_CONFIG.recognition.requiredMatches && bestChar) {
          votedChars.push(bestChar);
        } else {
          return null; // Position inconclusive
        }
      }

      for (const item of list) {
        totalConfidence += item.confidence;
      }

      const consensus = votedChars.join('');
      if (consensus.length === 6 && /^[GHYWX]{6}$/.test(consensus)) {
        return {
          geneString: consensus,
          confidence: totalConfidence / list.length
        };
      }
    }

    return null;
  }

  public reset(key?: string | number): void {
    if (key !== undefined) {
      delete this.history[key];
    } else {
      this.history = {};
    }
  }
}
