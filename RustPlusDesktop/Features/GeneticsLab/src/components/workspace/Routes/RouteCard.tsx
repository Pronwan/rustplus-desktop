import React from 'react';
import {
  Paper,
  Box,
  Typography,
  Button,
  Chip,
  Tooltip
} from '@mui/material';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import VisibilityIcon from '@mui/icons-material/Visibility';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import { ScoredRoute, useCalculation } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

interface RouteCardProps {
  scoredRoute: ScoredRoute;
  rankIndex: number;
  isSelected?: boolean;
  onInspect?: () => void;
}

export const RouteCard: React.FC<RouteCardProps> = ({
  scoredRoute,
  rankIndex,
  isSelected,
  onInspect
}) => {
  const { group, bestMap, analysis } = scoredRoute;
  const { setSelectedGroup, setSelectedMapIndex, comparedGroups, toggleCompareGroup } = useCalculation();
  const { startBreedingSession } = useWorkspace();

  const isCompared = comparedGroups.some(g => g.resultSaplingGeneString === group.resultSaplingGeneString);

  const handleInspect = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedGroup(group);
    setSelectedMapIndex(0);
    onInspect?.();
  };

  const handleStartBreeding = (e: React.MouseEvent) => {
    e.stopPropagation();
    startBreedingSession(bestMap, group.resultSaplingGeneString);
  };

  const isBest = rankIndex === 0;

  const invBadgeConfig = {
    available: { label: '✓ Ready', bg: 'rgba(76, 175, 80, 0.12)', color: 'var(--gl-success)', border: 'rgba(76, 175, 80, 0.35)' },
    partial: { label: `⚠ Missing ${analysis.missingClonesCount}`, bg: 'rgba(255, 167, 38, 0.12)', color: 'var(--gl-warning)', border: 'rgba(255, 167, 38, 0.35)' },
    missing: { label: `✕ Missing ${analysis.missingClonesCount}`, bg: 'rgba(229, 57, 53, 0.12)', color: 'var(--gl-error)', border: 'rgba(229, 57, 53, 0.35)' }
  };

  const invBadge = invBadgeConfig[analysis.inventoryStatus];

  return (
    <Paper
      onClick={handleInspect}
      variant="outlined"
      sx={{
        backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-bg)',
        border: '1px solid',
        borderColor: isSelected ? 'var(--gl-primary)' : isBest ? 'var(--gl-border-strong)' : 'var(--gl-surface)',
        borderRadius: '5px',
        p: 1.5,
        cursor: 'pointer',
        transition: 'all 0.15s ease',
        display: 'flex',
        flexDirection: 'column',
        gap: 1.25,
        position: 'relative',
        boxShadow: isSelected ? '0 0 12px rgba(0, 229, 255, 0.12)' : 'none',
        '&:hover': {
          borderColor: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-faint)',
          backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-header-bg)'
        }
      }}
    >
      {/* Top Banner: Rank / Score & Compare toggle */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <Typography
            variant="caption"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.75rem',
              color: isBest ? 'var(--gl-primary)' : 'var(--gl-text-primary)'
            }}
          >
            {isBest ? '⭐ BEST' : `ROUTE #${rankIndex + 1}`}
          </Typography>

          <Chip
            size="small"
            label={`Score ${analysis.recommendationScore}`}
            sx={{
              height: 18,
              fontSize: '0.65rem',
              fontWeight: 800,
              fontFamily: 'monospace',
              backgroundColor: analysis.recommendationScore >= 80 ? 'rgba(0, 229, 255, 0.15)' : 'rgba(255, 255, 255, 0.08)',
              color: analysis.recommendationScore >= 80 ? 'var(--gl-primary)' : 'var(--gl-text-secondary)',
              border: `1px solid ${analysis.recommendationScore >= 80 ? 'rgba(0, 229, 255, 0.35)' : 'rgba(255, 255, 255, 0.15)'}`,
              '& .MuiChip-label': { px: 0.6 }
            }}
          />
        </Box>

        <Tooltip title="Compare side-by-side" arrow>
          <Box
            onClick={(e) => {
              e.stopPropagation();
              toggleCompareGroup(group);
            }}
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 0.25,
              cursor: 'pointer',
              px: 0.5,
              py: 0.2,
              borderRadius: '3px',
              backgroundColor: isCompared ? 'rgba(255, 152, 0, 0.15)' : 'transparent',
              border: '1px solid',
              borderColor: isCompared ? 'var(--gl-warning)' : 'transparent',
              '&:hover': { backgroundColor: 'rgba(255, 255, 255, 0.05)' }
            }}
          >
            <CompareArrowsIcon sx={{ fontSize: 13, color: isCompared ? 'var(--gl-warning)' : 'var(--gl-text-muted)' }} />
            <Typography variant="caption" sx={{ fontSize: '0.65rem', fontWeight: 700, color: isCompared ? 'var(--gl-warning)' : 'var(--gl-text-muted)' }}>
              Compare
            </Typography>
          </Box>
        </Tooltip>
      </Box>

      {/* Target Result Genes */}
      <Box sx={{ display: 'flex', justifyContent: 'center', my: 0.25 }}>
        <GeneticsSequence genes={group.resultSaplingGeneString} size="small" showConnectors={true} />
      </Box>

      {/* Metrics Row */}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 0.5, textAlign: 'center' }}>
        <Box sx={{ p: 0.4, backgroundColor: 'var(--gl-input-bg)', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            CHANCE
          </Typography>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 800,
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              color: analysis.probabilityPercent >= 95 ? 'var(--gl-success)' : analysis.probabilityPercent >= 50 ? 'var(--gl-warning)' : 'var(--gl-error)'
            }}
          >
            {analysis.probabilityPercent}%
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: 'var(--gl-input-bg)', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            GENS
          </Typography>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 800,
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              color: analysis.generationCount === 1 ? 'var(--gl-success)' : 'var(--gl-warning)'
            }}
          >
            GEN.{analysis.generationCount}
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: 'var(--gl-input-bg)', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            CLONES
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', fontSize: '0.75rem', color: 'var(--gl-text-secondary)' }}>
            {analysis.uniqueCloneCount} unq
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: 'var(--gl-input-bg)', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            PLANTS
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', fontSize: '0.75rem', color: 'var(--gl-text-secondary)' }}>
            {analysis.totalPlacementsCount} tot
          </Typography>
        </Box>
      </Box>

      {/* Inventory Status Pill */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Chip
          size="small"
          label={invBadge.label}
          sx={{
            height: 18,
            fontSize: '0.65rem',
            fontWeight: 700,
            backgroundColor: invBadge.bg,
            color: invBadge.color,
            border: `1px solid ${invBadge.border}`,
            '& .MuiChip-label': { px: 0.6 }
          }}
        />

        {group.mapList.length > 1 && (
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.65rem' }}>
            +{group.mapList.length - 1} alt
          </Typography>
        )}
      </Box>

      {/* Card Action Buttons */}
      <Box sx={{ display: 'flex', gap: 0.75, pt: 0.5, borderTop: '1px solid var(--gl-elevated-bg)' }}>
        <Button
          variant={isSelected ? 'contained' : 'outlined'}
          color={isSelected ? 'primary' : 'inherit'}
          size="small"
          fullWidth
          onClick={handleInspect}
          startIcon={<VisibilityIcon sx={{ fontSize: 13 }} />}
          sx={{
            fontSize: '0.7rem',
            fontWeight: 800,
            py: 0.35,
            borderColor: isSelected ? undefined : 'var(--gl-surface)'
          }}
        >
          {isSelected ? 'INSPECTING' : 'INSPECT'}
        </Button>

        <Button
          variant="contained"
          size="small"
          fullWidth
          onClick={handleStartBreeding}
          startIcon={<PlayCircleIcon sx={{ fontSize: 13 }} />}
          sx={{
            fontSize: '0.7rem',
            fontWeight: 800,
            py: 0.35,
            backgroundColor: 'var(--gl-warning)',
            color: 'var(--gl-on-accent)',
            '&:hover': { backgroundColor: 'var(--gl-warning)' }
          }}
        >
          BREED
        </Button>
      </Box>
    </Paper>
  );
};
