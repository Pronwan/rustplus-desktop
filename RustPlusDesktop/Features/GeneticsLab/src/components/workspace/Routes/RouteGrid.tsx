import React, { useState, useEffect } from 'react';
import {
  Box,
  Typography,
  Button
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useScanner } from '../../../context/ScannerContext.tsx';
import { RouteToolbar } from './RouteToolbar.tsx';
import { RouteCard } from './RouteCard.tsx';
import { RouteComparisonModal } from './RouteComparisonModal.tsx';

const PAGE_SIZE = 18;

export const RouteGrid: React.FC = () => {
  const {
    filteredAndSortedRoutes,
    selectedGroup,
    isCalculating,
    results,
    runSimulation
  } = useCalculation();
  const { sourceSaplings, selectedPlant } = useWorkspace();
  const { isScannerActive, isScannerInitializing } = useScanner();

  const [visibleCount, setVisibleCount] = useState<number>(PAGE_SIZE);

  // Reset visible count when results or sorting changes
  useEffect(() => {
    setVisibleCount(PAGE_SIZE);
  }, [results, filteredAndSortedRoutes.length]);

  const isScannerBusy = isScannerActive || isScannerInitializing;
  const hasClones = sourceSaplings.length >= 2;
  const visibleRoutes = filteredAndSortedRoutes.slice(0, visibleCount);
  const hasMore = visibleCount < filteredAndSortedRoutes.length;

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
            backgroundColor: '#121212',
            border: '1px solid #222222',
            borderRadius: '6px',
            textAlign: 'center'
          }}
        >
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: '#00E5FF', mb: 1, fontFamily: 'monospace' }}>
            Simulating Breeding Permutations…
          </Typography>
          <Typography variant="caption" sx={{ color: '#888888', maxWidth: 360 }}>
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
            backgroundColor: '#121212',
            border: '1.5px dashed #282828',
            borderRadius: '6px',
            textAlign: 'center'
          }}
        >
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: '#FFFFFF', mb: 1 }}>
            Ready To Find Breeding Routes
          </Typography>
          <Typography variant="body2" sx={{ color: '#888888', mb: 2.5, maxWidth: 420 }}>
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
            sx={{ fontWeight: 800, px: 3, backgroundColor: '#00E5FF', color: '#000', '&:hover': { backgroundColor: '#33EBFF' } }}
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
            backgroundColor: '#141414',
            border: '1px solid #282828',
            borderRadius: '6px'
          }}
        >
          <Typography variant="subtitle2" sx={{ fontWeight: 800, color: '#FFFFFF', mb: 1 }}>
            No Routes Match Current Target / Filter
          </Typography>
          <Typography variant="caption" sx={{ color: '#888888', display: 'block', mb: 2 }}>
            Try changing the Target Match Mode to "At Least" or "Best Possible", or resetting inventory filters.
          </Typography>
        </Box>
      ) : (
        /* Route Cards Grid */
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: {
                xs: '1fr',
                sm: 'repeat(auto-fill, minmax(260px, 1fr))'
              },
              gap: 1.5
            }}
          >
            {visibleRoutes.map((scoredRoute, idx) => {
              const isSelected =
                selectedGroup?.resultSaplingGeneString === scoredRoute.group.resultSaplingGeneString;

              return (
                <RouteCard
                  key={scoredRoute.group.resultSaplingGeneString}
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
                onClick={() => setVisibleCount((prev) => Math.min(prev + PAGE_SIZE, filteredAndSortedRoutes.length))}
                endIcon={<ExpandMoreIcon sx={{ fontSize: 16 }} />}
                sx={{
                  color: '#00E5FF',
                  borderColor: 'rgba(0, 229, 255, 0.4)',
                  fontWeight: 700,
                  fontSize: '0.75rem',
                  '&:hover': { borderColor: '#00E5FF', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
                }}
              >
                Show More (+{Math.min(PAGE_SIZE, filteredAndSortedRoutes.length - visibleCount)})
              </Button>

              <Button
                variant="text"
                size="small"
                onClick={() => setVisibleCount(filteredAndSortedRoutes.length)}
                sx={{ color: '#888', fontSize: '0.72rem', fontWeight: 600, '&:hover': { color: '#FFF' } }}
              >
                Show All ({filteredAndSortedRoutes.length})
              </Button>
            </Box>
          )}
        </Box>
      )}

      {/* Comparison Modal */}
      <RouteComparisonModal />
    </Box>
  );
};
