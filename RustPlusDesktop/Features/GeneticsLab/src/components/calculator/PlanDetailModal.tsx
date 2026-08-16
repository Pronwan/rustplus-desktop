import React, { useState, useEffect } from 'react';
import {
  Dialog,
  DialogContent,
  Typography,
  Box,
  IconButton,
  Paper,
  Button
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import YardIcon from '@mui/icons-material/Yard';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { GeneticsMapGroup } from '../../domain/genetics/GeneticsMapGroup.ts';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { PlanterGuideModal } from '../modals/PlanterGuideModal.tsx';
import { useApp } from '../../context/AppContext.tsx';

interface SinglePlanCardProps {
  map: GeneticsMap;
  title?: string;
  isBest?: boolean;
  isSelected?: boolean;
  isHighlightedView?: boolean;
  onSelect?: () => void;
  onOpenParentPlan?: (parentSapling: Sapling, parentMap?: GeneticsMap) => void;
}

export const SinglePlanCard: React.FC<SinglePlanCardProps> = ({
  map,
  title,
  isBest,
  isSelected,
  isHighlightedView,
  onSelect,
  onOpenParentPlan
}) => {
  const { options, results } = useApp();
  const [isPlanterGuideOpen, setIsPlanterGuideOpen] = useState(false);
  const targetSapling = map.resultSapling;
  const score = targetSapling.getScore(options.geneScores);
  const genIndex = Math.max(1, targetSapling.generationIndex || 1);
  const chanceProd = map.getChanceProduct();

  const isGen1 = genIndex === 1;
  const genColor = isGen1 ? '#598518' : 'var(--gl-warning)';
  const chanceColor = chanceProd >= 0.99 ? '#598518' : chanceProd >= 0.5 ? 'var(--gl-warning)' : 'var(--gl-error)';

  return (
    <Paper
      onClick={onSelect}
      variant="outlined"
      sx={{
        backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-card-bg)',
        border: isSelected
          ? '2px solid var(--gl-primary)'
          : isHighlightedView || isBest
          ? '1.5px dashed #FFFFFF'
          : '1px solid var(--gl-border)',
        borderRadius: '4px',
        p: 2.5,
        width: 320,
        maxWidth: '100%',
        cursor: onSelect ? 'pointer' : 'default',
        transition: 'all 0.2s ease',
        boxShadow: isSelected ? '0 0 0 1px rgba(0, 229, 255, 0.35), 0 8px 24px rgba(0, 229, 255, 0.12)' : 'none',
        '&:hover': onSelect ? { borderColor: 'var(--gl-primary)', transform: 'translateY(-2px)' } : {}
      }}
    >
      {title && (
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 1.5, gap: 0.5 }}>
          <Typography
            variant="caption"
            sx={{
              display: 'block',
              textAlign: 'center',
              fontWeight: 800,
              color: isSelected ? 'var(--gl-primary)' : isBest ? 'var(--gl-primary)' : 'var(--gl-text-primary)',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.85rem'
            }}
          >
            {title}
          </Typography>
          {isSelected && (
            <Typography
              variant="caption"
              sx={{
                px: 0.9,
                py: 0.1,
                borderRadius: '3px',
                backgroundColor: 'rgba(0, 229, 255, 0.14)',
                border: '1px solid rgba(0, 229, 255, 0.5)',
                color: 'var(--gl-primary)',
                fontWeight: 800,
                fontFamily: 'monospace',
                fontSize: '0.62rem',
                letterSpacing: '0.08em'
              }}
            >
              ✓ SELECTED
            </Typography>
          )}
        </Box>
      )}

      {/* Target Result Genes */}
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 1.5 }}>
        <SaplingGeneRepr sapling={targetSapling} size="medium" showConnectors={true} />

        {/* Stats: GEN. X · Score: X · Chance: X% */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mt: 1 }}>
          <Typography
            variant="caption"
            sx={{ fontWeight: 800, color: genColor, fontFamily: 'monospace', fontSize: '0.82rem' }}
          >
            GEN.{genIndex}
          </Typography>

          <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)' }}>·</Typography>

          <Typography
            variant="caption"
            sx={{ fontWeight: 700, color: 'var(--gl-text-primary)', fontFamily: 'monospace', fontSize: '0.82rem' }}
          >
            Score: <span style={{ color: 'var(--gl-text-primary)', fontWeight: 800 }}>{score}</span>
          </Typography>

          <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)' }}>·</Typography>

          <Typography
            variant="caption"
            sx={{ fontWeight: 700, color: chanceColor, fontFamily: 'monospace', fontSize: '0.82rem' }}
          >
            Chance: <span style={{ fontWeight: 800 }}>{(chanceProd * 100).toFixed(0)}%</span>
          </Typography>
        </Box>
      </Box>

      {/* Center Plant */}
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 1.5 }}>
        <Typography
          variant="caption"
          sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem', fontFamily: 'monospace', mb: 0.5 }}
        >
          Center Plant:
        </Typography>

        {map.baseSapling ? (
          (() => {
            const isBaseGen = map.baseSapling.generationIndex > 0;
            const handleBaseClick = (e: React.MouseEvent) => {
              if (isBaseGen && onOpenParentPlan) {
                e.stopPropagation();
                let parentPlanMap: GeneticsMap | undefined;
                const variantGroup = map.baseSaplingVariants;
                if (variantGroup && variantGroup.mapList && variantGroup.mapList.length > 0) {
                  parentPlanMap = variantGroup.mapList[0];
                }
                if (!parentPlanMap) {
                  const foundGroup = results.find((g) => g.resultSaplingGeneString === map.baseSapling!.toString());
                  parentPlanMap = foundGroup?.mapList?.[0];
                }
                if (!parentPlanMap) {
                  for (const g of results) {
                    const m = g.mapList.find((ml) => ml.resultSapling.toString() === map.baseSapling!.toString());
                    if (m) {
                      parentPlanMap = m;
                      break;
                    }
                  }
                }
                onOpenParentPlan(map.baseSapling!, parentPlanMap);
              }
            };

            return (
              <Box
                onClick={handleBaseClick}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 0.75,
                  p: isBaseGen ? '2px 6px' : 0,
                  border: isBaseGen ? '1px solid var(--gl-warning)' : 'none',
                  borderRadius: '3px',
                  cursor: isBaseGen && onOpenParentPlan ? 'pointer' : 'default',
                  backgroundColor: isBaseGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                  '&:hover': isBaseGen && onOpenParentPlan ? { borderColor: 'var(--gl-primary)' } : {}
                }}
              >
                {isBaseGen ? (
                  <Typography
                    variant="caption"
                    sx={{ color: 'var(--gl-warning)', fontWeight: 800, fontSize: '0.72rem', fontFamily: 'monospace' }}
                  >
                    GEN.{map.baseSapling.generationIndex}
                  </Typography>
                ) : (
                  <Typography
                    variant="caption"
                    sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.75rem', minWidth: 14 }}
                  >
                    #{map.baseSapling.index !== undefined ? map.baseSapling.index + 1 : '1'}
                  </Typography>
                )}
                <SaplingGeneRepr sapling={map.baseSapling} size="small" showConnectors={true} />
              </Box>
            );
          })()
        ) : (
          <Typography
            variant="caption"
            sx={{ color: 'var(--gl-text-primary)', fontFamily: 'monospace', fontWeight: 700, fontSize: '0.78rem' }}
          >
            any extra plant of same type
          </Typography>
        )}
      </Box>

      {/* Surrounding Plants */}
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <Typography
          variant="caption"
          sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem', fontFamily: 'monospace', mb: 0.75 }}
        >
          Surrounding Plants:
        </Typography>

        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75, alignItems: 'center', width: '100%' }}>
          {map.crossbreedingSaplings.map((parent, pIdx) => {
            const isParentGen = parent.generationIndex > 0;
            const parentGenNum = parent.generationIndex;

            const isFirst = map.tieWinningCrossbreedingSaplingIndexes?.includes(pIdx);
            const isSecond = map.tieLosingCrossbreedingSaplingIndexes?.includes(pIdx);

            const handleParentClick = (e: React.MouseEvent) => {
              if (isParentGen && onOpenParentPlan) {
                e.stopPropagation();
                let parentPlanMap: GeneticsMap | undefined;
                const variantGroup = map.crossbreedingSaplingsVariants?.[pIdx];
                if (variantGroup && variantGroup.mapList && variantGroup.mapList.length > 0) {
                  parentPlanMap = variantGroup.mapList[0];
                }
                if (!parentPlanMap) {
                  const foundGroup = results.find((g) => g.resultSaplingGeneString === parent.toString());
                  parentPlanMap = foundGroup?.mapList?.[0];
                }
                if (!parentPlanMap) {
                  for (const g of results) {
                    const m = g.mapList.find((ml) => ml.resultSapling.toString() === parent.toString());
                    if (m) {
                      parentPlanMap = m;
                      break;
                    }
                  }
                }
                onOpenParentPlan(parent, parentPlanMap);
              }
            };

            return (
              <Box
                key={pIdx}
                onClick={handleParentClick}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  gap: 0.75,
                  p: isParentGen ? '2px 6px' : 0,
                  border: isParentGen ? '1px solid var(--gl-warning)' : 'none',
                  borderRadius: '3px',
                  cursor: isParentGen && onOpenParentPlan ? 'pointer' : 'default',
                  backgroundColor: isParentGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                  '&:hover': isParentGen && onOpenParentPlan ? { borderColor: 'var(--gl-primary)' } : {}
                }}
              >
                {isParentGen ? (
                  <Typography
                    variant="caption"
                    sx={{
                      color: 'var(--gl-warning)',
                      fontWeight: 800,
                      fontSize: '0.72rem',
                      fontFamily: 'monospace'
                    }}
                  >
                    GEN.{parentGenNum}
                  </Typography>
                ) : (
                  <Typography
                    variant="caption"
                    sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, fontSize: '0.72rem', fontFamily: 'monospace', minWidth: 16 }}
                  >
                    #{parent.index !== undefined ? parent.index + 1 : pIdx + 1}
                  </Typography>
                )}

                <SaplingGeneRepr sapling={parent} size="small" showConnectors={true} />

                {/* Priority order badge or chance */}
                {isFirst && (
                  <Typography variant="caption" sx={{ color: '#598518', fontWeight: 800, fontSize: '0.7rem', fontFamily: 'monospace' }}>
                    1st
                  </Typography>
                )}
                {isSecond && (
                  <Typography variant="caption" sx={{ color: '#94382A', fontWeight: 800, fontSize: '0.7rem', fontFamily: 'monospace' }}>
                    2nd
                  </Typography>
                )}
                {!isFirst && !isSecond && map.chance < 1 && (
                  <Typography variant="caption" sx={{ color: 'var(--gl-warning)', fontWeight: 700, fontSize: '0.7rem', fontFamily: 'monospace' }}>
                    {(map.chance * 100).toFixed(0)}%
                  </Typography>
                )}
              </Box>
            );
          })}
        </Box>

        {/* Planter Guide Button */}
        <Box sx={{ mt: 1.5, display: 'flex', justifyContent: 'center', width: '100%' }}>
          <Button
            size="small"
            onClick={(e) => {
              e.stopPropagation();
              setIsPlanterGuideOpen(true);
            }}
            startIcon={<YardIcon sx={{ fontSize: 14 }} />}
            sx={{
              color: 'var(--gl-primary)',
              fontFamily: 'monospace',
              fontSize: '0.72rem',
              fontWeight: 700,
              py: 0.25,
              px: 1.2,
              border: '1px solid rgba(0, 229, 255, 0.25)',
              borderRadius: '3px',
              backgroundColor: 'rgba(0, 229, 255, 0.04)',
              '&:hover': {
                backgroundColor: 'rgba(0, 229, 255, 0.12)',
                borderColor: 'var(--gl-primary)'
              }
            }}
          >
            PLANTER GUIDE
          </Button>
        </Box>
      </Box>

      {/* Planter Guide Modal */}
      <PlanterGuideModal
        open={isPlanterGuideOpen}
        onClose={() => setIsPlanterGuideOpen(false)}
        map={map}
      />
    </Paper>
  );
};

interface PlanHistoryItem {
  type: 'plan' | 'alternatives';
  sapling?: Sapling | null;
  map?: GeneticsMap | null;
  mapGroup?: GeneticsMapGroup | null;
}

interface PlanDetailModalProps {
  open: boolean;
  onClose: () => void;
  // Mode 1: Parent breeding plan overlay
  parentSapling?: Sapling | null;
  parentMap?: GeneticsMap | null;
  // Mode 2: Alternative options comparison overlay
  mapGroup?: GeneticsMapGroup | null;
  selectedMapIndex?: number;
  onSelectMapIndex?: (idx: number) => void;
}

export const PlanDetailModal: React.FC<PlanDetailModalProps> = ({
  open,
  onClose,
  parentSapling,
  parentMap,
  mapGroup,
  selectedMapIndex = 0,
  onSelectMapIndex
}) => {
  const { results } = useApp();
  const [history, setHistory] = useState<PlanHistoryItem[]>([]);

  useEffect(() => {
    if (open) {
      setHistory((prev) => {
        if (prev.length > 0) return prev;
        if (mapGroup && mapGroup.mapList.length > 1) {
          return [{ type: 'alternatives', mapGroup }];
        } else if (parentMap) {
          return [{ type: 'plan', sapling: parentSapling, map: parentMap }];
        }
        return [];
      });
    } else {
      setHistory([]);
    }
  }, [open, parentMap, parentSapling, mapGroup]);

  if (!open) return null;

  const currentEntry = history[history.length - 1];
  const isAlternativeView = currentEntry?.type === 'alternatives';
  const currentMap = currentEntry?.type === 'plan' ? currentEntry.map : parentMap;

  // Handle drill down to a nested parent plan (e.g. Gen 3 -> Gen 2 -> Gen 1)
  const handleDrillDownParent = (sapling: Sapling, map?: GeneticsMap) => {
    let resolvedMap = map;
    if (!resolvedMap && currentMap) {
      if (currentMap.baseSapling?.toString() === sapling.toString() && currentMap.baseSaplingVariants?.mapList?.[0]) {
        resolvedMap = currentMap.baseSaplingVariants.mapList[0];
      } else if (currentMap.crossbreedingSaplingsVariants) {
        for (const vg of currentMap.crossbreedingSaplingsVariants) {
          if (vg.resultSaplingGeneString === sapling.toString() && vg.mapList?.[0]) {
            resolvedMap = vg.mapList[0];
            break;
          }
        }
      }
    }
    if (!resolvedMap) {
      const foundGroup = results.find((g) => g.resultSaplingGeneString === sapling.toString());
      resolvedMap = foundGroup?.mapList?.[0];
    }
    if (!resolvedMap) {
      for (const g of results) {
        const m = g.mapList.find((ml) => ml.resultSapling.toString() === sapling.toString());
        if (m) {
          resolvedMap = m;
          break;
        }
      }
    }

    if (resolvedMap) {
      setHistory((prev) => [...prev, { type: 'plan', sapling, map: resolvedMap }]);
    }
  };

  const handleGoBack = () => {
    setHistory((prev) => (prev.length > 1 ? prev.slice(0, -1) : prev));
  };

  const handleJumpToHistory = (index: number) => {
    setHistory((prev) => prev.slice(0, index + 1));
  };

  // Label for previous step in history
  const previousEntry = history.length > 1 ? history[history.length - 2] : null;
  const prevGenIndex =
    previousEntry?.sapling?.generationIndex ||
    previousEntry?.map?.resultSapling?.generationIndex;
  const backLabel =
    previousEntry?.type === 'alternatives'
      ? 'Back to Alternatives'
      : prevGenIndex
      ? `Back to GEN.${prevGenIndex}`
      : 'Back';

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth={isAlternativeView ? 'lg' : 'sm'}
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: 'rgba(14, 14, 14, 0.96)',
            backdropFilter: 'blur(8px)',
            border: '1px solid var(--gl-border)',
            borderRadius: '6px',
            color: 'var(--gl-text-primary)',
            p: 3,
            position: 'relative'
          }
        }
      }}
    >
      {/* Top Header Bar with Back Button, Breadcrumbs, and Close Button */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          width: '100%',
          mb: 2,
          minHeight: 36
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {history.length > 1 && (
            <Button
              onClick={handleGoBack}
              startIcon={<ArrowBackIcon sx={{ fontSize: 16 }} />}
              size="small"
              sx={{
                color: 'var(--gl-primary)',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.76rem',
                fontWeight: 800,
                textTransform: 'none',
                letterSpacing: '0.3px',
                backgroundColor: 'rgba(0, 229, 255, 0.08)',
                border: '1px solid rgba(0, 229, 255, 0.35)',
                borderRadius: '4px',
                px: 1.4,
                py: 0.35,
                '&:hover': {
                  backgroundColor: 'rgba(0, 229, 255, 0.2)',
                  borderColor: 'var(--gl-primary)'
                }
              }}
            >
              {backLabel}
            </Button>
          )}
        </Box>

        {/* Breadcrumb Navigation Trail */}
        {history.length > 1 && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
            {history.map((item, idx) => {
              const isCurrent = idx === history.length - 1;
              const genIndex = item.sapling?.generationIndex || item.map?.resultSapling?.generationIndex || 1;
              const label = item.type === 'alternatives' ? 'Alternatives' : `GEN.${genIndex}`;

              return (
                <Box key={idx} sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                  {idx > 0 && (
                    <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontFamily: 'monospace' }}>
                      /
                    </Typography>
                  )}
                  {isCurrent ? (
                    <Typography
                      variant="caption"
                      sx={{
                        color: 'var(--gl-primary)',
                        fontWeight: 800,
                        fontFamily: 'monospace',
                        fontSize: '0.75rem',
                        px: 0.8,
                        py: 0.2,
                        backgroundColor: 'rgba(0, 229, 255, 0.12)',
                        borderRadius: '3px',
                        border: '1px solid rgba(0, 229, 255, 0.35)'
                      }}
                    >
                      {label}
                    </Typography>
                  ) : (
                    <Typography
                      variant="caption"
                      onClick={() => handleJumpToHistory(idx)}
                      sx={{
                        color: 'var(--gl-text-muted)',
                        fontWeight: 700,
                        fontFamily: 'monospace',
                        fontSize: '0.75rem',
                        cursor: 'pointer',
                        px: 0.6,
                        py: 0.2,
                        borderRadius: '3px',
                        transition: 'all 0.15s ease',
                        '&:hover': {
                          color: 'var(--gl-text-primary)',
                          backgroundColor: 'rgba(255, 255, 255, 0.08)'
                        }
                      }}
                    >
                      {label}
                    </Typography>
                  )}
                </Box>
              );
            })}
          </Box>
        )}

        <IconButton
          onClick={onClose}
          size="small"
          sx={{
            color: 'var(--gl-text-muted)',
            ml: 'auto',
            '&:hover': { color: 'var(--gl-text-primary)' }
          }}
        >
          <CloseIcon sx={{ fontSize: 20 }} />
        </IconButton>
      </Box>

      <DialogContent sx={{ p: 0, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        {/* VIEW 1: PARENT GEN. X BREEDING PLAN */}
        {!isAlternativeView && currentMap && (
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' }}>
            <Typography
              variant="body2"
              sx={{
                color: 'var(--gl-text-secondary)',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.85rem',
                lineHeight: 1.6,
                textAlign: 'center',
                maxWidth: 620,
                mb: 3
              }}
            >
              When a map shows a result chance below 100%, plant the Surrounding Plants labeled <strong>1st</strong>, <strong>2nd</strong>, and so on before the others, in that order. Which planter slot you use does not matter-only the order you place them around the center.
            </Typography>

            <SinglePlanCard map={currentMap} isBest={true} onOpenParentPlan={handleDrillDownParent} />
          </Box>
        )}

        {/* VIEW 2: ALTERNATIVE OPTIONS COMPARISON */}
        {isAlternativeView && mapGroup && (
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' }}>
            <Typography
              variant="body2"
              sx={{
                color: 'var(--gl-text-secondary)',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.9rem',
                lineHeight: 1.6,
                textAlign: 'center',
                mb: 3
              }}
            >
              Here you can see all the different ways you can crossbreed the same selected Sapling. Click an option to highlight and use it, then click any <span style={{ color: 'var(--gl-warning)', fontWeight: 700 }}>GEN.x</span> plant inside it to see how that generation is bred.
            </Typography>

            <Box
              sx={{
                display: 'flex',
                justifyContent: 'center',
                gap: 3,
                flexWrap: 'wrap',
                width: '100%'
              }}
            >
              {mapGroup.mapList.map((planMap, idx) => {
                const isFirst = idx === 0;
                const isSelected = idx === selectedMapIndex;
                const title = isFirst ? 'Best Option' : `Alternative Option ${idx + 1}`;

                return (
                  <SinglePlanCard
                    key={idx}
                    map={planMap}
                    title={title}
                    isBest={isFirst}
                    isSelected={isSelected}
                    onSelect={() => onSelectMapIndex?.(idx)}
                    onOpenParentPlan={handleDrillDownParent}
                  />
                );
              })}
            </Box>

            <Box sx={{ mt: 3, display: 'flex', justifyContent: 'center', width: '100%' }}>
              <Button
                onClick={onClose}
                sx={{
                  color: 'var(--gl-primary)',
                  fontFamily: 'monospace',
                  fontSize: '0.8rem',
                  fontWeight: 800,
                  py: 0.6,
                  px: 3,
                  border: '1px solid rgba(0, 229, 255, 0.4)',
                  borderRadius: '4px',
                  backgroundColor: 'rgba(0, 229, 255, 0.06)',
                  letterSpacing: '0.05em',
                  '&:hover': {
                    backgroundColor: 'rgba(0, 229, 255, 0.14)',
                    borderColor: 'var(--gl-primary)'
                  }
                }}
              >
                USE SELECTED PLAN
              </Button>
            </Box>
          </Box>
        )}
      </DialogContent>
    </Dialog>
  );
};

