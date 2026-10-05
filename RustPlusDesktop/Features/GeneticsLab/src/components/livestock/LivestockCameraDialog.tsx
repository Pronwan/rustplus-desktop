import React, { useEffect, useRef, useState } from 'react';
import { Box, Button, Dialog, IconButton, Typography, useMediaQuery, useTheme } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { useLivestock } from '../../context/LivestockContext.tsx';
import { LivestockGenePanel } from './LivestockGeneBadges.tsx';
import { ScanKindSelect } from './ScanKindSelect.tsx';

/**
 * Phone camera pointed at the monitor. The whole frame is searched, so the only instruction
 * is "get the gene panel in view"; badges outline themselves once found.
 */
export const LivestockCameraDialog: React.FC<{ open: boolean; onClose: () => void }> = ({ open, onClose }) => {
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const { scan, startCameraScan, stopScan } = useLivestock();
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const [videoBox, setVideoBox] = useState({ width: 0, height: 0 });

  useEffect(() => {
    if (!open) return;
    // Start once the video element exists; the dialog mounts it on open.
    const id = window.setTimeout(() => {
      if (videoRef.current) void startCameraScan(videoRef.current);
    }, 0);
    return () => {
      window.clearTimeout(id);
      stopScan();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    const video = videoRef.current;
    if (!open || !video || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => setVideoBox({ width: video.clientWidth, height: video.clientHeight }));
    observer.observe(video);
    return () => observer.disconnect();
  }, [open]);

  const live = scan.source === 'camera' ? scan.live : null;
  const frame = scan.frameSize;

  // Map frame coordinates onto the letterboxed (object-fit: contain) video.
  let overlay: React.ReactNode = null;
  if (live && frame && videoBox.width > 0) {
    const scale = Math.min(videoBox.width / frame.width, videoBox.height / frame.height);
    const offsetX = (videoBox.width - frame.width * scale) / 2;
    const offsetY = (videoBox.height - frame.height * scale) / 2;
    overlay = [...live.top.badges, ...(live.bottom?.badges ?? [])].map((badge, i) => (
      <Box
        key={i}
        sx={{
          position: 'absolute',
          left: offsetX + (badge.cx - badge.diameter / 2) * scale,
          top: offsetY + (badge.cy - badge.diameter / 2) * scale,
          width: badge.diameter * scale,
          height: badge.diameter * scale,
          borderRadius: '50%',
          border: `2px solid ${badge.detected ? '#00E5FF' : '#FFB300'}`,
          pointerEvents: 'none'
        }}
      />
    ));
  }

  const close = () => {
    stopScan();
    onClose();
  };

  return (
    <Dialog open={open} onClose={close} fullScreen={fullScreen} maxWidth="md" fullWidth>
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', px: 2, py: 1, gap: 1 }}>
        <Typography sx={{ fontWeight: 800 }}>Scan livestock with camera</Typography>
        <IconButton aria-label="Close camera" onClick={close}>
          <CloseIcon />
        </IconButton>
      </Box>
      <Box sx={{ position: 'relative', backgroundColor: '#000', flex: fullScreen ? 1 : undefined, minHeight: 260 }}>
        <Box
          component="video"
          ref={videoRef}
          muted
          playsInline
          sx={{ display: 'block', width: '100%', height: fullScreen ? '100%' : 'auto', maxHeight: fullScreen ? 'none' : '60vh', objectFit: 'contain' }}
        />
        <Box sx={{ position: 'absolute', inset: 0, pointerEvents: 'none' }}>{overlay}</Box>
        {scan.status === 'starting' && (
          <Typography sx={{ position: 'absolute', inset: 0, display: 'grid', placeItems: 'center', color: '#fff' }}>
            Starting camera...
          </Typography>
        )}
      </Box>
      <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
          <ScanKindSelect />
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)' }}>
            {scan.error
              ? scan.error
              : live
                ? 'Panel found. Hold steady; the animal is added as soon as two frames agree.'
                : 'Point the camera at the animal\'s gene panel on your monitor.'}
          </Typography>
        </Box>
        {live && <LivestockGenePanel rows={live.rows} size="md" />}
        <Button variant="outlined" onClick={close} sx={{ alignSelf: 'flex-start' }}>
          Done
        </Button>
      </Box>
    </Dialog>
  );
};
