import React, { useState, useRef, useEffect } from 'react';
import {
  Paper,
  Typography,
  Box,
  IconButton,
  Button,
  Tooltip
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import SearchIcon from '@mui/icons-material/Search';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import DesktopWindowsIcon from '@mui/icons-material/DesktopWindows';
import SettingsIcon from '@mui/icons-material/Settings';
import BugReportIcon from '@mui/icons-material/BugReport';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useApp } from '../../context/AppContext.tsx';
import { ScannerDiagnostics } from '../../services/scanner/scannerTypes.ts';
import { SCANNER_CONFIG } from '../../services/scanner/scannerConfig.ts';

export const ScannerWidget: React.FC = () => {
  const {
    isScannerActive,
    isScannerInitializing,
    isStarved,
    starvationReason,
    scannerPreviews,
    stopScanner,
    moveScannerRegion,
    scaleScannerRegion,
    resetScannerRegions,
    getScannerDiagnostics,
    setScannerPreviewEnabled
  } = useApp();

  const [isDiagnosticsOpen, setIsDiagnosticsOpen] = useState(false);
  const [diagnostics, setDiagnostics] = useState<ScannerDiagnostics | null>(null);

  const holdTimerRef = useRef<any>(null);
  const repeatTimerRef = useRef<any>(null);

  const startHold = (initialAction: () => void, repeatAction: () => void) => {
    initialAction();
    holdTimerRef.current = setTimeout(() => {
      repeatTimerRef.current = setInterval(() => {
        repeatAction();
      }, SCANNER_CONFIG.calibration.holdRepeatMs);
    }, SCANNER_CONFIG.calibration.holdDelayMs);
  };

  const stopHold = () => {
    if (holdTimerRef.current) clearTimeout(holdTimerRef.current);
    if (repeatTimerRef.current) clearInterval(repeatTimerRef.current);
    holdTimerRef.current = null;
    repeatTimerRef.current = null;
  };

  useEffect(() => {
    return () => stopHold();
  }, []);

  // Preview is UI-only work; only run it while this panel is actually mounted/visible.
  // Recognition keeps running regardless (including when the app is in the background).
  useEffect(() => {
    setScannerPreviewEnabled(true);
    return () => setScannerPreviewEnabled(false);
  }, [setScannerPreviewEnabled]);

  // Poll diagnostics when open
  useEffect(() => {
    if (!isDiagnosticsOpen || !isScannerActive) return;
    const interval = setInterval(() => {
      setDiagnostics(getScannerDiagnostics());
    }, 250);
    return () => clearInterval(interval);
  }, [isDiagnosticsOpen, isScannerActive, getScannerDiagnostics]);

  const effectiveIsStarved = isStarved || !!diagnostics?.isStarved;
  const effectiveStarvationReason = starvationReason || diagnostics?.starvationReason;

  if (!isScannerActive && !isScannerInitializing) return null;

  const renderRegionCard = (
    regionIndex: 0 | 1,
    title: string,
    description: string
  ) => (
    <Box
      sx={{
        backgroundColor: 'var(--gl-border-subtle)',
        border: '1px solid var(--gl-surface-hover)',
        borderRadius: '4px',
        p: 1.5,
        display: 'flex',
        flexDirection: 'column',
        gap: 1.25
      }}
    >
      {/* Title */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Typography
          variant="caption"
          sx={{
            fontFamily: '"Roboto Mono", monospace',
            fontSize: '0.82rem',
            color: 'var(--gl-text-primary)',
            fontWeight: 700
          }}
        >
          {title}
        </Typography>
        <Tooltip title={description}>
          <InfoOutlinedIcon sx={{ fontSize: 16, color: 'var(--gl-text-muted)', cursor: 'pointer' }} />
        </Tooltip>
      </Box>

      {/* Direct 6-Letter Preview Viewport (Rust Breeder Style) */}
      <Box
        sx={{
          width: '100%',
          height: 52,
          backgroundColor: 'var(--gl-app-bg)',
          borderRadius: '3px',
          border: '1px solid var(--gl-border-strong)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          overflow: 'hidden'
        }}
      >
        {scannerPreviews[regionIndex] ? (
          <Box
            component="img"
            src={scannerPreviews[regionIndex]}
            alt={title}
            sx={{ width: '100%', height: '100%', objectFit: 'fill', imageRendering: 'crisp-edges' }}
          />
        ) : (
          <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontSize: '0.72rem', fontFamily: 'monospace' }}>
            {isScannerInitializing ? 'Starting OCR Workers...' : 'Waiting for video...'}
          </Typography>
        )}
      </Box>

      {/* Calibration Controls (D-Pad Left, Zoom Right) */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pt: 0.25 }}>
        {/* D-Pad */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: 'repeat(3, 24px)',
            gridTemplateRows: 'repeat(3, 24px)',
            gap: '2px',
            alignItems: 'center',
            justifyItems: 'center'
          }}
        >
          <Box />
          <Tooltip title="Nudge Up">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => moveScannerRegion(regionIndex, 0, -1), () => moveScannerRegion(regionIndex, 0, -1))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ArrowUpwardIcon sx={{ fontSize: 14 }} />
            </IconButton>
          </Tooltip>
          <Box />

          <Tooltip title="Nudge Left">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => moveScannerRegion(regionIndex, -1, 0), () => moveScannerRegion(regionIndex, -1, 0))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ArrowBackIcon sx={{ fontSize: 14 }} />
            </IconButton>
          </Tooltip>
          <Box
            sx={{
              width: 14,
              height: 14,
              borderRadius: '2px',
              border: '1px solid var(--gl-text-faint)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center'
            }}
          >
            <Typography variant="caption" sx={{ fontSize: '0.55rem', color: 'var(--gl-text-muted)', fontFamily: 'monospace' }}>
              {regionIndex === 0 ? 'I' : 'P'}
            </Typography>
          </Box>
          <Tooltip title="Nudge Right">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => moveScannerRegion(regionIndex, 1, 0), () => moveScannerRegion(regionIndex, 1, 0))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ArrowForwardIcon sx={{ fontSize: 14 }} />
            </IconButton>
          </Tooltip>

          <Box />
          <Tooltip title="Nudge Down">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => moveScannerRegion(regionIndex, 0, 1), () => moveScannerRegion(regionIndex, 0, 1))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ArrowDownwardIcon sx={{ fontSize: 14 }} />
            </IconButton>
          </Tooltip>
          <Box />
        </Box>

        {/* Zoom Controls */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: '3px' }}>
          <Tooltip title="Enlarge Region">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => scaleScannerRegion(regionIndex, 2), () => scaleScannerRegion(regionIndex, 2))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ZoomInIcon sx={{ fontSize: 15 }} />
            </IconButton>
          </Tooltip>
          <Tooltip title="Shrink Region">
            <IconButton
              size="small"
              onMouseDown={() => startHold(() => scaleScannerRegion(regionIndex, -2), () => scaleScannerRegion(regionIndex, -2))}
              onMouseUp={stopHold}
              onMouseLeave={stopHold}
              sx={{ color: 'var(--gl-text-muted)', p: 0.25, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <ZoomOutIcon sx={{ fontSize: 15 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Box>
    </Box>
  );

  return (
    <Paper
      elevation={8}
      sx={{
        position: 'fixed',
        bottom: 20,
        right: 20,
        width: 330,
        maxWidth: 'calc(100vw - 40px)',
        backgroundColor: 'var(--gl-elevated-bg)',
        border: '1px solid var(--gl-surface-hover)',
        borderRadius: '6px',
        zIndex: 1300,
        overflow: 'hidden',
        boxShadow: '0 12px 40px rgba(0, 0, 0, 0.75)'
      }}
    >
      {/* Header */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          px: 2,
          py: 1.25,
          backgroundColor: 'var(--gl-elevated-bg)',
          borderBottom: '1px solid var(--gl-surface)'
        }}
      >
        <Typography
          variant="body2"
          sx={{
            fontWeight: 700,
            color: 'var(--gl-text-primary)',
            fontFamily: '"Roboto Mono", monospace',
            fontSize: '0.88rem'
          }}
        >
          Scanner Preview
        </Typography>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
          <Tooltip title={isDiagnosticsOpen ? 'Hide Diagnostics' : 'Dev Diagnostics'}>
            <IconButton
              size="small"
              onClick={() => setIsDiagnosticsOpen(!isDiagnosticsOpen)}
              sx={{ color: isDiagnosticsOpen ? 'var(--gl-primary)' : 'var(--gl-text-muted)', p: 0.5 }}
            >
              <BugReportIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>

          <Tooltip title="Stop Scanner">
            <IconButton
              size="small"
              onClick={stopScanner}
              sx={{ color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-error)' }, p: 0.5 }}
            >
              <CloseIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Box>

      {/* Diagnostics */}
      {isDiagnosticsOpen && (
        <Box sx={{ p: 1.5, backgroundColor: 'var(--gl-panel-bg)', borderBottom: '1px solid var(--gl-border)' }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 0.5, fontSize: '0.65rem', color: 'var(--gl-text-secondary)', fontFamily: 'monospace' }}>
            <Box>FPS: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.fps ?? 0}</span></Box>
            <Box>Res: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.captureResolution ?? '0x0'}</span></Box>
            <Box>Stage: <span style={{ color: 'var(--gl-primary)' }}>{diagnostics?.pipelineStage ?? 'idle'}</span></Box>
            <Box>Stage Age: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.pipelineStageAgeMs ?? 0}ms</span></Box>
            <Box>Tick Gap: <span style={{ color: (diagnostics?.tickGapMs ?? 0) > 150 ? 'var(--gl-error)' : 'var(--gl-text-primary)' }}>{diagnostics?.tickGapMs ?? 0}ms</span></Box>
            <Box>Frame Age: <span style={{ color: (diagnostics?.videoFrameAgeMs ?? 0) > 600 ? 'var(--gl-error)' : 'var(--gl-text-primary)' }}>{diagnostics?.videoFrameAgeMs ?? 0}ms</span></Box>
            <Box>Frame Gap: <span style={{ color: (diagnostics?.videoFrameGapMs ?? 0) > 450 ? 'var(--gl-error)' : 'var(--gl-text-primary)' }}>{diagnostics?.videoFrameGapMs ?? 0}ms</span></Box>
            <Box>Page: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.pageVisibility ?? 'visible'}</span></Box>
            <Box>OCR: <span style={{ color: (diagnostics?.lastOcrLatencyMs ?? 0) > 140 ? 'var(--gl-error)' : 'var(--gl-text-primary)' }}>{diagnostics?.lastOcrLatencyMs ?? 0}ms</span></Box>
            <Box>Row OCR: <span style={{ color: (diagnostics?.rowOcrLatencyMs ?? 0) > 140 ? 'var(--gl-error)' : 'var(--gl-text-primary)' }}>{diagnostics?.rowOcrLatencyMs ?? 0}ms</span></Box>
            <Box>Slot OCR: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.slotOcrLatencyMs ?? 0}ms</span></Box>
            <Box>Starvation: <span style={{ color: effectiveIsStarved ? 'var(--gl-error)' : 'var(--gl-success)', fontWeight: effectiveIsStarved ? 700 : 400 }}>{effectiveIsStarved ? (effectiveStarvationReason || 'YES') : 'NO'}</span></Box>
            <Box>Confidence: <span style={{ color: 'var(--gl-primary)' }}>{diagnostics?.confidence ?? 0}%</span></Box>
            <Box>Inv Activity: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.inventoryActivity ?? 0}</span></Box>
            <Box>Planter Act: <span style={{ color: 'var(--gl-text-primary)' }}>{diagnostics?.planterActivity ?? 0}</span></Box>
            <Box>Active ROI: <span style={{ color: 'var(--gl-primary)' }}>{diagnostics?.activeRegion ?? 'none'}</span></Box>
            <Box>Accepted: <span style={{ color: 'var(--gl-success)' }}>{diagnostics?.acceptedPlants ?? 0}</span></Box>
          </Box>
        </Box>
      )}

      {/* Cards Body */}
      <Box sx={{ p: 1.5, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {/* Performance / Starvation Hint */}
        {effectiveIsStarved ? (
          <Box
            sx={{
              display: 'flex',
              alignItems: 'flex-start',
              gap: 1,
              backgroundColor: 'rgba(239, 83, 80, 0.12)',
              border: '1px solid rgba(239, 83, 80, 0.7)',
              borderRadius: '4px',
              p: 1.2
            }}
          >
            <WarningAmberIcon sx={{ fontSize: 18, color: 'var(--gl-error)', mt: '1px', flexShrink: 0 }} />
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.4 }}>
              <Typography
                variant="caption"
                sx={{
                  color: 'var(--gl-error)',
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.72rem',
                  fontWeight: 700,
                  lineHeight: 1.3
                }}
              >
                ⚠️ Performance Starvation Detected ({effectiveStarvationReason || 'Capture/OCR Stall'})
              </Typography>
              <Typography
                variant="caption"
                sx={{
                  color: 'var(--gl-error)',
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.7rem',
                  lineHeight: 1.4
                }}
              >
                Rust is starving screen capture and OCR threads. Open Rust F1 console and type <strong>fps.limit 50</strong> (or switch to <strong>Borderless/Windowed</strong> mode) to restore real-time recognition.
              </Typography>
            </Box>
          </Box>
        ) : (
          <Box
            sx={{
              display: 'flex',
              alignItems: 'flex-start',
              gap: 1,
              backgroundColor: 'rgba(255, 167, 38, 0.06)',
              border: '1px solid rgba(255, 167, 38, 0.3)',
              borderRadius: '4px',
              p: 1
            }}
          >
            <InfoOutlinedIcon sx={{ fontSize: 15, color: 'var(--gl-warning)', mt: '1px', flexShrink: 0 }} />
            <Typography
              variant="caption"
              sx={{ color: 'var(--gl-gold)', fontFamily: '"Roboto Mono", monospace', fontSize: '0.7rem', lineHeight: 1.4 }}
            >
              Scanning feels slow or laggy? Run Rust in <strong>Borderless/Windowed</strong> and <strong>cap your in-game FPS to ≤ 50</strong> (F1: <code>fps.limit 50</code>). Uncapped game FPS starves background capture and stalls recognition.
            </Typography>
          </Box>
        )}

        {renderRegionCard(0, 'Inventory Region', 'Scans plants hovered inside inventory or storage boxes.')}
        {renderRegionCard(1, 'Planter Region', 'Scans plants hovered while looking at growing planter boxes.')}

        {/* Bottom Actions Row */}
        <Box sx={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 1.5, pt: 0.5 }}>
          <Button
            size="small"
            onClick={resetScannerRegions}
            sx={{
              color: 'var(--gl-text-muted)',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.75rem',
              fontWeight: 700,
              textTransform: 'uppercase',
              '&:hover': { color: 'var(--gl-text-primary)' }
            }}
          >
            RESET
          </Button>

          <Button
            size="small"
            variant="contained"
            onClick={() => {
              // Regions persist automatically on move/scale
            }}
            sx={{
              backgroundColor: 'var(--gl-primary)',
              color: '#FFFFFF',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.75rem',
              fontWeight: 700,
              textTransform: 'uppercase',
              px: 2,
              py: 0.5,
              boxShadow: 'none',
              '&:hover': { backgroundColor: 'var(--gl-primary)', boxShadow: 'none' }
            }}
          >
            SAVE REGIONS
          </Button>
        </Box>
      </Box>
    </Paper>
  );
};
