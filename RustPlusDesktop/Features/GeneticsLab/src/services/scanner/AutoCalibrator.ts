import { ScannerRegion } from '../storageService.ts';
import { CanvasFrameGrabber } from './vision/frameGrabber.ts';
import { detectBadgeCandidates, AnalysisFrame } from './vision/rasterOps.ts';
import { buildRowCandidates, BuiltCandidate, DEFAULT_BUILD_CANDIDATES_OPTIONS } from './vision/geometry.ts';
import { GeneImagePreprocessor } from './GeneImagePreprocessor.ts';
import { GeneRecognizer } from './scannerTypes.ts';

export interface AutoCalibrateResult {
  success: boolean;
  regionIndex: number;
  region?: ScannerRegion;
  detectedGenes?: string;
  confidence?: number;
  message: string;
}

export class AutoCalibrator {
  private static grabber = new CanvasFrameGrabber();

  /**
   * Automatically detects plant gene rows from the live desktop screen capture
   * and computes calibrated ScannerRegion coordinates.
   *
   * @param video The active HTMLVideoElement streaming game capture
   * @param preferredRegionIndex Optional target region (0 for Inventory, 1 for Planter)
   * @param recognizer Optional GeneRecognizer to read and verify genes
   */
  public static async calibrateFromVideo(
    video: HTMLVideoElement,
    preferredRegionIndex?: number,
    recognizer?: GeneRecognizer
  ): Promise<AutoCalibrateResult> {
    if (!video || video.videoWidth === 0 || video.videoHeight === 0) {
      return {
        success: false,
        regionIndex: preferredRegionIndex ?? 0,
        message: 'Screen capture is not active. Please start the scanner first.'
      };
    }

    const videoW = video.videoWidth;
    const videoH = video.videoHeight;

    // Use full-fidelity discovery resolution (up to 1920px wide) for precise sub-pixel boundary fitting
    const discoveryWidth = Math.min(videoW, 1920);
    const frame = this.grabber.grabAnalysis(video, discoveryWidth);
    if (!frame) {
      return {
        success: false,
        regionIndex: preferredRegionIndex ?? 0,
        message: 'Could not capture frame from screen video stream.'
      };
    }

    // 1. Detect Rust red/green badge candidates across the frame
    const components = detectBadgeCandidates(frame);
    if (components.length < 6) {
      return {
        success: false,
        regionIndex: preferredRegionIndex ?? 0,
        message: 'No plant gene badges detected on screen. Hover over a plant in Rust and try again.'
      };
    }

    // 2. Discover collinear 6-badge gene rows
    const rowCandidates = buildRowCandidates(
      components,
      frame,
      frame.width,
      frame.height,
      {
        ...DEFAULT_BUILD_CANDIDATES_OPTIONS,
        readingDirection: { x: 1, y: 0 },
        minDirectionalConfidence: 0.1,
        bandFactor: 0.9,
        maxSizeRatio: 2.2,
        minSpacingFactor: 0.45,
        maxSpacingFactor: 5.5,
        maxSpacingVariation: 0.55,
        paddingFactor: 0.05
      }
    );

    if (rowCandidates.length === 0) {
      return {
        success: false,
        regionIndex: preferredRegionIndex ?? 0,
        message: 'Found badges, but could not align a complete 6-gene row. Make sure the tooltip is fully visible.'
      };
    }

    // 3. Select best candidate based on target region or screen position
    let targetCandidate: BuiltCandidate;

    // Filter candidates strictly according to the region being calibrated:
    // Region 0 (Inventory Tooltip/Info panel): MUST be in the left half of the screen (X < 0.52)
    // Region 1 (Planter Tooltip): MUST be in the right half of the screen (X >= 0.50)
    let filteredCandidates = rowCandidates;
    if (preferredRegionIndex === 0) {
      const leftCandidates = rowCandidates.filter(c => c.center.x < frame.width * 0.52);
      if (leftCandidates.length > 0) {
        filteredCandidates = leftCandidates;
      }
    } else if (preferredRegionIndex === 1) {
      const rightCandidates = rowCandidates.filter(c => c.center.x >= frame.width * 0.50);
      if (rightCandidates.length > 0) {
        filteredCandidates = rightCandidates;
      }
    }

    // Pick highest scoring candidate within the targeted half
    targetCandidate = filteredCandidates.slice().sort((a, b) => b.score - a.score)[0];

    // Determine target region type if not explicitly requested
    const normCenterX = targetCandidate.center.x / frame.width;
    const resolvedRegionIndex = preferredRegionIndex !== undefined
      ? preferredRegionIndex
      : (normCenterX >= 0.52 ? 1 : 0);

    const members = targetCandidate.members;
    const minX = Math.min(...members.map(m => m.minX));
    const maxX = Math.max(...members.map(m => m.maxX));
    const avgBadgeW = members.reduce((sum, m) => sum + m.width, 0) / members.length;
    const totalW = Math.max(1, maxX - minX);
    
    // Zoom tightly towards the letter (0.62x badge width) matching default calibration (Image 2)
    // The top and bottom of the bounding box tightly hug the white letters, cropping out extraneous background colors.
    const letterH = avgBadgeW * 0.62;
    const centerY = targetCandidate.center.y;
    const tightMinY = Math.max(0, centerY - letterH / 2);

    // Compute normalized coordinates relative to full video dimensions
    const normX = minX / frame.width;
    const normY = tightMinY / frame.height;
    const normW = totalW / frame.width;
    const normH = letterH / frame.height;

    // Mathematical calibration matching ScannerService expectations:
    // normH = reg.WIDTH * reg.HEIGHT_TO_WIDTH_RATIO
    // geneWPx = wPx * reg.GENE_WIDTH_TO_WIDTH_RATIO
    const calibratedRegion: ScannerRegion = {
      TOP_LEFT_X: Math.max(0, Math.min(1, Math.round(normX * 10000) / 10000)),
      TOP_LEFT_Y: Math.max(0, Math.min(1, Math.round(normY * 10000) / 10000)),
      WIDTH: Math.max(0.01, Math.min(0.5, Math.round(normW * 10000) / 10000)),
      HEIGHT_TO_WIDTH_RATIO: Math.max(0.05, Math.min(0.25, Math.round((normH / normW) * 10000) / 10000)),
      GENE_WIDTH_TO_WIDTH_RATIO: Math.max(0.04, Math.min(0.25, Math.round(((avgBadgeW * 0.90) / totalW) * 10000) / 10000))
    };

    // 4. Test-recognize genes on the newly calibrated region
    let detectedGenes: string | undefined;
    let confidence: number | undefined;

    if (recognizer && recognizer.isWarm()) {
      try {
        const xPx = Math.round(videoW * calibratedRegion.TOP_LEFT_X);
        const yPx = Math.round(videoH * calibratedRegion.TOP_LEFT_Y);
        const wPx = Math.round(videoW * calibratedRegion.WIDTH);
        const hPx = Math.ceil(videoH * (calibratedRegion.WIDTH * calibratedRegion.HEIGHT_TO_WIDTH_RATIO));
        const geneWPx = Math.round(wPx * calibratedRegion.GENE_WIDTH_TO_WIDTH_RATIO);
        const gapWPx = Math.max(0, (wPx - geneWPx * 6) / 5);

        const strip = GeneImagePreprocessor.prepareStitchedGeneStrip(
          video,
          xPx,
          yPx,
          geneWPx,
          gapWPx,
          hPx
        );

        const ocrRes = await recognizer.recognizeRow(strip, 50);
        if (ocrRes && ocrRes.geneString) {
          detectedGenes = ocrRes.geneString;
          confidence = ocrRes.confidence;
        }
      } catch {
        // Non-critical diagnostic read
      }
    }

    const regionName = resolvedRegionIndex === 0 ? 'Inventory Tooltip' : 'Planter Tooltip';
    const geneDetails = detectedGenes ? ` (Verified genes: ${detectedGenes.split('').join('-')})` : '';

    return {
      success: true,
      regionIndex: resolvedRegionIndex,
      region: calibratedRegion,
      detectedGenes,
      confidence,
      message: `Successfully calibrated Region ${resolvedRegionIndex + 1} (${regionName})!${geneDetails}`
    };
  }
}
