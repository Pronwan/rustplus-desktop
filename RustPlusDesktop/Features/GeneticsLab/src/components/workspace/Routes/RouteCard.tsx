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
    available: { label: '✓ Ready', bg: 'rgba(76, 175, 80, 0.12)', color: '#4CAF50', border: 'rgba(76, 175, 80, 0.35)' },
    partial: { label: `⚠ Missing ${analysis.missingClonesCount}`, bg: 'rgba(255, 167, 38, 0.12)', color: '#FFA726', border: 'rgba(255, 167, 38, 0.35)' },
    missing: { label: `✕ Missing ${analysis.missingClonesCount}`, bg: 'rgba(229, 57, 53, 0.12)', color: '#E53935', border: 'rgba(229, 57, 53, 0.35)' }
  };

  const invBadge = invBadgeConfig[analysis.inventoryStatus];

  return (
    <Paper
      onClick={handleInspect}
      variant="outlined"
      sx={{
        backgroundColor: isSelected ? '#121E22' : '#141414',
        border: '1px solid',
        borderColor: isSelected ? '#00E5FF' : isBest ? '#383838' : '#222222',
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
          borderColor: isSelected ? '#00E5FF' : '#444444',
          backgroundColor: isSelected ? '#121E22' : '#181818'
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
              color: isBest ? '#00E5FF' : '#E0E0E0'
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
              color: analysis.recommendationScore >= 80 ? '#00E5FF' : '#AAAAAA',
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
              borderColor: isCompared ? '#FF9800' : 'transparent',
              '&:hover': { backgroundColor: 'rgba(255, 255, 255, 0.05)' }
            }}
          >
            <CompareArrowsIcon sx={{ fontSize: 13, color: isCompared ? '#FF9800' : '#666' }} />
            <Typography variant="caption" sx={{ fontSize: '0.65rem', fontWeight: 700, color: isCompared ? '#FF9800' : '#888' }}>
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
        <Box sx={{ p: 0.4, backgroundColor: '#1A1A1A', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: '#666', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            CHANCE
          </Typography>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 800,
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              color: analysis.probabilityPercent >= 95 ? '#4CAF50' : analysis.probabilityPercent >= 50 ? '#FFA726' : '#E53935'
            }}
          >
            {analysis.probabilityPercent}%
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: '#1A1A1A', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: '#666', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            GENS
          </Typography>
          <Typography
            variant="body2"
            sx={{
              fontWeight: 800,
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              color: analysis.generationCount === 1 ? '#4CAF50' : '#FFA726'
            }}
          >
            GEN.{analysis.generationCount}
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: '#1A1A1A', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: '#666', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            CLONES
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', fontSize: '0.75rem', color: '#CCCCCC' }}>
            {analysis.uniqueCloneCount} unq
          </Typography>
        </Box>

        <Box sx={{ p: 0.4, backgroundColor: '#1A1A1A', borderRadius: '3px' }}>
          <Typography variant="caption" sx={{ color: '#666', fontSize: '0.58rem', display: 'block', fontWeight: 700 }}>
            PLANTS
          </Typography>
          <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', fontSize: '0.75rem', color: '#CCCCCC' }}>
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
          <Typography variant="caption" sx={{ color: '#666', fontFamily: 'monospace', fontSize: '0.65rem' }}>
            +{group.mapList.length - 1} alt
          </Typography>
        )}
      </Box>

      {/* Card Action Buttons */}
      <Box sx={{ display: 'flex', gap: 0.75, pt: 0.5, borderTop: '1px solid #1E1E1E' }}>
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
            borderColor: isSelected ? undefined : '#2C2C2C'
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
            backgroundColor: '#FF9800',
            color: '#000',
            '&:hover': { backgroundColor: '#FFA726' }
          }}
        >
          BREED
        </Button>
      </Box>
    </Paper>
  );
};
