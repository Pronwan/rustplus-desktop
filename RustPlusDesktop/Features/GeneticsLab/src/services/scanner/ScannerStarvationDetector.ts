import { SCANNER_CONFIG } from './scannerConfig.ts';
import { StarvationReason } from './scannerTypes.ts';

export interface StarvationEvaluationMetrics {
  videoFrameAgeMs: number;
  videoFrameGapMs: number;
  lastOcrLatencyMs: number;
  rowOcrLatencyMs: number;
  tickGapMs: number;
  pipelineStage?: string;
  pipelineStageAgeMs?: number;
}

export interface StarvationEvaluationResult {
  isStarved: boolean;
  starvationReason?: StarvationReason;
  stateChanged: boolean;
}

export class ScannerStarvationDetector {
  private startedAt = 0;
  private isStarved = false;
  private starvationReason?: StarvationReason;
  private starvationConditionStart = 0;
  private recoveryConditionStart = 0;
  private lastDetectedReason?: StarvationReason;

  public start(now = Date.now()): void {
    this.reset(now);
  }

  public reset(now = Date.now()): void {
    this.startedAt = now;
    this.isStarved = false;
    this.starvationReason = undefined;
    this.starvationConditionStart = 0;
    this.recoveryConditionStart = 0;
    this.lastDetectedReason = undefined;
  }

  public getIsStarved(): boolean {
    return this.isStarved;
  }

  public getStarvationReason(): StarvationReason | undefined {
    return this.starvationReason;
  }

  public evaluate(metrics: StarvationEvaluationMetrics, now = Date.now()): StarvationEvaluationResult {
    const config = SCANNER_CONFIG.starvation;
    const previousStarved = this.isStarved;

    // 1. Startup grace period (skip initial permission prompt and worker startup warmup)
    if (this.startedAt > 0 && now - this.startedAt < config.startupGracePeriodMs) {
      return {
        isStarved: this.isStarved,
        starvationReason: this.starvationReason,
        stateChanged: false
      };
    }

    // 2. Symptom checks
    const reasons: StarvationReason[] = [];

    const isCaptureStalled =
      metrics.videoFrameAgeMs > config.frameAgeThresholdMs ||
      metrics.videoFrameGapMs > config.frameGapThresholdMs;

    if (isCaptureStalled) {
      reasons.push('CAPTURE_STALLED');
    }

    const isOcrSlow =
      metrics.rowOcrLatencyMs > config.ocrLatencyThresholdMs ||
      metrics.lastOcrLatencyMs > config.ocrLatencyThresholdMs;

    if (isOcrSlow) {
      reasons.push('OCR_LATENCY_SPIKE');
    }

    const isTickDelayed = metrics.tickGapMs > config.tickGapThresholdMs;
    if (isTickDelayed) {
      reasons.push('TICK_DELAYED');
    }

    const hasStarvationSymptoms = reasons.length > 0;

    // 3. State transition logic with debounced hysteresis
    if (hasStarvationSymptoms) {
      this.recoveryConditionStart = 0;
      if (this.starvationConditionStart === 0) {
        this.starvationConditionStart = now;
      }

      this.lastDetectedReason =
        reasons.length > 1 ? 'MULTIPLE' : reasons[0];

      if (now - this.starvationConditionStart >= config.sustainedDurationMs) {
        this.isStarved = true;
        this.starvationReason = this.lastDetectedReason;
      }
    } else {
      this.starvationConditionStart = 0;

      if (this.isStarved) {
        if (this.recoveryConditionStart === 0) {
          this.recoveryConditionStart = now;
        }

        if (now - this.recoveryConditionStart >= config.recoveryDurationMs) {
          this.isStarved = false;
          this.starvationReason = undefined;
          this.recoveryConditionStart = 0;
          this.lastDetectedReason = undefined;
        }
      }
    }

    const stateChanged = previousStarved !== this.isStarved;

    return {
      isStarved: this.isStarved,
      starvationReason: this.starvationReason,
      stateChanged
    };
  }
}
