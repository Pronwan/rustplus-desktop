import React, { useEffect, useRef, useState } from 'react';
import {
  Box,
  Typography,
  Button,
  Select,
  MenuItem,
  LinearProgress,
  Tooltip,
  Badge,
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import BlockIcon from '@mui/icons-material/Block';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import FilterListIcon from '@mui/icons-material/FilterList';
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
    filteredAndSortedRoutes,
    rawRouteCount,
    resultsCapped,
    calculationStatusMessage,
    groupSimilar,
    setGroupSimilar
  } = useCalculation();

  const { sourceSaplings } = useWorkspace();
  const { isScannerActive, isScannerInitializing } = useScanner();

  const isScannerBusy = isScannerActive || isScannerInitializing;
  const isReady = sourceSaplings.length >= 2 && !isScannerBusy;
  const [resultStatusMessage, setResultStatusMessage] = useState('');
  const resultControlsRef = useRef(`${sortBy}|${inventoryFilterMode}|${groupSimilar}`);

  useEffect(() => {
    const signature = `${sortBy}|${inventoryFilterMode}|${groupSimilar}`;
    if (signature === resultControlsRef.current) return;
    resultControlsRef.current = signature;
    const sortLabel = sortBy.replace(/-/g, ' ');
    const filterLabel = inventoryFilterMode === 'all' ? 'all inventory' : inventoryFilterMode.replace(/-/g, ' ');
    setResultStatusMessage(
      `Routes sorted by ${sortLabel}, filtered to ${filterLabel}${groupSimilar ? ', grouped by similar quality' : ''}. ${filteredAndSortedRoutes.length} matching routes shown.`
    );
  }, [sortBy, inventoryFilterMode, groupSimilar, filteredAndSortedRoutes.length]);

  return (
    <Box sx={{ mb: 2.5, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
      <Box className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {calculationStatusMessage}
      </Box>
      <Box className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {resultStatusMessage}
      </Box>
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

          <Tooltip
            title={
              groupSimilar
                ? 'Grouping routes of equal quality (same score, chance, generations & clones) into one card. Click to list every route.'
                : 'Showing every route individually. Click to group equal-quality routes.'
            }
            arrow
          >
            <Button
              type="button"
              aria-pressed={groupSimilar}
              onClick={() => setGroupSimilar(!groupSimilar)}
              startIcon={<FilterListIcon sx={{ fontSize: 14, color: groupSimilar ? 'var(--gl-primary)' : 'var(--gl-text-muted)' }} />}
              sx={{
                px: 0.9,
                py: 0.35,
                minWidth: 0,
                borderRadius: '4px',
                border: '1px solid',
                borderColor: groupSimilar ? 'var(--gl-primary)' : 'var(--gl-surface-hover)',
                backgroundColor: groupSimilar ? 'rgba(0, 229, 255, 0.1)' : 'var(--gl-panel-header-bg)',
                color: groupSimilar ? 'var(--gl-primary)' : 'var(--gl-text-muted)',
                fontSize: '0.68rem',
                '& .MuiButton-startIcon': { mr: 0.5 }
              }}
            >
              Group similar
            </Button>
          </Tooltip>

          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontWeight: 700 }}>
            {groupSimilar && rawRouteCount > filteredAndSortedRoutes.length
              ? `Showing ${filteredAndSortedRoutes.length} route groups (${rawRouteCount} matching routes)`
              : `Showing ${filteredAndSortedRoutes.length} matching route${filteredAndSortedRoutes.length === 1 ? '' : 's'}`}
            {resultsCapped && <Box component="span" sx={{ color: 'var(--gl-text-faint)', fontWeight: 600 }}> · Best 500 retained</Box>}
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
            aria-label="Calculation progress"
            aria-valuetext={`Generation ${progress.currentGeneration} of ${progress.totalGenerations}, ${progress.stage}, ${Math.round(progress.progressPercent)} percent`}
            variant="determinate"
            value={Math.min(100, Math.max(0, progress.progressPercent))}
            sx={{
              height: 6,
              borderRadius: 3,
              backgroundColor: 'var(--gl-surface)',
              overflow: 'hidden',
              // MUI drives the determinate bar with translateX but eases it
              // linearly, which reads as stepping against ~120ms progress ticks.
              // A decelerating curve over a slightly longer window turns the
              // same updates into continuous travel.
              '& .MuiLinearProgress-bar': {
                transition: 'transform 520ms cubic-bezier(0.32, 0.72, 0, 1)',
                backgroundColor: 'var(--gl-primary)'
              },
              // Slow sheen so long flat stretches (deep generations, or the
              // between-generation selection pause) still read as alive.
              '&::after': {
                content: '""',
                position: 'absolute',
                inset: 0,
                background:
                  'linear-gradient(90deg, transparent 0%, rgba(255,255,255,0.16) 50%, transparent 100%)',
                transform: 'translate3d(-100%, 0, 0)',
                animation: 'glProgressSheen 1900ms cubic-bezier(0.4, 0, 0.2, 1) infinite',
                pointerEvents: 'none'
              },
              '@keyframes glProgressSheen': {
                '0%': { transform: 'translate3d(-100%, 0, 0)' },
                '100%': { transform: 'translate3d(100%, 0, 0)' }
              },
              '@media (prefers-reduced-motion: reduce)': {
                '& .MuiLinearProgress-bar': { transition: 'none' },
                '&::after': { animation: 'none', opacity: 0 }
              }
            }}
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
            inputProps={{ 'aria-label': 'Sort routes by' }}
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
            <MenuItem value="target" sx={{ fontSize: '0.75rem' }}>Closest Target Match</MenuItem>
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
            inputProps={{ 'aria-label': 'Filter routes by inventory availability' }}
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
