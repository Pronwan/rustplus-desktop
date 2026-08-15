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
import { useApp } from '../../context/AppContext.tsx';
import { ScannerDiagnostics } from '../../services/scanner/scannerTypes.ts';
import { SCANNER_CONFIG } from '../../services/scanner/scannerConfig.ts';

export const ScannerWidget: React.FC = () => {
  const {
    isScannerActive,
    isScannerInitializing,
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

  if (!isScannerActive && !isScannerInitializing) return null;

  const renderRegionCard = (
    regionIndex: 0 | 1,
    title: string,
    description: string
  ) => (
    <Box
      sx={{
        backgroundColor: '#202020',
        border: '1px solid #333333',
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
            color: '#E0E0E0',
            fontWeight: 700
          }}
        >
          {title}
        </Typography>
        <Tooltip title={description}>
          <InfoOutlinedIcon sx={{ fontSize: 16, color: '#888888', cursor: 'pointer' }} />
        </Tooltip>
      </Box>

      {/* Direct 6-Letter Preview Viewport (Rust Breeder Style) */}
      <Box
        sx={{
          width: '100%',
          height: 52,
          backgroundColor: '#0a0a0a',
          borderRadius: '3px',
          border: '1px solid #3a3a3a',
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
          <Typography variant="caption" sx={{ color: '#555', fontSize: '0.72rem', fontFamily: 'monospace' }}>
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
            gridTemplateColumns: 'repeat(3, 26px)',
            gridTemplateRows: 'repeat(3, 26px)',
            gap: '2px',
            alignItems: 'center',
            justifyItems: 'center'
          }}
        >
          <Box />
          <IconButton
            size="small"
            sx={{ p: 0.25, color: '#FFF' }}
            onMouseDown={() => startHold(
              () => moveScannerRegion(regionIndex, 0, -1),
              () => moveScannerRegion(regionIndex, 0, -3)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
          >
            <ArrowUpwardIcon sx={{ fontSize: 16 }} />
          </IconButton>
          <Box />

          <IconButton
            size="small"
            sx={{ p: 0.25, color: '#FFF' }}
            onMouseDown={() => startHold(
              () => moveScannerRegion(regionIndex, -1, 0),
              () => moveScannerRegion(regionIndex, -3, 0)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
          >
            <ArrowBackIcon sx={{ fontSize: 16 }} />
          </IconButton>
          <DesktopWindowsIcon sx={{ fontSize: 14, color: '#666' }} />
          <IconButton
            size="small"
            sx={{ p: 0.25, color: '#FFF' }}
            onMouseDown={() => startHold(
              () => moveScannerRegion(regionIndex, 1, 0),
              () => moveScannerRegion(regionIndex, 3, 0)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
          >
            <ArrowForwardIcon sx={{ fontSize: 16 }} />
          </IconButton>

          <Box />
          <IconButton
            size="small"
            sx={{ p: 0.25, color: '#FFF' }}
            onMouseDown={() => startHold(
              () => moveScannerRegion(regionIndex, 0, 1),
              () => moveScannerRegion(regionIndex, 0, 3)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
          >
            <ArrowDownwardIcon sx={{ fontSize: 16 }} />
          </IconButton>
          <Box />
        </Box>

        {/* Zoom Buttons */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75, width: 145 }}>
          <Button
            size="small"
            variant="contained"
            endIcon={<ZoomInIcon sx={{ fontSize: 15 }} />}
            onMouseDown={() => startHold(
              () => scaleScannerRegion(regionIndex, -1),
              () => scaleScannerRegion(regionIndex, -3)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
            sx={{
              backgroundColor: '#2B2B2B',
              color: '#E0E0E0',
              fontSize: '0.72rem',
              fontWeight: 700,
              fontFamily: '"Roboto Mono", monospace',
              py: 0.4,
              boxShadow: 'none',
              border: '1px solid #3c3c3c',
              justifyContent: 'space-between',
              px: 1.5,
              '&:hover': { backgroundColor: '#383838', boxShadow: 'none' }
            }}
          >
            ZOOM IN
          </Button>

          <Button
            size="small"
            variant="contained"
            endIcon={<ZoomOutIcon sx={{ fontSize: 15 }} />}
            onMouseDown={() => startHold(
              () => scaleScannerRegion(regionIndex, 1),
              () => scaleScannerRegion(regionIndex, 3)
            )}
            onMouseUp={stopHold}
            onMouseLeave={stopHold}
            sx={{
              backgroundColor: '#2B2B2B',
              color: '#E0E0E0',
              fontSize: '0.72rem',
              fontWeight: 700,
              fontFamily: '"Roboto Mono", monospace',
              py: 0.4,
              boxShadow: 'none',
              border: '1px solid #3c3c3c',
              justifyContent: 'space-between',
              px: 1.5,
              '&:hover': { backgroundColor: '#383838', boxShadow: 'none' }
            }}
          >
            ZOOM OUT
          </Button>
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
        backgroundColor: '#1E1E1E',
        border: '1px solid #333333',
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
          backgroundColor: '#1E1E1E',
          borderBottom: '1px solid #2C2C2C'
        }}
      >
        <Typography
          variant="body2"
          sx={{
            fontWeight: 700,
            color: '#E0E0E0',
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
              sx={{ color: isDiagnosticsOpen ? '#00E5FF' : '#8E8E8E', p: 0.5 }}
            >
              <BugReportIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>

          <Tooltip title="Stop Scanner">
            <IconButton
              size="small"
              onClick={stopScanner}
              sx={{ color: '#8E8E8E', '&:hover': { color: '#E53935' }, p: 0.5 }}
            >
              <CloseIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Box>

      {/* Diagnostics */}
      {isDiagnosticsOpen && (
        <Box sx={{ p: 1.5, backgroundColor: '#141414', borderBottom: '1px solid #282828' }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 0.5, fontSize: '0.65rem', color: '#AAA', fontFamily: 'monospace' }}>
            <Box>FPS: <span style={{ color: '#FFF' }}>{diagnostics?.fps ?? 0}</span></Box>
            <Box>Res: <span style={{ color: '#FFF' }}>{diagnostics?.captureResolution ?? '0x0'}</span></Box>
            <Box>Stage: <span style={{ color: '#00E5FF' }}>{diagnostics?.pipelineStage ?? 'idle'}</span></Box>
            <Box>Stage Age: <span style={{ color: '#FFF' }}>{diagnostics?.pipelineStageAgeMs ?? 0}ms</span></Box>
            <Box>Tick Gap: <span style={{ color: '#FFF' }}>{diagnostics?.tickGapMs ?? 0}ms</span></Box>
            <Box>Frame Age: <span style={{ color: '#FFF' }}>{diagnostics?.videoFrameAgeMs ?? 0}ms</span></Box>
            <Box>Frame Gap: <span style={{ color: '#FFF' }}>{diagnostics?.videoFrameGapMs ?? 0}ms</span></Box>
            <Box>Page: <span style={{ color: '#FFF' }}>{diagnostics?.pageVisibility ?? 'visible'}</span></Box>
            <Box>OCR: <span style={{ color: '#FFF' }}>{diagnostics?.lastOcrLatencyMs ?? 0}ms</span></Box>
            <Box>Row OCR: <span style={{ color: '#FFF' }}>{diagnostics?.rowOcrLatencyMs ?? 0}ms</span></Box>
            <Box>Slot OCR: <span style={{ color: '#FFF' }}>{diagnostics?.slotOcrLatencyMs ?? 0}ms</span></Box>
            <Box>UI Queue: <span style={{ color: '#FFF' }}>{diagnostics?.uiUpdateLatencyMs ?? 0}ms</span></Box>
            <Box>Confidence: <span style={{ color: '#00E5FF' }}>{diagnostics?.confidence ?? 0}%</span></Box>
            <Box>Inv Activity: <span style={{ color: '#FFF' }}>{diagnostics?.inventoryActivity ?? 0}</span></Box>
            <Box>Planter Act: <span style={{ color: '#FFF' }}>{diagnostics?.planterActivity ?? 0}</span></Box>
            <Box>Active ROI: <span style={{ color: '#00E5FF' }}>{diagnostics?.activeRegion ?? 'none'}</span></Box>
            <Box>Accepted: <span style={{ color: '#4CAF50' }}>{diagnostics?.acceptedPlants ?? 0}</span></Box>
          </Box>
        </Box>
      )}

      {/* Cards Body */}
      <Box sx={{ p: 1.5, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {/* Performance hint: fullscreen games at high FPS starve the WebView capture */}
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
          <InfoOutlinedIcon sx={{ fontSize: 15, color: '#FFA726', mt: '1px', flexShrink: 0 }} />
          <Typography
            variant="caption"
            sx={{ color: '#C9A26B', fontFamily: '"Roboto Mono", monospace', fontSize: '0.7rem', lineHeight: 1.4 }}
          >
            Scanning feels slow or laggy? Run Rust in <strong>Borderless/Windowed</strong> and <strong>cap your
            in-game FPS</strong> (e.g. 60-144). A fullscreen game at uncapped FPS starves the screen capture and
            makes recognition stall.
          </Typography>
        </Box>

        {renderRegionCard(0, 'Inventory Region', 'Scans plants hovered inside inventory or storage boxes.')}
        {renderRegionCard(1, 'Planter Region', 'Scans plants hovered while looking at growing planter boxes.')}

        {/* Bottom Actions Row */}
        <Box sx={{ display: 'flex', justifyContent: 'flex-end', alignItems: 'center', gap: 1.5, pt: 0.5 }}>
          <Button
            size="small"
            onClick={resetScannerRegions}
            sx={{
              color: '#888888',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.75rem',
              fontWeight: 700,
              textTransform: 'uppercase',
              '&:hover': { color: '#FFFFFF' }
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
              backgroundColor: '#00838F',
              color: '#FFFFFF',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.75rem',
              fontWeight: 700,
              textTransform: 'uppercase',
              px: 2,
              py: 0.5,
              boxShadow: 'none',
              '&:hover': { backgroundColor: '#0097A7', boxShadow: 'none' }
            }}
          >
            SAVE REGIONS
          </Button>
        </Box>
      </Box>
    </Paper>
  );
};
