import { CameraScannerState, CameraTrackCapabilities } from './scannerTypes.ts';

/**
 * Tiny, dependency-free camera helpers.
 *
 * These live apart from `cameraScannerService.ts` so the app shell can decide whether to
 * offer camera mode without pulling the scanner (and later the vision runtime) into the
 * initial bundle.
 */

export const NO_CAMERA_CAPABILITIES: CameraTrackCapabilities = {
  zoom: false,
  torch: false,
  pointFocus: false
};

export function isCameraCaptureSupported(mediaDevices?: MediaDevices | null): boolean {
  const devices = mediaDevices !== undefined
    ? mediaDevices
    : (typeof navigator !== 'undefined' ? navigator.mediaDevices : null);

  return !!devices && typeof devices.getUserMedia === 'function';
}

/** getUserMedia is only available in a secure context. localhost counts as secure. */
export function isCameraSecureContext(): boolean {
  return typeof window !== 'undefined' && window.isSecureContext === true;
}

export function createIdleCameraState(): CameraScannerState {
  return {
    phase: 'idle',
    qualityIssues: [],
    overlay: null,
    isOcrReady: false,
    isOcrUnavailable: false,
    isDetectionAvailable: false,
    facingMode: 'environment',
    canSwitchCamera: false,
    capabilities: { ...NO_CAMERA_CAPABILITIES },
    captureResolution: '',
    acceptedCount: 0,
    lastAcceptedGenes: null,
    diagnostics: { readAttempts: 0, lastRawText: null, lastConfidence: null, pendingSamples: 0 },
    candidateCount: 0
  };
}
