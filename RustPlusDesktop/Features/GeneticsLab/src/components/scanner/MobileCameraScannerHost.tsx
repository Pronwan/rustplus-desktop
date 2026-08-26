import React, { Suspense, lazy } from 'react';
import { Box, CircularProgress } from '@mui/material';
import { useScanner } from '../../context/ScannerContext.tsx';

/**
 * Mount point for the phone camera scanner.
 *
 * The surface (and, from Phase 2, the vision runtime behind it) is only fetched once the
 * user explicitly opens camera mode, so desktop users never download it.
 */
const MobileCameraScanner = lazy(() => import('./MobileCameraScanner.tsx'));

export const MobileCameraScannerHost: React.FC = () => {
  const { isCameraScannerOpen } = useScanner();

  if (!isCameraScannerOpen) return null;

  return (
    <Suspense
      fallback={
        <Box
          role="status"
          aria-label="Loading camera scanner"
          sx={{
            position: 'fixed',
            inset: 0,
            zIndex: 2000,
            backgroundColor: '#000000',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center'
          }}
        >
          <CircularProgress size={28} />
        </Box>
      }
    >
      <MobileCameraScanner />
    </Suspense>
  );
};
