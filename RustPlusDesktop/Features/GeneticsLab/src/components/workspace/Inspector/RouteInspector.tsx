import React, { useEffect, useMemo, useState } from 'react';
import {
  Box,
  Button,
  ButtonBase,
  FormControl,
  IconButton,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Tab,
  Tabs,
  Tooltip,
  Typography
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DownloadIcon from '@mui/icons-material/Download';
import AccountTreeIcon from '@mui/icons-material/AccountTree';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import PlayCircleIcon from '@mui/icons-material/PlayCircle';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useNotification } from '../../../context/NotificationContext.tsx';
import { buildBreedingPlan, BreedingPlanStep } from '../../../domain/genetics/breedingPlan.ts';
import { GREEN_GENES } from '../../../domain/genetics/Gene.ts';
import type { GeneticsMap } from '../../../domain/genetics/GeneticsMap.ts';
import type { GeneticsMapGroup } from '../../../domain/genetics/GeneticsMapGroup.ts';
import type { Sapling } from '../../../domain/genetics/Sapling.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { generationVisual } from '../../../utils/generationStyle.ts';
import { buildPlanterSvg } from '../../../utils/planterExport.ts';
import { GeneExplanation } from './GeneExplanation.tsx';

type InspectorTab = 'steps' | 'why';

export const RouteInspector: React.FC<{ onClose?: () => void }> = ({ onClose }) => {
  const { results, selectedGroup, selectedMap, setSelectedGroup, selectedMapIndex, setSelectedMapIndex } = useCalculation();
  const { startBreedingSession } = useWorkspace();
  const { notifySuccess } = useNotification();
  const [activeTab, setActiveTab] = useState<InspectorTab>('steps');

  const targetGenes = selectedGroup?.resultSaplingGeneString ?? '';
  const routeChance = selectedMap ? Math.round(selectedMap.getChanceProduct() * 100) : 0;
  const planSteps = useMemo(
    () => selectedMap ? buildBreedingPlan(selectedMap) : [],
    [selectedMap]
  );
  const recipeOptions = useMemo(
    () => selectedGroup?.mapList.map(map => {
      return {
        routeChance: Math.round(map.getChanceProduct() * 100),
        runs: buildBreedingPlan(map).length
      };
    }) ?? [],
    [selectedGroup]
  );

  const generationCount = selectedMap?.resultSapling.generationIndex || 1;
  const selectedRecipeIndex = selectedGroup?.mapList[selectedMapIndex] ? selectedMapIndex : 0;

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
          textAlign: 'center'
        }}
      >
        <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'var(--gl-text-muted)', mb: 1 }}>
          No Route Selected
        </Typography>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', maxWidth: 240 }}>
          Select a route to see its source readiness and ordered breeding runs.
        </Typography>
      </Paper>
    );
  }

  const firstStep = planSteps[0];

  const handleCopyInstructions = () => {
    const lines = [
      `=== Rust Breeding Plan: ${targetGenes} ===`,
      `Route chance: ${routeChance}%`,
      `Generation depth: ${generationCount}`,
      `Breeding runs: ${planSteps.length}`
    ];

    planSteps.forEach((step, index) => {
      lines.push('', `Run ${index + 1}/${planSteps.length} · GEN.${step.generationIndex} · ${Math.round(step.chance * 100)}% run chance`);
      lines.push(`  Result: [${step.targetGeneString}]`);
      lines.push(
        step.centerSaplingString
          ? `  Center first: ${sourceLabel(step.centerSourceIndex)} [${step.centerSaplingString}]`
          : '  Center first: any compatible receiver plant'
      );
      step.surroundingSaplingsStrings.forEach((genes, surroundingIndex) => {
        lines.push(`  Surrounding ${surroundingIndex + 1}: ${sourceLabel(step.surroundingSourceIndexes[surroundingIndex])} [${genes}]`);
      });
    });

    navigator.clipboard.writeText(lines.join('\n'));
    notifySuccess('Complete breeding plan copied to clipboard!');
  };

  const handleExportFinalPlanter = () => {
    const svg = buildPlanterSvg({
      target: targetGenes,
      center: selectedMap.baseSapling?.toString(),
      surrounding: selectedMap.crossbreedingSaplings.map(plant => plant.toString())
    });
    const url = URL.createObjectURL(new Blob([svg], { type: 'image/svg+xml' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = `rust-planter-${targetGenes}.svg`;
    link.click();
    URL.revokeObjectURL(url);
    notifySuccess('Final planter image exported.');
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
        gap: 1.25,
        overflow: 'hidden'
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <Typography
          variant="subtitle2"
          sx={{ fontWeight: 800, fontFamily: '"Roboto Mono", monospace', fontSize: '0.85rem', letterSpacing: '0.5px' }}
        >
          ROUTE INSPECTOR
        </Typography>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
          <Tooltip title="Copy the complete breeding plan" arrow>
            <IconButton aria-label="Copy complete breeding plan" size="small" onClick={handleCopyInstructions} sx={headerActionSx}>
              <ContentCopyIcon sx={{ fontSize: 16 }} />
            </IconButton>
          </Tooltip>
          <Tooltip title="Export the final planter image" arrow>
            <IconButton aria-label="Export final planter image" size="small" onClick={handleExportFinalPlanter} sx={headerActionSx}>
              <DownloadIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Tooltip>
          <IconButton
            aria-label="Close route inspector"
            size="small"
            onClick={() => (onClose ? onClose() : setSelectedGroup(null))}
            sx={headerActionSx}
          >
            <CloseIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </Box>
      </Box>

      <Box
        sx={{
          flex: 1,
          minHeight: 0,
          overflowY: 'auto',
          pr: 0.5,
          display: 'flex',
          flexDirection: 'column',
          gap: 1.25,
          '& > *': { flexShrink: 0 },
          '&::-webkit-scrollbar': { width: 5 },
          '&::-webkit-scrollbar-thumb': { backgroundColor: 'var(--gl-surface-hover)', borderRadius: 3 }
        }}
      >
        <RouteResultOverview map={selectedMap} results={results} />

        {recipeOptions.length > 1 && (
          <FormControl size="small" fullWidth>
            <InputLabel id="route-recipe-label">Recipe</InputLabel>
            <Select
              labelId="route-recipe-label"
              value={selectedRecipeIndex}
              label="Recipe"
              onChange={event => setSelectedMapIndex(Number(event.target.value))}
              sx={{ fontSize: '0.78rem', backgroundColor: 'var(--gl-card-bg)' }}
            >
              {recipeOptions.map((option, index) => (
                <MenuItem key={index} value={index} sx={{ fontSize: '0.8rem' }}>
                  Recipe {index + 1} · {option.routeChance}% · {option.runs} run{option.runs === 1 ? '' : 's'}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        )}

        {firstStep && (
          <Box sx={{ px: 1, py: 0.9, backgroundColor: 'var(--gl-tint-cyan)', borderLeft: '3px solid var(--gl-primary)', borderRadius: '3px' }}>
            <Typography sx={{ color: 'var(--gl-primary)', fontSize: '0.75rem', fontWeight: 800, mb: 0.2 }}>
              NEXT
            </Typography>
            <Typography sx={{ color: 'var(--gl-text-primary)', fontSize: '0.8rem' }}>
              {firstStep.centerSaplingString
                ? `Plant ${sourceLabel(firstStep.centerSourceIndex)} in the center, then add ${firstStep.surroundingSaplingsStrings.length} surrounding clone${firstStep.surroundingSaplingsStrings.length === 1 ? '' : 's'}.`
                : `Plant a compatible receiver in the center, then add ${firstStep.surroundingSaplingsStrings.length} surrounding clone${firstStep.surroundingSaplingsStrings.length === 1 ? '' : 's'}.`}
            </Typography>
          </Box>
        )}

        <Box sx={{ borderBottom: '1px solid var(--gl-border)' }}>
          <Tabs
            value={activeTab}
            onChange={(_, value: InspectorTab) => setActiveTab(value)}
            aria-label="Route inspector views"
            variant="fullWidth"
            sx={{ minHeight: 42, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)', height: 2 } }}
          >
            <Tab id="route-inspector-tab-steps" aria-controls="route-inspector-panel-steps" value="steps" label="Steps" sx={tabSx} />
            <Tab id="route-inspector-tab-why" aria-controls="route-inspector-panel-why" value="why" label="Why it works" sx={tabSx} />
          </Tabs>
        </Box>

        <Box
          id={`route-inspector-panel-${activeTab}`}
          role="tabpanel"
          aria-labelledby={`route-inspector-tab-${activeTab}`}
          tabIndex={0}
          sx={{ outline: 'none', '&:focus-visible': { boxShadow: '0 0 0 2px var(--gl-primary)', borderRadius: '4px' } }}
        >
          {activeTab === 'steps' && (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
              {planSteps.map((step, index) => (
                <PlanStepPreview key={`${step.targetGeneString}-${index}`} step={step} index={index} total={planSteps.length} />
              ))}
            </Box>
          )}
          {activeTab === 'why' && <GeneExplanation map={selectedMap} />}
        </Box>
      </Box>

      <Box sx={{ pt: 1, borderTop: '1px solid var(--gl-surface)', flexShrink: 0 }}>
        <Button
          variant="contained"
          fullWidth
          onClick={() => startBreedingSession(selectedMap, targetGenes)}
          startIcon={<PlayCircleIcon sx={{ fontSize: 17 }} />}
          sx={primaryActionSx}
        >
          START GUIDED BREEDING
        </Button>
      </Box>
    </Paper>
  );
};

const PlanStepPreview: React.FC<{ step: BreedingPlanStep; index: number; total: number }> = ({ step, index, total }) => (
  <Paper variant="outlined" sx={{ p: 1.25, backgroundColor: 'var(--gl-card-bg)', borderColor: 'var(--gl-border)', borderRadius: '5px' }}>
    <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, mb: 1.25 }}>
      <Box>
        <Typography sx={{ color: 'var(--gl-primary)', fontSize: '0.78rem', fontWeight: 800 }}>
          RUN {index + 1} OF {total} · GEN.{step.generationIndex}
        </Typography>
        <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem' }}>
          Produces [{step.targetGeneString}]
        </Typography>
      </Box>
      <Typography sx={{ color: 'var(--gl-success)', fontSize: '0.75rem', fontWeight: 800, fontFamily: 'monospace' }}>
        {Math.round(step.chance * 100)}% run chance
      </Typography>
    </Box>

    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: 'repeat(3, minmax(0, 1fr))',
        gap: 0.65,
        p: 0.75,
        backgroundColor: 'var(--gl-app-bg)',
        border: '1px solid var(--gl-surface)',
        borderRadius: '5px'
      }}
    >
      {PLANTER_CELLS.map((surroundingIndex, cellIndex) => {
        const isCenter = surroundingIndex === 'center';
        const genes = isCenter ? step.centerSaplingString : step.surroundingSaplingsStrings[surroundingIndex];
        const sourceIndex = isCenter ? step.centerSourceIndex : step.surroundingSourceIndexes[surroundingIndex];
        const priority = !isCenter && step.priorityWinningIndices?.includes(surroundingIndex)
          ? '1st batch'
          : !isCenter && step.priorityLosingIndices?.includes(surroundingIndex)
          ? '2nd batch'
          : '';

        if (!isCenter && !genes) {
          return <Box key={cellIndex} aria-hidden="true" sx={{ minHeight: 58, border: '1px solid var(--gl-input-bg)', borderRadius: '4px' }} />;
        }

        const label = isCenter
          ? genes ? `Center ${sourceLabel(sourceIndex)}` : 'Center receiver'
          : `Surrounding ${surroundingIndex + 1} ${sourceLabel(sourceIndex)}`;

        return (
          <Box
            key={cellIndex}
            aria-label={`${label}${genes ? `, genetics ${genes}` : ''}${priority ? `, ${priority}` : ''}`}
            sx={{
              minHeight: 58,
              p: 0.45,
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 0.3,
              backgroundColor: isCenter ? 'var(--gl-tint-cyan)' : 'var(--gl-panel-header-bg)',
              border: '1px solid',
              borderColor: isCenter ? 'var(--gl-primary)' : 'var(--gl-surface-hover)',
              borderRadius: '4px'
            }}
          >
            <Typography aria-hidden="true" sx={{ color: isCenter ? 'var(--gl-primary)' : 'var(--gl-text-muted)', fontSize: '0.68rem', fontWeight: 800 }}>
              {label}
            </Typography>
            {genes ? <CompactGenes genes={genes} /> : (
              <Typography aria-hidden="true" sx={{ color: 'var(--gl-text-secondary)', fontSize: '0.68rem', fontWeight: 700 }}>
                ANY COMPATIBLE
              </Typography>
            )}
            {priority && (
              <Typography aria-hidden="true" sx={{ color: priority.startsWith('1') ? 'var(--gl-success)' : 'var(--gl-warning)', fontSize: '0.65rem', fontWeight: 800 }}>
                {priority}
              </Typography>
            )}
          </Box>
        );
      })}
    </Box>

    <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem', mt: 1 }}>
      Plant the center first, add the surrounding clones, wait for Crossbreeding, then take cuttings from the center.
    </Typography>
  </Paper>
);

const RouteResultOverview: React.FC<{ map: GeneticsMap; results: GeneticsMapGroup[] }> = ({ map, results }) => {
  const [stack, setStack] = useState<GeneticsMap[]>([map]);

  useEffect(() => setStack([map]), [map]);

  const current = stack[stack.length - 1];
  const previous = stack[stack.length - 2];
  const currentGeneration = current.resultSapling.generationIndex || 1;
  const generation = generationVisual(currentGeneration);
  const score = Number.isInteger(current.score) ? current.score : Number(current.score.toFixed(1));
  const routeChance = Math.round(current.getChanceProduct() * 100);
  const openSubPlan = (subMap: GeneticsMap) => setStack(currentStack => [...currentStack, subMap]);
  const findFallbackSubPlan = (plant: Sapling) =>
    results.find(group => group.resultSaplingGeneString === plant.toString())?.mapList[0] ??
    results.flatMap(group => group.mapList).find(candidate => candidate.resultSapling.toString() === plant.toString());

  return (
    <Paper
      variant="outlined"
      sx={{ flexShrink: 0, overflow: 'hidden', backgroundColor: 'var(--gl-card-bg)', borderColor: 'var(--gl-surface-hover)', borderRadius: '5px' }}
    >
      {previous && (
        <ButtonBase
          onClick={() => setStack(currentStack => currentStack.slice(0, -1))}
          focusRipple
          aria-label={`Back to generation ${previous.resultSapling.generationIndex || 1} result`}
          sx={{ width: '100%', minHeight: 36, px: 1.5, justifyContent: 'flex-start', gap: 0.75, color: 'var(--gl-primary)', borderBottom: '1px solid var(--gl-surface)', fontFamily: 'monospace', fontSize: '0.72rem', fontWeight: 800, '&:hover': { backgroundColor: 'var(--gl-surface)' }, '&.Mui-focusVisible': { outline: '2px solid var(--gl-primary)', outlineOffset: -2 } }}
        >
          <ArrowBackIcon sx={{ fontSize: 16 }} />
          BACK TO GEN.{previous.resultSapling.generationIndex || 1} RESULT
        </ButtonBase>
      )}

      <Box sx={{ p: 1.5, backgroundColor: 'var(--gl-input-bg)', textAlign: 'center' }}>
        <GeneticsSequence genes={current.resultSapling.toString()} size="medium" showConnectors />
        <Typography
          component="div"
          sx={{ mt: 1.1, color: 'var(--gl-text-secondary)', fontFamily: 'monospace', fontSize: '0.8rem', fontWeight: 800 }}
        >
          <Box component="span" sx={{ color: generation.color }}>GEN.{currentGeneration}</Box>
          {' · Score: '}
          <Box component="span" sx={{ color: 'var(--gl-text-primary)' }}>{score}</Box>
          {' · Chance: '}
          <Box component="span" sx={{ color: 'var(--gl-success)' }}>{routeChance}%</Box>
        </Typography>
      </Box>

      <Box sx={{ p: 1.5, textAlign: 'center', borderTop: '1px solid var(--gl-surface)' }}>
        <Typography sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.78rem', fontWeight: 700, mb: 0.75 }}>
          Center Plant:
        </Typography>
        {current.baseSapling ? (
          <OverviewPlantRow
            plant={current.baseSapling}
            subMap={current.baseSapling.generationIndex > 0 ? current.baseSaplingVariants?.mapList[0] ?? findFallbackSubPlan(current.baseSapling) : undefined}
            onOpenSubPlan={openSubPlan}
          />
        ) : (
          <Typography sx={{ color: 'var(--gl-text-primary)', fontFamily: 'monospace', fontSize: '0.8rem', fontWeight: 800 }}>
            any extra plant of same type
          </Typography>
        )}
      </Box>

      <Box sx={{ p: 1.5, borderTop: '1px solid var(--gl-surface)' }}>
        <Typography sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.8rem', fontWeight: 700, textAlign: 'center', mb: 1 }}>
          Surrounding Plants:
        </Typography>
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0.65 }}>
          {current.crossbreedingSaplings.map((plant, index) => (
            <OverviewPlantRow
              key={index}
              plant={plant}
              subMap={plant.generationIndex > 0 ? current.crossbreedingSaplingsVariants?.[index]?.mapList[0] ?? findFallbackSubPlan(plant) : undefined}
              onOpenSubPlan={openSubPlan}
            />
          ))}
        </Box>
      </Box>
    </Paper>
  );
};

const OverviewPlantRow: React.FC<{
  plant: Sapling;
  subMap?: GeneticsMap;
  onOpenSubPlan?: (subMap: GeneticsMap) => void;
}> = ({ plant, subMap, onOpenSubPlan }) => {
  const canOpen = Boolean(subMap && onOpenSubPlan);
  const content = (
    <>
      <Typography sx={{ width: 54, flexShrink: 0, color: canOpen ? 'var(--gl-warning)' : 'var(--gl-text-muted)', fontFamily: 'monospace', fontSize: '0.72rem', textAlign: 'right', pr: 1, fontWeight: canOpen ? 800 : 400 }}>
        {plantLabel(plant.index, plant.generationIndex)}
      </Typography>
      <GeneticsSequence genes={plant.toString()} size="small" showConnectors />
      {canOpen && <AccountTreeIcon aria-hidden="true" sx={{ ml: 0.75, fontSize: 15, color: 'var(--gl-primary)' }} />}
    </>
  );

  const rowSx = { display: 'flex', alignItems: 'center', width: '100%', maxWidth: 280, minHeight: 28, borderRadius: '3px' };
  return canOpen ? (
    <ButtonBase
      onClick={() => subMap && onOpenSubPlan?.(subMap)}
      aria-label={`Open ${plantLabel(plant.index, plant.generationIndex)} sub-plan`}
      focusRipple
      sx={{ ...rowSx, border: '1px solid var(--gl-primary)', cursor: 'pointer', '& *': { cursor: 'pointer' }, '&:hover': { backgroundColor: 'var(--gl-tint-cyan)' }, '&.Mui-focusVisible': { outline: '2px solid var(--gl-primary)', outlineOffset: 2 } }}
    >
      {content}
    </ButtonBase>
  ) : (
    <Box sx={rowSx}>{content}</Box>
  );
};

const CompactGenes: React.FC<{ genes: string }> = ({ genes }) => (
  <Box aria-hidden="true" sx={{ display: 'flex', gap: '1px' }}>
    {genes.split('').map((gene, index) => (
      <Box
        key={index}
        sx={{
          width: 15,
          height: 15,
          borderRadius: '50%',
          backgroundColor: (GREEN_GENES as readonly string[]).includes(gene) ? '#4A7C17' : '#8A2E22',
          color: '#FFF',
          fontSize: '0.6rem',
          fontWeight: 800,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center'
        }}
      >
        {gene}
      </Box>
    ))}
  </Box>
);

const sourceLabel = (index?: number) => index === undefined ? 'generated clone' : `#${index + 1}`;
const plantLabel = (index: number | undefined, generationIndex: number) =>
  generationIndex > 0 ? `GEN.${generationIndex}` : index === undefined ? 'SOURCE' : `#${index + 1}`;

const PLANTER_CELLS = [0, 1, 2, 3, 'center', 4, 5, 6, 7] as const;

const headerActionSx = { minWidth: 36, minHeight: 36, color: 'var(--gl-text-muted)' };
const tabSx = { minHeight: 42, py: 0.25, px: 0.75, minWidth: 0, fontSize: '0.75rem', fontWeight: 800 };
const primaryActionSx = {
  minHeight: 40,
  fontWeight: 800,
  backgroundColor: 'var(--gl-warning)',
  color: 'var(--gl-on-accent)',
  fontSize: '0.78rem',
  '&:hover': { backgroundColor: 'var(--gl-warning)' }
};
