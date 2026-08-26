import React, { useEffect, useMemo, useState } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Divider,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Stack,
  Switch,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography
} from '@mui/material';
import BoltIcon from '@mui/icons-material/Bolt';
import BuildIcon from '@mui/icons-material/Build';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/Download';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PrintIcon from '@mui/icons-material/Print';
import RestaurantIcon from '@mui/icons-material/Restaurant';
import ScienceIcon from '@mui/icons-material/Science';
import WaterDropIcon from '@mui/icons-material/WaterDrop';
import { useApp } from '../../context/AppContext.tsx';
import { useNotification } from '../../context/NotificationContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import {
  AuditPlannerInput,
  DEFAULT_AUDIT_INPUT,
  DEFAULT_CONDITIONS,
  DEFAULT_GOAL_INPUT,
  FARM_CROPS,
  FARM_GOAL_OPTIONS,
  FARM_MODEL_REVIEWED_ON,
  FarmConditions,
  FarmPlan,
  GoalPlannerInput,
  PLANTER_TYPES,
  auditFarmSetup,
  buildFarmPlanText,
  formatDuration,
  normalizeGenetics,
  planFarmFromGoal,
  timeBasisLabel
} from '../../domain/planner/farmPlanning.ts';
import { StorageService } from '../../services/storageService.ts';
import { buildFarmPlanSvg } from '../../utils/farmPlanExport.ts';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

type PlannerMode = 'goal' | 'audit';

interface FarmPlannerDraft {
  mode: PlannerMode;
  goal: GoalPlannerInput;
  audit: AuditPlannerInput;
  ownedItems: string[];
}

type SharedAssumptions = Pick<
  GoalPlannerInput,
  | 'conditions'
  | 'serverRate'
  | 'reserveClones'
  | 'waterSource'
  | 'powerSource'
  | 'bufferHours'
  | 'safetyMarginPercent'
  | 'measuredYieldPerPlant'
  | 'measuredCycleMinutes'
>;

const panelSx = {
  backgroundColor: 'var(--gl-panel-bg)',
  borderColor: 'var(--gl-border)',
  borderRadius: '6px'
};
const numberSx = { fontFamily: 'monospace', fontVariantNumeric: 'tabular-nums' };

function loadDraft(selectedPlant: string, targetGenetics: string): FarmPlannerDraft {
  const stored = StorageService.getFarmPlannerDraft<Partial<FarmPlannerDraft> | null>(null);
  const cropId = FARM_CROPS[selectedPlant] ? selectedPlant : DEFAULT_AUDIT_INPUT.cropId;
  const genetics = normalizeGenetics(targetGenetics);
  const usableGenetics = genetics.length === 6 ? genetics : DEFAULT_GOAL_INPUT.genetics;
  return {
    mode: stored?.mode === 'audit' ? 'audit' : 'goal',
    goal: {
      ...DEFAULT_GOAL_INPUT,
      genetics: usableGenetics,
      ...stored?.goal,
      conditions: { ...DEFAULT_CONDITIONS, ...stored?.goal?.conditions }
    },
    audit: {
      ...DEFAULT_AUDIT_INPUT,
      cropId,
      genetics: usableGenetics,
      ...stored?.audit,
      conditions: { ...DEFAULT_CONDITIONS, ...stored?.audit?.conditions }
    },
    ownedItems: Array.isArray(stored?.ownedItems) ? stored.ownedItems : []
  };
}

export const FarmOutputPlanner: React.FC = () => {
  const { setActiveTab } = useApp();
  const { selectedPlant, setSelectedPlant, targetConfig, setTargetPreset, allClones } = useWorkspace();
  const { notifySuccess, notifyInfo, notifyError } = useNotification();
  const [draft, setDraft] = useState<FarmPlannerDraft>(() => loadDraft(selectedPlant, targetConfig.targetGenetics));
  const plan = useMemo(() => planFarmFromGoal(draft.goal), [draft.goal]);
  const audit = useMemo(() => auditFarmSetup(draft.audit), [draft.audit]);
  const selectedGoal = FARM_GOAL_OPTIONS.find((option) => option.item === draft.goal.outputItem);
  const ownedCloneCount = allClones
    .filter((clone) => clone.genetics === draft.goal.genetics && plan.crops.some((crop) => crop.cropId === clone.cropType))
    .reduce((total, clone) => total + clone.quantity, 0);

  useEffect(() => StorageService.saveFarmPlannerDraft(draft), [draft]);

  const updateGoal = (patch: Partial<GoalPlannerInput>) =>
    setDraft((current) => ({ ...current, goal: { ...current.goal, ...patch } }));
  const updateAudit = (patch: Partial<AuditPlannerInput>) =>
    setDraft((current) => ({ ...current, audit: { ...current.audit, ...patch } }));

  const openBreedingWorkspace = (cropId: string, genetics: string) => {
    setSelectedPlant(cropId);
    setTargetPreset(genetics, 'exact');
    setActiveTab('workspace');
    notifyInfo(`Breeding target set to ${genetics}.`);
  };

  const openRecipe = () => {
    sessionStorage.setItem('GL_RECIPE_HANDOFF_V1', JSON.stringify({
      name: draft.goal.outputItem,
      multiplier: Math.max(1, Math.ceil(draft.goal.quantity))
    }));
    setActiveTab('recipes');
  };

  const copyPlan = async () => {
    try {
      await navigator.clipboard.writeText(buildFarmPlanText(plan));
      notifySuccess('Farm checklist copied.');
    } catch {
      notifyError('The browser could not copy the farm checklist.');
    }
  };

  const exportPlan = () => {
    const url = URL.createObjectURL(new Blob([buildFarmPlanSvg(plan)], { type: 'image/svg+xml' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `rust-farm-plan-${draft.goal.outputItem.toLowerCase().replace(/[^a-z0-9]+/g, '-')}.svg`;
    anchor.click();
    URL.revokeObjectURL(url);
    notifySuccess('Farm plan image exported.');
  };

  const auditRecommendation = () => {
    if (!plan.supported || plan.crops.length !== 1) return;
    const crop = plan.crops[0];
    setDraft((current) => ({
      ...current,
      mode: 'audit',
      audit: {
        ...current.audit,
        cropId: crop.cropId,
        genetics: crop.genetics,
        planterType: current.goal.planterType,
        planterCount: crop.planterCount,
        filledSlotsPerPlanter: PLANTER_TYPES[current.goal.planterType].slots,
        conditions: current.goal.conditions,
        serverRate: current.goal.serverRate,
        reserveClones: current.goal.reserveClones,
        waterSource: current.goal.waterSource,
        powerSource: current.goal.powerSource,
        bufferHours: current.goal.bufferHours,
        safetyMarginPercent: current.goal.safetyMarginPercent,
        availableWaterMlPerMinute: plan.infrastructure.recommendedWaterFlowMlPerMinute,
        availablePowerRw: plan.infrastructure.powerWithSafetyMarginRw,
        measuredYieldPerPlant: current.goal.measuredYieldPerPlant,
        measuredCycleMinutes: current.goal.measuredCycleMinutes
      }
    }));
  };

  return (
    <Box sx={{ maxWidth: 1440, mx: 'auto', p: { xs: 1.5, sm: 2, lg: 3 }, minWidth: 0 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: { xs: 'stretch', sm: 'flex-start' }, flexDirection: { xs: 'column', sm: 'row' }, gap: 1.5, mb: 2 }}>
        <Box>
          <Typography component="h1" variant="h5" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>Farm Operations Planner</Typography>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', mt: 0.5 }}>Size a build from an output goal, or find the bottleneck in a farm you already have.</Typography>
        </Box>
        <Chip label="Autosaved locally" size="small" variant="outlined" sx={{ alignSelf: { xs: 'flex-start', sm: 'center' }, color: 'var(--gl-success)', borderColor: 'var(--gl-success)' }} />
      </Box>

      <ToggleButtonGroup
        exclusive
        fullWidth
        size="small"
        value={draft.mode}
        onChange={(_, mode: PlannerMode | null) => mode && setDraft((current) => ({ ...current, mode }))}
        aria-label="Farm planner mode"
        sx={{ maxWidth: 440, mb: 2, '& .MuiToggleButton-root': { color: 'var(--gl-text-secondary)', fontWeight: 800 }, '& .Mui-selected': { color: 'var(--gl-primary) !important' } }}
      >
        <ToggleButton value="goal">Plan by goal</ToggleButton>
        <ToggleButton value="audit">Audit my setup</ToggleButton>
      </ToggleButtonGroup>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(320px, 390px) minmax(0, 1fr)' }, alignItems: 'start', gap: 2 }}>
        <Paper component="section" variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 }, minWidth: 0 }}>
          {draft.mode === 'goal' ? <GoalForm value={draft.goal} onChange={updateGoal} /> : <AuditForm value={draft.audit} onChange={updateAudit} />}
        </Paper>

        <Box component="section" aria-live="polite" sx={{ minWidth: 0 }}>
          {draft.mode === 'goal' ? (
            <GoalResults
              plan={plan}
              isRecipe={selectedGoal?.category !== 'crop'}
              ownedCloneCount={ownedCloneCount}
              ownedItems={draft.ownedItems}
              onToggleOwned={(item) => setDraft((current) => ({
                ...current,
                ownedItems: current.ownedItems.includes(item)
                  ? current.ownedItems.filter((owned) => owned !== item)
                  : [...current.ownedItems, item]
              }))}
              onBreed={() => plan.crops[0] && openBreedingWorkspace(plan.crops[0].cropId, draft.goal.genetics)}
              onRecipe={openRecipe}
              onAudit={auditRecommendation}
              onCopy={copyPlan}
              onExport={exportPlan}
            />
          ) : <AuditResults audit={audit} onBreed={() => openBreedingWorkspace(draft.audit.cropId, draft.audit.genetics)} />}
        </Box>
      </Box>
    </Box>
  );
};

const GoalForm: React.FC<{ value: GoalPlannerInput; onChange: (patch: Partial<GoalPlannerInput>) => void }> = ({ value, onChange }) => (
  <Stack spacing={2}>
    <Box>
      <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>What do you need?</Typography>
      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>The planner works backward from this target.</Typography>
    </Box>
    <FormControl size="small" fullWidth>
      <InputLabel id="goal-output-label">Output or recipe</InputLabel>
      <Select labelId="goal-output-label" label="Output or recipe" value={value.outputItem} onChange={(event) => onChange({ outputItem: event.target.value })}>
        {FARM_GOAL_OPTIONS.map((option) => <MenuItem key={option.item} value={option.item}>{option.item} · {option.category}</MenuItem>)}
      </Select>
    </FormControl>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 1.5 }}>
      <NumberField label="Quantity" value={value.quantity} min={0.01} onChange={(quantity) => onChange({ quantity: quantity ?? 0.01 })} />
      <FormControl size="small" fullWidth>
        <InputLabel id="goal-time-label">Time basis</InputLabel>
        <Select labelId="goal-time-label" label="Time basis" value={value.timeBasis} onChange={(event) => onChange({ timeBasis: event.target.value as GoalPlannerInput['timeBasis'] })}>
          <MenuItem value="harvest">Per harvest</MenuItem>
          <MenuItem value="hour">Per hour</MenuItem>
          <MenuItem value="session">Within session / deadline</MenuItem>
          <MenuItem value="day">Per day</MenuItem>
        </Select>
      </FormControl>
    </Box>
    {value.timeBasis === 'session' && <NumberField label="Hours available" value={value.horizonHours} min={0.1} step={0.5} onChange={(horizonHours) => onChange({ horizonHours: horizonHours ?? 0.1 })} />}
    <GeneticsField value={value.genetics} onChange={(genetics) => onChange({ genetics })} />
    <FormControl size="small" fullWidth>
      <InputLabel id="goal-planter-label">Planter type</InputLabel>
      <Select labelId="goal-planter-label" label="Planter type" value={value.planterType} onChange={(event) => onChange({ planterType: event.target.value as GoalPlannerInput['planterType'] })}>
        {Object.entries(PLANTER_TYPES).map(([id, planter]) => <MenuItem key={id} value={id}>{planter.name} · {planter.slots} slots</MenuItem>)}
      </Select>
    </FormControl>
    <SharedAssumptionFields value={value} onChange={onChange} />
  </Stack>
);

const AuditForm: React.FC<{ value: AuditPlannerInput; onChange: (patch: Partial<AuditPlannerInput>) => void }> = ({ value, onChange }) => (
  <Stack spacing={2}>
    <Box>
      <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>Describe the current farm</Typography>
      <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>Use measured supply where possible to expose the real bottleneck.</Typography>
    </Box>
    <FormControl size="small" fullWidth>
      <InputLabel id="audit-crop-label">Crop</InputLabel>
      <Select labelId="audit-crop-label" label="Crop" value={value.cropId} onChange={(event) => onChange({ cropId: event.target.value })}>
        {Object.values(FARM_CROPS).map((crop) => <MenuItem key={crop.id} value={crop.id}>{crop.name}</MenuItem>)}
      </Select>
    </FormControl>
    <GeneticsField value={value.genetics} onChange={(genetics) => onChange({ genetics })} />
    <FormControl size="small" fullWidth>
      <InputLabel id="audit-planter-label">Planter type</InputLabel>
      <Select labelId="audit-planter-label" label="Planter type" value={value.planterType} onChange={(event) => {
        const planterType = event.target.value as AuditPlannerInput['planterType'];
        onChange({ planterType, filledSlotsPerPlanter: PLANTER_TYPES[planterType].slots });
      }}>
        {Object.entries(PLANTER_TYPES).map(([id, planter]) => <MenuItem key={id} value={id}>{planter.name} · {planter.slots} slots</MenuItem>)}
      </Select>
    </FormControl>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 1.5 }}>
      <NumberField label="Planter count" value={value.planterCount} min={1} onChange={(planterCount) => onChange({ planterCount: planterCount ?? 1 })} />
      <NumberField label="Filled slots / planter" value={value.filledSlotsPerPlanter} min={1} max={PLANTER_TYPES[value.planterType].slots} onChange={(filledSlotsPerPlanter) => onChange({ filledSlotsPerPlanter: filledSlotsPerPlanter ?? 1 })} />
      <NumberField label="Available water · ml/min" value={value.availableWaterMlPerMinute} min={0} onChange={(availableWaterMlPerMinute) => onChange({ availableWaterMlPerMinute: availableWaterMlPerMinute ?? 0 })} />
      <NumberField label="Available power · rW" value={value.availablePowerRw} min={0} onChange={(availablePowerRw) => onChange({ availablePowerRw: availablePowerRw ?? 0 })} />
    </Box>
    <SharedAssumptionFields value={value} onChange={onChange} />
  </Stack>
);

const SharedAssumptionFields: React.FC<{ value: SharedAssumptions; onChange: (patch: Partial<SharedAssumptions>) => void }> = ({ value, onChange }) => (
  <>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 1.5 }}>
      <FormControl size="small" fullWidth>
        <InputLabel id="water-source-label">Water source</InputLabel>
        <Select labelId="water-source-label" label="Water source" value={value.waterSource} onChange={(event) => onChange({ waterSource: event.target.value as SharedAssumptions['waterSource'] })}>
          <MenuItem value="fresh-pump">Fresh water pump</MenuItem>
          <MenuItem value="salt-purifier">Salt water + purifier</MenuItem>
          <MenuItem value="catcher-barrel">Catchers + barrels</MenuItem>
          <MenuItem value="road-outlet">Road water outlet</MenuItem>
          <MenuItem value="custom">Custom / manual</MenuItem>
        </Select>
      </FormControl>
      <FormControl size="small" fullWidth>
        <InputLabel id="power-source-label">Power source</InputLabel>
        <Select labelId="power-source-label" label="Power source" value={value.powerSource} onChange={(event) => onChange({ powerSource: event.target.value as SharedAssumptions['powerSource'] })}>
          <MenuItem value="solar-battery">Solar + battery</MenuItem>
          <MenuItem value="wind-battery">Wind + battery</MenuItem>
          <MenuItem value="grid">Powerline / grid</MenuItem>
          <MenuItem value="existing">Existing circuit</MenuItem>
        </Select>
      </FormControl>
    </Box>
    <FormControlLabel control={<Switch checked={value.reserveClones} onChange={(event) => onChange({ reserveClones: event.target.checked })} />} label={<Typography variant="body2" sx={{ color: 'var(--gl-text-primary)' }}>Reserve plants for replacement clones</Typography>} />
    <Accordion disableGutters sx={{ ...panelSx, boxShadow: 'none', '&:before': { display: 'none' } }}>
      <AccordionSummary expandIcon={<ExpandMoreIcon />}><Typography variant="body2" sx={{ fontWeight: 800 }}>Advanced conditions & calibration</Typography></AccordionSummary>
      <AccordionDetails>
        <Stack spacing={1.5}>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2, minmax(0, 1fr))', gap: 1.25 }}>
            {(Object.keys(value.conditions) as Array<keyof FarmConditions>).map((condition) => (
              <NumberField key={condition} label={`${condition[0].toUpperCase()}${condition.slice(1)} · %`} value={value.conditions[condition]} min={0} max={100} onChange={(next) => onChange({ conditions: { ...value.conditions, [condition]: next ?? 0 } })} />
            ))}
            <NumberField label="Server rate · x" value={value.serverRate} min={0.01} step={0.1} onChange={(serverRate) => onChange({ serverRate: serverRate ?? 1 })} />
            <NumberField label="Safety margin · %" value={value.safetyMarginPercent} min={0} onChange={(safetyMarginPercent) => onChange({ safetyMarginPercent: safetyMarginPercent ?? 0 })} />
            <NumberField label="Water buffer · hours" value={value.bufferHours} min={0} step={0.5} onChange={(bufferHours) => onChange({ bufferHours: bufferHours ?? 0 })} />
            <NumberField optional label="Measured yield / plant" value={value.measuredYieldPerPlant} min={0.01} onChange={(measuredYieldPerPlant) => onChange({ measuredYieldPerPlant })} />
            <NumberField optional label="Measured cycle · min" value={value.measuredCycleMinutes} min={0.01} onChange={(measuredCycleMinutes) => onChange({ measuredCycleMinutes })} />
          </Box>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>Measured values replace community crop estimates for this saved plan.</Typography>
        </Stack>
      </AccordionDetails>
    </Accordion>
  </>
);

const GoalResults: React.FC<{
  plan: FarmPlan;
  isRecipe: boolean;
  ownedCloneCount: number;
  ownedItems: string[];
  onToggleOwned: (item: string) => void;
  onBreed: () => void;
  onRecipe: () => void;
  onAudit: () => void;
  onCopy: () => void;
  onExport: () => void;
}> = ({ plan, isRecipe, ownedCloneCount, ownedItems, onToggleOwned, onBreed, onRecipe, onAudit, onCopy, onExport }) => {
  if (!plan.supported) return <Alert severity="warning">{plan.warnings[0]}</Alert>;
  const period = timeBasisLabel(plan.goal.timeBasis, plan.goal.horizonHours);
  const planterName = PLANTER_TYPES[plan.goal.planterType].name.toLowerCase();
  const layout = plan.infrastructure.layout;

  return (
    <Stack spacing={2}>
      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2.5 }, borderLeft: '4px solid var(--gl-success)' }}>
        <Typography variant="overline" sx={{ color: 'var(--gl-success)', fontWeight: 900 }}>Recommended plan</Typography>
        <Typography component="h2" variant="h6" sx={{ color: 'var(--gl-text-primary)', fontWeight: 850, mt: 0.25 }}>Build {plan.totalPlanters} {planterName}{plan.totalPlanters === 1 ? '' : 's'} using {plan.goal.genetics}.</Typography>
        <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', mt: 0.75 }}>Expect about {formatNumber(plan.goalOutputForPeriod)} {plan.goal.outputItem} {period}, after reserving {plan.totalCloneReservePlants} plant{plan.totalCloneReservePlants === 1 ? '' : 's'} for clones. First harvest is approximately {formatDuration(plan.firstHarvestMinutes)}.</Typography>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', mt: 2 }}>
          <Chip size="small" color={ownedCloneCount > 0 ? 'success' : 'warning'} label={ownedCloneCount > 0 ? `${ownedCloneCount} matching clone${ownedCloneCount === 1 ? '' : 's'} owned` : 'Target clone not in inventory'} />
          <Button variant="contained" size="small" startIcon={<ScienceIcon />} onClick={onBreed}>Breed genetics</Button>
          {isRecipe && <Button variant="outlined" size="small" startIcon={<RestaurantIcon />} onClick={onRecipe}>Open recipe</Button>}
          {plan.crops.length === 1 && <Button variant="outlined" size="small" onClick={onAudit}>Audit this plan</Button>}
        </Box>
      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' }, gap: 1 }}>
        <Metric label="Planters" value={formatNumber(plan.totalPlanters)} detail={`${plan.totalSlots} occupied slots`} />
        <Metric label="First harvest" value={formatDuration(plan.firstHarvestMinutes)} detail={`${plan.crops.length} crop group${plan.crops.length === 1 ? '' : 's'}`} />
        <Metric label="Water flow" value={`${formatNumber(plan.infrastructure.recommendedWaterFlowMlPerMinute)} ml/min`} detail="includes safety margin" />
        <Metric label="Farm power" value={`${formatNumber(plan.infrastructure.powerWithSafetyMarginRw)} rW`} detail="includes safety margin" />
      </Box>

      {plan.warnings.length > 0 && <Alert severity="warning"><Stack spacing={0.25}>{plan.warnings.map((warning) => <span key={warning}>{warning}</span>)}</Stack></Alert>}

      <ResultSection title="Production" defaultExpanded>
        <Stack divider={<Divider flexItem />}>
          {plan.crops.map((crop) => (
            <Box key={crop.cropId} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr auto', sm: 'minmax(130px, 1fr) repeat(4, minmax(88px, auto))' }, gap: 1, py: 1, alignItems: 'center' }}>
              <Box>
                <Typography variant="body2" sx={{ color: 'var(--gl-text-primary)', fontWeight: 800 }}>{crop.cropName}</Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>{formatNumber(crop.requiredPerGoalUnit)} required · {crop.limitingCondition} limiting at {crop.effectiveConditionPercent}%</Typography>
              </Box>
              <Stat label="Planters" value={crop.planterCount} />
              <Stat label="Harvest plants" value={crop.harvestPlants} />
              <Stat label="Per harvest" value={formatNumber(crop.harvestPerCycle)} />
              <Stat label="Per hour" value={formatNumber(crop.productionPerHour)} />
            </Box>
          ))}
        </Stack>
        {plan.nonFarmIngredients.length > 0 && <Box sx={{ mt: 1.5 }}><Typography variant="caption" sx={{ color: 'var(--gl-warning)', fontWeight: 800 }}>Also collect:</Typography> {plan.nonFarmIngredients.map((ingredient) => <Chip key={ingredient.item} size="small" label={`${ingredient.item} × ${formatNumber(ingredient.quantity)}`} sx={{ ml: 0.75, mb: 0.5 }} />)}</Box>}
      </ResultSection>

      <ResultSection title="Water & power" icon={<WaterDropIcon fontSize="small" />} defaultExpanded>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
          <SystemCard icon={<WaterDropIcon />} title="Water" lines={[`${formatNumber(plan.infrastructure.waterDemandMlPerMinute)} ml/min crop demand`, `${formatNumber(plan.infrastructure.recommendedWaterFlowMlPerMinute)} ml/min recommended`, `${formatNumber(plan.infrastructure.storedWaterLiters)} L for the selected buffer`]} />
          <SystemCard icon={<BoltIcon />} title="Power" lines={[`${plan.infrastructure.powerDrawRw} rW modeled draw`, `${plan.infrastructure.powerWithSafetyMarginRw} rW with margin`, plan.goal.powerSource === 'solar-battery' ? `${plan.infrastructure.solarPanelsSuggested} solar + ${plan.infrastructure.largeBatteries} large battery` : `${plan.infrastructure.largeBatteries} large batter${plan.infrastructure.largeBatteries === 1 ? 'y' : 'ies'}`]} />
        </Box>
      </ResultSection>

      <ResultSection title="Build checklist" icon={<BuildIcon fontSize="small" />} defaultExpanded>
        <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', mb: 1.5 }}>Layout: {layout.sixPlanterModules} × six-planter modules, {layout.threePlanterModules} × three-planter modules, {layout.remainderPlanters} remainder. Verify light and sprinkler coverage in game.</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
          <Checklist title="Components" rows={plan.infrastructure.components.map((item) => ({ key: `component:${item.item}`, label: item.item, value: item.quantity }))} ownedItems={ownedItems} onToggle={onToggleOwned} />
          <Checklist title="Estimated crafting materials" rows={plan.infrastructure.materials.map((item) => ({ key: `material:${item.item}`, label: item.item, value: item.quantity }))} ownedItems={ownedItems} onToggle={onToggleOwned} />
        </Box>
        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, mt: 2 }}>
          <Button size="small" variant="outlined" startIcon={<ContentCopyIcon />} onClick={onCopy}>Copy checklist</Button>
          <Button size="small" variant="outlined" startIcon={<DownloadIcon />} onClick={onExport}>Export image</Button>
          <Button size="small" variant="outlined" startIcon={<PrintIcon />} onClick={() => window.print()}>Print</Button>
        </Box>
      </ResultSection>

      <ResultSection title="Schedule & assumptions">
        <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>Plant or replant {plan.totalSlots} slots, keep {plan.totalCloneReservePlants} plants for clone supply, and return in about {formatDuration(plan.firstHarvestMinutes)}. This plan uses {plan.confidence === 'user' ? 'your measured calibration' : 'community crop-rate estimates'} and current official component power ratings. Model reviewed {FARM_MODEL_REVIEWED_ON}.</Typography>
        <Alert severity="info" sx={{ mt: 1.5 }}>Placement, elevation, line of sight, monthly patches, and modded server settings can change real output. Calibrate yield and cycle time after one observed harvest.</Alert>
      </ResultSection>
    </Stack>
  );
};

const AuditResults: React.FC<{ audit: ReturnType<typeof auditFarmSetup>; onBreed: () => void }> = ({ audit, onBreed }) => {
  if (!audit.supported || !audit.crop || !audit.infrastructure) return <Alert severity="warning">{audit.headline}</Alert>;
  const severity = audit.status === 'sustainable' ? 'success' : audit.status === 'at-risk' ? 'warning' : 'error';
  return (
    <Stack spacing={2}>
      <Alert severity={severity}>
        <Typography variant="subtitle2" sx={{ fontWeight: 900, textTransform: 'uppercase' }}>{audit.status} · {audit.bottleneck === 'none' ? 'No modeled bottleneck' : `${audit.bottleneck} bottleneck`}</Typography>
        <Typography variant="body2">{audit.headline}</Typography>
      </Alert>
      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2.5 } }}>
        <Typography component="h2" variant="h6" sx={{ color: 'var(--gl-text-primary)', fontWeight: 850 }}>Best next change</Typography>
        <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', mt: 0.75 }}>{audit.recommendation}</Typography>
      </Paper>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', md: 'repeat(4, minmax(0, 1fr))' }, gap: 1 }}>
        <Metric label="Current output" value={`${formatNumber(audit.productionPerHour)}/hr`} detail={audit.crop.outputItem} />
        <Metric label="At ideal conditions" value={`${formatNumber(audit.potentialProductionPerHour)}/hr`} detail={`${audit.crop.limitingCondition} is ${audit.crop.effectiveConditionPercent}%`} />
        <Metric label="Water margin" value={`${signed(audit.waterMarginMlPerMinute)} ml/min`} detail={`${audit.infrastructure.recommendedWaterFlowMlPerMinute} required`} tone={audit.waterMarginMlPerMinute < 0 ? 'danger' : 'success'} />
        <Metric label="Power margin" value={`${signed(audit.powerMarginRw)} rW`} detail={`${audit.infrastructure.powerWithSafetyMarginRw} required`} tone={audit.powerMarginRw < 0 ? 'danger' : 'success'} />
      </Box>
      <ResultSection title="Required infrastructure" defaultExpanded>
        <Checklist title="Modeled setup" rows={audit.infrastructure.components.map((item) => ({ key: item.item, label: item.item, value: item.quantity }))} ownedItems={[]} onToggle={() => undefined} readOnly />
      </ResultSection>
      <Button variant="contained" startIcon={<ScienceIcon />} onClick={onBreed} sx={{ alignSelf: 'flex-start' }}>Improve genetics</Button>
    </Stack>
  );
};

const GeneticsField: React.FC<{ value: string; onChange: (value: string) => void }> = ({ value, onChange }) => (
  <Box>
    <TextField label="Genetics" size="small" fullWidth value={value} error={value.length !== 6} helperText={value.length === 6 ? 'G growth · Y yield · H hardiness · W water · X negative' : 'Enter exactly six genes.'} onChange={(event) => onChange(normalizeGenetics(event.target.value))} slotProps={{ htmlInput: { maxLength: 6, spellCheck: false, style: { fontFamily: 'monospace', fontWeight: 800, letterSpacing: '0.25em' } } }} />
    {value.length === 6 && <Box sx={{ mt: 1, display: 'flex', justifyContent: 'center' }}><GeneticsSequence genes={value} size="small" /></Box>}
  </Box>
);

const NumberField: React.FC<{ label: string; value?: number; min?: number; max?: number; step?: number; optional?: boolean; onChange: (value: number | undefined) => void }> = ({ label, value, min, max, step = 1, optional, onChange }) => (
  <TextField label={label} size="small" type="number" fullWidth value={value ?? ''} onChange={(event) => onChange(event.target.value === '' && optional ? undefined : Number(event.target.value))} slotProps={{ htmlInput: { min, max, step, inputMode: 'decimal' } }} />
);

const Metric: React.FC<{ label: string; value: React.ReactNode; detail: React.ReactNode; tone?: 'success' | 'danger' }> = ({ label, value, detail, tone }) => (
  <Paper variant="outlined" sx={{ ...panelSx, p: 1.5, minWidth: 0 }}>
    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 800, textTransform: 'uppercase' }}>{label}</Typography>
    <Typography variant="subtitle1" sx={{ ...numberSx, color: tone === 'danger' ? 'var(--gl-danger)' : tone === 'success' ? 'var(--gl-success)' : 'var(--gl-text-primary)', fontWeight: 900, overflowWrap: 'anywhere' }}>{value}</Typography>
    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>{detail}</Typography>
  </Paper>
);

const Stat: React.FC<{ label: string; value: React.ReactNode }> = ({ label, value }) => (
  <Box sx={{ textAlign: 'right' }}><Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block' }}>{label}</Typography><Typography variant="body2" sx={{ ...numberSx, color: 'var(--gl-text-primary)', fontWeight: 800 }}>{value}</Typography></Box>
);

const ResultSection: React.FC<{ title: string; icon?: React.ReactNode; defaultExpanded?: boolean; children: React.ReactNode }> = ({ title, icon, defaultExpanded, children }) => (
  <Accordion defaultExpanded={defaultExpanded} disableGutters sx={{ ...panelSx, boxShadow: 'none', '&:before': { display: 'none' } }}>
    <AccordionSummary expandIcon={<ExpandMoreIcon />}><Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'var(--gl-primary)' }}>{icon}<Typography variant="subtitle2" sx={{ color: 'var(--gl-text-primary)', fontWeight: 900 }}>{title}</Typography></Box></AccordionSummary>
    <AccordionDetails>{children}</AccordionDetails>
  </Accordion>
);

const SystemCard: React.FC<{ icon: React.ReactNode; title: string; lines: string[] }> = ({ icon, title, lines }) => (
  <Box sx={{ p: 1.5, border: '1px solid var(--gl-border)', borderRadius: 1 }}>
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, color: 'var(--gl-primary)', mb: 1 }}>{icon}<Typography variant="subtitle2" sx={{ color: 'var(--gl-text-primary)', fontWeight: 800 }}>{title}</Typography></Box>
    {lines.map((line) => <Typography key={line} variant="body2" sx={{ ...numberSx, color: 'var(--gl-text-secondary)', mb: 0.5 }}>{line}</Typography>)}
  </Box>
);

const Checklist: React.FC<{ title: string; rows: Array<{ key: string; label: string; value: number }>; ownedItems: string[]; onToggle: (key: string) => void; readOnly?: boolean }> = ({ title, rows, ownedItems, onToggle, readOnly }) => (
  <Box>
    <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 900, textTransform: 'uppercase' }}>{title}</Typography>
    <Stack spacing={0.25} sx={{ mt: 0.75 }}>
      {rows.map((row) => (
        <Box key={row.key} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', minHeight: 32, borderBottom: '1px solid var(--gl-border)' }}>
          {readOnly ? <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>{row.label}</Typography> : <FormControlLabel control={<Checkbox size="small" checked={ownedItems.includes(row.key)} onChange={() => onToggle(row.key)} />} label={<Typography variant="body2" sx={{ color: ownedItems.includes(row.key) ? 'var(--gl-text-muted)' : 'var(--gl-text-secondary)', textDecoration: ownedItems.includes(row.key) ? 'line-through' : 'none' }}>{row.label}</Typography>} />}
          <Typography variant="body2" sx={{ ...numberSx, color: 'var(--gl-text-primary)', fontWeight: 800 }}>{formatNumber(row.value)}</Typography>
        </Box>
      ))}
    </Stack>
  </Box>
);

const formatNumber = (value: number): string => value.toLocaleString(undefined, { maximumFractionDigits: 1 });
const signed = (value: number): string => `${value >= 0 ? '+' : ''}${formatNumber(value)}`;
