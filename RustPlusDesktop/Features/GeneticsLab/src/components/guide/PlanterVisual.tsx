import React, { useState, useEffect } from 'react';
import { Card, CardContent, Typography, Stack, Box, Button, ButtonGroup, Chip, Paper } from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import PauseIcon from '@mui/icons-material/Pause';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';

interface PlanterVisualProps {
  map?: GeneticsMap;
}

const STAGES = [
  {
    title: 'Stage 1: Plant Center Target Plant',
    desc: 'Plant your base plant (or any extra plant of the same type) in the center slot of the planter box, leaving surrounding slots empty.'
  },
  {
    title: 'Stage 2: Wait for Center to Reach Sapling Stage',
    desc: 'Wait for the center plant to grow until it reaches about 50% progress in the Sapling stage (the stage right before Crossbreeding triggers).'
  },
  {
    title: 'Stage 3: Plant Surrounding Donor Parents',
    desc: 'Plant the donor parents in the surrounding slots. When the center plant transitions into Crossbreeding, the surrounding plants donate their genes.'
  },
  {
    title: 'Stage 4: Take Cuttings of the Target Clone',
    desc: 'Once crossbreeding completes, the center plant transforms into your target clone with the desired gene combination! Take cuttings to propagate.'
  }
];

// Mapping of surrounding indices (0..7) to 3x3 grid positions (excluding center index 4)
const SURROUNDING_GRID_SLOTS = [0, 1, 2, 3, 5, 6, 7, 8];

export const PlanterVisual: React.FC<PlanterVisualProps> = ({ map }) => {
  const [activeStep, setActiveStep] = useState(0);
  const [isPlaying, setIsPlaying] = useState(true);

  useEffect(() => {
    if (!isPlaying) return;
    const timer = setInterval(() => {
      setActiveStep((prev) => (prev + 1) % STAGES.length);
    }, 4500);
    return () => clearInterval(timer);
  }, [isPlaying]);

  return (
    <Card
      variant="outlined"
      sx={{
        backgroundColor: '#141414',
        borderColor: '#282828',
        borderRadius: '6px',
        overflow: 'hidden'
      }}
    >
      <CardContent sx={{ p: 2.5 }}>
        <Stack spacing={2}>
          {/* Header Row */}
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <Chip
                label={`Step ${activeStep + 1} of 4`}
                size="small"
                sx={{
                  backgroundColor: '#00E5FF',
                  color: '#000000',
                  fontWeight: 800,
                  fontSize: '0.75rem',
                  borderRadius: '3px'
                }}
              />
              <Typography
                variant="subtitle1"
                sx={{
                  fontWeight: 800,
                  color: '#FFFFFF',
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.95rem'
                }}
              >
                {STAGES[activeStep].title}
              </Typography>
            </Box>

            <ButtonGroup size="small" variant="outlined">
              <Button
                onClick={() => setIsPlaying(!isPlaying)}
                startIcon={isPlaying ? <PauseIcon sx={{ fontSize: 14 }} /> : <PlayArrowIcon sx={{ fontSize: 14 }} />}
                sx={{
                  color: '#CCCCCC',
                  borderColor: '#383838',
                  fontSize: '0.75rem',
                  py: 0.25,
                  '&:hover': { borderColor: '#00E5FF' }
                }}
              >
                {isPlaying ? 'PAUSE' : 'PLAY'}
              </Button>
              <Button
                onClick={() => setActiveStep((s) => (s + 1) % STAGES.length)}
                startIcon={<SkipNextIcon sx={{ fontSize: 14 }} />}
                sx={{
                  color: '#CCCCCC',
                  borderColor: '#383838',
                  fontSize: '0.75rem',
                  py: 0.25,
                  '&:hover': { borderColor: '#00E5FF' }
                }}
              >
                NEXT
              </Button>
            </ButtonGroup>
          </Box>

          <Typography
            variant="body2"
            sx={{
              color: '#AAAAAA',
              fontFamily: '"Roboto Mono", monospace',
              minHeight: 40,
              fontSize: '0.85rem'
            }}
          >
            {STAGES[activeStep].desc}
          </Typography>

          {/* 3x3 Planter Box Visual */}
          <Box
            sx={{
              position: 'relative',
              width: '100%',
              maxWidth: 420,
              aspectRatio: '1/1',
              mx: 'auto',
              borderRadius: '6px',
              backgroundImage: 'url(./img/planter.webp)',
              backgroundSize: 'cover',
              backgroundPosition: 'center',
              border: '2px solid #282828',
              p: 2,
              display: 'grid',
              gridTemplateColumns: 'repeat(3, 1fr)',
              gridTemplateRows: 'repeat(3, 1fr)',
              gap: 1.5
            }}
          >
            {Array.from({ length: 9 }).map((_, slotIdx) => {
              const isCenter = slotIdx === 4;

              // Find which surrounding plant occupies this slot
              const surroundingIndex = SURROUNDING_GRID_SLOTS.indexOf(slotIdx);
              const surroundingParent =
                map && surroundingIndex >= 0 && surroundingIndex < map.crossbreedingSaplings.length
                  ? map.crossbreedingSaplings[surroundingIndex]
                  : null;

              let label = '';
              let badgeColor = '#4CAF50';
              let active = false;
              let saplingToRender = null;

              if (activeStep === 0) {
                // Step 1: Plant Center Target Plant First
                if (isCenter) {
                  label = 'Center Plant';
                  badgeColor = '#00E5FF';
                  active = true;
                  saplingToRender = map?.baseSapling || null;
                }
              } else if (activeStep === 1) {
                // Step 2: Wait for Center to Reach Sapling Stage
                if (isCenter) {
                  label = 'Sapling (50%)';
                  badgeColor = '#00E5FF';
                  active = true;
                  saplingToRender = map?.baseSapling || null;
                }
              } else if (activeStep === 2) {
                // Step 3: Plant Surrounding Donor Parents
                if (isCenter) {
                  label = 'Breeding Center';
                  badgeColor = '#00E5FF';
                  active = true;
                  saplingToRender = map?.baseSapling || null;
                } else if (surroundingParent) {
                  label = surroundingParent.generationIndex > 0 ? `GEN.${surroundingParent.generationIndex}` : `#${surroundingParent.index !== undefined ? surroundingParent.index + 1 : surroundingIndex + 1}`;
                  badgeColor = '#4CAF50';
                  active = true;
                  saplingToRender = surroundingParent;
                }
              } else if (activeStep === 3) {
                // Step 4: Harvest Cuttings of Target God Clone
                if (isCenter) {
                  label = 'Target Clone';
                  badgeColor = '#00E5FF';
                  active = true;
                  saplingToRender = map?.resultSapling || null;
                }
              }

              return (
                <Paper
                  key={slotIdx}
                  elevation={active ? 4 : 0}
                  sx={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    justifyContent: 'center',
                    p: 0.5,
                    backgroundColor: active ? 'rgba(14, 18, 24, 0.92)' : 'rgba(0, 0, 0, 0.45)',
                    border: '1.5px solid',
                    borderColor: active ? badgeColor : 'rgba(255, 255, 255, 0.08)',
                    borderRadius: '4px',
                    backdropFilter: 'blur(4px)',
                    transition: 'all 0.3s ease',
                    overflow: 'hidden'
                  }}
                >
                  {label && (
                    <Chip
                      label={label}
                      size="small"
                      sx={{
                        backgroundColor: badgeColor,
                        color: badgeColor === '#00E5FF' ? '#000000' : '#FFFFFF',
                        fontWeight: 800,
                        fontSize: '0.65rem',
                        height: 18,
                        fontFamily: 'monospace',
                        mb: saplingToRender ? 0.5 : 0
                      }}
                    />
                  )}

                  {saplingToRender && (
                    <Box sx={{ transform: 'scale(0.85)', transformOrigin: 'center' }}>
                      <SaplingGeneRepr sapling={saplingToRender} size="small" showConnectors={false} />
                    </Box>
                  )}
                </Paper>
              );
            })}
          </Box>
        </Stack>
      </CardContent>
    </Card>
  );
};
