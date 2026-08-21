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
            backgroundColor: 'var(--gl-panel-header-bg)',
            border: '1px solid var(--gl-border)',
            borderRadius: '4px',
            color: 'var(--gl-text-primary)',
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
          color: 'var(--gl-text-primary)',
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
          color: 'var(--gl-text-secondary)'
        }}
      >
        {/* Step 1 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)' }}>
            1. For best results, use <strong>Chrome</strong>, <strong>Edge</strong>, or <strong>Firefox</strong>. Other browsers may not work correctly.
          </Typography>
        </Box>

        {/* Step 2 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)', mb: 0.5 }}>
            2. By default, the scanner is configured to work with the game running with the following settings:
          </Typography>
          <Box sx={{ pl: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • SCREEN MODE: <strong>FULLSCREEN</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • RESOLUTION: <strong>16:9 screen ratio (1600x900, 1920x1080, 2560x1440, or 4K)</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • USER INTERFACE SCALE: <strong>1.00</strong>
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • Item ownership display is disabled. Run the command{' '}
              <code style={{ backgroundColor: 'var(--gl-input-bg)', padding: '2px 6px', borderRadius: '3px', border: '1px solid var(--gl-surface-hover)', color: 'var(--gl-primary)' }}>
                inventory.show_item_ownership false
              </code>{' '}
              in Rust console to turn it off.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • Windows <strong>HDR mode</strong> is not supported.
            </Typography>
          </Box>
        </Box>

        {/* Step 3 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)', mb: 1 }}>
            3. <strong style={{ color: 'var(--gl-primary)' }}>[NEW!]</strong> If you want to run the scanner with a different screen ratio or conflicting Rust settings, start scanning and adjust the screen regions by clicking the gear ⚙ icon. A properly configured scanner should have a preview like this:
          </Typography>
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 1 }}>
            <SaplingGeneRepr sapling={SAMPLE_SCAN_SAPLING} size="medium" showConnectors={true} />
          </Box>
        </Box>

        {/* Step 4 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)', mb: 0.5 }}>
            4. After the scanning begins, you need to display the genes on the screen by either:
          </Typography>
          <Box sx={{ pl: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • clicking on a Plant in your inventory or storage,
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • looking at a planted Plant.
            </Typography>
          </Box>
          <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-muted)', display: 'block', mt: 0.75 }}>
            It takes about a second to capture each Plant.
          </Typography>
        </Box>

        {/* Step 5 */}
        <Box>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)' }}>
            5. Enjoy! If it doesn't work, let me know on Discord!
          </Typography>
        </Box>

        {/* Phone camera scanner */}
        <Box sx={{ borderTop: '1px solid var(--gl-border)', pt: 1.5 }}>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-primary)', fontWeight: 700, mb: 0.5 }}>
            On a phone: Phone Camera Scanner <span style={{ color: 'var(--gl-primary)' }}>[BETA]</span>
          </Typography>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.85rem', color: 'var(--gl-text-secondary)', mb: 0.5 }}>
            Screen capture is still the recommended flow on desktop. On a phone you can instead point the
            rear camera at the monitor running Rust. Tap <strong>START</strong> on the camera banner below the
            header, or use "Use phone camera" in the header menu.
          </Typography>
          <Box sx={{ pl: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • Requires an <strong>HTTPS</strong> page and camera permission. Permission is asked only after you choose Open camera.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • All six genes must be visible, in focus, and free of glare. Landscape orientation works best.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • Hold the phone roughly facing the monitor — up to about a <strong>35°</strong> angle is supported.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-secondary)' }}>
              • Camera frames are processed on your device. Nothing from the camera is uploaded or saved.
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontSize: '0.8rem', color: 'var(--gl-text-muted)' }}>
              • The border locks onto the row automatically — no calibration. If two gene rows are visible, tap the one you want.
            </Typography>
          </Box>
        </Box>
      </DialogContent>

      <DialogActions sx={{ p: '12px 24px', display: 'flex', justifyContent: 'flex-end', gap: 1.5, borderTop: '1px solid var(--gl-border)' }}>
        <Button
          onClick={() => setIsScannerGuideOpen(false)}
          sx={{
            color: 'var(--gl-text-muted)',
            fontWeight: 700,
            fontFamily: 'monospace',
            '&:hover': { color: 'var(--gl-text-primary)', backgroundColor: 'transparent' }
          }}
        >
          CLOSE
        </Button>

        <Button
          variant="contained"
          onClick={handleStartScan}
          sx={{
            backgroundColor: 'var(--gl-primary)',
            color: 'var(--gl-on-accent)',
            fontWeight: 800,
            fontFamily: 'monospace',
            px: 2.5,
            '&:hover': { backgroundColor: 'var(--gl-primary-hover)' }
          }}
        >
          SCAN
        </Button>
      </DialogActions>
    </Dialog>
  );
};
