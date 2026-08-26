import React, { useState, useEffect } from 'react';
import {
  Box,
  Typography,
  Button,
  Paper
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useFlipGrid } from '../../../utils/useFlipGrid.ts';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useScanner } from '../../../context/ScannerContext.tsx';
import { RouteToolbar } from './RouteToolbar.tsx';
import { RouteCard } from './RouteCard.tsx';
import { RouteComparisonModal } from './RouteComparisonModal.tsx';
import { MissingCloneAdvisor } from '../TargetDesigner/MissingCloneAdvisor.tsx';

const PAGE_SIZE = 8;
export const nextRoutePageSize = (current: number, total: number) => Math.min(current + PAGE_SIZE, total);

export const RouteGrid: React.FC = () => {
  const {
    filteredAndSortedRoutes,
    selectedGroup,
    isCalculating,
    results,
    runSimulation,
    sortBy
  } = useCalculation();
  const { sourceSaplings, selectedPlant } = useWorkspace();
  const { isScannerActive, isScannerInitializing } = useScanner();

  const [visibleCount, setVisibleCount] = useState<number>(PAGE_SIZE);
  const [isAdvisorOpen, setIsAdvisorOpen] = useState(false);

  // Reset visible count when results or sorting changes
  useEffect(() => {
    setVisibleCount(PAGE_SIZE);
  }, [filteredAndSortedRoutes]);

  // Re-runs the FLIP pass whenever the visible ordering can have changed.
  const gridRef = useFlipGrid<HTMLDivElement>(
    `${sortBy}|${visibleCount}|${filteredAndSortedRoutes
      .slice(0, visibleCount)
      .map(r => r.group.resultSaplingGeneString)
      .join(',')}`,
    { animateMoves: !isCalculating }
  );

  const isScannerBusy = isScannerActive || isScannerInitializing;
  const hasClones = sourceSaplings.length >= 2;
  const visibleRoutes = filteredAndSortedRoutes.slice(0, visibleCount);
  const hasMore = visibleCount < filteredAndSortedRoutes.length;
  const hasReadyRoute = filteredAndSortedRoutes.some(route => route.analysis.inventoryStatus === 'available');

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', width: '100%' }}>
      {/* Top Controls Toolbar */}
      <RouteToolbar />

      {/* Main Results Content Area */}
      {isCalculating && results.length === 0 ? (
        /* Calculating Initial State */
        <Box
          sx={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            p: 6,
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1px solid var(--gl-surface)',
            borderRadius: '6px',
            textAlign: 'center'
          }}
        >
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-primary)', mb: 1, fontFamily: 'monospace' }}>
            Simulating Breeding Permutations…
          </Typography>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', maxWidth: 360 }}>
            Analyzing surrounding plant weights, tie scenarios, and recursive generation routes for {selectedPlant.replace(/-/g, ' ')}.
          </Typography>
        </Box>
      ) : results.length === 0 ? (
        /* Empty State: Not yet calculated */
        <Box
          sx={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            p: 5,
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1.5px dashed var(--gl-border)',
            borderRadius: '6px',
            textAlign: 'center'
          }}
        >
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)', mb: 1 }}>
            Ready To Find Breeding Routes
          </Typography>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', mb: 2.5, maxWidth: 420 }}>
            {hasClones
              ? `You have ${sourceSaplings.length} plants in your list. Choose your target above and click Calculate Routes.`
              : 'Type or paste at least 2 plants in the Gene Inputs panel on the left to start finding optimal crossbreeding combinations.'}
          </Typography>

          <Button
            variant="contained"
            size="medium"
            disabled={!hasClones || isScannerBusy}
            onClick={() => runSimulation()}
            startIcon={<PlayArrowIcon sx={{ fontSize: 18 }} />}
            sx={{ fontWeight: 800, px: 3, backgroundColor: 'var(--gl-primary)', color: 'var(--gl-on-accent)', '&:hover': { backgroundColor: 'var(--gl-primary-hover)' } }}
          >
            {isScannerBusy ? 'Stop Scanner To Calculate' : 'Calculate Routes'}
          </Button>
        </Box>
      ) : filteredAndSortedRoutes.length === 0 ? (
        /* Empty State: Filters returned no match */
        <Box
          sx={{
            p: 4,
            textAlign: 'center',
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1px solid var(--gl-border)',
            borderRadius: '6px'
          }}
        >
          <Typography variant="subtitle2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)', mb: 1 }}>
            No Routes Match Current Target / Filter
          </Typography>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', mb: 2 }}>
            Try changing the Target Match Mode to "At Least" or "Best Possible", or resetting inventory filters.
          </Typography>
          <Button
            variant="outlined"
            size="small"
            onClick={() => setIsAdvisorOpen(true)}
            startIcon={<AutoAwesomeIcon sx={{ fontSize: 16 }} />}
            sx={{ minHeight: 36, color: 'var(--gl-warning)', borderColor: 'rgba(255, 152, 0, 0.5)', fontWeight: 800 }}
          >
            What Clone Should I Collect?
          </Button>
        </Box>
      ) : (
        /* Ranked route list */
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          {!hasReadyRoute && (
            <Paper
              variant="outlined"
              sx={{ p: 1.25, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1.5, flexWrap: 'wrap', backgroundColor: 'rgba(255, 152, 0, 0.06)', borderColor: 'rgba(255, 152, 0, 0.35)' }}
            >
              <Box>
                <Typography sx={{ color: 'var(--gl-text-primary)', fontSize: '0.78rem', fontWeight: 800 }}>
                  No route is ready with your current clone bank
                </Typography>
                <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem' }}>
                  See the smallest useful clone upgrades for this target.
                </Typography>
              </Box>
              <Button
                variant="outlined"
                size="small"
                onClick={() => setIsAdvisorOpen(true)}
                startIcon={<AutoAwesomeIcon sx={{ fontSize: 16 }} />}
                sx={{ minHeight: 36, color: 'var(--gl-warning)', borderColor: 'rgba(255, 152, 0, 0.5)', fontWeight: 800 }}
              >
                What Should I Collect?
              </Button>
            </Paper>
          )}

          <Box
            ref={gridRef}
            sx={{
              display: 'flex',
              flexDirection: 'column',
              gap: 1
            }}
          >
            {visibleRoutes.map((scoredRoute, idx) => {
              const isSelected =
                selectedGroup?.resultSaplingGeneString === scoredRoute.group.resultSaplingGeneString;

              return (
                <RouteCard
                  key={scoredRoute.group.resultSaplingGeneString}
                  flipKey={scoredRoute.group.resultSaplingGeneString}
                  scoredRoute={scoredRoute}
                  rankIndex={idx}
                  isSelected={isSelected}
                />
              );
            })}
          </Box>

          {/* Show More Pagination Controls */}
          {hasMore && (
            <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 1.5, py: 2 }}>
              <Button
                variant="outlined"
                size="small"
                onClick={() => setVisibleCount((prev) => nextRoutePageSize(prev, filteredAndSortedRoutes.length))}
                endIcon={<ExpandMoreIcon sx={{ fontSize: 16 }} />}
                sx={{
                  color: 'var(--gl-primary)',
                  borderColor: 'rgba(0, 229, 255, 0.4)',
                  fontWeight: 700,
                  fontSize: '0.75rem',
                  '&:hover': { borderColor: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
                }}
              >
                Show More (+{Math.min(PAGE_SIZE, filteredAndSortedRoutes.length - visibleCount)})
              </Button>
            </Box>
          )}
        </Box>
      )}

      {/* Comparison Modal */}
      <RouteComparisonModal />
      <MissingCloneAdvisor open={isAdvisorOpen} onClose={() => setIsAdvisorOpen(false)} />
    </Box>
  );
};
