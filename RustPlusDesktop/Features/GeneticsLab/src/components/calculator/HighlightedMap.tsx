import React from 'react';
import { Card, Typography, Stack, Box, Chip } from '@mui/material';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';
import { useApp } from '../../context/AppContext.tsx';

interface HighlightedMapProps {
  map: GeneticsMap;
}

export const HighlightedMap: React.FC<HighlightedMapProps> = ({ map }) => {
  const { options } = useApp();
  const score = map.resultSapling.getScore(options.geneScores);

  return (
    <Card variant="outlined" sx={{ p: 1.5, backgroundColor: 'background.paper' }}>
      <Stack spacing={1.5}>
        {/* Breeding Plan Header */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
            <Chip
              label={`Gen ${map.resultSapling.generationIndex + 1}`}
              size="small"
              color="primary"
              variant="outlined"
              sx={{ fontWeight: 700, fontSize: '0.75rem' }}
            />
            <Chip
              label={`Probability: ${(map.chance * 100).toFixed(0)}%`}
              size="small"
              color={map.chance === 1 ? 'success' : 'warning'}
              sx={{ fontWeight: 700, fontSize: '0.75rem' }}
            />
          </Box>

          <Chip
            label={`Score: ${score.toFixed(1)}`}
            size="small"
            color="primary"
            sx={{ fontWeight: 700, fontSize: '0.75rem' }}
          />
        </Box>

        {/* Target Result Plant */}
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', py: 1, backgroundColor: 'rgba(96, 205, 255, 0.04)', borderRadius: 1 }}>
          <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, mb: 0.5 }}>
            Target Result
          </Typography>
          <SaplingGeneRepr sapling={map.resultSapling} size="large" showSlotNumbers />
        </Box>

        {/* Center Arrow Indicator */}
        <Box sx={{ display: 'flex', justifyContent: 'center' }}>
          <ArrowDownwardIcon fontSize="small" sx={{ color: 'primary.main' }} />
        </Box>

        {/* Surrounding Parents */}
        <Box>
          <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, display: 'block', mb: 1 }}>
            Required Parents ({map.crossbreedingSaplings.length} plants {map.baseSapling ? '+ Center' : ''}):
          </Typography>

          <Stack spacing={1}>
            {map.baseSapling && (
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip label="Center" size="small" color="secondary" sx={{ fontWeight: 700, fontSize: '0.7rem' }} />
                <SaplingGeneRepr sapling={map.baseSapling} size="small" />
              </Box>
            )}

            {map.crossbreedingSaplings.map((parent, idx) => (
              <Box key={idx} sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Chip label={`Parent ${idx + 1}`} size="small" variant="outlined" sx={{ fontWeight: 600, fontSize: '0.7rem' }} />
                <SaplingGeneRepr sapling={parent} size="small" />
              </Box>
            ))}
          </Stack>
        </Box>
      </Stack>
    </Card>
  );
};
