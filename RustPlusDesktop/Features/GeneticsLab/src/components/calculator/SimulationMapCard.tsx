import React, { useState } from 'react';
import { Card, CardContent, Typography, Box, Paper, Divider, Tooltip, Button } from '@mui/material';
import YardIcon from '@mui/icons-material/Yard';
import { GeneticsMapGroup } from '../../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';
import { PlanDetailModal } from './PlanDetailModal.tsx';
import { PlanterGuideModal } from '../modals/PlanterGuideModal.tsx';
import { useApp } from '../../context/AppContext.tsx';

interface SimulationMapCardProps {
  group: GeneticsMapGroup;
  onSelectGroup?: () => void;
  // Highlight a specific plan (map index within this group) as the pinned result,
  // without changing the plan shown on this source card.
  onHighlightMap?: (mapIndex: number) => void;
}

const CENTER_PLANT_TOOLTIP = `Plant that takes the genes from Surrounding Plants during Crossbreeding stage. Plant it in the center and let it grow alone until it reaches about 50% progress in the Sapling stage. After that, plant the Surrounding Plants around it.

It is always an extra plant, so if the app shows five Surrounding Plants you will have to plant six plants in total.

You have to plant the exact plant that the app tells you to, otherwise crossbreeding will not work as expected.`;

const SURROUNDING_PLANTS_TOOLTIP = `Plants that provide genes to the Center Plant during Crossbreeding stage. Plant them around the Center Plant after the Center Plant reaches Sapling stage.`;

export const SimulationMapCard: React.FC<SimulationMapCardProps> = ({ group, onSelectGroup, onHighlightMap }) => {
  const { options, results } = useApp();
  const [selectedMapIndex, setSelectedMapIndex] = useState(0);
  const [isAlternativesModalOpen, setIsAlternativesModalOpen] = useState(false);
  const [isPlanterGuideOpen, setIsPlanterGuideOpen] = useState(false);
  const [isContainerHovered, setIsContainerHovered] = useState(false);
  const [hoveredAltIndex, setHoveredAltIndex] = useState<number | null>(null);

  // Parent plan modal state
  const [isParentModalOpen, setIsParentModalOpen] = useState(false);
  const [focusedParentSapling, setFocusedParentSapling] = useState<Sapling | null>(null);
  const [focusedParentMap, setFocusedParentMap] = useState<GeneticsMap | null>(null);

  // The source card always shows the Best Option. Choosing an alternative highlights it as
  // a pinned result elsewhere rather than swapping what this card displays.
  const bestMap = group.mapList[0];
  const targetSapling = new Sapling(group.resultSaplingGeneString, bestMap?.resultSapling.generationIndex);
  const score = targetSapling.getScore(options.geneScores);
  const chanceProd = bestMap ? bestMap.getChanceProduct() : 1;
  const genIndex = Math.max(1, bestMap?.resultSapling.generationIndex || 1);

  const isGen1 = genIndex === 1;
  const genColor = isGen1 ? '#598518' : 'var(--gl-warning)';
  const chanceColor = chanceProd >= 0.99 ? '#598518' : chanceProd >= 0.5 ? 'var(--gl-warning)' : 'var(--gl-error)';

  const hasAlternativePlans = group.mapList.length > 1;

  const handleOpenParent = (parent: Sapling, pIdx: number, e: React.MouseEvent) => {
    e.stopPropagation();
    // Lookup parent's breeding map
    const parentGroup =
      (pIdx >= 0 ? bestMap?.crossbreedingSaplingsVariants?.[pIdx] : bestMap?.baseSaplingVariants) ||
      results.find((g) => g.resultSaplingGeneString === parent.toString());
    const parentMap = parentGroup?.mapList[0] || null;

    setFocusedParentSapling(parent);
    setFocusedParentMap(parentMap);
    setIsParentModalOpen(true);
  };

  return (
    <Box
      onMouseEnter={() => setIsContainerHovered(true)}
      onMouseLeave={() => {
        setIsContainerHovered(false);
        setHoveredAltIndex(null);
      }}
      sx={{
        position: 'relative',
        mb: 3,
        width: 310,
        maxWidth: 310
      }}
    >
      {/* Motion Stacked behind cards on hover (Screenshot 1 & 2) */}
      {hasAlternativePlans && (
        <>
          {group.mapList.slice(1, 3).map((altMap, altIdx) => {
            const z = altIdx === 0 ? 1 : 0;
            const isThisHovered = hoveredAltIndex === altIdx;

            // Resting, Container Hover, and Direct Card Hover states
            let transX = 0;
            let transY = 0;
            let rot = 0;
            let scale = 1;

            if (isContainerHovered) {
              if (altIdx === 0) {
                transX = isThisHovered ? 28 : 20;
                transY = isThisHovered ? -16 : -11;
                rot = isThisHovered ? 8 : 5.5;
                scale = isThisHovered ? 1.02 : 1;
              } else {
                transX = isThisHovered ? 52 : 40;
                transY = isThisHovered ? -28 : -22;
                rot = isThisHovered ? 14 : 11;
                scale = isThisHovered ? 1.02 : 1;
              }
            }

            return (
              <Paper
                key={altIdx}
                onMouseEnter={() => setHoveredAltIndex(altIdx)}
                onMouseLeave={() => setHoveredAltIndex(null)}
                onClick={(e) => {
                  e.stopPropagation();
                  setIsAlternativesModalOpen(true);
                }}
                variant="outlined"
                sx={{
                  position: 'absolute',
                  top: 0,
                  left: 0,
                  right: 0,
                  height: '100%',
                  backgroundColor: 'var(--gl-panel-bg)',
                  borderColor: isThisHovered ? 'var(--gl-primary)' : 'var(--gl-border)',
                  borderRadius: '4px',
                  p: 1.75,
                  zIndex: z,
                  transform: `translate(${transX}px, ${transY}px) rotate(${rot}deg) scale(${scale})`,
                  transformOrigin: 'bottom left',
                  transition: 'transform 0.35s cubic-bezier(0.2, 0.9, 0.3, 1), opacity 0.25s ease, visibility 0.25s ease, border-color 0.2s ease',
                  cursor: 'pointer',
                  opacity: isContainerHovered ? (isThisHovered ? 1 : altIdx === 0 ? 0.9 : 0.8) : 0,
                  visibility: isContainerHovered ? 'visible' : 'hidden',
                  pointerEvents: isContainerHovered ? 'auto' : 'none',
                  display: 'flex',
                  flexDirection: 'column',
                  alignItems: 'center',
                  boxShadow: '0 8px 24px rgba(0,0,0,0.6)'
                }}
              >
                <SaplingGeneRepr sapling={altMap.resultSapling} size="medium" showConnectors={true} />
                <Typography variant="caption" sx={{ color: '#598518', fontWeight: 800, mt: 1, fontFamily: 'monospace' }}>
                  GEN.{(altMap.resultSapling.generationIndex ?? 0) + 1} · Chance: {(altMap.getChanceProduct() * 100).toFixed(0)}%
                </Typography>
              </Paper>
            );
          })}
        </>
      )}

      {/* Main Front Card */}
      <Card
        onClick={() => {
          onSelectGroup?.();
        }}
        variant="outlined"
        sx={{
          position: 'relative',
          zIndex: 2,
          backgroundColor: 'var(--gl-panel-header-bg)',
          border: isContainerHovered ? '1.5px solid #FFFFFF' : '1px solid var(--gl-border)',
          borderRadius: '4px',
          cursor: 'pointer',
          transition: 'border-color 0.2s ease, box-shadow 0.2s ease',
          boxShadow: isContainerHovered ? '0 6px 20px rgba(0,0,0,0.5)' : 'none'
        }}
      >
        <CardContent sx={{ p: 2, '&:last-child': { pb: 2 } }}>
          {/* Top Result Gene Discs */}
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 1 }}>
            <SaplingGeneRepr sapling={targetSapling} size="medium" showConnectors={true} />

            {/* Subtitle: GEN. X · Score: X · Chance: X% */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mt: 0.75 }}>
              <Typography
                variant="caption"
                sx={{
                  fontWeight: 800,
                  color: genColor,
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.8rem'
                }}
              >
                GEN.{genIndex}
              </Typography>

              <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)' }}>·</Typography>

              <Typography
                variant="caption"
                sx={{
                  fontWeight: 700,
                  color: 'var(--gl-text-primary)',
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.8rem'
                }}
              >
                Score: <span style={{ color: 'var(--gl-text-primary)', fontWeight: 800 }}>{score}</span>
              </Typography>

              <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)' }}>·</Typography>

              <Typography
                variant="caption"
                sx={{
                  fontWeight: 700,
                  color: chanceColor,
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.8rem'
                }}
              >
                Chance: <span style={{ fontWeight: 800 }}>{(chanceProd * 100).toFixed(0)}%</span>
              </Typography>
            </Box>
          </Box>

          <Divider sx={{ borderColor: 'var(--gl-surface)', my: 1 }} />

          {/* Center Plant Section */}
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', mb: 1 }}>
            <Tooltip
              title={
                <Typography sx={{ fontSize: '0.78rem', fontFamily: 'monospace', whiteSpace: 'pre-line', p: 0.5 }}>
                  {CENTER_PLANT_TOOLTIP}
                </Typography>
              }
              arrow
              placement="top"
            >
              <Typography
                variant="caption"
                sx={{
                  color: 'var(--gl-text-muted)',
                  fontSize: '0.75rem',
                  fontFamily: '"Roboto Mono", monospace',
                  mb: 0.5,
                  cursor: 'help'
                }}
              >
                Center Plant:
              </Typography>
            </Tooltip>

            {bestMap?.baseSapling ? (
              (() => {
                const isBaseGen = bestMap.baseSapling.generationIndex > 0;
                return (
                  <Box
                    onClick={(e) => {
                      if (isBaseGen) handleOpenParent(bestMap.baseSapling!, -1, e);
                    }}
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 0.75,
                      p: isBaseGen ? '2px 6px' : 0,
                      border: isBaseGen ? '1px solid var(--gl-warning)' : 'none',
                      borderRadius: '3px',
                      cursor: isBaseGen ? 'pointer' : 'default',
                      backgroundColor: isBaseGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                      '&:hover': isBaseGen ? { borderColor: 'var(--gl-primary)' } : {}
                    }}
                  >
                    {isBaseGen ? (
                      <Typography variant="caption" sx={{ color: 'var(--gl-warning)', fontWeight: 800, fontSize: '0.72rem', fontFamily: 'monospace' }}>
                        GEN.{bestMap.baseSapling.generationIndex}
                      </Typography>
                    ) : (
                      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.75rem', minWidth: 14 }}>
                        #{bestMap.baseSapling.index !== undefined ? bestMap.baseSapling.index + 1 : '1'}
                      </Typography>
                    )}
                    <SaplingGeneRepr sapling={bestMap.baseSapling} size="small" showConnectors={true} />
                  </Box>
                );
              })()
            ) : (
              <Typography
                variant="caption"
                sx={{
                  color: 'var(--gl-text-secondary)',
                  fontFamily: '"Roboto Mono", monospace',
                  fontWeight: 700,
                  fontSize: '0.78rem'
                }}
              >
                any extra plant of same type
              </Typography>
            )}
          </Box>

          <Divider sx={{ borderColor: 'var(--gl-surface)', my: 1 }} />

          {/* Surrounding Plants Section */}
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
            <Tooltip
              title={
                <Typography sx={{ fontSize: '0.78rem', fontFamily: 'monospace', p: 0.5 }}>
                  {SURROUNDING_PLANTS_TOOLTIP}
                </Typography>
              }
              arrow
              placement="top"
            >
              <Typography
                variant="caption"
                sx={{
                  color: 'var(--gl-text-muted)',
                  fontSize: '0.75rem',
                  fontFamily: '"Roboto Mono", monospace',
                  mb: 0.75,
                  cursor: 'help'
                }}
              >
                Surrounding Plants:
              </Typography>
            </Tooltip>

            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.6, alignItems: 'center', width: '100%' }}>
              {bestMap?.crossbreedingSaplings.map((parent, pIdx) => {
                const isParentGen = parent.generationIndex > 0;
                const parentGenNum = parent.generationIndex;

                const isFirst = bestMap.tieWinningCrossbreedingSaplingIndexes?.includes(pIdx);
                const isSecond = bestMap.tieLosingCrossbreedingSaplingIndexes?.includes(pIdx);

                return (
                  <Box
                    key={pIdx}
                    onClick={(e) => {
                      if (isParentGen) handleOpenParent(parent, pIdx, e);
                    }}
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      gap: 0.75,
                      p: isParentGen ? '2px 6px' : 0,
                      border: isParentGen ? '1px solid var(--gl-warning)' : 'none',
                      borderRadius: '3px',
                      cursor: isParentGen ? 'pointer' : 'default',
                      backgroundColor: isParentGen ? 'rgba(255, 167, 38, 0.04)' : 'transparent',
                      '&:hover': isParentGen ? { borderColor: 'var(--gl-primary)' } : {}
                    }}
                  >
                    {isParentGen ? (
                      <Typography
                        variant="caption"
                        sx={{
                          color: 'var(--gl-warning)',
                          fontWeight: 800,
                          fontSize: '0.72rem',
                          fontFamily: 'monospace',
                          textDecoration: 'underline'
                        }}
                      >
                        GEN.{parentGenNum}
                      </Typography>
                    ) : (
                      <Typography
                        variant="caption"
                        sx={{
                          color: 'var(--gl-text-muted)',
                          fontWeight: 700,
                          fontSize: '0.72rem',
                          fontFamily: 'monospace',
                          minWidth: 16
                        }}
                      >
                        #{parent.index !== undefined ? parent.index + 1 : pIdx + 1}
                      </Typography>
                    )}

                    <SaplingGeneRepr sapling={parent} size="small" showConnectors={true} />

                    {/* Priority order badge (1st, 2nd) or chance percentage */}
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
                    {!isFirst && !isSecond && bestMap.chance < 1 && (
                      <Typography variant="caption" sx={{ color: 'var(--gl-warning)', fontWeight: 700, fontSize: '0.7rem', fontFamily: 'monospace' }}>
                        {(bestMap.chance * 100).toFixed(0)}%
                      </Typography>
                    )}
                  </Box>
                );
              })}
            </Box>

            {/* Plant Guide of Current Plan Button */}
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
        </CardContent>
      </Card>

      {/* Modal 1: Parent GEN.X Breeding Plan Overlay (Screenshot 3) */}
      <PlanDetailModal
        open={isParentModalOpen}
        onClose={() => setIsParentModalOpen(false)}
        parentSapling={focusedParentSapling}
        parentMap={focusedParentMap}
      />

      {/* Modal 2: Alternative Options Comparison Overlay (Screenshot 5) */}
      <PlanDetailModal
        open={isAlternativesModalOpen}
        onClose={() => setIsAlternativesModalOpen(false)}
        mapGroup={group}
        selectedMapIndex={selectedMapIndex}
        onSelectMapIndex={(idx) => {
          setSelectedMapIndex(idx);
          // Highlight the chosen plan as the pinned "Highlighted Result" instead of
          // overwriting the plan shown on this source card.
          onHighlightMap?.(idx);
        }}
      />

      {/* Modal 3: Interactive Planter Guide for this specific plan */}
      <PlanterGuideModal
        open={isPlanterGuideOpen}
        onClose={() => setIsPlanterGuideOpen(false)}
        map={bestMap}
      />
    </Box>
  );
};
