import React, { useState, useEffect, useRef } from 'react';
import { Card, CardContent, Typography, Stack, Box, Button, IconButton, ButtonGroup } from '@mui/material';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import TuneIcon from '@mui/icons-material/Tune';

interface ScannerPreviewProps {
  regionIndex: number;
  title: string;
  previewDataUrl?: string;
  onMove: (dx: number, dy: number) => void;
  onScale: (dw: number) => void;
}

export const ScannerPreview: React.FC<ScannerPreviewProps> = ({
  regionIndex,
  title,
  previewDataUrl,
  onMove,
  onScale
}) => {
  const [isConfigOpen, setIsConfigOpen] = useState(false);
  const holdTimerRef = useRef<any>(null);
  const repeatTimerRef = useRef<any>(null);

  const startHold = (action: () => void) => {
    action();
    holdTimerRef.current = setTimeout(() => {
      repeatTimerRef.current = setInterval(() => {
        action();
      }, 50);
    }, 200);
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

  return (
    <Card variant="outlined">
      <CardContent sx={{ p: 2 }}>
        <Stack spacing={1.5}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
              {title}
            </Typography>
            <Button
              size="small"
              variant="outlined"
              startIcon={<TuneIcon fontSize="small" />}
              onClick={() => setIsConfigOpen(!isConfigOpen)}
              sx={{ fontSize: '0.75rem', py: 0.25 }}
            >
              {isConfigOpen ? 'Hide' : 'Calibrate'}
            </Button>
          </Box>

          {/* Video / Crop Feed */}
          <Box
            sx={{
              width: '100%',
              height: 100,
              backgroundColor: '#000000',
              borderRadius: 1.5,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              overflow: 'hidden',
              border: '1px solid rgba(255, 255, 255, 0.1)'
            }}
          >
            {previewDataUrl ? (
              <Box
                component="img"
                src={previewDataUrl}
                alt={title}
                sx={{ maxWidth: '100%', maxHeight: '100%', objectFit: 'contain' }}
              />
            ) : (
              <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                Waiting for video stream...
              </Typography>
            )}
          </Box>

          {/* Calibration Controls */}
          {isConfigOpen && (
            <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1, pt: 1 }}>
              <IconButton
                aria-label="Move scanner preview up"
                size="small"
                sx={{ border: '1px solid', borderColor: 'divider' }}
                onMouseDown={() => startHold(() => onMove(0, -2))}
                onMouseUp={stopHold}
                onMouseLeave={stopHold}
              >
                <ArrowUpwardIcon fontSize="small" />
              </IconButton>

              <Stack direction="row" spacing={1}>
                <IconButton
                  aria-label="Move scanner preview left"
                  size="small"
                  sx={{ border: '1px solid', borderColor: 'divider' }}
                  onMouseDown={() => startHold(() => onMove(-2, 0))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  <ArrowBackIcon fontSize="small" />
                </IconButton>

                <IconButton
                  aria-label="Move scanner preview down"
                  size="small"
                  sx={{ border: '1px solid', borderColor: 'divider' }}
                  onMouseDown={() => startHold(() => onMove(0, 2))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  <ArrowDownwardIcon fontSize="small" />
                </IconButton>

                <IconButton
                  aria-label="Move scanner preview right"
                  size="small"
                  sx={{ border: '1px solid', borderColor: 'divider' }}
                  onMouseDown={() => startHold(() => onMove(2, 0))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  <ArrowForwardIcon fontSize="small" />
                </IconButton>
              </Stack>

              <ButtonGroup size="small" variant="outlined">
                <Button
                  startIcon={<ZoomOutIcon fontSize="small" />}
                  onMouseDown={() => startHold(() => onScale(-2))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  Zoom Out
                </Button>
                <Button
                  startIcon={<ZoomInIcon fontSize="small" />}
                  onMouseDown={() => startHold(() => onScale(2))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  Zoom In
                </Button>
              </ButtonGroup>
            </Box>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
};
