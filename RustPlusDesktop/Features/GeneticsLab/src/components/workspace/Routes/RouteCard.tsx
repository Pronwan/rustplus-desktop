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
import { generationVisual } from '../../../utils/generationStyle.ts';

interface RouteCardProps {
  scoredRoute: ScoredRoute;
  rankIndex: number;
  isSelected?: boolean;
  onInspect?: () => void;
  /** Identity used by the grid's FLIP pass to track this card across reorders. */
  flipKey?: string;
}

export const RouteCard: React.FC<RouteCardProps> = ({
  scoredRoute,
  rankIndex,
  isSelected,
  onInspect,
  flipKey
}) => {
  const { group, bestMap, analysis } = scoredRoute;
  const { setSelectedGroup, setSelectedMapIndex, setIsInspectorOpen, comparedGroups, toggleCompareGroup } = useCalculation();
  const { startBreedingSession } = useWorkspace();

  const isCompared = comparedGroups.some(g => g.resultSaplingGeneString === group.resultSaplingGeneString);

  const handleInspect = (e: React.MouseEvent) => {
    e.stopPropagation();
    setSelectedGroup(group);
    setSelectedMapIndex(0);
    setIsInspectorOpen(true); // explicit user intent → allowed to open the drawer on small screens
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
  const genVis = generationVisual(analysis.generationCount);

  return (
    <Paper
      onClick={handleInspect}
      variant="outlined"
      data-flip-key={flipKey}
      sx={{
        backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-bg)',
        border: '1px solid',
        borderColor: isSelected ? 'var(--gl-primary)' : isBest ? 'var(--gl-border-strong)' : 'var(--gl-surface)',
        // Generation identity: a colored left accent makes GEN 1/2/3 scannable in the grid.
        borderLeft: `4px solid ${genVis.border}`,
        borderRadius: '5px',
        p: 1.5,
        cursor: 'pointer',
        // Colour and elevation interpolate on their own curve; `transform` is
        // deliberately excluded so the grid's FLIP pass owns positional motion.
        transition:
          'background-color 220ms cubic-bezier(0.32, 0.72, 0, 1),' +
          ' border-color 220ms cubic-bezier(0.32, 0.72, 0, 1),' +
          ' box-shadow 220ms cubic-bezier(0.32, 0.72, 0, 1)',
        display: 'flex',
        flexDirection: 'column',
        gap: 1.25,
        position: 'relative',
        boxShadow: isSelected ? '0 0 12px rgba(0, 229, 255, 0.12)' : 'none',
        '&:hover': {
          borderColor: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-faint)',
          backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-header-bg)',
          boxShadow: isSelected
            ? '0 0 14px rgba(0, 229, 255, 0.18)'
            : '0 1px 10px rgba(0, 0, 0, 0.18)'
        },
        // Physical press feedback, and only while the pointer is down.
        '&:active': {
          transform: 'scale(0.994)',
          transition: 'transform 90ms cubic-bezier(0.32, 0.72, 0, 1)'
        },
        '@media (prefers-reduced-motion: reduce)': {
          transition: 'none',
          '&:active': { transform: 'none' }
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

          <Tooltip title={`Gene Quality Score: ${bestMap.score} / 6.0`} arrow>
            <Chip
              size="small"
              label={`Score ${bestMap.score}`}
              sx={{
                height: 18,
                fontSize: '0.65rem',
                fontWeight: 800,
                fontFamily: 'monospace',
                backgroundColor: bestMap.score >= 5 ? 'rgba(0, 229, 255, 0.15)' : 'rgba(255, 255, 255, 0.08)',
                color: bestMap.score >= 5 ? 'var(--gl-primary)' : 'var(--gl-text-secondary)',
                border: `1px solid ${bestMap.score >= 5 ? 'rgba(0, 229, 255, 0.35)' : 'rgba(255, 255, 255, 0.15)'}`,
                '& .MuiChip-label': { px: 0.6 }
              }}
            />
          </Tooltip>

          <Tooltip title={`Requires ${analysis.generationCount} breeding generation${analysis.generationCount > 1 ? 's' : ''} (steps)`} arrow>
            <Chip
              size="small"
              label={`${genVis.icon} ${genVis.label}`}
              sx={{
                height: 18,
                fontSize: '0.65rem',
                fontWeight: 800,
                fontFamily: 'monospace',
                backgroundColor: genVis.tint,
                color: genVis.color,
                border: `1px solid ${genVis.border}`,
                '& .MuiChip-label': { px: 0.6 }
              }}
            />
          </Tooltip>
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

        <Box sx={{ p: 0.4, backgroundColor: genVis.tint, borderRadius: '3px', border: `1px solid ${genVis.border}` }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            GENS
          </Typography>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 800,
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              color: genVis.color
            }}
          >
            {genVis.icon} GEN.{analysis.generationCount}
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

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          {scoredRoute.equivalents && scoredRoute.equivalents.length > 0 && (
            <Tooltip title={`${scoredRoute.equivalents.length} other routes reach an equally-good result. Toggle "Group similar" off to list them all.`} arrow>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.65rem' }}>
                ≡ +{scoredRoute.equivalents.length} similar
              </Typography>
            </Tooltip>
          )}
          {group.mapList.length > 1 && (
            <Tooltip title="Alternative plant layouts that reach this same result. Open the inspector to pick one." arrow>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.65rem' }}>
                +{group.mapList.length - 1} alt
              </Typography>
            </Tooltip>
          )}
        </Box>
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
