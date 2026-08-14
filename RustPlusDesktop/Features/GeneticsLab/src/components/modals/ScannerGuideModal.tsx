import React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Typography,
  Box,
  Button
} from '@mui/material';
import { useApp } from '../../context/AppContext.tsx';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';

const SAMPLE_SCAN_SAPLING = new Sapling('XGGWYX');

export const ScannerGuideModal: React.FC = () => {
  const {
    isScannerGuideOpen,
    setIsScannerGuideOpen,
    startScanner
  } = useApp();

  const handleStartScan = async () => {
    setIsScannerGuideOpen(false);
    await startScanner();
  };

  return (
    <Dialog
      open={isScannerGuideOpen}
      onClose={() => setIsScannerGuideOpen(false)}
      maxWidth="sm"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: '#181818',
            border: '1px solid #282828',
            borderRadius: '4px',
            color: '#E0E0E0',
            maxHeight: '90vh'
          }
        }
      }}
    >
      <DialogTitle
        sx={{
          m: 0,
          p: '20px 24px 12px',
          fontWeight: 800,
          fontFamily: '"Roboto Mono", monospace',
          color: '#FFFFFF',
          fontSize: '1.25rem'
        }}
      >
        How to Scan Rust for Plants?
      </DialogTitle>

      <DialogContent
        sx={{
          p: '12px 24px 20px',
          display: 'flex',
          flexDirection: 'column',
          gap: 2,
          fontFamily: '"Roboto Mono", monospace',
          fontSize: '0.85rem',
          lineHeight: 1.6,
          color: '#CCCCCC'
        }}
      >
        {/* Step 1 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: '#CCCCCC' }}>
            1. For best results, use <strong>Chrome</strong>, <strong>Edge</strong>, or <strong>Firefox</strong>. Other browsers may not work correctly.
          </Typography>
        </Box>

        {/* Step 2 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: '#CCCCCC', mb: 0.5 }}>
            2. By default, the scanner is configured to work with the game running with the following settings:
          </Typography>
          <Box sx={{ pl: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • SCREEN MODE: <strong>FULLSCREEN</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • RESOLUTION: <strong>16:9 screen ratio (1600x900, 1920x1080, 2560x1440, or 4K)</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • USER INTERFACE SCALE: <strong>1.00</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • Item ownership display is disabled. Run the command{' '}
              <code style={{ backgroundColor: '#111111', padding: '2px 6px', borderRadius: '3px', border: '1px solid #333', color: '#00E5FF' }}>
                inventory.show_item_ownership false
              </code>{' '}
              in Rust console to turn it off.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • Windows <strong>HDR mode</strong> is not supported.
            </Typography>
          </Box>
        </Box>

        {/* Step 3 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: '#CCCCCC', mb: 1 }}>
            3. <strong style={{ color: '#00E5FF' }}>[NEW!]</strong> If you want to run the scanner with a different screen ratio or conflicting Rust settings, start scanning and adjust the screen regions by clicking the gear ⚙ icon. A properly configured scanner should have a preview like this:
          </Typography>
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 1 }}>
            <SaplingGeneRepr sapling={SAMPLE_SCAN_SAPLING} size="medium" showConnectors={true} />
          </Box>
        </Box>

        {/* Step 4 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: '#CCCCCC', mb: 0.5 }}>
            4. After the scanning begins, you need to display the genes on the screen by either:
          </Typography>
          <Box sx={{ pl: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • clicking on a Plant in your inventory or storage,
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#B0B0B0' }}>
              • looking at a planted Plant.
            </Typography>
          </Box>
          <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#888888', display: 'block', mt: 0.75 }}>
            It takes about a second to capture each Plant.
          </Typography>
        </Box>

        {/* Step 5 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: '#CCCCCC' }}>
            5. Enjoy! If it doesn't work, let me know on Discord!
          </Typography>
        </Box>
      </DialogContent>

      <DialogActions sx={{ p: '12px 24px', display: 'flex', justifyContent: 'flex-end', gap: 1.5, borderTop: '1px solid #282828' }}>
        <Button
          onClick={() => setIsScannerGuideOpen(false)}
          sx={{
            color: '#8E8E8E',
            fontWeight: 700,
            fontFamily: 'monospace',
            '&:hover': { color: '#FFFFFF', backgroundColor: 'transparent' }
          }}
        >
          CLOSE
        </Button>

        <Button
          variant="contained"
          onClick={handleStartScan}
          sx={{
            backgroundColor: '#00E5FF',
            color: '#000000',
            fontWeight: 800,
            fontFamily: 'monospace',
            px: 2.5,
            '&:hover': { backgroundColor: '#33EBFF' }
          }}
        >
          SCAN
        </Button>
      </DialogActions>
    </Dialog>
  );
};
