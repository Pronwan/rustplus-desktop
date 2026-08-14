import React, { useState } from 'react';
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
  const genColor = isGen1 ? '#598518' : '#FFA726';
  const chanceColor = chanceProd >= 0.99 ? '#598518' : chanceProd >= 0.5 ? '#FFA726' : '#E53935';

  return (
    <Paper
      onClick={onSelect}
      variant="outlined"
      sx={{
        backgroundColor: '#161616',
        border: isHighlightedView || isSelected || isBest ? '1.5px dashed #FFFFFF' : '1px solid #282828',
        borderRadius: '4px',
        p: 2.5,
        width: 320,
        maxWidth: '100%',
        cursor: onSelect ? 'pointer' : 'default',
        transition: 'all 0.2s ease',
        '&:hover': onSelect ? { borderColor: '#00E5FF', transform: 'translateY(-2px)' } : {}
      }}
    >
      {title && (
        <Typography
          variant="caption"
          sx={{
            display: 'block',
            textAlign: 'center',
            fontWeight: 800,
            color: isBest ? '#00E5FF' : '#E0E0E0',
            fontFamily: '"Roboto Mono", monospace',
            fontSize: '0.85rem',
            mb: 1.5
          }}
        >
          {title}
        </Typography>
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

          <Typography variant="caption" sx={{ color: '#555555' }}>·</Typography>

          <Typography
            variant="caption"
            sx={{ fontWeight: 700, color: '#E0E0E0', fontFamily: 'monospace', fontSize: '0.82rem' }}
          >
            Score: <span style={{ color: '#FFFFFF', fontWeight: 800 }}>{score}</span>
          </Typography>

          <Typography variant="caption" sx={{ color: '#555555' }}>·</Typography>

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
          sx={{ color: '#888888', fontSize: '0.75rem', fontFamily: 'monospace', mb: 0.5 }}
        >
          Center Plant:
        </Typography>

        {map.baseSapling ? (
          (() => {
            const isBaseGen = map.baseSapling.generationIndex > 0;
            const handleBaseClick = (e: React.MouseEvent) => {
              if (isBaseGen && onOpenParentPlan) {
                e.stopPropagation();
                const parentGroup =
                  map.baseSaplingVariants ||
                  results.find((g) => g.resultSaplingGeneString === map.baseSapling!.toString());
                const parentMap = parentGroup?.mapList[0];
                onOpenParentPlan(map.baseSapling!, parentMap);
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
                  border: isBaseGen ? '1px solid #FFA726' : 'none',
                  borderRadius: '3px',
                  cursor: isBaseGen && onOpenParentPlan ? 'pointer' : 'default',
                  backgroundColor: isBaseGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                  '&:hover': isBaseGen && onOpenParentPlan ? { borderColor: '#00E5FF' } : {}
                }}
              >
                {isBaseGen ? (
                  <Typography
                    variant="caption"
                    sx={{ color: '#FFA726', fontWeight: 800, fontSize: '0.72rem', fontFamily: 'monospace' }}
                  >
                    GEN.{map.baseSapling.generationIndex}
                  </Typography>
                ) : (
                  <Typography
                    variant="caption"
                    sx={{ color: '#666', fontFamily: 'monospace', fontSize: '0.75rem', minWidth: 14 }}
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
            sx={{ color: '#E0E0E0', fontFamily: 'monospace', fontWeight: 700, fontSize: '0.78rem' }}
          >
            any extra plant of same type
          </Typography>
        )}
      </Box>

      {/* Surrounding Plants */}
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        <Typography
          variant="caption"
          sx={{ color: '#888888', fontSize: '0.75rem', fontFamily: 'monospace', mb: 0.75 }}
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
                const parentGroup =
                  map.crossbreedingSaplingsVariants?.[pIdx] ||
                  results.find((g) => g.resultSaplingGeneString === parent.toString());
                const parentMap = parentGroup?.mapList[0];
                onOpenParentPlan(parent, parentMap);
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
                  border: isParentGen ? '1px solid #FFA726' : 'none',
                  borderRadius: '3px',
                  cursor: isParentGen && onOpenParentPlan ? 'pointer' : 'default',
                  backgroundColor: isParentGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                  '&:hover': isParentGen && onOpenParentPlan ? { borderColor: '#00E5FF' } : {}
                }}
              >
                {isParentGen ? (
                  <Typography
                    variant="caption"
                    sx={{
                      color: '#FFA726',
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
                    sx={{ color: '#666666', fontWeight: 700, fontSize: '0.72rem', fontFamily: 'monospace', minWidth: 16 }}
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
                  <Typography variant="caption" sx={{ color: '#FFA726', fontWeight: 700, fontSize: '0.7rem', fontFamily: 'monospace' }}>
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
              color: '#00E5FF',
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
                borderColor: '#00E5FF'
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
  if (!open) return null;

  const isAlternativeView = !!mapGroup && mapGroup.mapList.length > 1;

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
            border: '1px solid #282828',
            borderRadius: '6px',
            color: '#E0E0E0',
            p: 3,
            position: 'relative'
          }
        }
      }}
    >
      {/* Top Right Close Button */}
      <IconButton
        onClick={onClose}
        size="small"
        sx={{
          position: 'absolute',
          top: 16,
          right: 16,
          color: '#888888',
          '&:hover': { color: '#FFFFFF' }
        }}
      >
        <CloseIcon sx={{ fontSize: 20 }} />
      </IconButton>

      <DialogContent sx={{ p: 0, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
        {/* VIEW 1: PARENT GEN. X BREEDING PLAN (Screenshot 3) */}
        {!isAlternativeView && parentMap && (
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' }}>
            <Typography
              variant="body2"
              sx={{
                color: '#CCCCCC',
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

            <SinglePlanCard map={parentMap} isBest={true} />
          </Box>
        )}

        {/* VIEW 2: ALTERNATIVE OPTIONS COMPARISON (Screenshot 5) */}
        {isAlternativeView && mapGroup && (
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '100%' }}>
            <Typography
              variant="body2"
              sx={{
                color: '#CCCCCC',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.9rem',
                lineHeight: 1.6,
                textAlign: 'center',
                mb: 3
              }}
            >
              Here you can see all the different ways you can crossbreed the same selected Sapling.
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
                    onSelect={() => {
                      if (onSelectMapIndex) {
                        onSelectMapIndex(idx);
                        onClose();
                      }
                    }}
                  />
                );
              })}
            </Box>
          </Box>
        )}
      </DialogContent>
    </Dialog>
  );
};
