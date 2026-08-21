import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Box, Button, IconButton, Typography, useMediaQuery } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import PauseIcon from '@mui/icons-material/Pause';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import CameraswitchIcon from '@mui/icons-material/Cameraswitch';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import LockIcon from '@mui/icons-material/Lock';
import { useScanner } from '../../context/ScannerContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import {
  CameraStatusTone,
  describeCameraStatus
} from '../../services/scanner/cameraStatusMessages.ts';
import {
  computeContainRect,
  elementToFrame,
  quadToSvgPoints
} from '../../services/scanner/cameraOverlayGeometry.ts';
import { CAMERA_SCANNER_CONFIG } from '../../services/scanner/cameraScannerConfig.ts';

const CAMERA_CONFIRMATION_SAMPLES = CAMERA_SCANNER_CONFIG.confirmation.samples;

const TONE_COLORS: Record<CameraStatusTone, string> = {
  neutral: '#9CA3AF',
  warn: '#F59E0B',
  active: '#00E5FF',
  success: '#22C55E',
  error: '#EF4444'
};

/** Every primary control is at least this tall/wide, per the mobile touch-target rule. */
const TOUCH_TARGET = 44;

const SURFACE_PADDING = {
  paddingTop: 'max(12px, env(safe-area-inset-top))',
  paddingBottom: 'max(12px, env(safe-area-inset-bottom))',
  paddingLeft: 'max(12px, env(safe-area-inset-left))',
  paddingRight: 'max(12px, env(safe-area-inset-right))'
};

/**
 * Full-screen phone camera scanner surface.
 *
 * Stream lifecycle lives in `cameraScannerService` and all detection in `DynamicGeneLocator`.
 * This component presents the preview, maps the detected row onto the letterboxed video, and
 * carries the status vocabulary.
 */
export const MobileCameraScanner: React.FC = () => {
  const {
    isCameraScannerOpen,
    isCameraScannerSupported,
    isCameraSecureOrigin,
    cameraState,
    cameraLastResultKind,
    closeCameraScanner,
    startCameraScanner,
    pauseCameraScanner,
    resumeCameraScanner,
    switchCameraFacing,
    attachCameraVideo,
    selectCameraCandidateAt,
    isCameraDebugEnabled,
    toggleCameraDebug
  } = useScanner();
  const { clones } = useWorkspace();

  const prefersReducedMotion = useMediaQuery('(prefers-reduced-motion: reduce)');
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const previousPhaseRef = useRef(cameraState.phase);
  const [recentlyLostTarget, setRecentlyLostTarget] = useState(false);
  const [videoBox, setVideoBox] = useState({ width: 0, height: 0 });

  const isLive = cameraState.phase !== 'idle' && cameraState.phase !== 'error';
  const hasStarted = cameraState.phase !== 'idle';

  // "Genetics lost" is a transition, not a state the service reports: it is the moment a
  // tracked row stops being tracked.
  useEffect(() => {
    const previous = previousPhaseRef.current;
    previousPhaseRef.current = cameraState.phase;

    if (cameraState.phase === 'searching' && (previous === 'tracking' || previous === 'reading')) {
      setRecentlyLostTarget(true);
      return;
    }
    if (cameraState.phase !== 'searching') {
      setRecentlyLostTarget(false);
    }
  }, [cameraState.phase]);

  const status = useMemo(
    () => describeCameraStatus(cameraState, { lastResultKind: cameraLastResultKind, recentlyLostTarget }),
    [cameraState, cameraLastResultKind, recentlyLostTarget]
  );

  const resizeObserverRef = useRef<ResizeObserver | null>(null);

  const setVideoElement = useCallback(
    (element: HTMLVideoElement | null) => {
      resizeObserverRef.current?.disconnect();
      resizeObserverRef.current = null;

      videoRef.current = element;
      attachCameraVideo(element);
      if (!element) return;

      setVideoBox({ width: element.clientWidth, height: element.clientHeight });

      // The overlay is positioned in CSS pixels, so it has to follow the element through
      // rotation, browser chrome appearing, and split-screen resizes.
      if (typeof ResizeObserver !== 'undefined') {
        const observer = new ResizeObserver(() => {
          setVideoBox({ width: element.clientWidth, height: element.clientHeight });
        });
        observer.observe(element);
        resizeObserverRef.current = observer;
      }
    },
    [attachCameraVideo]
  );

  // Release the camera if this surface unmounts for any reason.
  useEffect(() => {
    return () => {
      resizeObserverRef.current?.disconnect();
      resizeObserverRef.current = null;
      attachCameraVideo(null);
    };
  }, [attachCameraVideo]);

  const overlay = cameraState.overlay;
  const containRect = useMemo(
    () =>
      overlay ? computeContainRect(overlay.frameWidth, overlay.frameHeight, videoBox.width, videoBox.height) : null,
    [overlay, videoBox.width, videoBox.height]
  );

  /**
   * A tap selects a candidate for the current appearance. It saves nothing, and it never
   * claims to have changed focus on a device that cannot do point focus.
   */
  const handleStageTap = useCallback(
    (event: React.PointerEvent<HTMLDivElement>) => {
      if (!containRect || !overlay || overlay.candidates.length === 0) return;
      const element = videoRef.current;
      if (!element) return;

      const elementBounds = element.getBoundingClientRect();
      const point = elementToFrame(
        {
          x: event.clientX - elementBounds.left,
          y: event.clientY - elementBounds.top
        },
        containRect
      );

      if (
        point.x < 0 ||
        point.y < 0 ||
        point.x > overlay.frameWidth ||
        point.y > overlay.frameHeight
      ) {
        return;
      }
      selectCameraCandidateAt(point);
    },
    [containRect, overlay, selectCameraCandidateAt]
  );

  if (!isCameraScannerOpen) return null;

  const toneColor = TONE_COLORS[status.tone];
  const canStart = isCameraScannerSupported && isCameraSecureOrigin;

  return (
    <Box
      role="dialog"
      aria-modal="true"
      aria-label="Phone camera scanner"
      sx={{
        position: 'fixed',
        inset: 0,
        zIndex: 2000,
        backgroundColor: '#000000',
        color: '#FFFFFF',
        display: 'flex',
        flexDirection: 'column',
        ...SURFACE_PADDING
      }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1, flexShrink: 0 }}>
        <Typography
          component="h2"
          sx={{ fontFamily: 'monospace', fontWeight: 800, fontSize: '0.9rem', flex: 1, minWidth: 0 }}
        >
          Camera Scanner
          <Box component="span" sx={{ ml: 1, px: 0.75, py: 0.25, fontSize: '0.6rem', borderRadius: '3px', backgroundColor: '#F59E0B', color: '#111827', fontWeight: 900 }}>
            BETA
          </Box>
        </Typography>

        {isLive && (
          <Box
            aria-hidden="true"
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 0.5,
              fontFamily: 'monospace',
              fontSize: '0.7rem',
              color: '#EF4444',
              fontWeight: 800
            }}
          >
            <Box sx={{ width: 8, height: 8, borderRadius: '50%', backgroundColor: '#EF4444' }} />
            CAMERA ON
          </Box>
        )}

        <Typography sx={{ fontFamily: 'monospace', fontSize: '0.75rem', color: '#9CA3AF' }}>
          {clones.length} clones
        </Typography>

        <IconButton
          aria-label="Close camera scanner"
          onClick={closeCameraScanner}
          sx={{ minWidth: TOUCH_TARGET, minHeight: TOUCH_TARGET, color: '#FFFFFF' }}
        >
          <CloseIcon />
        </IconButton>
      </Box>

      {/* Stage */}
      <Box
        sx={{
          flex: 1,
          minHeight: 0,
          position: 'relative',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          mt: 1
        }}
      >
        {cameraState.phase === 'error' ? (
          <Box sx={{ maxWidth: 460, mx: 'auto', px: 2 }}>
            <Typography sx={{ fontFamily: 'monospace', fontWeight: 800, fontSize: '1rem', color: '#EF4444', mb: 1 }}>
              Camera stopped
            </Typography>
            <Typography sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#D1D5DB' }}>
              {cameraState.errorMessage}
            </Typography>
            {cameraState.errorCode === 'permission-denied' && (
              <Typography sx={{ fontFamily: 'monospace', fontSize: '0.75rem', color: '#9CA3AF', mt: 1.5 }}>
                Open the site permissions for this page in your browser (usually the padlock or
                menu next to the address bar), set Camera to Allow, then choose Restart camera.
              </Typography>
            )}
          </Box>
        ) : isLive ? (
          <Box
            onPointerDown={handleStageTap}
            sx={{ position: 'relative', width: '100%', height: '100%' }}
          >
            <video
              ref={setVideoElement}
              autoPlay
              muted
              playsInline
              aria-label="Live camera preview"
              style={{
                display: 'block',
                width: '100%',
                height: '100%',
                objectFit: 'contain',
                backgroundColor: '#000000'
              }}
            />

            {containRect && overlay ? (
              <Box
                component="svg"
                aria-hidden="true"
                viewBox={`0 0 ${videoBox.width} ${videoBox.height}`}
                width={videoBox.width}
                height={videoBox.height}
                sx={{ position: 'absolute', left: 0, top: 0, pointerEvents: 'none' }}
              >
                {/* Non-selected candidates, so an ambiguous scene shows what can be tapped. */}
                {overlay.candidates
                  .filter(candidate => candidate.id !== overlay.targetId)
                  .map(candidate => (
                    <polygon
                      key={candidate.id}
                      points={quadToSvgPoints(candidate.corners, containRect)}
                      fill="rgba(245, 158, 11, 0.12)"
                      stroke={TONE_COLORS.warn}
                      strokeWidth={2}
                      strokeDasharray="6 4"
                    />
                  ))}

                {overlay.target && (
                  <polygon
                    points={quadToSvgPoints(overlay.target, containRect)}
                    fill="none"
                    stroke={toneColor}
                    strokeWidth={3}
                    style={{
                      transition: prefersReducedMotion ? 'none' : 'stroke 180ms ease-out'
                    }}
                  />
                )}
              </Box>
            ) : (
              /* No detection yet this frame: a static framing aid, clearly not a lock. */
              <Box
                aria-hidden="true"
                sx={{
                  position: 'absolute',
                  left: '10%',
                  right: '10%',
                  top: '50%',
                  transform: 'translateY(-50%)',
                  height: '18%',
                  border: `2px dashed ${toneColor}`,
                  borderRadius: '6px',
                  transition: prefersReducedMotion ? 'none' : 'border-color 180ms ease-out',
                  pointerEvents: 'none'
                }}
              />
            )}
          </Box>
        ) : (
          <PermissionExplainer
            canStart={canStart}
            isSupported={isCameraScannerSupported}
            isSecureOrigin={isCameraSecureOrigin}
            onStart={startCameraScanner}
            onClose={closeCameraScanner}
          />
        )}
      </Box>

      {/* Status */}
      {hasStarted && (
        <Box
          sx={{
            flexShrink: 0,
            mt: 1,
            mx: 1,
            p: 1.25,
            borderRadius: '6px',
            // Opaque backing so the text stays readable over bright and dark camera content.
            backgroundColor: '#0B0B0BEE',
            borderLeft: `4px solid ${toneColor}`
          }}
        >
          <Typography sx={{ fontFamily: 'monospace', fontWeight: 800, fontSize: '0.85rem', color: toneColor }}>
            {status.headline}
          </Typography>
          <Typography sx={{ fontFamily: 'monospace', fontSize: '0.78rem', color: '#E5E7EB' }}>
            {status.instruction}
          </Typography>

          {cameraState.isOcrUnavailable && (
            <Typography sx={{ fontFamily: 'monospace', fontSize: '0.72rem', color: '#F59E0B', mt: 0.5 }}>
              Text recognition is unavailable. The preview still works; close and reopen to retry.
            </Typography>
          )}

          {/* Beta diagnostics. Without this a failing read is indistinguishable from a
              hanging one, and there is nothing to report back for tuning. Tapping it shows
              the exact image being handed to the recogniser. */}
          {cameraState.diagnostics.readAttempts > 0 && (
            <Typography
              component="button"
              onClick={toggleCameraDebug}
              aria-label="Toggle OCR debug view"
              sx={{
                display: 'block',
                width: '100%',
                textAlign: 'left',
                background: 'none',
                border: 'none',
                p: 0,
                fontFamily: 'monospace',
                fontSize: '0.66rem',
                color: isCameraDebugEnabled ? '#00E5FF' : '#6B7280',
                mt: 0.5,
                wordBreak: 'break-all'
              }}
            >
              {`ocr "${cameraState.diagnostics.lastRawText ?? '—'}"`}
              {cameraState.diagnostics.lastSource ? ` (${cameraState.diagnostics.lastSource})` : ''}
              {` · ${cameraState.diagnostics.pendingSamples}/${cameraState.diagnostics.sampleWindow || CAMERA_CONFIRMATION_SAMPLES} samples`}
              {` · ${cameraState.diagnostics.readAttempts} reads`}
              {!cameraState.diagnostics.slotsWithinBounds && ' · SLOTS OUT OF BOUNDS'}
            </Typography>
          )}

          {isCameraDebugEnabled && (
            <Box sx={{ mt: 0.75 }}>
              <Typography sx={{ fontFamily: 'monospace', fontSize: '0.62rem', color: '#6B7280' }}>
                {`ink ${cameraState.diagnostics.slotInk.map(v => v.toFixed(2)).join(' ')}`}
              </Typography>
              {cameraState.diagnostics.stripPreview ? (
                <Box
                  component="img"
                  src={cameraState.diagnostics.stripPreview}
                  alt="Image passed to the text recogniser"
                  sx={{
                    mt: 0.5,
                    width: '100%',
                    imageRendering: 'pixelated',
                    border: '1px solid #374151',
                    backgroundColor: '#FFFFFF'
                  }}
                />
              ) : (
                <Typography sx={{ fontFamily: 'monospace', fontSize: '0.62rem', color: '#6B7280' }}>
                  waiting for the next read…
                </Typography>
              )}
            </Box>
          )}
        </Box>
      )}

      {/* Polite announcements. Tracking and quality churn is deliberately not announced. */}
      <Box
        aria-live="polite"
        aria-atomic="true"
        sx={{
          position: 'absolute',
          width: 1,
          height: 1,
          overflow: 'hidden',
          clip: 'rect(0 0 0 0)',
          whiteSpace: 'nowrap'
        }}
      >
        {status.announce ? `${status.headline}. ${status.instruction}` : ''}
      </Box>

      {/* Controls */}
      {hasStarted && (
        <Box sx={{ flexShrink: 0, display: 'flex', gap: 1, mt: 1, px: 1 }}>
          {cameraState.phase === 'error' ? (
            <Button
              fullWidth
              variant="contained"
              onClick={startCameraScanner}
              sx={{ minHeight: TOUCH_TARGET, fontFamily: 'monospace', fontWeight: 800 }}
            >
              Restart camera
            </Button>
          ) : (
            <>
              <Button
                fullWidth
                variant="outlined"
                startIcon={cameraState.phase === 'paused' ? <PlayArrowIcon /> : <PauseIcon />}
                onClick={cameraState.phase === 'paused' ? resumeCameraScanner : pauseCameraScanner}
                sx={{ minHeight: TOUCH_TARGET, fontFamily: 'monospace', fontWeight: 800, color: '#FFFFFF', borderColor: '#4B5563' }}
              >
                {cameraState.phase === 'paused' ? 'Resume' : 'Pause'}
              </Button>

              {cameraState.canSwitchCamera && (
                <Button
                  fullWidth
                  variant="outlined"
                  startIcon={<CameraswitchIcon />}
                  onClick={switchCameraFacing}
                  sx={{ minHeight: TOUCH_TARGET, fontFamily: 'monospace', fontWeight: 800, color: '#FFFFFF', borderColor: '#4B5563' }}
                >
                  Switch
                </Button>
              )}
            </>
          )}
        </Box>
      )}
    </Box>
  );
};

interface PermissionExplainerProps {
  canStart: boolean;
  isSupported: boolean;
  isSecureOrigin: boolean;
  onStart: () => void;
  onClose: () => void;
}

/**
 * Shown before any permission is requested, so the user knows what they are agreeing to.
 * On an unsupported browser or insecure origin it explains that instead of presenting a
 * permission flow that cannot succeed.
 */
const PermissionExplainer: React.FC<PermissionExplainerProps> = ({
  canStart,
  isSupported,
  isSecureOrigin,
  onStart,
  onClose
}) => {
  const blockedReason = !isSecureOrigin
    ? 'Camera scanning needs a secure (HTTPS) connection. Open this page over HTTPS and try again.'
    : !isSupported
      ? 'This browser cannot open a camera. You can still add clones manually, or use the desktop screen scanner.'
      : null;

  return (
    <Box sx={{ maxWidth: 460, mx: 'auto', px: 2, textAlign: 'left' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1.5 }}>
        <PhotoCameraIcon sx={{ color: '#00E5FF' }} />
        <Typography sx={{ fontFamily: 'monospace', fontWeight: 800, fontSize: '1rem' }}>
          Scan with your phone camera
        </Typography>
      </Box>

      <Typography sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#D1D5DB', mb: 1.5 }}>
        Point the rear camera at the monitor running Rust and hover a plant, the same way you
        would with the desktop scanner. Landscape orientation works best.
      </Typography>

      <Box sx={{ display: 'flex', alignItems: 'flex-start', gap: 1, mb: 2 }}>
        <LockIcon sx={{ color: '#22C55E', fontSize: 18, mt: '2px' }} />
        <Typography sx={{ fontFamily: 'monospace', fontSize: '0.75rem', color: '#9CA3AF' }}>
          Frames are processed on this device only. Nothing from the camera is uploaded or saved.
        </Typography>
      </Box>

      {blockedReason ? (
        <Typography sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#F59E0B', mb: 2 }}>
          {blockedReason}
        </Typography>
      ) : null}

      <Box sx={{ display: 'flex', gap: 1 }}>
        <Button
          variant="contained"
          onClick={onStart}
          disabled={!canStart}
          sx={{ minHeight: TOUCH_TARGET, flex: 1, fontFamily: 'monospace', fontWeight: 800 }}
        >
          Open camera
        </Button>
        <Button
          variant="outlined"
          onClick={onClose}
          sx={{ minHeight: TOUCH_TARGET, fontFamily: 'monospace', fontWeight: 800, color: '#FFFFFF', borderColor: '#4B5563' }}
        >
          Close
        </Button>
      </Box>
    </Box>
  );
};

export default MobileCameraScanner;
