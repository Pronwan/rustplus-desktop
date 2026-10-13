import React, { Suspense, lazy, useEffect, useMemo, useState } from 'react';
import {
  Box,
  Button,
  Chip,
  IconButton,
  MenuItem,
  Paper,
  Select,
  Slider,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
  useMediaQuery
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import PauseIcon from '@mui/icons-material/Pause';
import NavigateNextIcon from '@mui/icons-material/NavigateNext';
import NavigateBeforeIcon from '@mui/icons-material/NavigateBefore';
import ThreeSixtyIcon from '@mui/icons-material/ThreeSixty';
import CenterFocusStrongIcon from '@mui/icons-material/CenterFocusStrong';
import ViewInArIcon from '@mui/icons-material/ViewInAr';
import TouchAppIcon from '@mui/icons-material/TouchApp';
import type { PlanterType } from '../../domain/planner/farmPlanning.ts';
import type { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { PLANT_TYPES } from '../../context/AppContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { PlanterVisual } from '../guide/PlanterVisual.tsx';
import { GROWTH_STAGES, PLANTER_MODELS, neighboursOf, type GrowthStage } from './planterModels.ts';
import { breedingPlanFromMap, demoBreedingPlan, donorSlotsFor, type BreedingPlan } from './breedingLayout.ts';
import type { SlotContent, SlotHighlight } from './PlanterScene.ts';
import { isWebGLAvailable } from './webgl.ts';

// three.js is ~600 KB; it loads only when a guide is actually shown.
const Planter3DView = lazy(() => import('./Planter3DView.tsx'));

type GuideMode = 'steps' | 'explore' | 'growth';

const PLANTER_ORDER: PlanterType[] = ['large', 'small', 'triangle'];

const STAGE_NOTES: Record<GrowthStage, string> = {
  seed: 'Planting phase. A seed carries random genetics; a cutting copies its parent exactly.',
  seedling: 'A small sprout forms as health builds. Keep it watered and lit.',
  sapling: 'Genes become visible and cloning unlocks. Take cuttings now to lock a gene string in before crossbreeding can change it.',
  mature: 'Crossbreeding has happened by now. Mass and shape are nearly final and yield starts to build.',
  fruiting: 'Fruit, cloth or berries develop. Ripe comes next: harvest then for maximum yield.',
  dying: 'Left unharvested, the plant withers and its yield is lost.'
};

const formatPlant = (plant: string) => plant.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());

const slotList = (numbers: number[]) =>
  numbers.length === 0 ? 'none' : numbers.length === 1 ? `slot ${numbers[0]}` : `slots ${numbers.slice(0, -1).join(', ')} and ${numbers[numbers.length - 1]}`;

interface PlantingGuide3DProps {
  /** A calculated plan to walk through; without one a worked example is shown. */
  map?: GeneticsMap | null;
  /** Viewport height in px (desktop); phones get a shorter one. */
  height?: number;
  initialMode?: GuideMode;
}

export const PlantingGuide3D: React.FC<PlantingGuide3DProps> = ({ map, height = 440, initialMode = 'steps' }) => {
  const { selectedPlant } = useWorkspace();
  const isPhone = useMediaQuery('(max-width:600px)');
  const [webglOk] = useState(isWebGLAvailable);

  const plan: BreedingPlan | null = useMemo(() => (map ? breedingPlanFromMap(map) : demoBreedingPlan()), [map]);
  const isExample = !map;

  const [mode, setMode] = useState<GuideMode>(plan ? initialMode : 'explore');
  const [planterType, setPlanterType] = useState<PlanterType>('large');
  const [plantType, setPlantType] = useState<string>(selectedPlant || 'hemp');
  const [step, setStep] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const [hoveredSlot, setHoveredSlot] = useState<number | null>(null);
  const [stageIndex, setStageIndex] = useState(2);
  const [autoRotate, setAutoRotate] = useState(false);
  const [resetKey, setResetKey] = useState(0);

  const model = PLANTER_MODELS[planterType];
  const centre = model.centerSlot;
  // Selection can outlive a planter switch by one render; a 9-slot index means nothing in a
  // 3-slot planter.
  const inPlanter = (slot: number | null) => (slot !== null && slot < model.slots.length ? slot : null);
  const selected = inPlanter(selectedSlot);
  const hovered = inPlanter(hoveredSlot);
  const centreNeighbours = useMemo(() => neighboursOf(model, centre), [model, centre]);
  const donorSlots = useMemo(() => (plan ? donorSlotsFor(model, plan.donors.length) : []), [model, plan]);

  // A plan needs room for all its donors around the centre slot.
  const planterFits = (type: PlanterType) => {
    if (mode !== 'steps' || !plan) return true;
    const m = PLANTER_MODELS[type];
    return neighboursOf(m, m.centerSlot).length >= plan.donors.length;
  };

  useEffect(() => {
    if (!planterFits(planterType)) setPlanterType('large');
  }, [mode, plan]);

  useEffect(() => {
    if (!isPlaying || mode !== 'steps') return;
    const timer = window.setInterval(() => setStep((s) => (s + 1) % 4), 4200);
    return () => window.clearInterval(timer);
  }, [isPlaying, mode]);

  useEffect(() => setSelectedSlot(null), [planterType, mode]);

  // ── What the scene shows ─────────────────────────────────────────────────────────────
  const view = useMemo(() => {
    const count = model.slots.length;
    const slots: (SlotContent | null)[] = Array(count).fill(null);
    const highlights: SlotHighlight[] = Array(count).fill('none');
    let flow: { from: number[]; to: number | null } | undefined;
    let celebrate: number | null = null;

    if (mode === 'steps' && plan) {
      const target = plan.target ?? undefined;
      highlights.fill('dim');
      highlights[centre] = 'target';
      if (step === 0) {
        slots[centre] = { stage: 'seedling', role: 'target', genes: target, caption: target ? 'Target plant' : 'Any plant' };
      } else if (step === 1) {
        slots[centre] = { stage: 'sapling', role: 'target', genes: target, caption: 'Sapling' };
      } else if (step === 2) {
        slots[centre] = { stage: 'sapling', role: 'target', genes: target, caption: 'Crossbreeding' };
        donorSlots.forEach((slot, i) => {
          slots[slot] = { stage: 'mature', role: 'donor', genes: plan.donors[i], caption: `Donor ${i + 1}` };
          highlights[slot] = 'donor';
        });
        flow = { from: donorSlots, to: centre };
      } else {
        slots[centre] = { stage: 'mature', role: 'result', genes: plan.result, caption: 'Your clone' };
        donorSlots.forEach((slot, i) => {
          slots[slot] = { stage: 'mature', role: 'donor', genes: plan.donors[i] };
        });
        celebrate = centre;
      }
    } else if (mode === 'explore') {
      for (let i = 0; i < count; i++) slots[i] = { stage: 'mature', role: 'plant' };
      const focus = selected ?? hovered;
      if (focus !== null) {
        const neighbours = neighboursOf(model, focus);
        highlights.fill('dim');
        highlights[focus] = 'selected';
        neighbours.forEach((n) => (highlights[n] = 'neighbour'));
        if (selected !== null) flow = { from: neighbours, to: selected };
      }
    } else {
      const stage = GROWTH_STAGES[stageIndex].id;
      for (let i = 0; i < count; i++) slots[i] = { stage, role: 'plant' };
    }

    return { slots, highlights, flow, celebrate };
  }, [mode, plan, step, model, centre, donorSlots, selected, hovered, stageIndex]);

  // ── Copy for the panel under the viewport ───────────────────────────────────────────
  const donorNumbers = donorSlots.map((i) => model.slots[i].number);
  const centreNumber = model.slots[centre].number;
  const steps = [
    {
      title: 'Plant the target in the centre',
      body: plan?.target
        ? `Plant the base plant (${plan.target}) in slot ${centreNumber}, the centre. Leave every other slot empty.`
        : `Plant any ${formatPlant(plantType).toLowerCase()} in slot ${centreNumber}, the centre. Its own genes are overwritten, so any plant works. Leave every other slot empty.`
    },
    {
      title: 'Grow it to sapling',
      body: 'Wait until the centre plant is about halfway through the Sapling stage, the one right before the crossbreeding window opens.'
    },
    {
      title: 'Plant the donors around it',
      body: `Plant the ${plan?.donors.length ?? 0} donor clones in ${slotList(donorNumbers)}. When the centre enters Crossbreeding, each gene position takes the gene with the highest total weight among its neighbours: red genes weigh 1.0, green 0.6.`
    },
    {
      title: 'Take cuttings of your clone',
      body: `Once crossbreeding has finished, the centre plant carries ${plan?.result ?? 'the target genes'}. Take cuttings to keep that gene string for good.`
    }
  ];

  const focusSlot = selected ?? hovered;
  const focusNeighbours = focusSlot !== null ? neighboursOf(model, focusSlot).map((i) => model.slots[i].number) : [];

  if (!webglOk) return <PlanterVisual map={map ?? undefined} />;

  const viewportHeight = isPhone ? 320 : height;

  return (
    <Paper
      variant="outlined"
      role="region"
      aria-label="3D planter guide"
      sx={{ backgroundColor: 'var(--gl-panel-bg)', borderColor: 'var(--gl-border)', borderRadius: '8px', overflow: 'hidden' }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', px: 2, py: 1.25, borderBottom: '1px solid var(--gl-border)' }}>
        <ViewInArIcon sx={{ color: 'var(--gl-primary)' }} />
        <Typography sx={{ fontWeight: 800, fontFamily: 'var(--font-family-mono)', fontSize: '0.95rem', color: 'var(--gl-text-primary)' }}>
          3D Planter Guide
        </Typography>
        {isExample && mode === 'steps' && (
          <Chip size="small" label="Worked example" variant="outlined" sx={{ height: 20, fontSize: '0.65rem', fontWeight: 700 }} />
        )}
        <Box sx={{ flex: 1 }} />
        <ToggleButtonGroup
          size="small"
          exclusive
          value={mode}
          onChange={(_, value: GuideMode | null) => value && setMode(value)}
          aria-label="Guide mode"
          sx={{ '& .MuiToggleButton-root': { px: 1.25, py: 0.4, fontSize: '0.7rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)' } }}
        >
          {plan && <ToggleButton value="steps">Breeding steps</ToggleButton>}
          <ToggleButton value="explore">Slot layout</ToggleButton>
          <ToggleButton value="growth">Growth stages</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {/* Viewport */}
      <Box sx={{ position: 'relative', height: viewportHeight }}>
        <Suspense
          fallback={
            <Box sx={{ height: '100%', display: 'grid', placeItems: 'center', color: 'var(--gl-text-muted)', fontFamily: 'var(--font-family-mono)', fontSize: '0.75rem' }}>
              Loading 3D view…
            </Box>
          }
        >
          <Planter3DView
            planterType={planterType}
            plantType={plantType}
            slots={view.slots}
            highlights={view.highlights}
            flow={view.flow}
            celebrate={view.celebrate}
            autoRotate={autoRotate}
            resetKey={resetKey}
            onSlotClick={(slot) => {
              if (mode === 'explore') setSelectedSlot((current) => (current === slot ? null : slot));
            }}
            onSlotHover={setHoveredSlot}
            fallback={<PlanterVisual map={map ?? undefined} />}
          />
        </Suspense>

        {/* Planter + plant pickers */}
        <Box sx={{ position: 'absolute', top: 10, left: 10, right: 92, display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={planterType}
            onChange={(_, value: PlanterType | null) => value && setPlanterType(value)}
            aria-label="Planter"
            sx={{
              backgroundColor: 'rgba(10, 12, 16, 0.6)',
              backdropFilter: 'blur(6px)',
              '& .MuiToggleButton-root': { px: 1, py: 0.35, fontSize: '0.66rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)', color: '#e5e7eb' },
              '& .Mui-selected': { color: 'var(--gl-primary) !important' }
            }}
          >
            {PLANTER_ORDER.map((type) => {
              const fits = planterFits(type);
              const button = (
                <ToggleButton key={type} value={type} disabled={!fits}>
                  {PLANTER_MODELS[type].name.replace(' Planter', '')} · {PLANTER_MODELS[type].slots.length}
                </ToggleButton>
              );
              return fits ? button : (
                <Tooltip key={type} title={`This plan has ${plan?.donors.length} donors; the centre of a ${PLANTER_MODELS[type].name.toLowerCase()} only touches ${neighboursOf(PLANTER_MODELS[type], PLANTER_MODELS[type].centerSlot).length}.`}>
                  <span>{button}</span>
                </Tooltip>
              );
            })}
          </ToggleButtonGroup>
          <Select
            size="small"
            value={plantType}
            onChange={(event) => setPlantType(event.target.value)}
            aria-label="Plant"
            sx={{
              height: 30,
              fontSize: '0.72rem',
              fontWeight: 700,
              fontFamily: 'var(--font-family-mono)',
              color: '#e5e7eb',
              backgroundColor: 'rgba(10, 12, 16, 0.6)',
              backdropFilter: 'blur(6px)',
              '& .MuiOutlinedInput-notchedOutline': { borderColor: 'rgba(255,255,255,0.15)' },
              '& .MuiSvgIcon-root': { color: '#e5e7eb' }
            }}
          >
            {PLANT_TYPES.map((plant) => (
              <MenuItem key={plant} value={plant} sx={{ fontSize: '0.78rem' }}>
                {formatPlant(plant)}
              </MenuItem>
            ))}
          </Select>
        </Box>

        {/* Camera controls */}
        <Box sx={{ position: 'absolute', top: 10, right: 10, display: 'flex', gap: 0.5 }}>
          <Tooltip title={autoRotate ? 'Stop turning' : 'Turn slowly'}>
            <IconButton
              size="small"
              onClick={() => setAutoRotate((v) => !v)}
              aria-pressed={autoRotate}
              aria-label="Auto-rotate"
              sx={{ backgroundColor: 'rgba(10,12,16,0.6)', color: autoRotate ? 'var(--gl-primary)' : '#e5e7eb', '&:hover': { backgroundColor: 'rgba(10,12,16,0.8)' } }}
            >
              <ThreeSixtyIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title="Reset view">
            <IconButton
              size="small"
              onClick={() => setResetKey((k) => k + 1)}
              aria-label="Reset view"
              sx={{ backgroundColor: 'rgba(10,12,16,0.6)', color: '#e5e7eb', '&:hover': { backgroundColor: 'rgba(10,12,16,0.8)' } }}
            >
              <CenterFocusStrongIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>

        {/* Hint */}
        <Typography
          sx={{
            position: 'absolute',
            bottom: 8,
            right: 12,
            fontSize: '0.62rem',
            fontFamily: 'var(--font-family-mono)',
            color: 'rgba(229, 231, 235, 0.6)',
            pointerEvents: 'none',
            display: { xs: 'none', sm: 'block' }
          }}
        >
          Drag to orbit · scroll to zoom{mode === 'explore' ? ' · click a slot' : ''}
        </Typography>
      </Box>

      {/* Panel */}
      <Box sx={{ px: 2, py: 1.75, borderTop: '1px solid var(--gl-border)' }}>
        {mode === 'steps' && plan && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
              {steps.map((s, i) => (
                <Chip
                  key={s.title}
                  onClick={() => {
                    setStep(i);
                    setIsPlaying(false);
                  }}
                  label={`${i + 1}. ${s.title}`}
                  size="small"
                  sx={{
                    fontWeight: 800,
                    fontSize: '0.68rem',
                    fontFamily: 'var(--font-family-mono)',
                    borderRadius: '4px',
                    backgroundColor: i === step ? 'var(--gl-primary)' : 'transparent',
                    color: i === step ? 'var(--gl-on-accent)' : 'var(--gl-text-secondary)',
                    border: '1px solid',
                    borderColor: i === step ? 'var(--gl-primary)' : 'var(--gl-border-strong)'
                  }}
                />
              ))}
            </Box>
            <Typography sx={{ fontSize: '0.85rem', lineHeight: 1.6, color: 'var(--gl-text-secondary)', fontFamily: 'var(--font-family-mono)', minHeight: 44 }}>
              {steps[step].body}
            </Typography>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <Button size="small" variant="outlined" startIcon={<NavigateBeforeIcon />} disabled={step === 0} onClick={() => { setStep((s) => s - 1); setIsPlaying(false); }}>
                Back
              </Button>
              <Button size="small" variant="contained" endIcon={<NavigateNextIcon />} disabled={step === 3} onClick={() => { setStep((s) => s + 1); setIsPlaying(false); }}>
                Next
              </Button>
              <Button size="small" startIcon={isPlaying ? <PauseIcon /> : <PlayArrowIcon />} onClick={() => setIsPlaying((p) => !p)}>
                {isPlaying ? 'Pause' : 'Play'}
              </Button>
              <Box sx={{ flex: 1 }} />
              <Typography sx={{ fontSize: '0.68rem', color: 'var(--gl-text-muted)', fontFamily: 'var(--font-family-mono)' }}>
                Step {step + 1} of 4
              </Typography>
            </Box>
          </Box>
        )}

        {mode === 'explore' && (
          <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
            <TouchAppIcon sx={{ color: 'var(--gl-primary)', mt: 0.25 }} />
            <Box>
              <Typography sx={{ fontWeight: 800, fontSize: '0.85rem', color: 'var(--gl-text-primary)', fontFamily: 'var(--font-family-mono)' }}>
                {focusSlot === null
                  ? `${model.name}: ${model.slots.length} slots`
                  : `Slot ${model.slots[focusSlot].number} crosses with ${focusNeighbours.length} neighbour${focusNeighbours.length === 1 ? '' : 's'}`}
              </Typography>
              <Typography sx={{ fontSize: '0.8rem', lineHeight: 1.6, color: 'var(--gl-text-secondary)', fontFamily: 'var(--font-family-mono)' }}>
                {focusSlot === null
                  ? `Hover or click a slot to see which plants can pass genes to it. The centre (slot ${centreNumber}) touches ${centreNeighbours.length}, so it is the slot to breed in.`
                  : `During crossbreeding, slot ${model.slots[focusSlot].number} takes genes from ${slotList(focusNeighbours)}.${focusNeighbours.length < centreNeighbours.length ? ` For the most donors use the centre, slot ${centreNumber}.` : ''}`}
              </Typography>
            </Box>
          </Box>
        )}

        {mode === 'growth' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
            <Box sx={{ px: 1.5 }}>
              <Slider
                value={stageIndex}
                min={0}
                max={GROWTH_STAGES.length - 1}
                step={1}
                marks={GROWTH_STAGES.map((s, i) => ({ value: i, label: isPhone ? s.label.slice(0, 4) : s.label }))}
                onChange={(_, value) => setStageIndex(value as number)}
                aria-label="Growth stage"
                sx={{ '& .MuiSlider-markLabel': { fontSize: '0.68rem', fontFamily: 'var(--font-family-mono)' } }}
              />
            </Box>
            <Typography sx={{ fontSize: '0.8rem', lineHeight: 1.6, color: 'var(--gl-text-secondary)', fontFamily: 'var(--font-family-mono)' }}>
              <strong style={{ color: 'var(--gl-primary)' }}>{GROWTH_STAGES[stageIndex].label}.</strong> {STAGE_NOTES[GROWTH_STAGES[stageIndex].id]}
              {plantType !== 'pumpkin' && ' Only pumpkin has a model for every stage; other plants are shown scaled.'}
            </Typography>
          </Box>
        )}
      </Box>
    </Paper>
  );
};
