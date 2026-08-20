import React from 'react';
import {
  Paper,
  Box,
  Typography,
  Button,
  ButtonBase,
  Chip,
  Tooltip
} from '@mui/material';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import { ScoredRoute, useCalculation } from '../../../context/CalculationContext.tsx';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { generationVisual } from '../../../utils/generationStyle.ts';

interface RouteCardProps {
  scoredRoute: ScoredRoute;
  rankIndex: number;
  isSelected?: boolean;
  onInspect?: () => void;
  /** Identity used by the list's FLIP pass to track this route across reorders. */
  flipKey?: string;
}

export const RouteCard: React.FC<RouteCardProps> = ({
  scoredRoute,
  rankIndex,
  isSelected,
  onInspect,
  flipKey
}) => {
  const { group, analysis } = scoredRoute;
  const { setSelectedGroup, setSelectedMapIndex, setIsInspectorOpen, comparedGroups, toggleCompareGroup } = useCalculation();
  const isCompared = comparedGroups.some(g => g.resultSaplingGeneString === group.resultSaplingGeneString);
  const isBest = rankIndex === 0;
  const generation = generationVisual(analysis.generationCount);

  const selectRoute = () => {
    setSelectedGroup(group);
    setSelectedMapIndex(0);
    setIsInspectorOpen(true);
    onInspect?.();
  };

  const readiness = analysis.inventoryStatus === 'available'
    ? { label: 'Ready now', color: 'var(--gl-success)', border: 'rgba(76, 175, 80, 0.35)', tint: 'rgba(76, 175, 80, 0.12)' }
    : { label: `Missing ${analysis.missingClonesCount}`, color: 'var(--gl-warning)', border: 'rgba(255, 167, 38, 0.35)', tint: 'rgba(255, 167, 38, 0.12)' };

  const reason = analysis.inventoryStatus === 'available'
    ? `Ready with your clone bank; uses ${analysis.totalPlacementsCount} total plant${analysis.totalPlacementsCount === 1 ? '' : 's'}`
    : `Collect ${analysis.missingClonesCount} clone${analysis.missingClonesCount === 1 ? '' : 's'} to make this route ready`;

  return (
    <Paper
      variant="outlined"
      data-flip-key={flipKey}
      sx={{
        display: 'flex',
        alignItems: 'stretch',
        overflow: 'hidden',
        backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-bg)',
        borderColor: isSelected ? 'var(--gl-primary)' : isBest ? 'var(--gl-border-strong)' : 'var(--gl-surface)',
        borderLeft: `4px solid ${generation.border}`,
        borderRadius: '5px',
        transition: 'background-color 160ms ease, border-color 160ms ease',
        '&:hover': {
          borderColor: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-faint)',
          backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-header-bg)'
        },
        '@media (prefers-reduced-motion: reduce)': { transition: 'none' }
      }}
    >
      <ButtonBase
        disableRipple
        onClick={selectRoute}
        aria-label={`Select route ${rankIndex + 1}, ${group.resultSaplingGeneString}, ${readiness.label}`}
        sx={{
          flex: 1,
          minWidth: 0,
          p: 1.25,
          display: 'grid',
          gridTemplateColumns: { xs: 'auto 1fr', md: '60px 180px minmax(0, 1fr)' },
          gridTemplateAreas: {
            xs: '"rank genes" "details details" "metrics metrics"',
            md: '"rank genes details" "rank genes metrics"'
          },
          alignItems: 'center',
          gap: { xs: 1, md: 1.5 },
          textAlign: 'left',
          '&.Mui-focusVisible': { outline: '2px solid var(--gl-primary)', outlineOffset: -2 }
        }}
      >
        <Typography
          variant="caption"
          sx={{
            fontWeight: 900,
            fontFamily: '"Roboto Mono", monospace',
            fontSize: '0.75rem',
            color: isBest ? 'var(--gl-primary)' : 'var(--gl-text-primary)',
            whiteSpace: 'nowrap',
            gridArea: 'rank'
          }}
        >
          {isBest ? '★ BEST' : `#${rankIndex + 1}`}
        </Typography>

        <Box sx={{ gridArea: 'genes', display: 'flex', justifyContent: { xs: 'flex-end', md: 'flex-start' } }}>
          <GeneticsSequence genes={group.resultSaplingGeneString} size="small" showConnectors={true} />
        </Box>

        <Box sx={{ gridArea: 'details', minWidth: 0 }}>
          <Typography sx={{ color: 'var(--gl-text-primary)', fontSize: '0.78rem', fontWeight: 800 }}>
            {isBest ? 'Recommended route' : `Alternative route ${rankIndex + 1}`}
          </Typography>
          <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem' }}>
            {reason}
          </Typography>
        </Box>

        <Box sx={{ gridArea: 'metrics', display: 'flex', alignItems: 'center', justifyContent: 'flex-start', gap: 0.75, flexWrap: 'wrap' }}>
          <Chip
            size="small"
            label={readiness.label}
            sx={{ height: 24, fontSize: '0.75rem', fontWeight: 800, color: readiness.color, border: `1px solid ${readiness.border}`, backgroundColor: readiness.tint }}
          />
          <Chip
            size="small"
            label={`${analysis.probabilityPercent}%`}
            sx={{ height: 24, fontSize: '0.75rem', fontWeight: 800, color: 'var(--gl-text-secondary)', backgroundColor: 'var(--gl-input-bg)' }}
          />
          <Chip
            size="small"
            label={`${generation.icon} ${analysis.generationCount} gen`}
            sx={{ height: 24, fontSize: '0.75rem', fontWeight: 800, color: generation.color, border: `1px solid ${generation.border}`, backgroundColor: generation.tint }}
          />
          <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem', whiteSpace: 'nowrap' }}>
            {analysis.uniqueCloneCount} clones · {analysis.totalPlacementsCount} plants
          </Typography>
        </Box>
      </ButtonBase>

      <Box sx={{ display: 'flex', alignItems: 'center', px: 0.75, borderLeft: '1px solid var(--gl-surface)' }}>
        <Tooltip title="Add this route to comparison" arrow>
          <Button
            type="button"
            aria-pressed={isCompared}
            aria-label={`${isCompared ? 'Remove' : 'Add'} route ${rankIndex + 1} ${isCompared ? 'from' : 'to'} comparison`}
            onClick={() => toggleCompareGroup(group)}
            startIcon={<CompareArrowsIcon sx={{ fontSize: 16 }} />}
            sx={{
              minWidth: 0,
              minHeight: 36,
              px: 1,
              color: isCompared ? 'var(--gl-warning)' : 'var(--gl-text-muted)',
              border: '1px solid',
              borderColor: isCompared ? 'var(--gl-warning)' : 'transparent',
              backgroundColor: isCompared ? 'rgba(255, 152, 0, 0.12)' : 'transparent',
              fontSize: '0.75rem',
              fontWeight: 800,
              '& .MuiButton-startIcon': { mr: { xs: 0, sm: 0.5 } }
            }}
          >
            <Box component="span" sx={{ display: { xs: 'none', sm: 'inline' } }}>Compare</Box>
          </Button>
        </Tooltip>
      </Box>
    </Paper>
  );
};
