import React from 'react';
import { Paper, Box, Typography, IconButton, Button, Tooltip, CircularProgress } from '@mui/material';
import StopIcon from '@mui/icons-material/Stop';
import SettingsIcon from '@mui/icons-material/Settings';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { useScanner } from '../../context/ScannerContext.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

export const CompactScannerStatus: React.FC = () => {
  const {
    isScannerActive,
    isScannerInitializing,
    lastScannedGenes,
    lastConfidence,
    stopScanner,
    setIsCalibrationModalOpen
  } = useScanner();

  if (!isScannerActive && !isScannerInitializing) return null;

  return (
    <Paper
      elevation={8}
      sx={{
        position: 'fixed',
        bottom: 24,
        right: 24,
        zIndex: 1300,
        p: 1.5,
        backgroundColor: '#181818',
        border: '1.5px solid #00E5FF',
        borderRadius: '8px',
        boxShadow: '0 8px 32px rgba(0, 229, 255, 0.25)',
        display: 'flex',
        alignItems: 'center',
        gap: 2
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        {isScannerInitializing ? (
          <CircularProgress size={16} sx={{ color: '#00E5FF' }} />
        ) : (
          <AutoAwesomeIcon sx={{ fontSize: 18, color: '#00E5FF', animation: 'pulse 2s infinite' }} />
        )}

        <Box sx={{ display: 'flex', flexDirection: 'column' }}>
          <Typography variant="caption" sx={{ fontWeight: 800, color: '#00E5FF', fontFamily: 'monospace', fontSize: '0.75rem' }}>
            {isScannerInitializing ? 'INITIALIZING OCR…' : 'LIVE SCANNER ACTIVE'}
          </Typography>
          <Typography variant="caption" sx={{ color: '#888888', fontSize: '0.68rem' }}>
            Hover clone tooltips in Rust
          </Typography>
        </Box>
      </Box>

      {lastScannedGenes && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, pl: 1, borderLeft: '1px solid #333' }}>
          <GeneticsSequence genes={lastScannedGenes} size="small" />
          <Typography variant="caption" sx={{ color: '#4CAF50', fontWeight: 800, fontFamily: 'monospace' }}>
            {lastConfidence}%
          </Typography>
        </Box>
      )}

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
        <Tooltip title="Scanner Calibration & ROIs" arrow>
          <IconButton
            size="small"
            onClick={() => setIsCalibrationModalOpen(true)}
            sx={{ color: '#888888', '&:hover': { color: '#00E5FF' } }}
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
