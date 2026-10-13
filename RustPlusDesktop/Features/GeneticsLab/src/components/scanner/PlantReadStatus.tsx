import React, { useEffect, useState } from 'react';
import { Box, Typography } from '@mui/material';
import { useScanner } from '../../context/ScannerContext.tsx';
import type { ScannerDiagnostics } from '../../services/scanner/scannerTypes.ts';
import { PlantIcon, plantLabel } from '../common/PlantPicker.tsx';

/**
 * The last plant-name read in the HUD: the plant it named, or what the letters said when
 * they named none, over the crop it read with the band outlined (green: read, red: missed).
 * A screenshot of this shows why a clone landed in the wrong group.
 */
export const PlantReadStatus: React.FC = () => {
  const { getScannerDiagnostics, isScannerActive } = useScanner();
  const [read, setRead] = useState<ScannerDiagnostics['plantRead']>();

  useEffect(() => {
    if (!isScannerActive) return;
    const poll = () => setRead(getScannerDiagnostics()?.plantRead);
    poll();
    const timer = window.setInterval(poll, 1000);
    return () => window.clearInterval(timer);
  }, [isScannerActive, getScannerDiagnostics]);

  if (!read) return null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, borderTop: '1px solid rgba(255, 255, 255, 0.06)', pt: 0.75 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, minWidth: 0 }}>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.68rem', fontWeight: 600 }}>
          PLANT:
        </Typography>
        {read.plant ? (
          <>
            <PlantIcon plant={read.plant} size={16} />
            <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontSize: '0.7rem', fontWeight: 800 }}>
              {plantLabel(read.plant)}
            </Typography>
          </>
        ) : (
          <Typography variant="caption" sx={{ color: 'var(--gl-error)', fontSize: '0.7rem', fontWeight: 800 }}>
            not read
          </Typography>
        )}
        <Typography
          variant="caption"
          sx={{ ml: 'auto', color: 'var(--gl-text-muted)', fontSize: '0.62rem', fontFamily: 'monospace', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}
        >
          {read.regionType} · saw "{read.text || '—'}" · {read.glyphs} letters
        </Typography>
      </Box>
      <Box
        component="img"
        src={read.imageUrl}
        alt={`Plant name crop: ${read.text || 'no letters found'}`}
        sx={{ display: 'block', maxWidth: '100%', maxHeight: 64, objectFit: 'contain', alignSelf: 'flex-start', borderRadius: '3px', imageRendering: 'pixelated' }}
      />
    </Box>
  );
};
