import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from 'react';
import {
  CrossbreedingOrchestrator,
  MAX_RETURNED_RESULTS,
  SimulatorEvent
} from '../services/orchestrator.ts';
import { StorageService, ExtendedApplicationOptions, DEFAULT_OPTIONS } from '../services/storageService.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { ProgressState } from './AppContext.tsx';
import { useWorkspace } from './WorkspaceContext.tsx';
import { analyzeRoute, RouteAnalysis, RouteSortOption, compareScoredRoutes } from '../domain/genetics/routeScoring.ts';
import {
  targetHasConstraint,
  isExactMatch,
  meetsAtLeast
} from '../utils/targetMatch.ts';
import { useNotification } from './NotificationContext.tsx';

export type { RouteSortOption };

export interface ScoredRoute {
  group: GeneticsMapGroup;
  bestMap: GeneticsMap;
  analysis: RouteAnalysis;
  /** Other routes merged into this one because they are of equal quality. */
  equivalents?: ScoredRoute[];
}

/**
 * Quality signature used to collapse near-identical routes. Two routes with the
 * same score, chance, generations, clone count, plant count and inventory status
 * are treated as equivalent — only one representative card is shown.
 */
const routeSignature = (r: ScoredRoute): string => {
  const a = r.analysis;
  return [
    r.bestMap.score,
    Math.round(a.probabilityPercent),
    a.generationCount,
    a.uniqueCloneCount,
    a.totalPlacementsCount,
    a.inventoryStatus
  ].join('|');
};

interface CalculationContextType {
  isCalculating: boolean;
  progress: ProgressState | null;
  results: GeneticsMapGroup[];
  scoredRoutes: ScoredRoute[];
  filteredAndSortedRoutes: ScoredRoute[];
  /** Total matching routes before equivalent-quality grouping is applied. */
  rawRouteCount: number;
  resultsCapped: boolean;
  calculationStatusMessage: string;

  // Sorting & Filtering
  sortBy: RouteSortOption;
  setSortBy: (sort: RouteSortOption) => void;
  groupSimilar: boolean;
  setGroupSimilar: (on: boolean) => void;
  inventoryFilterMode: 'all' | 'available-only' | 'partial-or-better';
  setInventoryFilterMode: (mode: 'all' | 'available-only' | 'partial-or-better') => void;

  // Selection & Inspector
  selectedGroup: GeneticsMapGroup | null;
  setSelectedGroup: (group: GeneticsMapGroup | null) => void;
  selectedMapIndex: number;
  setSelectedMapIndex: (idx: number) => void;
  selectedMap: GeneticsMap | null;
  /** User-intent flag: the inspector was explicitly opened (e.g. Inspect click).
      Auto-selection of a route does NOT set this, so on small screens the modal
      inspector drawer only opens when the user asks for it. */
  isInspectorOpen: boolean;
  setIsInspectorOpen: (open: boolean) => void;
  highlightedGroup: GeneticsMapGroup | null;
  setHighlightedGroup: (group: GeneticsMapGroup | null) => void;

  // Comparison
  comparedGroups: GeneticsMapGroup[];
  toggleCompareGroup: (group: GeneticsMapGroup) => void;
  clearComparedGroups: () => void;
  isCompareModalOpen: boolean;
  setIsCompareModalOpen: (open: boolean) => void;

  // Solver controls
  options: ExtendedApplicationOptions;
  updateOptions: (opts: Partial<ExtendedApplicationOptions>) => void;
  setCalculationPreset: (preset: 'fast' | 'balanced' | 'thorough') => void;
  runSimulation: () => Promise<void>;
  cancelSimulation: () => void;
  skipCurrentGeneration: () => void;
}

const CalculationContext = createContext<CalculationContextType | null>(null);

export const CalculationProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { sourceSaplings, clones, targetConfig, geneInputText, saveCurrentGeneSet } = useWorkspace();
  const { notifyInfo, notifyWarning } = useNotification();

  const [options, setOptionsState] = useState<ExtendedApplicationOptions>(() => StorageService.getOptions());
  const [results, setResults] = useState<GeneticsMapGroup[]>([]);
  const [progress, setProgress] = useState<ProgressState | null>(null);
  const [isCalculating, setIsCalculating] = useState(false);
  const [calculationStatusMessage, setCalculationStatusMessage] = useState('');
  const announcedGenerationRef = useRef(1);

  const [sortBy, setSortBy] = useState<RouteSortOption>('recommended');
  const [inventoryFilterMode, setInventoryFilterMode] = useState<'all' | 'available-only' | 'partial-or-better'>('all');
  const [groupSimilar, setGroupSimilar] = useState<boolean>(true);

  const [selectedGroup, setSelectedGroup] = useState<GeneticsMapGroup | null>(null);
  const [selectedMapIndex, setSelectedMapIndex] = useState<number>(0);
  const [isInspectorOpen, setIsInspectorOpen] = useState<boolean>(false);
  const [highlightedGroup, setHighlightedGroup] = useState<GeneticsMapGroup | null>(null);

  const [comparedGroups, setComparedGroups] = useState<GeneticsMapGroup[]>([]);
  const [isCompareModalOpen, setIsCompareModalOpen] = useState(false);
  /**
   * Bumped when the solver finishes and links parent routes, which changes every
   * recursive chance product and therefore invalidates cached route analyses.
   */
  const [analysisEpoch, setAnalysisEpoch] = useState(0);

  const orchestrator = useMemo(() => new CrossbreedingOrchestrator(), []);

  // Update options helper
  const updateOptions = useCallback((opts: Partial<ExtendedApplicationOptions>) => {
    const updated = { ...options, ...opts };
    setOptionsState(updated);
    StorageService.saveOptions(updated);
  }, [options]);

  const setCalculationPreset = useCallback((preset: 'fast' | 'balanced' | 'thorough') => {
    let opts: Partial<ExtendedApplicationOptions> = { calculationPreset: preset };
    if (preset === 'fast') {
      opts = {
        ...opts,
        numberOfGenerations: 1,
        maxCrossbreedingSaplingsNumber: 3,
        numberOfSaplingsAddedBetweenGenerations: 10
      };
    } else if (preset === 'balanced') {
      opts = {
        ...opts,
        numberOfGenerations: 2,
        maxCrossbreedingSaplingsNumber: 3,
        numberOfSaplingsAddedBetweenGenerations: 20
      };
    } else {
      // thorough
      opts = {
        ...opts,
        numberOfGenerations: 3,
        maxCrossbreedingSaplingsNumber: 4,
        numberOfSaplingsAddedBetweenGenerations: 30
      };
    }
    updateOptions(opts);
    notifyInfo(`Calculation preset: ${preset.toUpperCase()}`);
  }, [updateOptions, notifyInfo]);

  // Orchestrator Event Listener
  useEffect(() => {
    const unsubscribe = orchestrator.addEventListener((event: SimulatorEvent) => {
      if (event.type === 'PROGRESS_UPDATE') {
        const generation = event.generationIndex ?? 1;
        if (generation > announcedGenerationRef.current) {
          announcedGenerationRef.current = generation;
          setCalculationStatusMessage(`Calculation generation ${generation} of ${options.numberOfGenerations || 2}.`);
        }
        setProgress((prev) => ({
          isRunning: true,
          currentGeneration: event.generationIndex ?? 1,
          totalGenerations: options.numberOfGenerations || 2,
          progressPercent: event.progressPercent ?? 0,
          estimatedTimeRemainingSeconds: ((event.estimatedTimeMs ?? 0) / 1000),
          stage: event.stage ?? prev?.stage ?? 'Searching combinations…',
          processedCombinations: event.processedCombinations ?? prev?.processedCombinations ?? 0,
          totalCombinations: event.totalCombinations ?? prev?.totalCombinations ?? 0
        }));
      } else if (event.type === 'PARTIAL_RESULTS' || event.type === 'DONE_GENERATION') {
        if (event.mapGroups) {
          setResults(event.mapGroups);
        }
      } else if (event.type === 'DONE') {
        setAnalysisEpoch(e => e + 1);
        if (event.mapGroups) {
          setResults(event.mapGroups);
          setCalculationStatusMessage(
            event.mapGroups.length > 0
              ? `Calculation complete. ${event.mapGroups.length} viable breeding routes retained.`
              : 'Calculation complete. No viable routes found with the current plants.'
          );
        }
        setIsCalculating(false);
        setProgress(null);
      }
    });

    return () => {
      unsubscribe();
    };
  }, [orchestrator, options.numberOfGenerations]);

  // Selected route map
  const selectedMap = useMemo(() => {
    if (!selectedGroup || !selectedGroup.mapList.length) return null;
    return selectedGroup.mapList[selectedMapIndex] || selectedGroup.mapList[0];
  }, [selectedGroup, selectedMapIndex]);

  /**
   * Route analysis is the most expensive main-thread step (it walks each route's
   * dependency tree and tallies inventory). Results stream in roughly once a
   * second and the solver keeps unchanged groups identity-stable, so caching by
   * group instance means only genuinely new routes are analysed on each update
   * instead of all 500. The cache is dropped whenever an input to the analysis
   * changes.
   */
  const analysisCache = useMemo(
    () => new WeakMap<GeneticsMapGroup, RouteAnalysis>(),
    [clones, targetConfig.targetGenetics, analysisEpoch]
  );

  const scoredRoutes: ScoredRoute[] = useMemo(() => {
    return results.map(group => {
      const bestMap = group.mapList[0];
      let analysis = analysisCache.get(group);
      if (analysis === undefined) {
        analysis = analyzeRoute(bestMap, clones, targetConfig.targetGenetics);
        analysisCache.set(group, analysis);
      }
      return { group, bestMap, analysis };
    });
  }, [results, clones, targetConfig.targetGenetics, analysisCache]);

  // Filter & sort routes (before equivalent-quality grouping)
  const filteredRoutes = useMemo(() => {
    let list = [...scoredRoutes];

    const target = targetConfig.targetGenetics || '';
    const hasTarget = targetHasConstraint(target);

    // Pre-filter 1: Ignore genotypes that already exist in the source/input plants
    const sourceGenotypeSet = new Set(sourceSaplings.map(s => s.toString()));
    list = list.filter(r => !sourceGenotypeSet.has(r.group.resultSaplingGeneString));

    // Pre-filter 2: Ignore results where score < options.minimumTrackedScore
    if (options.minimumTrackedScore > 0) {
      list = list.filter(r => r.bestMap.score >= options.minimumTrackedScore);
    }

    // Target Filtering — each mode keeps a different set of routes.
    if (hasTarget) {
      if (targetConfig.matchMode === 'exact') {
        // Only results that match the target slot-by-slot (wildcards match any).
        list = list.filter(r => isExactMatch(r.group.resultSaplingGeneString, target));
      } else if (targetConfig.matchMode === 'at-least') {
        // Only results that contain at least the target's count of each gene.
        list = list.filter(r => meetsAtLeast(r.group.resultSaplingGeneString, target));
      }
      // 'best-possible' never filters — it always shows all achievable routes.
    }

    // Inventory Filtering
    if (inventoryFilterMode === 'available-only') {
      list = list.filter(r => r.analysis.inventoryStatus === 'available');
    } else if (inventoryFilterMode === 'partial-or-better') {
      list = list.filter(r => r.analysis.inventoryStatus !== 'missing');
    }

    // Reliability filter: only keep dependable routes — 50% or 100% chance.
    // Compound-tie routes (25%, 12.5%, …) are gambly and hidden. Fall back to the
    // unfiltered list if nothing clears the bar so results are never left empty.
    const reliable = list.filter(r => r.analysis.probabilityPercent >= 50);
    if (reliable.length > 0) list = reliable;

    // 2-level Rust Breeder ranking according to selected sortBy
    list.sort((a, b) => compareScoredRoutes(a, b, sortBy, target));

    return list;
  }, [scoredRoutes, sourceSaplings, options.minimumTrackedScore, targetConfig, inventoryFilterMode, sortBy]);

  // Collapse equal-quality routes into a single representative (keeps the flood
  // of identical "Score 98 · 100% · GEN.1 · 3 clones" cards down to one each).
  const filteredAndSortedRoutes = useMemo(() => {
    if (!groupSimilar) return filteredRoutes;

    const clusters: ScoredRoute[] = [];
    const bySignature = new Map<string, ScoredRoute>();

    for (const route of filteredRoutes) {
      const sig = routeSignature(route);
      const rep = bySignature.get(sig);
      if (rep) {
        (rep.equivalents ||= []).push(route);
      } else {
        const representative: ScoredRoute = { ...route, equivalents: [] };
        bySignature.set(sig, representative);
        clusters.push(representative);
      }
    }
    return clusters;
  }, [filteredRoutes, groupSimilar]);

  const rawRouteCount = filteredRoutes.length;
  const resultsCapped = results.length >= MAX_RETURNED_RESULTS;

  // Automatically select best matching route or clear when results/target change
  useEffect(() => {
    if (filteredAndSortedRoutes.length > 0) {
      const exists = selectedGroup && filteredAndSortedRoutes.some(
        r => r.group.resultSaplingGeneString === selectedGroup.resultSaplingGeneString
      );
      if (!exists) {
        setSelectedGroup(filteredAndSortedRoutes[0].group);
        setSelectedMapIndex(0);
      }
    } else {
      setSelectedGroup(null);
      setSelectedMapIndex(0);
    }
  }, [filteredAndSortedRoutes, selectedGroup]);

  // Solver runner
  const runSimulation = useCallback(async () => {
    if (sourceSaplings.length < 2) {
      notifyWarning('You need at least 2 plants entered to calculate breeding routes.');
      return;
    }

    // Auto-save input set if enabled
    if (options.autoSaveInputSets && geneInputText.trim()) {
      saveCurrentGeneSet();
    }

    setIsCalculating(true);
    announcedGenerationRef.current = 1;
    setCalculationStatusMessage('Calculation started. Generation 1 is being prepared.');
    setProgress({
      isRunning: true,
      currentGeneration: 1,
      totalGenerations: options.numberOfGenerations || 2,
      progressPercent: 0,
      estimatedTimeRemainingSeconds: 3,
      stage: 'Building combinations…',
      processedCombinations: 0,
      totalCombinations: 0
    });
    setResults([]);
    setSelectedGroup(null);
    setHighlightedGroup(null);

    // Yield to frame
    setTimeout(async () => {
      // The target is handed to the solver so the FINAL generation can reject
      // non-matching results before building them. Earlier generations stay
      // unfiltered because their results feed beam selection.
      await orchestrator.simulateBestGenetics(sourceSaplings, options, {
        targetGenetics: targetConfig.targetGenetics,
        matchMode: targetConfig.matchMode
      });
    }, 16);
  }, [orchestrator, sourceSaplings, options, geneInputText, saveCurrentGeneSet, notifyWarning, targetConfig]);

  const cancelSimulation = useCallback(() => {
    orchestrator.cancelSimulation();
    setIsCalculating(false);
    setProgress(null);
    const sorted = orchestrator.getSortedResults();
    setResults(sorted);
    setCalculationStatusMessage(`Calculation cancelled. Displaying ${sorted.length} partial results.`);
  }, [orchestrator]);

  const skipCurrentGeneration = useCallback(() => {
    orchestrator.skipToNextGeneration();
    setResults(orchestrator.getSortedResults());
    notifyInfo('Skipped current generation.');
  }, [orchestrator, notifyInfo]);

  // Compare routes
  const toggleCompareGroup = useCallback((group: GeneticsMapGroup) => {
    setComparedGroups(prev => {
      const exists = prev.some(g => g.resultSaplingGeneString === group.resultSaplingGeneString);
      if (exists) {
        return prev.filter(g => g.resultSaplingGeneString !== group.resultSaplingGeneString);
      }
      if (prev.length >= 3) {
        notifyWarning('You can compare up to 3 routes at a time.');
        return prev;
      }
      return [...prev, group];
    });
  }, [notifyWarning]);

  const clearComparedGroups = useCallback(() => {
    setComparedGroups([]);
  }, []);

  return (
    <CalculationContext.Provider
      value={{
        isCalculating,
        progress,
        results,
        scoredRoutes,
        filteredAndSortedRoutes,
        rawRouteCount,
        resultsCapped,
        calculationStatusMessage,
        sortBy,
        setSortBy,
        groupSimilar,
        setGroupSimilar,
        inventoryFilterMode,
        setInventoryFilterMode,
        selectedGroup,
        setSelectedGroup,
        selectedMapIndex,
        setSelectedMapIndex,
        isInspectorOpen,
        setIsInspectorOpen,
        selectedMap,
        highlightedGroup,
        setHighlightedGroup,
        comparedGroups,
        toggleCompareGroup,
        clearComparedGroups,
        isCompareModalOpen,
        setIsCompareModalOpen,
        options,
        updateOptions,
        setCalculationPreset,
        runSimulation,
        cancelSimulation,
        skipCurrentGeneration
      }}
    >
      {children}
    </CalculationContext.Provider>
  );
};

export const useCalculation = () => {
  const context = useContext(CalculationContext);
  if (!context) throw new Error('useCalculation must be used within a CalculationProvider');
  return context;
};
