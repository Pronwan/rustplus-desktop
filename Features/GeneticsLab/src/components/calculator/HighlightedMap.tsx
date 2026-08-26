import React from 'react';
import { Card, CardContent, Typography, Stack, Box, Chip, Divider } from '@mui/material';
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
        <Stack direction="row" alignItems="center" justifyContent="space-between">
          <Stack direction="row" spacing={1} alignItems="center">
            <Chip
              label={`Gen ${map.resultSapling.generation || 1}`}
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
          </Stack>

          <Chip
            label={`Score: ${score.toFixed(1)}`}
            size="small"
            color="primary"
            sx={{ fontWeight: 700, fontSize: '0.75rem' }}
          />
        </Stack>

        {/* Target Result Plant */}
        <Stack alignItems="center" sx={{ py: 1, backgroundColor: 'rgba(96, 205, 255, 0.04)', borderRadius: 1 }}>
          <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, mb: 0.5 }}>
            Target Result
          </Typography>
          <SaplingGeneRepr sapling={map.resultSapling} size="large" showSlotNumbers />
        </Stack>

        {/* Center Arrow Indicator */}
        <Stack alignItems="center">
          <ArrowDownwardIcon fontSize="small" sx={{ color: 'primary.main' }} />
        </Stack>

        {/* Surrounding Parents */}
        <Box>
          <Typography variant="caption" sx={{ color: 'text.secondary', fontWeight: 600, display: 'block', mb: 1 }}>
            Required Parents ({map.surroundingSaplings.length} plants {map.baseSapling ? '+ Center' : ''}):
          </Typography>

          <Stack spacing={1}>
            {map.baseSapling && (
              <Stack direction="row" alignItems="center" spacing={1}>
                <Chip label="Center" size="small" color="secondary" sx={{ fontWeight: 700, fontSize: '0.7rem' }} />
                <SaplingGeneRepr sapling={map.baseSapling} size="small" />
              </Stack>
            )}

            {map.surroundingSaplings.map((parent, idx) => (
              <Stack key={idx} direction="row" alignItems="center" spacing={1}>
                <Chip label={`Parent ${idx + 1}`} size="small" variant="outlined" sx={{ fontWeight: 600, fontSize: '0.7rem' }} />
                <SaplingGeneRepr sapling={parent} size="small" />
              </Stack>
            ))}
          </Stack>
        </Box>
      </Stack>
    </Card>
  );
};
