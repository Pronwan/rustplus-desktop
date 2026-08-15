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
  private fallbackWorker: TesseractWorker | null = null;
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
      await this.initLineWorkerWithRetry();
      this.warm = true;
    } catch (err) {
      console.warn('[GeneticsLab] Failed to warm up OCR worker:', err);
    } finally {
      this.isInitializing = false;
    }
  }

  private async initLineWorkerWithRetry(): Promise<void> {
    this.cleanupWorkers();

    const workerPath = this.getAssetUrl('tesseract/worker.min.js');
    const langPath = this.getAssetUrl('tesseract');

    try {
      this.lineWorker = await this.createConfiguredWorker(
        workerPath,
        langPath,
        this.getAssetUrl('tesseract/tesseract-core-lstm.wasm.js'),
        'GHYWX',
        '7'
      );
      this.initRetryCount = 0;
    } catch (primaryErr) {
      console.warn('[GeneticsLab] Primary LSTM worker failed, attempting fallback core:', primaryErr);
      if (this.lineWorker) {
        try { this.lineWorker.terminate(); } catch {}
        this.lineWorker = null;
      }

      if (this.initRetryCount < this.MAX_INIT_RETRIES) {
        this.initRetryCount++;
        this.lineWorker = await this.createConfiguredWorker(
          workerPath,
          langPath,
          this.getAssetUrl('tesseract/tesseract-core.wasm.js'),
          'GHYWX',
          '7'
        );
      } else {
        throw primaryErr;
      }
    }
  }

  private async createConfiguredWorker(
    workerPath: string,
    langPath: string,
    corePath: string,
    whitelist: string,
    pageSegMode: string
  ): Promise<TesseractWorker> {
    const workerPromise = (async () => {
      const worker = await createWorker('eng', 1, { workerPath, corePath, langPath, gzip: true });
      await worker.setParameters({
        tessedit_char_whitelist: whitelist,
        tessedit_pageseg_mode: pageSegMode as any
      });
      return worker;
    })();

    const timeoutPromise = new Promise<never>((_, reject) => {
      setTimeout(() => reject(new Error('Tesseract worker initialization timed out')), 8000);
    });

    return Promise.race([workerPromise, timeoutPromise]);
  }

  /**
   * Convert a canvas to a synchronous data-URL string before handing it to Tesseract.
   *
   * Tesseract.js 'loadImage' feeds an HTMLCanvasElement through canvas.toBlob(), whose
   * completion callback is gated by the page's rendering/compositor lifecycle. When the
   * WebView is unfocused or occluded (i.e. every time the user is looking at the game),
   * that callback can stop firing, so recognize() never resolves and the scan pipeline
   * hangs at the row-ocr/slot-ocr stage until the app regains focus.
   *
   * toDataURL() encodes synchronously on the calling thread and is not tied to frame
   * production, and Tesseract's loadImage decodes the resulting base64 string synchronously
   * (no toBlob). This keeps OCR running while the app is in the background.
   */
  private static canvasToInput(canvas: HTMLCanvasElement): string {
    return canvas.toDataURL('image/png');
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
      const res = await this.lineWorker.recognize(TesseractGeneRecognizer.canvasToInput(canvas));
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
   * Fallback Slot Recognition using secondary worker if single-line was ambiguous.
   */
  public async recognizeSlots(canvases: HTMLCanvasElement[]): Promise<GeneRecognitionResult | null> {
    if (canvases.length !== 6) return null;

    if (!this.fallbackWorker) {
      try {
        const workerPath = this.getAssetUrl('tesseract/worker.min.js');
        const langPath = this.getAssetUrl('tesseract');
        const corePath = this.getAssetUrl('tesseract/tesseract-core-lstm.wasm.js');
        this.fallbackWorker = await this.createConfiguredWorker(
          workerPath,
          langPath,
          corePath,
          SCANNER_CONFIG.recognition.whitelist,
          '10'
        );
      } catch {
        if (!this.lineWorker) return null;
      }
    }

    const worker = this.fallbackWorker || this.lineWorker;
    if (!worker) return null;

    const results = await Promise.all(canvases.map(async (canvas) => {
      try {
        const res = await worker.recognize(TesseractGeneRecognizer.canvasToInput(canvas));
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
    if (this.fallbackWorker) {
      try {
        this.fallbackWorker.terminate();
      } catch {
        // ignore
      }
      this.fallbackWorker = null;
    }
  }
}
