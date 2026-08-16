import React, { useState } from 'react';
import {
  Paper,
  Box,
  Typography,
  Tabs,
  Tab,
  Button,
  Chip,
  IconButton,
  Tooltip
} from '@mui/material';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import CloseIcon from '@mui/icons-material/Close';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import ErrorIcon from '@mui/icons-material/Error';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useNotification } from '../../../context/NotificationContext.tsx';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { RouteTree } from './RouteTree.tsx';
import { GeneExplanation } from './GeneExplanation.tsx';
import { GREEN_GENES } from '../../../domain/genetics/Gene.ts';

export const RouteInspector: React.FC<{ onClose?: () => void }> = ({ onClose }) => {
  const { selectedGroup, selectedMap, setSelectedGroup } = useCalculation();
  const { clones, startBreedingSession } = useWorkspace();
  const { notifySuccess } = useNotification();

  const [activeTab, setActiveTab] = useState<'tree' | 'planter' | 'genes'>('tree');

  if (!selectedGroup || !selectedMap) {
    return (
      <Paper
        variant="outlined"
        sx={{
          backgroundColor: 'var(--gl-panel-bg)',
          borderColor: 'var(--gl-surface)',
          borderRadius: '6px',
          p: 3,
          height: '100%',
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          textAlign: 'center',
          color: 'var(--gl-text-muted)'
        }}
      >
        <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'var(--gl-text-muted)', mb: 1 }}>
          No Route Selected
        </Typography>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', maxWidth: 220 }}>
          Click "Inspect" on any route card in the center panel to view its full breeding tree, 3×3 planter layout, and gene mechanics.
        </Typography>
      </Paper>
    );
  }

  // Extract leaf required clones and check availability
  const leafClones: { genetics: string; count: number; available: number }[] = [];
  const mapCounts: Record<string, number> = {};

  function extractLeaves(m: any) {
    if (m.baseSapling) {
      if (m.baseSapling.generationIndex > 0 && m.baseSaplingVariants?.mapList?.[0]) {
        extractLeaves(m.baseSaplingVariants.mapList[0]);
      } else {
        const g = m.baseSapling.toString();
        mapCounts[g] = (mapCounts[g] || 0) + 1;
      }
    }
    m.crossbreedingSaplings.forEach((p: any, idx: number) => {
      if (p.generationIndex > 0 && m.crossbreedingSaplingsVariants?.[idx]?.mapList?.[0]) {
        extractLeaves(m.crossbreedingSaplingsVariants[idx].mapList[0]);
      } else {
        const g = p.toString();
        mapCounts[g] = (mapCounts[g] || 0) + 1;
      }
    });
  }

  extractLeaves(selectedMap);

  Object.entries(mapCounts).forEach(([genetics, count]) => {
    const owned = clones.filter(c => c.genetics === genetics).reduce((sum, c) => sum + (c.quantity || 1), 0);
    leafClones.push({ genetics, count, available: owned });
  });

  const handleCopyInstructions = () => {
    const lines: string[] = [];
    lines.push(`=== Rust Breeding Plan: ${selectedGroup.resultSaplingGeneString} ===`);
    lines.push(`Target: ${selectedGroup.resultSaplingGeneString} (${Math.round((selectedMap.chance || 1) * 100)}% chance)`);
    lines.push(`Generations: ${selectedMap.resultSapling.generationIndex || 1}`);
    lines.push('');
    lines.push('Required Base Clones:');
    leafClones.forEach(c => {
      lines.push(`  - [${c.genetics}] x${c.count} (In Bank: ${c.available})`);
    });
    lines.push('');
    lines.push('Planting Setup (Large Planter):');
    if (selectedMap.baseSapling) {
      lines.push(`  Center: [${selectedMap.baseSapling.toString()}] (Plant 1st)`);
    }
    selectedMap.crossbreedingSaplings.forEach((s: any, idx: number) => {
      lines.push(`  Surrounding #${idx + 1}: [${s.toString()}] (Plant 2nd)`);
    });

    navigator.clipboard.writeText(lines.join('\n'));
    notifySuccess('Breeding instructions copied to clipboard!');
  };

  const handleStartBreeding = () => {
    startBreedingSession(selectedMap, selectedGroup.resultSaplingGeneString);
  };

  return (
    <Paper
      variant="outlined"
      sx={{
        backgroundColor: 'var(--gl-panel-bg)',
        borderColor: 'var(--gl-surface)',
        borderRadius: '6px',
        p: 2,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        gap: 1.5,
        overflow: 'hidden'
      }}
    >
      {/* Header Bar */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography
            variant="subtitle2"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.85rem',
              color: 'var(--gl-text-primary)',
              letterSpacing: '0.5px'
            }}
          >
            ROUTE INSPECTOR
          </Typography>
          <Chip
            size="small"
            label={`Score ${Math.round((selectedMap.score || 0.8) * 100)}`}
            sx={{
              height: 20,
              fontSize: '0.68rem',
              fontWeight: 800,
              fontFamily: 'monospace',
              backgroundColor: 'rgba(0, 229, 255, 0.12)',
              color: 'var(--gl-primary)',
              border: '1px solid rgba(0, 229, 255, 0.3)'
            }}
          />
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
          <Tooltip title="Copy step-by-step instructions" arrow>
            <IconButton size="small" onClick={handleCopyInstructions} sx={{ color: 'var(--gl-text-muted)' }}>
              <ContentCopyIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>
          <IconButton size="small" onClick={() => (onClose ? onClose() : setSelectedGroup(null))} sx={{ color: 'var(--gl-text-muted)' }}>
            <CloseIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </Box>
      </Box>

      {/* Target Outcome Card */}
      <Paper
        variant="outlined"
        sx={{
          backgroundColor: 'var(--gl-card-bg)',
          borderColor: 'var(--gl-surface)',
          borderRadius: '4px',
          p: 1.5,
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          gap: 1
        }}
      >
        <GeneticsSequence genes={selectedGroup.resultSaplingGeneString} size="medium" showConnectors={true} />

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-success)', fontWeight: 800, fontFamily: 'monospace' }}>
            {Math.round((selectedMap.chance || 1) * 100)}% Probability
          </Typography>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontFamily: 'monospace' }}>
            GEN.{selectedMap.resultSapling.generationIndex || 1} ({selectedMap.resultSapling.generationIndex || 1} Step{(selectedMap.resultSapling.generationIndex || 1) > 1 ? 's' : ''})
          </Typography>
        </Box>
      </Paper>

      {/* Required Inventory Summary */}
      <Box sx={{ backgroundColor: 'var(--gl-card-bg)', border: '1px solid var(--gl-surface)', borderRadius: '4px', p: 1.25 }}>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 800, fontSize: '0.68rem', mb: 0.75, display: 'block' }}>
          REQUIRED CLONES INVENTORY:
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, maxHeight: 95, overflowY: 'auto' }}>
          {leafClones.map((leaf) => {
            const isReady = leaf.available >= leaf.count;
            return (
              <Box
                key={leaf.genetics}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  py: 0.25,
                  px: 0.5,
                  backgroundColor: 'var(--gl-input-bg)',
                  borderRadius: '3px'
                }}
              >
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                  {isReady ? (
                    <CheckCircleIcon sx={{ fontSize: 13, color: 'var(--gl-success)' }} />
                  ) : (
                    <WarningAmberIcon sx={{ fontSize: 13, color: 'var(--gl-warning)' }} />
                  )}
                  <GeneticsSequence genes={leaf.genetics} size="small" showConnectors={false} />
                </Box>

                <Typography
                  variant="caption"
                  sx={{
                    fontFamily: 'monospace',
                    fontSize: '0.7rem',
                    fontWeight: 700,
                    color: isReady ? 'var(--gl-success)' : 'var(--gl-warning)'
                  }}
                >
                  Have {leaf.available} / Need {leaf.count}
                </Typography>
              </Box>
            );
          })}
        </Box>
      </Box>

      {/* Tabs: Breeding Tree | 3x3 Planter | Gene Weights */}
      <Box sx={{ borderBottom: '1px solid var(--gl-border)' }}>
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          sx={{ minHeight: 30, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)', height: 2 } }}
        >
          <Tab
            value="tree"
            label="Breeding Tree"
            sx={{ minHeight: 30, py: 0.25, px: 1, fontSize: '0.72rem', fontWeight: 800, color: activeTab === 'tree' ? 'var(--gl-primary)' : 'var(--gl-text-muted)' }}
          />
          <Tab
            value="planter"
            label="3x3 Planter"
            sx={{ minHeight: 30, py: 0.25, px: 1, fontSize: '0.72rem', fontWeight: 800, color: activeTab === 'planter' ? 'var(--gl-primary)' : 'var(--gl-text-muted)' }}
          />
          <Tab
            value="genes"
            label="Gene Weights"
            sx={{ minHeight: 30, py: 0.25, px: 1, fontSize: '0.72rem', fontWeight: 800, color: activeTab === 'genes' ? 'var(--gl-primary)' : 'var(--gl-text-muted)' }}
          />
        </Tabs>
      </Box>

      {/* Tab Content Container */}
      <Box sx={{ flex: 1, overflowY: 'auto', pr: 0.5, '&::-webkit-scrollbar': { width: 5 }, '&::-webkit-scrollbar-thumb': { backgroundColor: 'var(--gl-surface-hover)', borderRadius: 3 } }}>
        {activeTab === 'tree' && <RouteTree map={selectedMap} />}

        {activeTab === 'planter' && (
          <Box sx={{ p: 1, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', textAlign: 'center', display: 'block', fontSize: '0.72rem' }}>
              Standard 3×3 Planter Box Crossbreeding Placement
            </Typography>

            {/* 3x3 Visual Grid (Fitted cleanly without overflow) */}
            <Box
              sx={{
                display: 'grid',
                gridTemplateColumns: 'repeat(3, 1fr)',
                gap: 0.75,
                width: '100%',
                maxWidth: 320,
                mx: 'auto',
                backgroundColor: 'var(--gl-app-bg)',
                p: 1,
                borderRadius: '6px',
                border: '1px solid var(--gl-surface)'
              }}
            >
              {[
                { row: 0, col: 0, sIdx: 0 },
                { row: 0, col: 1, sIdx: 1 },
                { row: 0, col: 2, sIdx: 2 },
                { row: 1, col: 0, sIdx: 3 },
                { row: 1, col: 1, isCenter: true },
                { row: 1, col: 2, sIdx: 4 },
                { row: 2, col: 0, sIdx: 5 },
                { row: 2, col: 1, sIdx: 6 },
                { row: 2, col: 2, sIdx: 7 }
              ].map((cell, idx) => {
                if (cell.isCenter) {
                  const centerGeneStr = selectedMap.baseSapling ? selectedMap.baseSapling.toString() : '';
                  return (
                    <Box
                      key={idx}
                      sx={{
                        backgroundColor: 'var(--gl-tint-cyan)',
                        border: '1.5px solid var(--gl-primary)',
                        borderRadius: '4px',
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        p: 0.5,
                        minHeight: 52
                      }}
                    >
                      <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontSize: '0.58rem', mb: 0.25 }}>
                        CENTER
                      </Typography>
                      {centerGeneStr ? (
                        <Box sx={{ display: 'flex', gap: '1px' }}>
                          {centerGeneStr.split('').map((g, gi) => {
                            const isGreen = (GREEN_GENES as readonly string[]).includes(g);
                            return (
                              <Box
                                key={gi}
                                sx={{
                                  width: 12,
                                  height: 12,
                                  borderRadius: '50%',
                                  backgroundColor: isGreen ? '#4A7C17' : '#8A2E22',
                                  color: '#FFF',
                                  fontSize: '0.55rem',
                                  fontWeight: 800,
                                  display: 'flex',
                                  alignItems: 'center',
                                  justifyContent: 'center'
                                }}
                              >
                                {g}
                              </Box>
                            );
                          })}
                        </Box>
                      ) : (
                        <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontSize: '0.6rem' }}>Empty</Typography>
                      )}
                    </Box>
                  );
                }

                const plant = cell.sIdx !== undefined ? selectedMap.crossbreedingSaplings[cell.sIdx] : undefined;
                const geneStr = plant ? plant.toString() : '';

                return (
                  <Box
                    key={idx}
                    sx={{
                      backgroundColor: plant ? 'var(--gl-panel-header-bg)' : 'var(--gl-app-bg)',
                      border: '1px solid',
                      borderColor: plant ? 'var(--gl-surface-hover)' : 'var(--gl-input-bg)',
                      borderRadius: '4px',
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'center',
                      justifyContent: 'center',
                      p: 0.5,
                      minHeight: 52
                    }}
                  >
                    {plant ? (
                      <>
                        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontSize: '0.58rem', mb: 0.25 }}>
                          #{plant.index !== undefined ? plant.index + 1 : (cell.sIdx !== undefined ? cell.sIdx + 1 : idx + 1)}
                        </Typography>
                        <Box sx={{ display: 'flex', gap: '1px' }}>
                          {geneStr.split('').map((g, gi) => {
                            const isGreen = (GREEN_GENES as readonly string[]).includes(g);
                            return (
                              <Box
                                key={gi}
                                sx={{
                                  width: 12,
                                  height: 12,
                                  borderRadius: '50%',
                                  backgroundColor: isGreen ? '#4A7C17' : '#8A2E22',
                                  color: '#FFF',
                                  fontSize: '0.55rem',
                                  fontWeight: 800,
                                  display: 'flex',
                                  alignItems: 'center',
                                  justifyContent: 'center'
                                }}
                              >
                                {g}
                              </Box>
                            );
                          })}
                        </Box>
                      </>
                    ) : (
                      <Typography variant="caption" sx={{ color: 'var(--gl-surface)', fontSize: '0.6rem' }}>
                        Empty
                      </Typography>
                    )}
                  </Box>
                );
              })}
            </Box>

            {/* Clean Detailed Plant Breakdown List */}
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75, mt: 0.5 }}>
              {selectedMap.baseSapling && (
                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', p: 0.75, backgroundColor: 'var(--gl-tint-cyan)', borderRadius: '4px', border: '1px solid rgba(0, 229, 255, 0.3)' }}>
                  <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontSize: '0.68rem' }}>
                    CENTER (1st)
                  </Typography>
                  <GeneticsSequence genes={selectedMap.baseSapling.toString()} size="small" />
                </Box>
              )}

              {selectedMap.crossbreedingSaplings.map((plant: any, pIdx: number) => (
                <Box key={pIdx} sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', p: 0.75, backgroundColor: 'var(--gl-card-bg)', borderRadius: '4px', border: '1px solid var(--gl-border)' }}>
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontSize: '0.68rem' }}>
                    Surrounding #{pIdx + 1}
                  </Typography>
                  <GeneticsSequence genes={plant.toString()} size="small" />
                </Box>
              ))}
            </Box>
          </Box>
        )}

        {activeTab === 'genes' && <GeneExplanation map={selectedMap} />}
      </Box>

      {/* Bottom Action: Start Breeding */}
      <Box sx={{ pt: 1, borderTop: '1px solid var(--gl-surface)' }}>
        <Button
          variant="contained"
          size="medium"
          fullWidth
          onClick={handleStartBreeding}
          startIcon={<PlayCircleIcon sx={{ fontSize: 16 }} />}
          sx={{
            fontWeight: 800,
            py: 0.8,
            backgroundColor: 'var(--gl-warning)',
            color: 'var(--gl-on-accent)',
            fontSize: '0.78rem',
            '&:hover': { backgroundColor: 'var(--gl-warning)' }
          }}
        >
          START BREEDING MODE
        </Button>
      </Box>
    </Paper>
  );
};
