import React, { useMemo } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  IconButton,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Paper,
  Chip
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { analyzeRoute } from '../../../domain/genetics/routeScoring.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

export const RouteComparisonModal: React.FC = () => {
  const {
    comparedGroups,
    clearComparedGroups,
    isCompareModalOpen,
    setIsCompareModalOpen,
    setSelectedGroup
  } = useCalculation();
  const { clones, targetConfig, startBreedingSession } = useWorkspace();

  const comparedAnalyses = useMemo(() => {
    return comparedGroups.map(group => {
      const bestMap = group.mapList[0];
      const analysis = analyzeRoute(bestMap, clones, targetConfig.targetGenetics);
      return { group, bestMap, analysis };
    });
  }, [comparedGroups, clones, targetConfig.targetGenetics]);

  const handleClose = () => {
    setIsCompareModalOpen(false);
  };

  const handleSelectAndInspect = (group: any) => {
    setSelectedGroup(group);
    setIsCompareModalOpen(false);
  };

  const handleStartBreeding = (bestMap: any, targetStr: string) => {
    startBreedingSession(bestMap, targetStr);
    setIsCompareModalOpen(false);
  };

  if (!isCompareModalOpen || comparedGroups.length === 0) return null;

  return (
    <Dialog
      open={isCompareModalOpen}
      onClose={handleClose}
      maxWidth="md"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1px solid var(--gl-surface-hover)',
            borderRadius: '6px',
            color: 'var(--gl-text-primary)'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Route Comparison ({comparedGroups.length} Routes)
        </Typography>
        <IconButton size="small" onClick={handleClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 1.5 }}>
        <TableContainer component={Paper} variant="outlined" sx={{ backgroundColor: 'var(--gl-panel-header-bg)', borderColor: 'var(--gl-border)' }}>
          <Table size="small">
            <TableHead>
              <TableRow sx={{ backgroundColor: 'var(--gl-border-subtle)' }}>
                <TableCell sx={{ color: 'var(--gl-text-muted)', fontWeight: 800, fontSize: '0.75rem', width: 140 }}>METRIC</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontSize: '0.8rem' }}>
                    ROUTE {String.fromCharCode(65 + idx)}
                  </TableCell>
                ))}
              </TableRow>
            </TableHead>

            <TableBody>
              {/* Genetics Row */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>GENETICS</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center">
                    <GeneticsSequence genes={item.group.resultSaplingGeneString} size="small" showConnectors={true} />
                  </TableCell>
                ))}
              </TableRow>

              {/* Recommendation Score */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>RECOMMENDED SCORE</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center">
                    <Chip
                      size="small"
                      label={`Score ${item.analysis.recommendationScore}`}
                      sx={{
                        fontWeight: 800,
                        backgroundColor: 'rgba(0, 229, 255, 0.12)',
                        color: 'var(--gl-primary)',
                        border: '1px solid rgba(0, 229, 255, 0.3)'
                      }}
                    />
                  </TableCell>
                ))}
              </TableRow>

              {/* Probability */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>PROBABILITY</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell
                    key={idx}
                    align="center"
                    sx={{
                      fontWeight: 800,
                      fontFamily: 'monospace',
                      color: item.analysis.probabilityPercent >= 95 ? 'var(--gl-success)' : 'var(--gl-warning)'
                    }}
                  >
                    {item.analysis.probabilityPercent}%
                  </TableCell>
                ))}
              </TableRow>

              {/* Generations */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>GENERATIONS</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center" sx={{ fontWeight: 800, fontFamily: 'monospace', color: 'var(--gl-text-primary)' }}>
                    GEN.{item.analysis.generationCount} ({item.analysis.generationCount === 1 ? '1 Step' : `${item.analysis.generationCount} Steps`})
                  </TableCell>
                ))}
              </TableRow>

              {/* Unique Clones Required */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>UNIQUE CLONES</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center" sx={{ fontWeight: 800, fontFamily: 'monospace', color: 'var(--gl-text-primary)' }}>
                    {item.analysis.uniqueCloneCount} Clones
                  </TableCell>
                ))}
              </TableRow>

              {/* Total Placements */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>TOTAL PLACEMENTS</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center" sx={{ fontWeight: 800, fontFamily: 'monospace', color: 'var(--gl-text-primary)' }}>
                    {item.analysis.totalPlacementsCount} Plants
                  </TableCell>
                ))}
              </TableRow>

              {/* Inventory Match */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>INVENTORY STATUS</TableCell>
                {comparedAnalyses.map((item, idx) => {
                  const isAvail = item.analysis.inventoryStatus === 'available';
                  return (
                    <TableCell key={idx} align="center">
                      <Chip
                        size="small"
                        label={isAvail ? '✓ Ready in Inventory' : `Missing ${item.analysis.missingClonesCount}`}
                        sx={{
                          fontSize: '0.68rem',
                          fontWeight: 800,
                          backgroundColor: isAvail ? 'rgba(76, 175, 80, 0.15)' : 'rgba(255, 167, 38, 0.15)',
                          color: isAvail ? 'var(--gl-success)' : 'var(--gl-warning)',
                          border: `1px solid ${isAvail ? 'rgba(76, 175, 80, 0.4)' : 'rgba(255, 167, 38, 0.4)'}`
                        }}
                      />
                    </TableCell>
                  );
                })}
              </TableRow>

              {/* Difficulty */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>DIFFICULTY</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center" sx={{ fontWeight: 800, color: 'var(--gl-primary)' }}>
                    {item.analysis.difficulty}
                  </TableCell>
                ))}
              </TableRow>

              {/* Action Buttons */}
              <TableRow>
                <TableCell sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, fontSize: '0.75rem' }}>ACTION</TableCell>
                {comparedAnalyses.map((item, idx) => (
                  <TableCell key={idx} align="center">
                    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75, alignItems: 'center' }}>
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => handleSelectAndInspect(item.group)}
                        sx={{ fontSize: '0.7rem', py: 0.3, width: 120 }}
                      >
                        Inspect Route
                      </Button>
                      <Button
                        size="small"
                        variant="contained"
                        color="secondary"
                        onClick={() => handleStartBreeding(item.bestMap, item.group.resultSaplingGeneString)}
                        startIcon={<PlayCircleIcon sx={{ fontSize: 13 }} />}
                        sx={{ fontSize: '0.7rem', py: 0.3, width: 120, backgroundColor: 'var(--gl-warning)', color: 'var(--gl-on-accent)' }}
                      >
                        Start Breed
                      </Button>
                    </Box>
                  </TableCell>
                ))}
              </TableRow>
            </TableBody>
          </Table>
        </TableContainer>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2, justifyContent: 'space-between' }}>
        <Button onClick={clearComparedGroups} color="error" size="small">
          Clear Comparison
        </Button>
        <Button onClick={handleClose} variant="contained" size="small">
          Close
        </Button>
      </DialogActions>
    </Dialog>
  );
};
