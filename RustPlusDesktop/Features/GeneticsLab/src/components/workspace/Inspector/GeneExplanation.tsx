import React, { useState } from 'react';
import {
  Box,
  Typography,
  Paper,
  ButtonBase,
  LinearProgress
} from '@mui/material';
import { GeneticsMap } from '../../../domain/genetics/GeneticsMap.ts';
import { GREEN_GENES, GREEN_GENE_WEIGHT, RED_GENE_WEIGHT } from '../../../domain/genetics/Gene.ts';
import { HoverScrollRow } from '../../common/HoverScrollRow.tsx';

interface GeneExplanationProps {
  map: GeneticsMap;
}

export const GeneExplanation: React.FC<GeneExplanationProps> = ({ map }) => {
  const [selectedSlot, setSelectedSlot] = useState(0);

  // Compute exact slot weights for the selected position
  const slotData = React.useMemo(() => {
    const weights: Record<string, { weight: number; contributors: { label: string; gene: string; isGreen: boolean }[] }> = {
      G: { weight: 0, contributors: [] },
      Y: { weight: 0, contributors: [] },
      H: { weight: 0, contributors: [] },
      W: { weight: 0, contributors: [] },
      X: { weight: 0, contributors: [] }
    };

    // Surrounding plants
    map.crossbreedingSaplings.forEach((parent, pIdx) => {
      const g = parent.genes[selectedSlot];
      const type = g.type;
      const w = g.weight;
      if (weights[type]) {
        weights[type].weight = Math.round((weights[type].weight + w) * 100) / 100;
        weights[type].contributors.push({
          label: `Surrounding #${parent.index !== undefined ? parent.index + 1 : pIdx + 1}`,
          gene: type,
          isGreen: g.isGreen
        });
      }
    });

    const winningGene = map.resultSapling.genes[selectedSlot].type;
    const centerGene = map.baseSapling ? map.baseSapling.genes[selectedSlot].type : null;

    return {
      weights,
      winningGene,
      centerGene
    };
  }, [map, selectedSlot]);

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 800 }}>
        INSPECT GENE POSITION (1 - 6):
      </Typography>

      {/* Slot Selector — scrolls horizontally with hover arrows when it overflows */}
      <HoverScrollRow gap={0.5}>
        {[0, 1, 2, 3, 4, 5].map((slot) => {
          const resGene = map.resultSapling.genes[slot].type;
          const isGreen = GREEN_GENES.includes(resGene as any);
          const isActive = selectedSlot === slot;
          return (
            <ButtonBase
              key={slot}
              onClick={() => setSelectedSlot(slot)}
              sx={{
                flexShrink: 0,
                display: 'flex',
                alignItems: 'center',
                gap: 0.5,
                py: 0.4,
                px: 1,
                borderRadius: '4px',
                borderBottom: '2px solid',
                borderColor: isActive ? 'var(--gl-primary)' : 'transparent',
                backgroundColor: isActive ? 'rgba(0, 229, 255, 0.10)' : 'transparent',
                '&:hover': { backgroundColor: 'var(--gl-surface)' }
              }}
            >
              <Typography variant="caption" sx={{ fontSize: '0.68rem', color: 'var(--gl-text-muted)' }}>#{slot + 1}</Typography>
              <Typography variant="caption" sx={{ fontWeight: 800, color: isGreen ? 'var(--gl-success)' : 'var(--gl-error)' }}>
                {resGene}
              </Typography>
            </ButtonBase>
          );
        })}
      </HoverScrollRow>

      {/* Position Explanation Content */}
      <Paper variant="outlined" sx={{ p: 1.5, backgroundColor: 'var(--gl-panel-header-bg)', borderColor: 'var(--gl-border)', borderRadius: '4px' }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800 }}>
            WINNING GENE: [{slotData.winningGene}]
          </Typography>
          {slotData.centerGene && (
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
              Center Plant: [{slotData.centerGene}]
            </Typography>
          )}
        </Box>

        {/* Weights Bar Chart */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1, my: 1 }}>
          {Object.entries(slotData.weights)
            .filter(([_, data]) => data.weight > 0)
            .sort((a, b) => b[1].weight - a[1].weight)
            .map(([gene, data]) => {
              const isWinner = gene === slotData.winningGene;
              const isGreen = gene === 'G' || gene === 'Y' || gene === 'H';

              return (
                <Box key={gene}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.25 }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                      <Typography
                        variant="caption"
                        sx={{
                          fontWeight: 800,
                          fontFamily: 'monospace',
                          color: isGreen ? 'var(--gl-success)' : 'var(--gl-error)'
                        }}
                      >
                        [{gene}] {isGreen ? '(0.6 weight)' : '(1.0 weight)'}
                      </Typography>
                      {isWinner && (
                        <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontSize: '0.65rem' }}>
                          ✓ WINNER
                        </Typography>
                      )}
                    </Box>

                    <Typography variant="caption" sx={{ fontWeight: 800, fontFamily: 'monospace', color: 'var(--gl-text-primary)' }}>
                      {data.weight.toFixed(1)} total weight
                    </Typography>
                  </Box>

                  <LinearProgress
                    variant="determinate"
                    value={Math.min(100, (data.weight / 3.0) * 100)}
                    sx={{
                      height: 6,
                      borderRadius: 3,
                      backgroundColor: 'var(--gl-surface)',
                      '& .MuiLinearProgress-bar': {
                        backgroundColor: isGreen ? 'var(--gl-success)' : 'var(--gl-error)'
                      }
                    }}
                  />

                  {/* Contributor List */}
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.65rem', mt: 0.25, display: 'block' }}>
                    Contributed by: {data.contributors.map(c => c.label).join(', ')}
                  </Typography>
                </Box>
              );
            })}
        </Box>

        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.72rem', mt: 1, display: 'block', borderTop: '1px solid var(--gl-surface)', pt: 1 }}>
          💡 <strong>Rule:</strong> Green genes (G, Y, H) have 0.6 crossbreeding weight. Red genes (W, X) have 1.0 weight. The gene with the highest total weight at this position wins.
        </Typography>
      </Paper>
    </Box>
  );
};
