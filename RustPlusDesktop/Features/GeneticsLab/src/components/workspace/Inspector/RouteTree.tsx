import React, { useState, useEffect } from 'react';
import { Box, Typography, Paper, Chip, ButtonBase, IconButton, Tooltip } from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import KeyboardArrowRightIcon from '@mui/icons-material/KeyboardArrowRight';
import AccountTreeIcon from '@mui/icons-material/AccountTree';
import { GeneticsMap } from '../../../domain/genetics/GeneticsMap.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { generationVisual } from '../../../utils/generationStyle.ts';

interface RouteTreeProps {
  map: GeneticsMap;
}

/** The pre-computed sub-plan that explains how a GEN.n parent is itself bred. */
const getSubMap = (parentMapGroup?: { mapList: GeneticsMap[] }): GeneticsMap | null =>
  parentMapGroup && parentMapGroup.mapList.length > 0 ? parentMapGroup.mapList[0] : null;

export const RouteTree: React.FC<RouteTreeProps> = ({ map }) => {
  // Navigation stack: each entry is one generation deep into the recipe.
  // stack[0] is the inspected route; drilling into a GEN.n node pushes its sub-plan.
  const [stack, setStack] = useState<GeneticsMap[]>([map]);

  // Reset the drill-down whenever a different route is inspected.
  useEffect(() => {
    setStack([map]);
  }, [map]);

  const current = stack[stack.length - 1];
  const depth = stack.length - 1;
  const genIndex = current.resultSapling.generationIndex || 1;

  const drillInto = (subMap: GeneticsMap | null) => {
    if (subMap) setStack(prev => [...prev, subMap]);
  };
  const goBack = () => setStack(prev => (prev.length > 1 ? prev.slice(0, -1) : prev));
  const goToLevel = (level: number) => setStack(prev => prev.slice(0, level + 1));

  // Sub-plans available at this level
  const centerSubMap =
    current.baseSapling && current.baseSapling.generationIndex > 0
      ? getSubMap(current.baseSaplingVariants)
      : null;

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%', py: 1 }}>
      {/* Breadcrumb / Back bar — only once we've drilled into a sub-generation */}
      {depth > 0 && (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 0.5,
            width: '100%',
            mb: 1.25,
            p: '4px 6px',
            backgroundColor: 'var(--gl-panel-header-bg)',
            border: '1px solid var(--gl-border)',
            borderRadius: '4px',
            flexWrap: 'wrap'
          }}
        >
          <Tooltip title="Back to previous generation" arrow>
            <IconButton aria-label="Go back in route tree" size="small" onClick={goBack} sx={{ color: 'var(--gl-primary)', p: 0.25 }}>
              <ArrowBackIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>

          {stack.map((node, idx) => {
            const isLast = idx === stack.length - 1;
            const label = idx === 0 ? 'Target' : `Gen.${node.resultSapling.generationIndex || 1}`;
            return (
              <React.Fragment key={idx}>
                {idx > 0 && <KeyboardArrowRightIcon sx={{ fontSize: 14, color: 'var(--gl-text-faint)' }} />}
                <ButtonBase
                  onClick={() => goToLevel(idx)}
                  disabled={isLast}
                  sx={{
                    px: 0.6,
                    py: 0.1,
                    borderRadius: '3px',
                    fontFamily: 'monospace',
                    fontSize: '0.68rem',
                    fontWeight: 800,
                    color: isLast ? 'var(--gl-primary)' : 'var(--gl-text-secondary)',
                    '&:hover': { color: 'var(--gl-primary)', textDecoration: isLast ? 'none' : 'underline' }
                  }}
                >
                  {label} [{node.resultSapling.toString()}]
                </ButtonBase>
              </React.Fragment>
            );
          })}
        </Box>
      )}

      {/* Root Node: Result Target for this level */}
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
            label={`${depth > 0 ? 'SUB-PLAN' : 'TARGET'} · GEN.${genIndex}`}
            sx={{
              height: 18,
              fontSize: '0.65rem',
              fontWeight: 800,
              backgroundColor: 'rgba(0, 229, 255, 0.15)',
              color: 'var(--gl-primary)'
            }}
          />
          <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace' }}>
            {(current.getChanceProduct() * 100).toFixed(0)}% Chance
          </Typography>
        </Box>
        <GeneticsSequence genes={current.resultSapling.toString()} size="small" showConnectors={true} />
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

          {current.baseSapling ? (
            <PlantRow
              genes={current.baseSapling.toString()}
              genLabel={current.baseSapling.generationIndex > 0 ? current.baseSapling.generationIndex : null}
              indexLabel={
                current.baseSapling.generationIndex > 0
                  ? null
                  : `#${current.baseSapling.index !== undefined ? current.baseSapling.index + 1 : '?'}`
              }
              subMap={centerSubMap}
              onDrill={() => drillInto(centerSubMap)}
            />
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
            {current.crossbreedingSaplings.map((parent, pIdx) => {
              const isFirst = current.tieWinningCrossbreedingSaplingIndexes?.includes(pIdx);
              const isSecond = current.tieLosingCrossbreedingSaplingIndexes?.includes(pIdx);
              const isParentGen = parent.generationIndex > 0;
              const subMap = isParentGen ? getSubMap(current.crossbreedingSaplingsVariants?.[pIdx]) : null;

              return (
                <PlantRow
                  key={pIdx}
                  genes={parent.toString()}
                  genLabel={isParentGen ? parent.generationIndex : null}
                  indexLabel={isParentGen ? null : `#${parent.index !== undefined ? parent.index + 1 : '?'}`}
                  subMap={subMap}
                  onDrill={() => drillInto(subMap)}
                  priority={isFirst ? '1st' : isSecond ? '2nd' : undefined}
                />
              );
            })}
          </Box>
        </Box>
      </Paper>

      {depth === 0 && stack[0].resultSapling.generationIndex > 1 && (
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.68rem', mt: 1, textAlign: 'center' }}>
          Tip: click any <strong style={{ color: 'var(--gl-warning)' }}>GEN.n</strong> plant to see how it is bred.
        </Typography>
      )}
    </Box>
  );
};

/** One plant row in the recipe. Becomes a clickable drill-down when it carries a sub-plan. */
const PlantRow: React.FC<{
  genes: string;
  genLabel: number | null;
  indexLabel: string | null;
  subMap: GeneticsMap | null;
  onDrill: () => void;
  priority?: '1st' | '2nd';
}> = ({ genes, genLabel, indexLabel, subMap, onDrill, priority }) => {
  const clickable = !!subMap;

  const inner = (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, width: '100%' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flex: 1 }}>
        {genLabel !== null ? (
          <Chip
            size="small"
            label={`${generationVisual(genLabel).icon} GEN.${genLabel}`}
            sx={{ height: 16, fontSize: '0.62rem', backgroundColor: generationVisual(genLabel).color, color: 'var(--gl-on-accent)', fontWeight: 800 }}
          />
        ) : (
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, minWidth: 16 }}>
            {indexLabel}
          </Typography>
        )}
        <GeneticsSequence genes={genes} size="small" showConnectors={true} />
      </Box>

      {priority === '1st' && (
        <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.72rem' }}>1st</Typography>
      )}
      {priority === '2nd' && (
        <Typography variant="caption" sx={{ color: 'var(--gl-error)', fontWeight: 800, fontFamily: 'monospace', fontSize: '0.72rem' }}>2nd</Typography>
      )}
      {clickable && (
        <Tooltip title="View how this generation is bred" arrow>
          <AccountTreeIcon sx={{ fontSize: 15, color: 'var(--gl-primary)' }} />
        </Tooltip>
      )}
    </Box>
  );

  const commonSx = {
    width: '100%',
    maxWidth: 320,
    p: '4px 8px',
    backgroundColor: 'var(--gl-elevated-bg)',
    border: genLabel !== null ? `1px solid ${generationVisual(genLabel).border}` : '1px solid var(--gl-border)',
    borderRadius: '4px'
  };

  if (clickable) {
    return (
      <ButtonBase
        onClick={onDrill}
        sx={{
          ...commonSx,
          textAlign: 'left',
          transition: 'all 0.15s ease',
          '&:hover': {
            borderColor: 'var(--gl-primary)',
            backgroundColor: 'var(--gl-card-hover-bg)',
            boxShadow: '0 0 8px rgba(0, 229, 255, 0.25)'
          }
        }}
      >
        {inner}
      </ButtonBase>
    );
  }

  return <Box sx={{ display: 'flex', ...commonSx }}>{inner}</Box>;
};
