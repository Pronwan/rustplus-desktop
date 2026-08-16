import React from 'react';
import {
  Box,
  Typography,
  Button,
  Select,
  MenuItem,
  LinearProgress,
  Tooltip,
  Badge,
  IconButton
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import BlockIcon from '@mui/icons-material/Block';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import TuneIcon from '@mui/icons-material/Tune';
import { useCalculation, RouteSortOption } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useScanner } from '../../../context/ScannerContext.tsx';

export const RouteToolbar: React.FC = () => {
  const {
    isCalculating,
    progress,
    runSimulation,
    cancelSimulation,
    skipCurrentGeneration,
    options,
    setCalculationPreset,
    sortBy,
    setSortBy,
    inventoryFilterMode,
    setInventoryFilterMode,
    comparedGroups,
    setIsCompareModalOpen,
    filteredAndSortedRoutes
  } = useCalculation();

  const { sourceSaplings } = useWorkspace();
  const { isScannerActive, isScannerInitializing } = useScanner();

  const isScannerBusy = isScannerActive || isScannerInitializing;
  const isReady = sourceSaplings.length >= 2 && !isScannerBusy;

  return (
    <Box sx={{ mb: 2.5, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      {/* Top Row: Primary Calculate Button & Calculation Presets */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1.5 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {isCalculating ? (
            <>
              <Button
                variant="contained"
                color="error"
                size="small"
                onClick={cancelSimulation}
                startIcon={<BlockIcon sx={{ fontSize: 16 }} />}
                sx={{ fontWeight: 800, px: 2, py: 0.8 }}
              >
                CANCEL
              </Button>
              <Button
                variant="outlined"
                color="warning"
                size="small"
                onClick={skipCurrentGeneration}
                startIcon={<SkipNextIcon sx={{ fontSize: 16 }} />}
                sx={{ fontWeight: 800, py: 0.8 }}
              >
                SKIP GEN
              </Button>
            </>
          ) : (
            <Tooltip
              title={
                isScannerBusy
                  ? 'Stop the scanner before calculating routes'
                  : isReady
                  ? 'Calculate optimal breeding routes (Ctrl+Enter)'
                  : 'Add at least 2 plants to calculate'
              }
              arrow
            >
              <span>
                <Button
                  variant="contained"
                  size="small"
                  onClick={() => runSimulation()}
                  disabled={!isReady}
                  startIcon={<PlayArrowIcon sx={{ fontSize: 18 }} />}
                  sx={{
                    fontWeight: 800,
                    px: 3,
                    py: 0.8,
                    fontSize: '0.85rem',
                    letterSpacing: '0.5px'
                  }}
                >
                  CALCULATE ROUTES
                </Button>
              </span>
            </Tooltip>
          )}

          {/* Calculation Presets */}
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, ml: 1 }}>
            {(['fast', 'balanced', 'thorough'] as const).map((preset) => {
              const isSelected = options.calculationPreset === preset;
              const tips: Record<typeof preset, string> = {
                fast: 'Fast — 1 generation, up to 3 surrounding plants. Quickest results with the fewest breeding steps; may miss deeper multi-generation routes.',
                balanced: 'Balanced — up to 2 generations and a wider search. A good trade-off between speed and finding higher-quality routes. Recommended default.',
                thorough: 'Thorough — up to 3 generations, 4 surrounding plants and the widest search. Finds the best possible routes but takes the longest to calculate.'
              };
              return (
                <Tooltip key={preset} title={tips[preset]} arrow>
                  <Button
                    size="small"
                    variant={isSelected ? 'contained' : 'outlined'}
                    color={isSelected ? 'primary' : 'inherit'}
                    disabled={isCalculating}
                    onClick={() => setCalculationPreset(preset)}
                    sx={{
                      py: 0.4,
                      px: 1.2,
                      fontSize: '0.72rem',
                      fontWeight: 800,
                      borderColor: isSelected ? undefined : 'var(--gl-surface-hover)',
                      backgroundColor: isSelected ? undefined : 'var(--gl-panel-header-bg)'
                    }}
                  >
                    {preset.toUpperCase()}
                  </Button>
                </Tooltip>
              );
            })}
          </Box>
        </Box>

        {/* Right Controls: Compare & Count */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          {comparedGroups.length > 0 && (
            <Badge badgeContent={comparedGroups.length} color="secondary">
              <Button
                variant="outlined"
                size="small"
                onClick={() => setIsCompareModalOpen(true)}
                startIcon={<CompareArrowsIcon sx={{ fontSize: 16 }} />}
                sx={{
                  borderColor: 'var(--gl-warning)',
                  color: 'var(--gl-warning)',
                  fontWeight: 800,
                  fontSize: '0.75rem',
                  backgroundColor: 'rgba(255, 152, 0, 0.08)'
                }}
              >
                COMPARE ({comparedGroups.length})
              </Button>
            </Badge>
          )}

          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontWeight: 700 }}>
            {filteredAndSortedRoutes.length} route{filteredAndSortedRoutes.length === 1 ? '' : 's'}
          </Typography>
        </Box>
      </Box>

      {/* Progress Bar when Calculating */}
      {isCalculating && progress && (
        <Box sx={{ p: 1.5, backgroundColor: 'var(--gl-panel-header-bg)', border: '1px solid var(--gl-border)', borderRadius: '4px' }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.75 }}>
            <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontFamily: 'monospace' }}>
              {progress.stage || `Generation ${progress.currentGeneration} of ${progress.totalGenerations}`}
            </Typography>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontFamily: 'monospace' }}>
              {progress.processedCombinations.toLocaleString()} combinations · {progress.progressPercent.toFixed(0)}%
            </Typography>
          </Box>
          <LinearProgress
            variant="determinate"
            value={Math.min(100, Math.max(0, progress.progressPercent))}
            sx={{ height: 6, borderRadius: 3, backgroundColor: 'var(--gl-surface)' }}
          />
        </Box>
      )}

      {/* Second Row: Sorting & Inventory Filter Bar */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1.5, pt: 1, borderTop: '1px solid var(--gl-border-subtle)' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700 }}>
            Sort By:
          </Typography>
          <Select
            size="small"
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as RouteSortOption)}
            sx={{
              height: 28,
              fontSize: '0.75rem',
              fontWeight: 700,
              backgroundColor: 'var(--gl-card-bg)',
              color: 'var(--gl-text-primary)',
              '& .MuiSelect-select': { py: 0.5, px: 1.2 },
              '& fieldset': { borderColor: 'var(--gl-surface)' }
            }}
          >
            <MenuItem value="recommended" sx={{ fontSize: '0.75rem' }}>Recommended Score</MenuItem>
            <MenuItem value="probability" sx={{ fontSize: '0.75rem' }}>Highest Probability</MenuItem>
            <MenuItem value="generations" sx={{ fontSize: '0.75rem' }}>Fewest Generations</MenuItem>
            <MenuItem value="clones" sx={{ fontSize: '0.75rem' }}>Fewest Clones</MenuItem>
            <MenuItem value="inventory" sx={{ fontSize: '0.75rem' }}>Best Inventory Match</MenuItem>
          </Select>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700 }}>
            Inventory Filter:
          </Typography>
          <Select
            size="small"
            value={inventoryFilterMode}
            onChange={(e) => setInventoryFilterMode(e.target.value as any)}
            sx={{
              height: 28,
              fontSize: '0.75rem',
              fontWeight: 700,
              backgroundColor: 'var(--gl-card-bg)',
              color: 'var(--gl-primary)',
              '& .MuiSelect-select': { py: 0.5, px: 1.2 },
              '& fieldset': { borderColor: 'var(--gl-surface)' }
            }}
          >
            <MenuItem value="all" sx={{ fontSize: '0.75rem' }}>All Routes</MenuItem>
            <MenuItem value="available-only" sx={{ fontSize: '0.75rem' }}>Available Clones Only</MenuItem>
            <MenuItem value="partial-or-better" sx={{ fontSize: '0.75rem' }}>Partial / Available</MenuItem>
          </Select>
        </Box>
      </Box>
    </Box>
  );
};
