import React from 'react';
import {
  Card,
  CardContent,
  Stack,
  Typography,
  LinearProgress,
  Button,
  Chip,
  Box
} from '@mui/material';
import StopIcon from '@mui/icons-material/Stop';
import FastForwardIcon from '@mui/icons-material/FastForward';
import { useApp } from '../../context/AppContext.tsx';

export const ProgressSection: React.FC = () => {
  const { progress, cancelSimulation, skipCurrentGeneration } = useApp();

  if (!progress || !progress.isRunning) return null;

  const percent = Math.min(100, Math.max(0, Math.round(progress.progressPercent)));

  const formatEta = (seconds: number) => {
    if (seconds <= 0) return 'Almost done...';
    if (seconds < 60) return `${Math.ceil(seconds)}s remaining`;
    const mins = Math.floor(seconds / 60);
    const secs = Math.ceil(seconds % 60);
    return `${mins}m ${secs}s remaining`;
  };

  return (
    <Card variant="outlined" sx={{ borderColor: 'primary.main', backgroundColor: 'rgba(96, 205, 255, 0.04)' }}>
      <CardContent sx={{ p: 2 }}>
        <Stack spacing={1.5}>
          {/* Header Row */}
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Chip
                label={`Generation ${progress.currentGeneration} of ${progress.totalGenerations}`}
                color="primary"
                size="small"
                sx={{ fontWeight: 700 }}
              />
              <Typography variant="body2" sx={{ fontWeight: 600 }}>
                {percent}% Complete
              </Typography>
            </Box>

            <Box sx={{ display: 'flex', gap: 1 }}>
              {progress.currentGeneration < progress.totalGenerations && (
                <Button
                  size="small"
                  variant="outlined"
                  color="warning"
                  startIcon={<FastForwardIcon fontSize="small" />}
                  onClick={skipCurrentGeneration}
                >
                  Skip Generation
                </Button>
              )}

              <Button
                size="small"
                variant="outlined"
                color="error"
                startIcon={<StopIcon fontSize="small" />}
                onClick={cancelSimulation}
              >
                Stop
              </Button>
            </Box>
          </Box>

          {/* Progress Bar */}
          <Box sx={{ width: '100%' }}>
            <LinearProgress
              variant="determinate"
              value={percent}
              sx={{ height: 8, borderRadius: 4 }}
            />
          </Box>

          {/* Metrics Row */}
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              Calculating combinations...
            </Typography>

            <Typography variant="caption" sx={{ color: 'primary.main', fontWeight: 600 }}>
              {formatEta(progress.estimatedTimeRemainingSeconds)}
            </Typography>
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
};
