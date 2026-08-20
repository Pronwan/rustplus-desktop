import React, { useEffect, useRef, useState } from 'react';
import {
  Box,
  Typography,
  Button,
  Select,
  MenuItem,
  Menu,
  LinearProgress,
  Tooltip,
  Badge,
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import BlockIcon from '@mui/icons-material/Block';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import CompareArrowsIcon from '@mui/icons-material/CompareArrows';
import TuneIcon from '@mui/icons-material/Tune';
import CheckIcon from '@mui/icons-material/Check';
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
    results,
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
  const [viewMenuAnchor, setViewMenuAnchor] = useState<HTMLElement | null>(null);
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

          <Select
            inputProps={{ 'aria-label': 'Search depth' }}
            size="small"
            value={options.calculationPreset}
            disabled={isCalculating}
            onChange={(event) => setCalculationPreset(event.target.value as 'fast' | 'balanced' | 'thorough')}
            sx={{ height: 34, minWidth: 150, fontSize: '0.75rem', fontWeight: 800, backgroundColor: 'var(--gl-panel-header-bg)', '& fieldset': { borderColor: 'var(--gl-surface-hover)' } }}
          >
            <MenuItem value="fast" sx={{ fontSize: '0.75rem' }}>Search: Fast</MenuItem>
            <MenuItem value="balanced" sx={{ fontSize: '0.75rem' }}>Search: Balanced</MenuItem>
            <MenuItem value="thorough" sx={{ fontSize: '0.75rem' }}>Search: Thorough</MenuItem>
          </Select>
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

          {results.length > 0 && (
            <>
              <Button
                type="button"
                aria-haspopup="menu"
                aria-expanded={Boolean(viewMenuAnchor)}
                onClick={(event) => setViewMenuAnchor(event.currentTarget)}
                startIcon={<TuneIcon sx={{ fontSize: 16 }} />}
                sx={{ minHeight: 32, px: 1, color: 'var(--gl-text-muted)', border: '1px solid var(--gl-surface-hover)', fontSize: '0.75rem', fontWeight: 800 }}
              >
                View
              </Button>
              <Menu anchorEl={viewMenuAnchor} open={Boolean(viewMenuAnchor)} onClose={() => setViewMenuAnchor(null)}>
                <MenuItem onClick={() => { setGroupSimilar(!groupSimilar); setViewMenuAnchor(null); }} sx={{ minWidth: 220, fontSize: '0.78rem' }}>
                  <Box sx={{ width: 24 }}>{groupSimilar && <CheckIcon sx={{ fontSize: 18, color: 'var(--gl-primary)' }} />}</Box>
                  Group similar routes
                </MenuItem>
              </Menu>
            </>
          )}

          {results.length > 0 && (
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.75rem', fontWeight: 700 }}>
              {groupSimilar && rawRouteCount > filteredAndSortedRoutes.length
                ? `${filteredAndSortedRoutes.length} groups · ${rawRouteCount} routes`
                : `${filteredAndSortedRoutes.length} route${filteredAndSortedRoutes.length === 1 ? '' : 's'}`}
              {resultsCapped && <Box component="span" sx={{ color: 'var(--gl-text-faint)', fontWeight: 600 }}> · best 500 kept</Box>}
            </Typography>
          )}
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
                transition: 'transform 160ms ease-out',
                backgroundColor: 'var(--gl-primary)'
              },
              '@media (prefers-reduced-motion: reduce)': {
                '& .MuiLinearProgress-bar': { transition: 'none' }
              }
            }}
          />
        </Box>
      )}

      {/* Second Row: Sorting & Inventory Filter Bar */}
      {results.length > 0 && <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1.5, pt: 1, borderTop: '1px solid var(--gl-border-subtle)' }}>
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
            <MenuItem value="recommended" sx={{ fontSize: '0.75rem' }}>Recommended</MenuItem>
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
      </Box>}
    </Box>
  );
};
