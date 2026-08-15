import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from 'react';
import {
  CrossbreedingOrchestrator,
  SimulatorEvent
} from '../services/orchestrator.ts';
import { StorageService, ExtendedApplicationOptions, DEFAULT_OPTIONS } from '../services/storageService.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { ProgressState } from './AppContext.tsx';
import { useWorkspace } from './WorkspaceContext.tsx';
import { analyzeRoute, RouteAnalysis } from '../domain/genetics/routeScoring.ts';
import { useNotification } from './NotificationContext.tsx';

export type RouteSortOption =
  | 'recommended'
  | 'probability'
  | 'generations'
  | 'clones'
  | 'inventory';

export interface ScoredRoute {
  group: GeneticsMapGroup;
  bestMap: GeneticsMap;
  analysis: RouteAnalysis;
}

interface CalculationContextType {
  isCalculating: boolean;
  progress: ProgressState | null;
  results: GeneticsMapGroup[];
  scoredRoutes: ScoredRoute[];
  filteredAndSortedRoutes: ScoredRoute[];

  // Sorting & Filtering
  sortBy: RouteSortOption;
  setSortBy: (sort: RouteSortOption) => void;
  inventoryFilterMode: 'all' | 'available-only' | 'partial-or-better';
  setInventoryFilterMode: (mode: 'all' | 'available-only' | 'partial-or-better') => void;

  // Selection & Inspector
  selectedGroup: GeneticsMapGroup | null;
  setSelectedGroup: (group: GeneticsMapGroup | null) => void;
  selectedMapIndex: number;
  setSelectedMapIndex: (idx: number) => void;
  selectedMap: GeneticsMap | null;
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
  const { notifyInfo, notifySuccess, notifyWarning } = useNotification();

  const [options, setOptionsState] = useState<ExtendedApplicationOptions>(() => StorageService.getOptions());
  const [results, setResults] = useState<GeneticsMapGroup[]>([]);
  const [progress, setProgress] = useState<ProgressState | null>(null);
  const [isCalculating, setIsCalculating] = useState(false);

  const [sortBy, setSortBy] = useState<RouteSortOption>('recommended');
  const [inventoryFilterMode, setInventoryFilterMode] = useState<'all' | 'available-only' | 'partial-or-better'>('all');

  const [selectedGroup, setSelectedGroup] = useState<GeneticsMapGroup | null>(null);
  const [selectedMapIndex, setSelectedMapIndex] = useState<number>(0);
  const [highlightedGroup, setHighlightedGroup] = useState<GeneticsMapGroup | null>(null);

  const [comparedGroups, setComparedGroups] = useState<GeneticsMapGroup[]>([]);
  const [isCompareModalOpen, setIsCompareModalOpen] = useState(false);

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
        if (event.mapGroups) {
          setResults(event.mapGroups);
          if (event.mapGroups.length > 0) {
            notifySuccess(`Found ${event.mapGroups.length} viable breeding routes`);
          } else {
            notifyWarning('No viable routes found with current plants. Try adding more plants.');
          }
        }
        setIsCalculating(false);
        setProgress(null);
      }
    });

    return () => {
      unsubscribe();
    };
  }, [orchestrator, options.numberOfGenerations, notifySuccess, notifyWarning]);

  // Selected route map
  const selectedMap = useMemo(() => {
    if (!selectedGroup || !selectedGroup.mapList.length) return null;
    return selectedGroup.mapList[selectedMapIndex] || selectedGroup.mapList[0];
  }, [selectedGroup, selectedMapIndex]);

  // Score all routes
  const scoredRoutes: ScoredRoute[] = useMemo(() => {
    return results.map(group => {
      const bestMap = group.mapList[0];
      const analysis = analyzeRoute(bestMap, clones, targetConfig.targetGenetics);
      return {
        group,
        bestMap,
        analysis
      };
    });
  }, [results, clones, targetConfig.targetGenetics]);

  // Filter & sort routes
  const filteredAndSortedRoutes = useMemo(() => {
    let list = [...scoredRoutes];

    // Target Filtering
    if (targetConfig.targetGenetics) {
      const targetStr = targetConfig.targetGenetics.toUpperCase();

      if (targetConfig.matchMode === 'exact') {
        list = list.filter(r => {
          const res = r.group.resultSaplingGeneString;
          for (let i = 0; i < 6; i++) {
            const req = targetStr[i];
            if (req && req !== '*' && req !== '?' && res[i] !== req) {
              return false;
            }
          }
          return true;
        });
      }
    }

    // Inventory Filtering
    if (inventoryFilterMode === 'available-only') {
      list = list.filter(r => r.analysis.inventoryStatus === 'available');
    } else if (inventoryFilterMode === 'partial-or-better') {
      list = list.filter(r => r.analysis.inventoryStatus !== 'missing');
    }

    // Sorting
    list.sort((a, b) => {
      if (sortBy === 'recommended') {
        return b.analysis.recommendationScore - a.analysis.recommendationScore;
      }
      if (sortBy === 'probability') {
        return b.analysis.probabilityPercent - a.analysis.probabilityPercent;
      }
      if (sortBy === 'generations') {
        if (a.analysis.generationCount !== b.analysis.generationCount) {
          return a.analysis.generationCount - b.analysis.generationCount;
        }
        return b.analysis.probabilityPercent - a.analysis.probabilityPercent;
      }
      if (sortBy === 'clones') {
        if (a.analysis.uniqueCloneCount !== b.analysis.uniqueCloneCount) {
          return a.analysis.uniqueCloneCount - b.analysis.uniqueCloneCount;
        }
        return a.analysis.totalPlacementsCount - b.analysis.totalPlacementsCount;
      }
      if (sortBy === 'inventory') {
        const invOrder = { available: 3, partial: 2, missing: 1 };
        const diff = invOrder[b.analysis.inventoryStatus] - invOrder[a.analysis.inventoryStatus];
        if (diff !== 0) return diff;
        return b.analysis.recommendationScore - a.analysis.recommendationScore;
      }
      return 0;
    });

    return list;
  }, [scoredRoutes, targetConfig, inventoryFilterMode, sortBy]);

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
      await orchestrator.simulateBestGenetics(sourceSaplings, options);
    }, 16);
  }, [orchestrator, sourceSaplings, options, geneInputText, saveCurrentGeneSet, notifyWarning]);

  const cancelSimulation = useCallback(() => {
    orchestrator.cancelSimulation();
    setIsCalculating(false);
    setProgress(null);
    const sorted = orchestrator.getSortedResults();
    setResults(sorted);
    notifyInfo('Calculation cancelled. Displaying partial results.');
  }, [orchestrator, notifyInfo]);

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
        sortBy,
        setSortBy,
        inventoryFilterMode,
        setInventoryFilterMode,
        selectedGroup,
        setSelectedGroup,
        selectedMapIndex,
        setSelectedMapIndex,
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
