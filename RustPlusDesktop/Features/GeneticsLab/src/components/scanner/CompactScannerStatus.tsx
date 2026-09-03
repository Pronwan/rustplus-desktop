import React, { useState } from 'react';
import { Paper, Box, Typography, IconButton, Button, Tooltip, CircularProgress } from '@mui/material';
import StopIcon from '@mui/icons-material/Stop';
import SettingsIcon from '@mui/icons-material/Settings';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import AutoFixHighIcon from '@mui/icons-material/AutoFixHigh';
import CropFreeIcon from '@mui/icons-material/CropFree';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import { useScanner } from '../../context/ScannerContext.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';
import { ScanningRegionsView } from './ScanningRegionsView.tsx';

export const CompactScannerStatus: React.FC = () => {
  const {
    isScannerActive,
    isScannerInitializing,
    lastScannedGenes,
    lastConfidence,
    stopScanner,
    setIsCalibrationModalOpen,
    autoCalibrateScanner,
    isAutoCalibrating
  } = useScanner();

  // Always start expanded so users see both regions and controls immediately
  const [isExpanded, setIsExpanded] = useState(true);

  if (!isScannerActive && !isScannerInitializing) return null;

  // Expanded View: Full Scanning Regions & Calibration outside without modal
  if (isExpanded) {
    return (
      <Paper
        elevation={10}
        sx={{
          position: 'fixed',
          bottom: 20,
          right: 20,
          zIndex: 1300,
          width: { xs: 340, sm: 390 },
          maxWidth: 'calc(100vw - 32px)',
          maxHeight: 'calc(100vh - 40px)',
          overflowY: 'auto',
          p: 1.5,
          backgroundColor: 'rgba(16, 20, 26, 0.96)',
          backdropFilter: 'blur(20px)',
          border: '1px solid rgba(0, 229, 255, 0.3)',
          borderRadius: '10px',
          boxShadow: '0 16px 48px rgba(0, 0, 0, 0.8), 0 0 24px rgba(0, 229, 255, 0.12)',
          display: 'flex',
          flexDirection: 'column',
          gap: 1.25,
          '&::-webkit-scrollbar': { width: 5 },
          '&::-webkit-scrollbar-thumb': { backgroundColor: 'rgba(255, 255, 255, 0.1)', borderRadius: 3 }
        }}
      >
        {/* Expanded Header */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderBottom: '1px solid rgba(255, 255, 255, 0.08)', pb: 1 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Box
              sx={{
                width: 8,
                height: 8,
                borderRadius: '50%',
                backgroundColor: 'var(--gl-primary, #00E5FF)',
                boxShadow: '0 0 8px #00E5FF',
                animation: 'pulse 2s infinite'
              }}
            />
            <Box sx={{ display: 'flex', flexDirection: 'column' }}>
              <Typography variant="caption" sx={{ fontWeight: 800, color: 'var(--gl-primary)', letterSpacing: '0.04em', fontSize: '0.78rem' }}>
                SCANNER HUD
              </Typography>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.64rem' }}>
                Active Tooltip Tracking
              </Typography>
            </Box>
          </Box>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
            <Tooltip title="Collapse to compact status bar" arrow>
              <IconButton
                aria-label="Collapse scanner HUD"
                size="small"
                onClick={() => setIsExpanded(false)}
                sx={{
                  color: 'var(--gl-text-muted)',
                  p: 0.35,
                  borderRadius: '4px',
                  '&:hover': { color: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
                }}
              >
                <KeyboardArrowDownIcon sx={{ fontSize: 20 }} />
              </IconButton>
            </Tooltip>

            <Button
              size="small"
              variant="contained"
              color="error"
              onClick={stopScanner}
              startIcon={<StopIcon sx={{ fontSize: 13 }} />}
              sx={{
                fontWeight: 800,
                fontSize: '0.7rem',
                py: 0.25,
                px: 1,
                borderRadius: '4px',
                boxShadow: '0 2px 8px rgba(239, 83, 80, 0.35)'
              }}
            >
              STOP
            </Button>
          </Box>
        </Box>

        {/* Live Scanning Regions Viewport & Controls */}
        <ScanningRegionsView compact onClose={() => setIsExpanded(false)} />

        {/* Bottom Status bar with Last Scanned sequence */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderTop: '1px solid rgba(255, 255, 255, 0.06)', pt: 0.75 }}>
          {lastScannedGenes ? (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.68rem', fontWeight: 600 }}>
                LAST:
              </Typography>
              <GeneticsSequence genes={lastScannedGenes} size="small" />
              <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.7rem' }}>
                {lastConfidence}%
              </Typography>
            </Box>
          ) : (
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.66rem' }}>
              Hover clone tooltips in Rust to scan
            </Typography>
          )}

          <Tooltip title="Open full calibration dialog (JSON import/export)" arrow>
            <IconButton
              aria-label="Open advanced calibration dialog"
              size="small"
              onClick={() => setIsCalibrationModalOpen(true)}
              sx={{ color: 'var(--gl-text-muted)', p: 0.3, '&:hover': { color: 'var(--gl-primary)' } }}
            >
              <SettingsIcon sx={{ fontSize: 15 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Paper>
    );
  }

  // Compact Bar View
  return (
    <Paper
      elevation={8}
      sx={{
        position: 'fixed',
        bottom: 24,
        right: 24,
        zIndex: 1300,
        p: 1.25,
        backgroundColor: 'var(--gl-panel-header-bg)',
        border: '1.5px solid var(--gl-primary)',
        borderRadius: '8px',
        boxShadow: '0 8px 32px rgba(0, 229, 255, 0.25)',
        display: 'flex',
        alignItems: 'center',
        gap: 1.75
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        {isScannerInitializing ? (
          <CircularProgress size={16} sx={{ color: 'var(--gl-primary)' }} />
        ) : (
          <AutoAwesomeIcon sx={{ fontSize: 18, color: 'var(--gl-primary)', animation: 'pulse 2s infinite' }} />
        )}

        <Box sx={{ display: 'flex', flexDirection: 'column' }}>
          <Typography variant="caption" sx={{ fontWeight: 800, color: 'var(--gl-primary)', fontFamily: 'monospace', fontSize: '0.75rem' }}>
            {isScannerInitializing ? 'INITIALIZING OCR…' : 'LIVE SCANNER ACTIVE'}
          </Typography>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.68rem' }}>
            Hover clone tooltips in Rust
          </Typography>
        </Box>
      </Box>

      {lastScannedGenes && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, pl: 1, borderLeft: '1px solid var(--gl-surface-hover)' }}>
          <GeneticsSequence genes={lastScannedGenes} size="small" />
          <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace' }}>
            {lastConfidence}%
          </Typography>
        </Box>
      )}

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
        {/* Show Scanning Regions Outside Button */}
        <Button
          size="small"
          variant="outlined"
          onClick={() => setIsExpanded(true)}
          startIcon={<CropFreeIcon sx={{ fontSize: 14 }} />}
          sx={{
            borderColor: 'var(--gl-primary)',
            color: 'var(--gl-primary)',
            fontWeight: 800,
            fontSize: '0.7rem',
            py: 0.2,
            px: 0.9,
            '&:hover': {
              backgroundColor: 'rgba(0, 229, 255, 0.1)',
              borderColor: 'var(--gl-primary)'
            }
          }}
        >
          Regions
        </Button>

        {/* 1-Click Quick Auto-Calibrate */}
        <Tooltip title="1-Click Auto Calibrate from screen (hover plant in Rust and click)" arrow>
          <IconButton
            size="small"
            onClick={() => autoCalibrateScanner()}
            disabled={isAutoCalibrating}
            aria-label="1-Click Auto Calibrate from screen"
            sx={{
              color: 'var(--gl-primary)',
              border: '1px solid var(--gl-primary)',
              p: 0.4,
              backgroundColor: 'rgba(0, 229, 255, 0.08)',
              '&:hover': { backgroundColor: 'rgba(0, 229, 255, 0.2)' }
            }}
          >
            {isAutoCalibrating ? <CircularProgress size={14} color="inherit" /> : <AutoFixHighIcon sx={{ fontSize: 16 }} />}
          </IconButton>
        </Tooltip>

        <Tooltip title="Scanner Calibration Dialog" arrow>
          <IconButton
            aria-label="Open scanner calibration dialog"
            size="small"
            onClick={() => setIsCalibrationModalOpen(true)}
            sx={{ color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-primary)' } }}
          >
            <SettingsIcon sx={{ fontSize: 17 }} />
          </IconButton>
        </Tooltip>

        <Button
          size="small"
          variant="contained"
          color="error"
          onClick={stopScanner}
          startIcon={<StopIcon sx={{ fontSize: 14 }} />}
          sx={{ fontWeight: 800, fontSize: '0.72rem', py: 0.3 }}
        >
          STOP
        </Button>
      </Box>
    </Paper>
  );
};
