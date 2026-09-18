import { GeneRecognitionResult } from './scannerTypes.ts';
import { SCANNER_CONFIG } from './scannerConfig.ts';

/**
 * Confirms a read across frames.
 *
 * The previous rule was three *consecutive identical* strings, with the accumulator cleared
 * the moment any read differed. That is strictly worse than it sounds. One slot flickering
 * between two letters -- which is exactly what a marginal glyph does -- resets the count on
 * every other frame, so the row never reaches three and the scanner sits there reading
 * correctly and emitting nothing. The failure is invisible: no error, no rejection, just
 * silence.
 *
 * Majority per position fixes that directly. Five frames that agree on every letter except
 * one wobbling slot still name the wobbling slot correctly, because four of the five frames
 * voted for the same letter there. Noise has to be consistent to win, and consistent noise
 * is not noise.
 *
 * The window is also allowed to be skipped entirely. A desktop tooltip is a still image and
 * the classifier reports how sure it is per slot; when every slot is unambiguous there is
 * nothing for a second frame to add, and waiting three of them to confirm a read that was
 * never in doubt is just latency the user feels while scanning a tray of clones.
 */
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
    /** Skip the window: the caller has slot-level evidence that this frame is unambiguous. */
    acceptImmediately = false
  ): GeneRecognitionResult | null {
    if (!result.geneString || result.geneString.length !== 6) return null;
    if (result.confidence < this.minConfidence) return null;

    if (acceptImmediately) {
      this.history[key] = [];
      return result;
    }

    const previous = this.history[key];
    const list = previous && previous.length > 0 ? previous : [];

    // A genuinely different plant must not inherit the outgoing one's votes. A single
    // wobbling slot is not a different plant, so similarity is what decides, not equality.
    if (list.length > 0 && agreementWith(list[list.length - 1].geneString, result.geneString) < 4) {
      this.history[key] = [result];
      return null;
    }

    list.push(result);
    while (list.length > SCANNER_CONFIG.recognition.temporalSamples) list.shift();
    this.history[key] = list;

    const required = SCANNER_CONFIG.recognition.requiredMatches;
    if (list.length < required) return null;

    const winner = confirmedString(list, required);
    if (!winner) return null;

    this.history[key] = [];
    return {
      geneString: winner,
      confidence: Math.round(list.reduce((sum, entry) => sum + entry.confidence, 0) / list.length)
    };
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

/** How many of the six positions two reads agree on. */
function agreementWith(a: string, b: string): number {
  let same = 0;
  for (let i = 0; i < 6; i++) if (a[i] === b[i]) same++;
  return same;
}

/**
 * The winning letter at every position, or null while any position is still short of votes.
 *
 * Each position needs `required` frames naming the same letter -- the same bar the old rule
 * set for the whole row, applied per position instead. Six clean frames confirm on the third
 * exactly as before; a row with one flickering slot confirms a frame or two later instead of
 * never, because the five steady slots are not made to start over every time the sixth
 * changes its mind.
 */
function confirmedString(samples: GeneRecognitionResult[], required: number): string | null {
  let out = '';

  for (let position = 0; position < 6; position++) {
    const tally = new Map<string, number>();
    for (const sample of samples) {
      const letter = sample.geneString[position];
      tally.set(letter, (tally.get(letter) ?? 0) + 1);
    }

    let bestLetter = '';
    let bestCount = 0;
    for (const [letter, count] of tally) {
      if (count > bestCount) {
        bestCount = count;
        bestLetter = letter;
      }
    }

    if (bestCount < required) return null;
    out += bestLetter;
  }

  return out;
}
