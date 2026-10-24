import React, { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import {
  Box,
  Button,
  Chip,
  IconButton,
  Paper,
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
import FullscreenIcon from '@mui/icons-material/Fullscreen';
import FullscreenExitIcon from '@mui/icons-material/FullscreenExit';
import CloseIcon from '@mui/icons-material/Close';
import ViewInArIcon from '@mui/icons-material/ViewInAr';
import TouchAppIcon from '@mui/icons-material/TouchApp';
import SpaIcon from '@mui/icons-material/Spa';
import type { PlanterType } from '../../domain/planner/farmPlanning.ts';
import type { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { useCalculation } from '../../context/CalculationContext.tsx';
import { PlanterVisual } from '../guide/PlanterVisual.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';
import { GROWTH_STAGES, PLANTER_MODELS, neighboursOf, type GrowthStage } from './planterModels.ts';
import { breedingPlansFromRoute, demoBreedingPlan, donorSlotsFor, type BreedingPlan } from './breedingLayout.ts';
import type { SlotContent, SlotHighlight } from './PlanterScene.ts';
import { isWebGLAvailable } from './webgl.ts';
import { LazyPlanter3DView as Planter3DView } from './lazyPlanter3DView.ts';

type GuideMode = 'steps' | 'explore' | 'growth';

const PLANTER_ORDER: PlanterType[] = ['large', 'small', 'triangle'];

const STEP_TITLES = ['Plant the centre', 'Grow to sapling', 'Plant the donors', 'Take cuttings'];

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

const overlayButton = {
  backgroundColor: 'rgba(10,12,16,0.62)',
  backdropFilter: 'blur(6px)',
  color: '#e5e7eb',
  '&:hover': { backgroundColor: 'rgba(10,12,16,0.85)' }
} as const;

/** What a clicked slot holds, for the info card. */
interface SlotDetail {
  slot: number;
  title: string;
  genes?: string;
  relation: string;
}

interface PlantingGuide3DProps {
  /** A route to walk through. Without one the guide uses the route selected (or the best
   *  one) in the breeding workspace, and falls back to a worked example. */
  map?: GeneticsMap | null;
  /** Viewport height; responsive default when omitted. */
  height?: number | string;
  initialMode?: GuideMode;
}

export const PlantingGuide3D: React.FC<PlantingGuide3DProps> = ({ map, height, initialMode = 'steps' }) => {
  const { selectedPlant } = useWorkspace();
  const { hasCalculated, selectedMap, filteredAndSortedRoutes } = useCalculation();
  const isPhone = useMediaQuery('(max-width:600px)');
  const [webglOk] = useState(isWebGLAvailable);
  const rootRef = useRef<HTMLDivElement | null>(null);

  // ── Which route to show ─────────────────────────────────────────────────────────────
  const workspaceRoute = hasCalculated ? selectedMap ?? filteredAndSortedRoutes[0]?.bestMap ?? null : null;
  const route = map ?? workspaceRoute;
  const routeLabel = map
    ? 'This route'
    : selectedMap && hasCalculated
      ? `Your selected ${formatPlant(selectedPlant).toLowerCase()} route`
      : workspaceRoute
        ? `Your best ${formatPlant(selectedPlant).toLowerCase()} route`
        : 'Worked example';
  const plans: BreedingPlan[] = useMemo(() => {
    if (route) return breedingPlansFromRoute(route);
    const demo = demoBreedingPlan();
    return demo ? [demo] : [];
  }, [route]);

  const [mode, setMode] = useState<GuideMode>(initialMode);
  const [planIndex, setPlanIndex] = useState(0);
  const [planterType, setPlanterType] = useState<PlanterType>('large');
  const [step, setStep] = useState(0);
  const [isPlaying, setIsPlaying] = useState(false);
  const [selectedSlot, setSelectedSlot] = useState<number | null>(null);
  const [hoveredSlot, setHoveredSlot] = useState<number | null>(null);
  const [stageIndex, setStageIndex] = useState(2);
  const [autoRotate, setAutoRotate] = useState(false);
  const [resetKey, setResetKey] = useState(0);
  const [isFullscreen, setIsFullscreen] = useState(false);

  const plan: BreedingPlan | null = plans[Math.min(planIndex, plans.length - 1)] ?? null;
  const activeMode: GuideMode = plan || mode !== 'steps' ? mode : 'explore';
  const plantType = selectedPlant || 'hemp';

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
    if (activeMode !== 'steps' || !plan) return true;
    const m = PLANTER_MODELS[type];
    return neighboursOf(m, m.centerSlot).length >= plan.donors.length;
  };

  useEffect(() => {
    if (!planterFits(planterType)) setPlanterType('large');
  }, [activeMode, plan]);

  // A different route or generation starts from its first step.
  useEffect(() => {
    setPlanIndex(0);
  }, [route]);
  useEffect(() => {
    setStep(0);
    setSelectedSlot(null);
  }, [plan]);
  useEffect(() => setSelectedSlot(null), [planterType, activeMode, step]);

  useEffect(() => {
    if (!isPlaying || activeMode !== 'steps') return;
    const timer = window.setInterval(() => setStep((s) => (s + 1) % 4), 4200);
    return () => window.clearInterval(timer);
  }, [isPlaying, activeMode]);

  useEffect(() => {
    const onChange = () => setIsFullscreen(document.fullscreenElement === rootRef.current);
    document.addEventListener('fullscreenchange', onChange);
    return () => document.removeEventListener('fullscreenchange', onChange);
  }, []);

  const toggleFullscreen = () => {
    if (document.fullscreenElement) void document.exitFullscreen();
    else void rootRef.current?.requestFullscreen?.().catch(() => setIsFullscreen(false));
  };

  // ── What the scene shows ─────────────────────────────────────────────────────────────
  const view = useMemo(() => {
    const count = model.slots.length;
    const slots: (SlotContent | null)[] = Array(count).fill(null);
    const highlights: SlotHighlight[] = Array(count).fill('none');
    let flow: { from: number[]; to: number | null } | undefined;
    let celebrate: number | null = null;

    if (activeMode === 'steps' && plan) {
      const target = plan.target ?? undefined;
      const order = (i: number) => (plan.plantFirst?.includes(i) ? ' · 1st' : plan.plantSecond?.includes(i) ? ' · 2nd' : '');
      highlights.fill('dim');
      highlights[centre] = 'target';
      if (step === 0) {
        slots[centre] = { stage: 'seedling', role: 'target', genes: target, caption: target ? 'Centre plant' : 'Any plant' };
        donorSlots.forEach((slot) => (highlights[slot] = 'none'));
      } else if (step === 1) {
        slots[centre] = { stage: 'sapling', role: 'target', genes: target, caption: 'Sapling' };
      } else if (step === 2) {
        slots[centre] = { stage: 'sapling', role: 'target', genes: target, caption: 'Crossbreeding' };
        donorSlots.forEach((slot, i) => {
          slots[slot] = { stage: 'mature', role: 'donor', genes: plan.donors[i], caption: `Donor ${i + 1}${order(i)}` };
          highlights[slot] = 'donor';
        });
        flow = { from: donorSlots, to: centre };
      } else {
        slots[centre] = { stage: 'mature', role: 'result', genes: plan.result, caption: 'Result' };
        donorSlots.forEach((slot, i) => {
          slots[slot] = { stage: 'mature', role: 'donor', genes: plan.donors[i], caption: `Donor ${i + 1}` };
        });
        celebrate = centre;
      }
      if (selected !== null && slots[selected]) highlights[selected] = 'selected';
    } else if (activeMode === 'explore') {
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
      if (selected !== null) highlights[selected] = 'selected';
    }

    return { slots, highlights, flow, celebrate };
  }, [activeMode, plan, step, model, centre, donorSlots, selected, hovered, stageIndex]);

  // ── Copy ────────────────────────────────────────────────────────────────────────────
  const centreNumber = model.slots[centre].number;
  const donorNumbers = donorSlots.map((i) => model.slots[i].number);
  const firstNumbers = donorSlots.filter((_, i) => plan?.plantFirst?.includes(i)).map((i) => model.slots[i].number);
  const stepBodies = [
    plan?.target
      ? `Plant the centre plant (${plan.target}) in slot ${centreNumber}. Leave every other slot empty for now; the lit slots are where the donors will go.`
      : `Plant any ${formatPlant(plantType).toLowerCase()} in slot ${centreNumber}, the centre. Its own genes are overwritten, so any plant works. Leave the other slots empty for now.`,
    'Wait until the centre plant is about halfway through the Sapling stage, the one right before the crossbreeding window opens.',
    `Plant the ${plan?.donors.length ?? 0} donor clones in ${slotList(donorNumbers)}.${
      firstNumbers.length ? ` Plant ${slotList(firstNumbers)} first: they break a tie.` : ''
    } When the centre enters Crossbreeding, each gene position takes the gene with the highest total weight among its neighbours (red 1.0, green 0.6). Click a plant to see its genes.`,
    `When crossbreeding finishes, the centre carries ${plan?.result ?? 'the target genes'}${
      plan && plan.chance < 1 ? ` (${Math.round(plan.chance * 100)}% chance)` : ''
    }. Take cuttings to keep it.${plans.length > 1 && planIndex < plans.length - 1 ? ' Its cuttings are a donor in the next generation.' : ''}`
  ];

  const detail: SlotDetail | null = useMemo(() => {
    if (selected === null) return null;
    const number = model.slots[selected].number;
    const content = view.slots[selected];
    const neighbours = neighboursOf(model, selected).map((i) => model.slots[i].number);
    if (activeMode === 'steps' && plan) {
      if (!content) return { slot: selected, title: `Slot ${number}`, relation: 'Empty in this step.' };
      if (content.role === 'donor') {
        const i = donorSlots.indexOf(selected);
        const order = plan.plantFirst?.includes(i) ? ' · plant first' : plan.plantSecond?.includes(i) ? ' · plant second' : '';
        return {
          slot: selected,
          title: `Slot ${number} · Donor ${i + 1}${order}`,
          genes: content.genes,
          relation: `Passes its genes to the centre, slot ${centreNumber}, during crossbreeding.`
        };
      }
      if (content.role === 'result') {
        return {
          slot: selected,
          title: `Slot ${number} · Result`,
          genes: content.genes,
          relation: `What the centre becomes${plan.chance < 1 ? ` (${Math.round(plan.chance * 100)}% chance)` : ''}. Take cuttings to keep it.`
        };
      }
      return {
        slot: selected,
        title: `Slot ${number} · Centre plant`,
        genes: content.genes,
        relation: content.genes
          ? `Takes genes from ${slotList(donorNumbers)} when it crossbreeds.`
          : 'Any plant of this type; crossbreeding overwrites its genes.'
      };
    }
    return {
      slot: selected,
      title: `Slot ${number}`,
      relation: `Crosses with ${slotList(neighbours)}.`
    };
  }, [selected, model, view.slots, activeMode, plan, donorSlots, centreNumber, donorNumbers]);

  if (!webglOk) return <PlanterVisual map={map ?? undefined} />;

  const viewportHeight = isFullscreen ? undefined : height ?? (isPhone ? 360 : { sm: 480, lg: 'min(68vh, 680px)' });

  return (
    <Paper
      ref={rootRef}
      variant="outlined"
      role="region"
      aria-label="3D planter guide"
      sx={{
        backgroundColor: 'var(--gl-panel-bg)',
        borderColor: 'var(--gl-border)',
        borderRadius: isFullscreen ? 0 : '8px',
        overflow: 'hidden',
        ...(isFullscreen && { height: '100vh', display: 'flex', flexDirection: 'column' })
      }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', px: 2, py: 1.25, borderBottom: '1px solid var(--gl-border)' }}>
        <ViewInArIcon sx={{ color: 'var(--gl-primary)' }} />
        <Typography sx={{ fontWeight: 800, fontFamily: 'var(--font-family-mono)', fontSize: '0.95rem', color: 'var(--gl-text-primary)' }}>
          3D Planter Guide
        </Typography>
        <Tooltip title="Change the plant at the top left">
          <Chip
            size="small"
            icon={<SpaIcon sx={{ fontSize: 14 }} />}
            label={formatPlant(plantType)}
            sx={{ height: 22, fontSize: '0.68rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)' }}
          />
        </Tooltip>
        <Box sx={{ flex: 1 }} />
        <ToggleButtonGroup
          size="small"
          exclusive
          value={activeMode}
          onChange={(_, value: GuideMode | null) => value && setMode(value)}
          aria-label="Guide mode"
          sx={{ '& .MuiToggleButton-root': { px: 1.5, py: 0.5, fontSize: '0.72rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)' } }}
        >
          {plan && <ToggleButton value="steps">Breeding steps</ToggleButton>}
          <ToggleButton value="explore">Slot layout</ToggleButton>
          <ToggleButton value="growth">Growth stages</ToggleButton>
        </ToggleButtonGroup>
      </Box>

      {/* Route + generation picker */}
      {activeMode === 'steps' && plan && (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap', px: 2, py: 1, borderBottom: '1px solid var(--gl-border)', backgroundColor: 'var(--gl-panel-header-bg)' }}>
          <Typography sx={{ fontSize: '0.72rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)', color: route ? 'var(--gl-primary)' : 'var(--gl-text-muted)' }}>
            {routeLabel}
          </Typography>
          {plans.length > 1 &&
            plans.map((p, i) => (
              <Chip
                key={i}
                size="small"
                onClick={() => setPlanIndex(i)}
                label={
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
                    <span>{`${i + 1}. Gen ${p.generation}`}</span>
                    <GeneticsSequence genes={p.result} size="small" />
                  </Box>
                }
                sx={{
                  height: 26,
                  fontWeight: 800,
                  fontSize: '0.66rem',
                  fontFamily: 'var(--font-family-mono)',
                  border: '1px solid',
                  borderColor: i === planIndex ? 'var(--gl-primary)' : 'var(--gl-border-strong)',
                  backgroundColor: i === planIndex ? 'rgba(var(--gl-primary-rgb), 0.16)' : 'transparent'
                }}
              />
            ))}
          {plans.length === 1 && <GeneticsSequence genes={plan.result} size="small" />}
          {!route && (
            <Typography sx={{ fontSize: '0.68rem', color: 'var(--gl-text-muted)', fontFamily: 'var(--font-family-mono)' }}>
              Calculate routes in the Breeding Workspace to see your own plan here.
            </Typography>
          )}
        </Box>
      )}

      {/* Viewport */}
      <Box sx={{ position: 'relative', height: viewportHeight, ...(isFullscreen && { flex: 1, minHeight: 0 }) }}>
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
            onSlotClick={(slot) => setSelectedSlot((current) => (current === slot ? null : slot))}
            onSlotHover={setHoveredSlot}
            fallback={<PlanterVisual map={map ?? undefined} />}
          />
        </Suspense>

        {/* Planter picker */}
        <Box sx={{ position: 'absolute', top: 12, left: 12, right: 150 }}>
          <ToggleButtonGroup
            size="small"
            exclusive
            value={planterType}
            onChange={(_, value: PlanterType | null) => value && setPlanterType(value)}
            aria-label="Planter"
            sx={{
              ...overlayButton,
              '& .MuiToggleButton-root': { px: 1.25, py: 0.5, fontSize: '0.7rem', fontWeight: 800, fontFamily: 'var(--font-family-mono)', color: '#e5e7eb' },
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
                <Tooltip key={type} title={`This step has ${plan?.donors.length} donors; the centre of a ${PLANTER_MODELS[type].name.toLowerCase()} only touches ${neighboursOf(PLANTER_MODELS[type], PLANTER_MODELS[type].centerSlot).length}.`}>
                  <span>{button}</span>
                </Tooltip>
              );
            })}
          </ToggleButtonGroup>
        </Box>

        {/* Camera controls */}
        <Box sx={{ position: 'absolute', top: 12, right: 12, display: 'flex', gap: 0.75 }}>
          <Tooltip title={autoRotate ? 'Stop turning' : 'Turn slowly'}>
            <IconButton onClick={() => setAutoRotate((v) => !v)} aria-pressed={autoRotate} aria-label="Auto-rotate" sx={{ ...overlayButton, color: autoRotate ? 'var(--gl-primary)' : '#e5e7eb' }}>
              <ThreeSixtyIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title="Reset view">
            <IconButton onClick={() => setResetKey((k) => k + 1)} aria-label="Reset view" sx={overlayButton}>
              <CenterFocusStrongIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          <Tooltip title={isFullscreen ? 'Exit full screen' : 'Full screen'}>
            <IconButton onClick={toggleFullscreen} aria-label={isFullscreen ? 'Exit full screen' : 'Full screen'} sx={overlayButton}>
              {isFullscreen ? <FullscreenExitIcon fontSize="small" /> : <FullscreenIcon fontSize="small" />}
            </IconButton>
          </Tooltip>
        </Box>

        {/* Clicked plant */}
        {detail && (
          <Box
            role="status"
            sx={{
              position: 'absolute',
              left: 12,
              bottom: 12,
              maxWidth: 'min(380px, calc(100% - 24px))',
              p: 1.5,
              borderRadius: '8px',
              backgroundColor: 'rgba(10,12,16,0.86)',
              backdropFilter: 'blur(8px)',
              border: '1px solid var(--gl-primary)',
              color: '#f3f4f6',
              display: 'flex',
              flexDirection: 'column',
              gap: 1
            }}
          >
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography sx={{ flex: 1, fontWeight: 800, fontSize: '0.8rem', fontFamily: 'var(--font-family-mono)', color: 'var(--gl-primary)' }}>
                {detail.title}
              </Typography>
              <IconButton size="small" aria-label="Close plant details" onClick={() => setSelectedSlot(null)} sx={{ color: '#9ca3af', p: 0.25 }}>
                <CloseIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Box>
            {detail.genes && <GeneticsSequence genes={detail.genes} size="medium" showConnectors />}
            <Typography sx={{ fontSize: '0.75rem', lineHeight: 1.5, color: '#d1d5db', fontFamily: 'var(--font-family-mono)' }}>{detail.relation}</Typography>
          </Box>
        )}

        <Typography
          sx={{
            position: 'absolute',
            bottom: 10,
            right: 14,
            fontSize: '0.64rem',
            fontFamily: 'var(--font-family-mono)',
            color: 'rgba(229, 231, 235, 0.65)',
            pointerEvents: 'none',
            display: { xs: 'none', sm: 'block' }
          }}
        >
          Click a plant for details · drag to orbit · scroll to zoom
        </Typography>
      </Box>

      {/* Panel */}
      <Box sx={{ px: 2, py: 1.75, borderTop: '1px solid var(--gl-border)' }}>
        {activeMode === 'steps' && plan && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr 1fr', sm: 'repeat(4, 1fr)' }, gap: 0.75 }}>
              {STEP_TITLES.map((title, i) => (
                <Button
                  key={title}
                  size="small"
                  onClick={() => {
                    setStep(i);
                    setIsPlaying(false);
                  }}
                  sx={{
                    justifyContent: 'flex-start',
                    fontWeight: 800,
                    fontSize: '0.72rem',
                    fontFamily: 'var(--font-family-mono)',
                    textTransform: 'none',
                    borderRadius: '6px',
                    py: 0.75,
                    backgroundColor: i === step ? 'var(--gl-primary)' : 'transparent',
                    color: i === step ? 'var(--gl-on-accent)' : i < step ? 'var(--gl-text-primary)' : 'var(--gl-text-secondary)',
                    border: '1px solid',
                    borderColor: i === step ? 'var(--gl-primary)' : 'var(--gl-border-strong)',
                    '&:hover': { backgroundColor: i === step ? 'var(--gl-primary)' : 'rgba(var(--gl-primary-rgb), 0.08)' }
                  }}
                >
                  {i + 1}. {title}
                </Button>
              ))}
            </Box>
            <Typography sx={{ fontSize: '0.86rem', lineHeight: 1.6, color: 'var(--gl-text-secondary)', fontFamily: 'var(--font-family-mono)', minHeight: 46 }}>
              {stepBodies[step]}
            </Typography>
            <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>
              <Button variant="outlined" startIcon={<NavigateBeforeIcon />} disabled={step === 0} onClick={() => { setStep((s) => s - 1); setIsPlaying(false); }}>
                Back
              </Button>
              <Button variant="contained" endIcon={<NavigateNextIcon />} disabled={step === 3} onClick={() => { setStep((s) => s + 1); setIsPlaying(false); }}>
                Next
              </Button>
              <Button startIcon={isPlaying ? <PauseIcon /> : <PlayArrowIcon />} onClick={() => setIsPlaying((p) => !p)}>
                {isPlaying ? 'Pause' : 'Play'}
              </Button>
              <Box sx={{ flex: 1 }} />
              {step === 3 && planIndex < plans.length - 1 && (
                <Button variant="contained" color="success" endIcon={<NavigateNextIcon />} onClick={() => setPlanIndex((i) => i + 1)}>
                  Next generation
                </Button>
              )}
            </Box>
          </Box>
        )}

        {activeMode === 'explore' && (
          <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'flex-start' }}>
            <TouchAppIcon sx={{ color: 'var(--gl-primary)', mt: 0.25 }} />
            <Box>
              <Typography sx={{ fontWeight: 800, fontSize: '0.86rem', color: 'var(--gl-text-primary)', fontFamily: 'var(--font-family-mono)' }}>
                {model.name}: {model.slots.length} slots
              </Typography>
              <Typography sx={{ fontSize: '0.8rem', lineHeight: 1.6, color: 'var(--gl-text-secondary)', fontFamily: 'var(--font-family-mono)' }}>
                Hover or click a slot to see which plants can pass genes to it. The centre (slot {centreNumber}) touches {centreNeighbours.length}, so it is the slot to breed in.
              </Typography>
            </Box>
          </Box>
        )}

        {activeMode === 'growth' && (
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
                sx={{ '& .MuiSlider-markLabel': { fontSize: '0.7rem', fontFamily: 'var(--font-family-mono)' } }}
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
