import React, { useCallback, useState } from 'react';
import { Box, Button, IconButton, Typography, useMediaQuery, useTheme } from '@mui/material';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import CloseIcon from '@mui/icons-material/Close';
import { useScanner } from '../../context/ScannerContext.tsx';
import { StorageService } from '../../services/storageService.ts';

/**
 * Compact discovery banner for the phone camera scanner.
 *
 * On a phone the desktop "SCAN" action cannot work, so the camera entry needs to be visible
 * rather than buried in the overflow menu. It stays one line tall, and dismissing it is
 * remembered — the overflow menu keeps the action available afterwards.
 */
export const CameraScannerBanner: React.FC = () => {
  const theme = useTheme();
  const isCompact = useMediaQuery(theme.breakpoints.down('sm'));
  const {
    isCameraScannerSupported,
    isCameraSecureOrigin,
    isCameraScannerOpen,
    openCameraScanner
  } = useScanner();

  const [isDismissed, setIsDismissed] = useState(() => StorageService.getOptions().hidePhoneCameraBanner === true);

  const dismiss = useCallback(() => {
    setIsDismissed(true);
    StorageService.saveOptions({ hidePhoneCameraBanner: true });
  }, []);

  if (!isCompact || !isCameraScannerSupported || !isCameraSecureOrigin) return null;
  if (isDismissed || isCameraScannerOpen) return null;

  return (
    <Box
      role="region"
      aria-label="Phone camera scanner available"
      sx={{
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        px: 1.25,
        py: 0.25,
        borderBottom: '1px solid var(--gl-border)',
        backgroundColor: 'var(--gl-panel-header-bg)'
      }}
    >
      <PhotoCameraIcon sx={{ fontSize: 18, color: 'var(--gl-primary)', flexShrink: 0 }} />

      <Typography
        sx={{
          flex: 1,
          minWidth: 0,
          fontFamily: 'monospace',
          fontSize: '0.72rem',
          lineHeight: 1.25,
          color: 'var(--gl-text-secondary)'
        }}
      >
        Scan genetics with your phone camera
        <Box
          component="span"
          sx={{
            ml: 0.75,
            px: 0.5,
            fontSize: '0.58rem',
            borderRadius: '3px',
            backgroundColor: '#F59E0B',
            color: '#111827',
            fontWeight: 900
          }}
        >
          BETA
        </Box>
      </Typography>

      <Button
        size="small"
        variant="contained"
        onClick={openCameraScanner}
        sx={{
          flexShrink: 0,
          // Primary mobile call to action, so it keeps the full 44px touch target.
          minHeight: 44,
          px: 1.5,
          fontSize: '0.68rem',
          fontWeight: 800,
          fontFamily: 'monospace',
          whiteSpace: 'nowrap'
        }}
      >
        START
      </Button>

      <IconButton
        aria-label="Dismiss phone camera scanner banner"
        onClick={dismiss}
        size="small"
        sx={{ flexShrink: 0, width: 44, height: 44, color: 'var(--gl-text-muted)' }}
      >
        <CloseIcon sx={{ fontSize: 16 }} />
      </IconButton>
    </Box>
  );
};
