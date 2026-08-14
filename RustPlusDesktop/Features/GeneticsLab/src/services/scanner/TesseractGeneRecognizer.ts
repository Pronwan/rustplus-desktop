import { createWorker, Worker as TesseractWorker } from 'tesseract.js';
import { GeneRecognitionResult, GeneRecognizer } from './scannerTypes.ts';
import { SCANNER_CONFIG } from './scannerConfig.ts';

export function normalizeGeneGlyph(raw: string): string {
  const c = raw.trim().toUpperCase();
  if (['G', 'H', 'Y', 'W', 'X'].includes(c)) return c;
  if (c === '7' || c === 'V' || c === 'T' || c === 'J') return 'Y';
  if (c === '6' || c === '0' || c === 'C' || c === 'O' || c === 'Q') return 'G';
  if (c === '8' || c === 'K' || c === 'Z' || c === 'S' || c === '%') return 'X';
  if (c === '1' || c === 'I' || c === '|' || c === 'L' || c === 'N' || c === 'E' || c === 'B') return 'H';
  if (c === 'M' || c === 'U') return 'W';
  return '';
}

export class TesseractGeneRecognizer implements GeneRecognizer {
  private lineWorker: TesseractWorker | null = null;
  private slotWorkers: TesseractWorker[] = [];
  private isInitializing = false;
  private warm = false;
  private initRetryCount = 0;
  private readonly MAX_INIT_RETRIES = 2;

  private getAssetUrl(path: string): string {
    try {
      return new URL(path, window.location.href).href;
    } catch {
      return `./${path}`;
    }
  }

  public async warmup(): Promise<void> {
    if (this.warm || this.isInitializing) return;
    this.isInitializing = true;

    try {
      await this.initWorkersWithRetry();
      this.warm = true;
    } finally {
      this.isInitializing = false;
    }
  }

  private async initWorkersWithRetry(): Promise<void> {
    this.cleanupWorkers();

    const workerPath = this.getAssetUrl('tesseract/worker.min.js');
    const langPath = this.getAssetUrl('tesseract');

    try {
      await this.initWorkers(workerPath, langPath, this.getAssetUrl('tesseract/tesseract-core-lstm.wasm.js'));
      this.initRetryCount = 0;
    } catch (primaryErr) {
      console.warn('Primary Tesseract initialization encountered issue, attempting fallback', primaryErr);
      this.cleanupWorkers();

      if (this.initRetryCount < this.MAX_INIT_RETRIES) {
        this.initRetryCount++;
        await this.initWorkers(workerPath, langPath, this.getAssetUrl('tesseract/tesseract-core.wasm.js'));
      } else {
        throw primaryErr;
      }
    }
  }

  private async initWorkers(workerPath: string, langPath: string, corePath: string): Promise<void> {
    this.lineWorker = await this.createConfiguredWorker(workerPath, langPath, corePath, 'GHYWX', '7');

    await Promise.all(Array.from({ length: SCANNER_CONFIG.recognition.workerCount }, async () => {
      const worker = await this.createConfiguredWorker(
        workerPath,
        langPath,
        corePath,
        SCANNER_CONFIG.recognition.whitelist,
        '10'
      );
      this.slotWorkers.push(worker);
    }));
  }

  private async createConfiguredWorker(
    workerPath: string,
    langPath: string,
    corePath: string,
    whitelist: string,
    pageSegMode: string
  ): Promise<TesseractWorker> {
    const worker = await createWorker('eng', 1, { workerPath, corePath, langPath, gzip: true });
    await worker.setParameters({
      tessedit_char_whitelist: whitelist,
      tessedit_pageseg_mode: pageSegMode as any
    });
    return worker;
  }

  public isWarm(): boolean {
    return this.warm && this.lineWorker !== null;
  }

  /**
   * Primary Ultra-Fast Single-Line Recognition (~15ms - 25ms total).
   * Reads the preprocessed, stitched 6-glyph strip in ONE single OCR operation.
   */
  public async recognizeRow(canvas: HTMLCanvasElement): Promise<GeneRecognitionResult | null> {
    if (!this.lineWorker) return null;

    try {
      const res = await this.lineWorker.recognize(canvas);
      const raw = (res.data.text || '').trim().toUpperCase().replace(/[^GHYWX]/g, '');

      if (raw.length === 6 && /^[GHYWX]{6}$/.test(raw)) {
        const confidence = res.data.confidence || 88;
        if (confidence >= SCANNER_CONFIG.recognition.minAverageConfidence) {
          return {
            geneString: raw,
            confidence,
            slotConfidences: [confidence, confidence, confidence, confidence, confidence, confidence]
          };
        }
      }
      return null;
    } catch {
      return null;
    }
  }

  /**
   * Fallback Slot Recognition using secondary worker.
   */
  public async recognizeSlots(canvases: HTMLCanvasElement[]): Promise<GeneRecognitionResult | null> {
    if (this.slotWorkers.length < canvases.length || canvases.length !== 6) return null;

    const results = await Promise.all(canvases.map(async (canvas, index) => {
      try {
        const res = await this.slotWorkers[index].recognize(canvas);
        const raw = (res.data.text || '').trim();
        const gene = normalizeGeneGlyph(raw);
        return {
          gene,
          confidence: res.data.confidence || (gene ? 85 : 0)
        };
      } catch {
        return { gene: '', confidence: 0 };
      }
    }));

    const geneChars = results.map(r => r.gene);
    const candidate = geneChars.join('');

    if (candidate.length === 6 && /^[GHYWX]{6}$/.test(candidate)) {
      const confidences = results.map(r => r.confidence);
      const avgConfidence = confidences.reduce((a, b) => a + b, 0) / confidences.length;
      const minConfidence = Math.min(...confidences);

      if (
        minConfidence >= SCANNER_CONFIG.recognition.minGeneConfidence &&
        avgConfidence >= SCANNER_CONFIG.recognition.minAverageConfidence
      ) {
        return {
          geneString: candidate,
          confidence: avgConfidence,
          slotConfidences: confidences
        };
      }
    }

    return null;
  }

  public async terminate(): Promise<void> {
    this.cleanupWorkers();
    this.warm = false;
  }

  private cleanupWorkers(): void {
    if (this.lineWorker) {
      try {
        this.lineWorker.terminate();
      } catch {
        // ignore
      }
      this.lineWorker = null;
    }
    for (const worker of this.slotWorkers) {
      try {
        worker.terminate();
      } catch {
        // ignore
      }
    }
    this.slotWorkers = [];
  }
}
