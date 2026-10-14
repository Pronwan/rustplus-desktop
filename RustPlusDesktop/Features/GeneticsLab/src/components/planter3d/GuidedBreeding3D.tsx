import React, { Suspense, useEffect, useMemo, useState } from 'react';
import { Box, Button, ToggleButton, Tooltip, Typography } from '@mui/material';
import ViewInArIcon from '@mui/icons-material/ViewInAr';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import CloseIcon from '@mui/icons-material/Close';
import { IconButton } from '@mui/material';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';
import type { BreedingSessionStep } from '../../services/storageService.ts';
import type { PlanterType } from '../../domain/planner/farmPlanning.ts';
import { PLANTER_MODELS, neighboursOf } from './planterModels.ts';
import { donorSlotsFor } from './breedingLayout.ts';
import { LazyPlanter3DView as Planter3DView } from './lazyPlanter3DView.ts';
import type { SlotContent, SlotHighlight } from './PlanterScene.ts';
import { isWebGLAvailable } from './webgl.ts';

const HIDDEN_KEY = 'GL_GUIDED_3D_HIDDEN';
const PLANTER_KEY = 'GL_GUIDED_3D_PLANTER';
const PLANTER_ORDER: PlanterType[] = ['large', 'small', 'triangle'];

const readPlanter = (): PlanterType => {
  try {
    const stored = localStorage.getItem(PLANTER_KEY);
    return stored === 'small' || stored === 'triangle' ? stored : 'large';
  } catch {
    return 'large';
  }
};

/** Slots around a planter's centre, i.e. the most donors it can hold for one crossbreed. */
const donorRoom = (type: PlanterType) => neighboursOf(PLANTER_MODELS[type], PLANTER_MODELS[type].centerSlot).length;

const readHidden = () => {
  try {
    return localStorage.getItem(HIDDEN_KEY) === 'true';
  } catch {
    return false;
  }
};

interface GuidedBreeding3DProps {
  step: BreedingSessionStep;
  plantType: string;
}

/**
 * The current generation of a guided breeding session in a 3D planter. It follows the
 * session's checkboxes: empty slots marked where to plant, then the centre plant, then the
 * donors with their genes flowing into the centre, then the step's result.
 */
export const GuidedBreeding3D: React.FC<GuidedBreeding3DProps> = ({ step, plantType }) => {
  const [webglOk] = useState(isWebGLAvailable);
  const [isHidden, setIsHidden] = useState(readHidden);
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  // A new generation (or a ticked box) changes what is planted; drop a stale selection.
  useEffect(() => setSelectedSlot(null), [step]);

  // The chosen planter is remembered; a generation with more donors than it can hold
  // around its centre falls back to the large planter.
  const [chosenPlanter, setChosenPlanter] = useState<PlanterType>(readPlanter);
  const fits = (type: PlanterType) => donorRoom(type) >= step.surroundingSaplingsStrings.length;
  const planterType: PlanterType = fits(chosenPlanter) ? chosenPlanter : 'large';
  const choosePlanter = (type: PlanterType) => {
    setChosenPlanter(type);
    setSelectedSlot(null);
    try {
      localStorage.setItem(PLANTER_KEY, type);
    } catch {
      // Not remembered; harmless.
    }
  };

  const model = PLANTER_MODELS[planterType];
  const centre = model.centerSlot;
  const donors = step.surroundingSaplingsStrings;
  const donorSlots = useMemo(() => donorSlotsFor(model, donors.length), [model, donors.length]);
  const hasCentrePlant = Boolean(step.centerSaplingString);
  const centreDone = !hasCentrePlant || step.isCenterPlanted;
  const allPlanted = centreDone && step.isSurroundingPlanted;

  const view = useMemo(() => {
    const slots: (SlotContent | null)[] = Array(model.slots.length).fill(null);
    const highlights: SlotHighlight[] = Array(model.slots.length).fill('dim');
    highlights[centre] = 'target';

    if (allPlanted) {
      slots[centre] = { stage: 'mature', role: 'result', genes: step.targetGeneString, caption: 'Result' };
    } else if (step.isCenterPlanted && hasCentrePlant) {
      slots[centre] = { stage: 'sapling', role: 'target', genes: step.centerSaplingString, caption: 'Centre' };
    } else if (hasCentrePlant) {
      slots[centre] = null;
    } else {
      slots[centre] = { stage: 'sapling', role: 'target', caption: 'Any plant' };
    }

    donorSlots.forEach((slot, i) => {
      highlights[slot] = 'donor';
      if (step.isSurroundingPlanted) {
        const order = step.priorityWinningIndices?.includes(i) ? ' · 1st' : step.priorityLosingIndices?.includes(i) ? ' · 2nd' : '';
        slots[slot] = { stage: 'mature', role: 'donor', genes: donors[i], caption: `#${i + 1}${order}` };
      }
    });

    return {
      slots,
      highlights,
      flow: allPlanted ? { from: donorSlots, to: centre } : undefined,
      celebrate: allPlanted ? centre : null
    };
    // The flags are listed as well as `step` so a step edited in place still re-renders.
  }, [model, centre, donorSlots, donors, step, step.isCenterPlanted, step.isSurroundingPlanted, hasCentrePlant, allPlanted]);

  const selectedContent = selectedSlot !== null ? view.slots[selectedSlot] : null;

  if (!webglOk) return null;

  const toggle = () => {
    setIsHidden((hidden) => {
      try {
        localStorage.setItem(HIDDEN_KEY, String(!hidden));
      } catch {
        // Not remembered; harmless.
      }
      return !hidden;
    });
  };

  const centreNumber = model.slots[centre].number;
  const donorNumbers = donorSlots.map((i) => model.slots[i].number).join(', ');
  const hint = allPlanted
    ? `Everything is planted. When the centre has crossbred, it carries ${step.targetGeneString}.`
    : !centreDone
      ? `Plant the centre plant in slot ${centreNumber} first.`
      : `Now plant the ${donors.length} donor clones in slots ${donorNumbers}.`;

  return (
    // flexShrink 0: the assistant dialog is a flex column and would squeeze the viewport.
    <Box sx={{ flexShrink: 0, border: '1px solid var(--gl-border)', borderRadius: '6px', overflow: 'hidden' }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 1.5, py: 0.75, backgroundColor: 'var(--gl-panel-header-bg)' }}>
        <ViewInArIcon sx={{ fontSize: 18, color: 'var(--gl-primary)' }} />
        <Typography sx={{ flex: 1, minWidth: 0, fontSize: '0.75rem', fontFamily: 'var(--font-family-mono)', color: 'var(--gl-text-secondary)' }}>
          {isHidden ? 'Large planter in 3D' : hint}
        </Typography>
        <Button
          size="small"
          onClick={toggle}
          endIcon={isHidden ? <ExpandMoreIcon /> : <ExpandLessIcon />}
          sx={{ fontSize: '0.68rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)', flexShrink: 0 }}
        >
          {isHidden ? 'Show 3D' : 'Hide'}
        </Button>
      </Box>
      {!isHidden && (
        <Box sx={{ position: 'relative', height: { xs: 320, sm: 440 } }}>
          <Box sx={{ position: 'absolute', top: 10, left: 10, zIndex: 1 }}>
            {/* A plain group: ToggleButtonGroup would clone its props onto the tooltip spans. */}
            <Box
              role="group"
              aria-label="Planter"
              sx={{
                display: 'flex',
                borderRadius: '4px',
                overflow: 'hidden',
                backgroundColor: 'rgba(10, 12, 16, 0.62)',
                backdropFilter: 'blur(6px)',
                '& .MuiToggleButton-root': { px: 1.25, py: 0.4, fontSize: '0.68rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)', color: '#e5e7eb', borderRadius: 0 },
                '& .Mui-selected': { color: 'var(--gl-primary) !important' },
                '& .Mui-disabled': { color: 'rgba(229,231,235,0.35) !important' }
              }}
            >
              {PLANTER_ORDER.map((type) => {
                const ok = fits(type);
                const label = `${PLANTER_MODELS[type].name.replace(' Planter', '')} · ${PLANTER_MODELS[type].slots.length}`;
                return (
                  <Tooltip
                    key={type}
                    title={
                      ok
                        ? `${PLANTER_MODELS[type].name}: room for ${donorRoom(type)} donors around the centre`
                        : `Doesn't fit: this generation has ${donors.length} donors, the ${PLANTER_MODELS[type].name.toLowerCase()} has room for ${donorRoom(type)} around its centre`
                    }
                  >
                    {/* The span keeps the tooltip working on a disabled button. */}
                    <span>
                      <ToggleButton size="small" value={type} disabled={!ok} selected={planterType === type} onChange={() => ok && choosePlanter(type)}>
                        {label}
                      </ToggleButton>
                    </span>
                  </Tooltip>
                );
              })}
            </Box>
          </Box>
          <Suspense fallback={null}>
            <Planter3DView
              planterType={planterType}
              plantType={plantType}
              slots={view.slots}
              highlights={selectedSlot !== null && selectedContent ? view.highlights.map((h, i) => (i === selectedSlot ? 'selected' : h)) : view.highlights}
              flow={view.flow}
              celebrate={view.celebrate}
              onSlotClick={(slot) => setSelectedSlot((current) => (current === slot ? null : slot))}
              fallback={null}
            />
          </Suspense>
          {selectedSlot !== null && (
            <Box
              role="status"
              sx={{
                position: 'absolute',
                left: 12,
                bottom: 12,
                maxWidth: 'min(360px, calc(100% - 24px))',
                p: 1.25,
                borderRadius: '8px',
                backgroundColor: 'rgba(10,12,16,0.86)',
                border: '1px solid var(--gl-primary)',
                color: '#f3f4f6',
                display: 'flex',
                flexDirection: 'column',
                gap: 0.75
              }}
            >
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                <Typography sx={{ flex: 1, fontWeight: 800, fontSize: '0.78rem', fontFamily: 'var(--font-family-mono)', color: 'var(--gl-primary)' }}>
                  Slot {model.slots[selectedSlot].number}
                  {selectedContent?.caption ? ` · ${selectedContent.caption}` : selectedSlot === centre ? ' · Centre' : donorSlots.includes(selectedSlot) ? ` · Donor #${donorSlots.indexOf(selectedSlot) + 1}` : ''}
                </Typography>
                <IconButton size="small" aria-label="Close plant details" onClick={() => setSelectedSlot(null)} sx={{ color: '#9ca3af', p: 0.25 }}>
                  <CloseIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Box>
              {selectedContent?.genes ? (
                <GeneticsSequence genes={selectedContent.genes} size="medium" showConnectors />
              ) : donorSlots.includes(selectedSlot) ? (
                <GeneticsSequence genes={donors[donorSlots.indexOf(selectedSlot)]} size="medium" showConnectors />
              ) : null}
              <Typography sx={{ fontSize: '0.72rem', color: '#d1d5db', fontFamily: 'var(--font-family-mono)' }}>
                {selectedSlot === centre
                  ? allPlanted
                    ? 'Result of this generation once crossbreeding finishes.'
                    : hasCentrePlant
                      ? 'Centre plant: takes genes from the donors around it.'
                      : 'Any plant of this type; crossbreeding overwrites its genes.'
                  : donorSlots.includes(selectedSlot)
                    ? `Donor: passes its genes to the centre, slot ${centreNumber}.${step.isSurroundingPlanted ? '' : ' Not planted yet.'}`
                    : 'Leave this slot empty.'}
              </Typography>
            </Box>
          )}
        </Box>
      )}
    </Box>
  );
};
