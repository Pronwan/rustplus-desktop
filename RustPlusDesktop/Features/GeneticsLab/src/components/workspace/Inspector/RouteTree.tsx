import React from 'react';
import { Box, Typography, Paper, Chip } from '@mui/material';
import { GeneticsMap } from '../../../domain/genetics/GeneticsMap.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

interface RouteTreeProps {
  map: GeneticsMap;
  onSelectSubPlan?: (subMap: GeneticsMap) => void;
}

export const RouteTree: React.FC<RouteTreeProps> = ({ map, onSelectSubPlan }) => {
  const genIndex = map.resultSapling.generationIndex || 1;
  const isGen1 = genIndex === 1;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', py: 1 }}>
      {/* Root Node: Result Target */}
      <Paper
        variant="outlined"
        sx={{
          p: 1.5,
          borderRadius: '5px',
          backgroundColor: 'var(--gl-card-hover-bg)',
          border: '1.5px solid var(--gl-primary)',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 0.5,
          boxShadow: '0 0 12px rgba(0, 229, 255, 0.2)',
          zIndex: 2
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Chip
            size="small"
            label={`TARGET · GEN.${genIndex}`}
            sx={{
              height: 18,
              fontSize: '0.65rem',
              fontWeight: 800,
              backgroundColor: 'rgba(0, 229, 255, 0.15)',
              color: 'var(--gl-primary)'
            }}
          />
          <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace' }}>
            {(map.getChanceProduct() * 100).toFixed(0)}% Chance
          </Typography>
        </Box>
        <GeneticsSequence genes={map.resultSapling.toString()} size="small" showConnectors={true} />
      </Paper>

      {/* Down Connector Line */}
      <Box sx={{ width: '2px', height: '24px', backgroundColor: 'var(--gl-border-strong)' }} />

      {/* Crossbreeding Recipe Container */}
      <Paper
        variant="outlined"
        sx={{
          p: 2,
          borderRadius: '6px',
          backgroundColor: 'var(--gl-card-bg)',
          border: '1px solid var(--gl-border)',
          width: '100%',
          display: 'flex',
          flexDirection: 'column',
          gap: 1.5
        }}
      >
        {/* Center Plant Section */}
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.72rem', fontWeight: 700, mb: 0.5 }}>
            CENTER PLANT:
          </Typography>

          {map.baseSapling ? (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, p: '4px 8px', backgroundColor: 'var(--gl-border-subtle)', borderRadius: '4px', border: '1px solid var(--gl-surface-hover)' }}>
              {map.baseSapling.generationIndex > 0 ? (
                <Chip size="small" label={`GEN.${map.baseSapling.generationIndex}`} sx={{ height: 16, fontSize: '0.62rem', backgroundColor: 'var(--gl-warning)', color: 'var(--gl-on-accent)', fontWeight: 800 }} />
              ) : (
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, minWidth: 16 }}>
                  #{map.baseSapling.index !== undefined ? map.baseSapling.index + 1 : '1'}
                </Typography>
              )}
              <GeneticsSequence genes={map.baseSapling.toString()} size="small" showConnectors={true} />
            </Box>
          ) : (
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontFamily: 'monospace' }}>
              Any extra plant of same type
            </Typography>
          )}
        </Box>

        {/* Surrounding Plants Section */}
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.72rem', fontWeight: 700, mb: 0.75 }}>
            SURROUNDING PLANTS:
          </Typography>

          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75, width: '100%', alignItems: 'center' }}>
            {map.crossbreedingSaplings.map((parent, pIdx) => {
              const isFirst = map.tieWinningCrossbreedingSaplingIndexes?.includes(pIdx);
              const isSecond = map.tieLosingCrossbreedingSaplingIndexes?.includes(pIdx);
              const isParentGen = parent.generationIndex > 0;

              return (
                <Box
                  key={pIdx}
                  sx={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    width: '100%',
                    maxWidth: 320,
                    p: '4px 8px',
                    backgroundColor: 'var(--gl-elevated-bg)',
                    border: isParentGen ? '1px solid var(--gl-warning)' : '1px solid var(--gl-border)',
                    borderRadius: '4px'
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    {isParentGen ? (
                      <Chip size="small" label={`GEN.${parent.generationIndex}`} sx={{ height: 16, fontSize: '0.62rem', backgroundColor: 'var(--gl-warning)', color: 'var(--gl-on-accent)', fontWeight: 800 }} />
                    ) : (
                      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, minWidth: 16 }}>
                        #{parent.index !== undefined ? parent.index + 1 : pIdx + 1}
                      </Typography>
                    )}
                    <GeneticsSequence genes={parent.toString()} size="small" showConnectors={true} />
                  </Box>

                  {/* Priority Badge */}
                  {isFirst && (
                    <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.72rem' }}>
                      1st
                    </Typography>
                  )}
                  {isSecond && (
                    <Typography variant="caption" sx={{ color: 'var(--gl-error)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.72rem' }}>
                      2nd
                    </Typography>
                  )}
                </Box>
              );
            })}
          </Box>
        </Box>
      </Paper>
    </Box>
  );
};
