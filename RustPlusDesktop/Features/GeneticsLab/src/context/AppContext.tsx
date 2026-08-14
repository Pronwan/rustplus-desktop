import React, { createContext, useContext, useState, useEffect, useMemo, useCallback } from 'react';
import {
  ApplicationOptions,
  CrossbreedingOrchestrator,
  SimulatorEvent
} from '../services/orchestrator.ts';
import { StorageService, CookieConsentState, StoredGeneSet, DEFAULT_OPTIONS } from '../services/storageService.ts';
import { ScannerService, ScannerEvent } from '../services/scannerService.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { AudioService } from '../services/audioService.ts';

export type ActiveTab = 'calculator' | 'guide' | 'recipes';

export const PLANT_TYPES = [
  'mixed-berry',
  'red-berry',
  'blue-berry',
  'yellow-berry',
  'green-berry',
  'white-berry',
  'hemp',
  'potato',
  'pumpkin',
  'corn',
  'wheat',
  'sunflower',
  'rose',
  'orchid'
] as const;

export interface ProgressState {
  isRunning: boolean;
  currentGeneration: number;
  totalGenerations: number;
  progressPercent: number;
  estimatedTimeRemainingSeconds: number;
}

interface AppContextType {
  activeTab: ActiveTab;
  setActiveTab: (tab: ActiveTab) => void;

  themeMode: 'dark' | 'light';
  setThemeMode: (mode: 'dark' | 'light') => void;
  toggleTheme: () => void;

  selectedPlant: string;
  setSelectedPlant: (plant: string) => void;

  options: ApplicationOptions;
  updateOptions: (opts: Partial<ApplicationOptions>) => void;

  consent: CookieConsentState;
  updateConsent: (consent: CookieConsentState) => void;

  // Modals
  isOptionsModalOpen: boolean;
  setIsOptionsModalOpen: (open: boolean) => void;
  isAboutModalOpen: boolean;
  setIsAboutModalOpen: (open: boolean) => void;
  isConsentModalOpen: boolean;
  setIsConsentModalOpen: (open: boolean) => void;
  isScannerGuideOpen: boolean;
  setIsScannerGuideOpen: (open: boolean) => void;

  // Calculator Inputs & State
  geneInputText: string;
  setGeneInputText: (text: string) => void;
  sourceSaplings: Sapling[];
  setSourceSaplings: (saplings: Sapling[]) => void;
  results: GeneticsMapGroup[];
  highlightedGroup: GeneticsMapGroup | null;
  setHighlightedGroup: (group: GeneticsMapGroup | null) => void;
  progress: ProgressState | null;
  isCalculating: boolean;
  runSimulation: () => Promise<void>;
  cancelSimulation: () => void;
  skipCurrentGeneration: () => void;

  // Saved Gene Sets
  savedGeneSets: StoredGeneSet[];
  saveCurrentGeneSet: () => void;
  loadSavedGeneSet: (set: StoredGeneSet) => void;
  deleteSavedGeneSet: (timestamp: number) => void;

  // Scanner (Non-Modal / Inline Bottom-Right Widget)
  isScannerActive: boolean;
  isScannerInitializing: boolean;
  scannerPreviews: Record<number, string>;
  scannerStatusMessage: string;
  startScanner: () => Promise<void>;
  stopScanner: () => void;
  moveScannerRegion: (regionIdx: number, dx: number, dy: number) => void;
  scaleScannerRegion: (regionIdx: number, dw: number) => void;
  resetScannerRegions: () => void;
  getScannerDiagnostics: () => import('../services/scanner/scannerTypes.ts').ScannerDiagnostics;
}

const AppContext = createContext<AppContextType | null>(null);

export const AppProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [activeTab, setActiveTab] = useState<ActiveTab>('calculator');
  const [options, setOptionsState] = useState<ApplicationOptions>(() => StorageService.getOptions());
  const [themeMode, setThemeModeState] = useState<'dark' | 'light'>(() => (options.darkMode ? 'dark' : 'light'));
  const [selectedPlant, setSelectedPlantState] = useState<string>(() => StorageService.getSelectedPlantType());
  const [consent, setConsent] = useState<CookieConsentState>(() => StorageService.getConsent());

  // Modal States
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);
  const [isAboutModalOpen, setIsAboutModalOpen] = useState(false);
  const [isConsentModalOpen, setIsConsentModalOpen] = useState(false);
  const [isScannerGuideOpen, setIsScannerGuideOpen] = useState(false);

  // Calculator State
  const [geneInputText, setGeneInputText] = useState('');
  const [sourceSaplings, setSourceSaplings] = useState<Sapling[]>([]);
  const [results, setResults] = useState<GeneticsMapGroup[]>([]);
  const [highlightedGroup, setHighlightedGroup] = useState<GeneticsMapGroup | null>(null);
  const [progress, setProgress] = useState<ProgressState | null>(null);
  const [isCalculating, setIsCalculating] = useState(false);

  const [savedGeneSets, setSavedGeneSets] = useState<StoredGeneSet[]>(() => StorageService.getSavedGeneSets());

  // Scanner State
  const [isScannerActive, setIsScannerActive] = useState(false);
  const [isScannerInitializing, setIsScannerInitializing] = useState(false);
  const [scannerPreviews, setScannerPreviews] = useState<Record<number, string>>({});
  const [scannerStatusMessage, setScannerStatusMessage] = useState('');

  const orchestrator = useMemo(() => new CrossbreedingOrchestrator(), []);
  const scannerService = useMemo(() => new ScannerService(), []);

  // Sync theme
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', themeMode);
  }, [themeMode]);

  // Sync favicon
  useEffect(() => {
    const faviconLink = document.querySelector<HTMLLinkElement>("link[rel~='icon']");
    if (faviconLink) {
      faviconLink.href = `./img/items/${selectedPlant}.webp`;
    }
  }, [selectedPlant]);

  // Scanner Event Listener
  useEffect(() => {
    const unsubscribe = scannerService.addEventListener((evt: ScannerEvent) => {
      if (evt.type === 'INITIALIZING') {
        setIsScannerInitializing(true);
        setScannerStatusMessage('Initializing OCR engine...');
      } else if (evt.type === 'STARTED') {
        setIsScannerInitializing(false);
        setIsScannerActive(true);
        setScannerStatusMessage('Scanner active. Hover over plants in Rust.');
      } else if (evt.type === 'STOPPED') {
        setIsScannerActive(false);
        setIsScannerInitializing(false);
        setScannerStatusMessage('');
      } else if (evt.type === 'PREVIEW') {
        if (evt.regionIndex !== undefined && evt.previewDataUrl) {
          setScannerPreviews((prev) => ({ ...prev, [evt.regionIndex!]: evt.previewDataUrl! }));
        }
      } else if (evt.type === 'SAPLING-FOUND') {
        if (evt.geneString) {
          const found = evt.geneString.toUpperCase();
          if (Sapling.isValidGeneString(found)) {
            setSourceSaplings((prev) => {
              const existingStrings = prev.map((s) => s.toString());
              scannerService.acknowledgeGeneHandled(found);
              if (!existingStrings.includes(found)) {
                AudioService.playPop(options.sounds);
                const updated = [...prev, new Sapling(found, 0, prev.length)];
                setGeneInputText(updated.map((s) => s.toString()).join('\n'));
                return updated;
              }
              // Duplicate: do not play pop sound
              return prev;
            });
          }
        }
      } else if (evt.type === 'ERROR') {
        setIsScannerActive(false);
        setIsScannerInitializing(false);
        setScannerStatusMessage(`Error: ${evt.error}`);
      }
    });

    return () => {
      unsubscribe();
    };
  }, [scannerService]);

  // Orchestrator Event Listener
  useEffect(() => {
    const unsubscribe = orchestrator.addEventListener((event: SimulatorEvent) => {
      if (event.type === 'PROGRESS_UPDATE') {
        setProgress({
          isRunning: true,
          currentGeneration: event.generationIndex ?? 1,
          totalGenerations: options.numberOfGenerations || 2,
          progressPercent: event.progressPercent ?? 0,
          estimatedTimeRemainingSeconds: ((event.estimatedTimeMs ?? 0) / 1000)
        });
      } else if (event.type === 'PARTIAL_RESULTS') {
        if (event.mapGroups) {
          setResults(event.mapGroups);
        }
      } else if (event.type === 'DONE_GENERATION') {
        if (event.mapGroups) {
          setResults(event.mapGroups);
        }
      } else if (event.type === 'DONE') {
        if (event.mapGroups) {
          setResults(event.mapGroups);
        }
        setIsCalculating(false);
        setProgress(null);
      }
    });

    return () => {
      unsubscribe();
    };
  }, [orchestrator, options.numberOfGenerations]);

  const setThemeMode = (mode: 'dark' | 'light') => {
    setThemeModeState(mode);
    const updated = { ...options, darkMode: mode === 'dark' };
    setOptionsState(updated);
    StorageService.saveOptions(updated);
  };

  const toggleTheme = () => {
    setThemeMode(themeMode === 'dark' ? 'light' : 'dark');
  };

  const setSelectedPlant = (plant: string) => {
    setSelectedPlantState(plant);
    StorageService.saveSelectedPlantType(plant);
  };

  const updateOptions = (opts: Partial<ApplicationOptions>) => {
    const next = { ...options, ...opts };
    setOptionsState(next);
    if (opts.darkMode !== undefined) {
      setThemeModeState(opts.darkMode ? 'dark' : 'light');
    }
    StorageService.saveOptions(next);
  };

  const updateConsent = (newConsent: CookieConsentState) => {
    setConsent(newConsent);
    StorageService.saveConsent(newConsent);
    if (!newConsent.functional) {
      setSavedGeneSets([]);
    }
  };

  const saveCurrentGeneSet = useCallback(() => {
    if (!sourceSaplings.length) return;
    const geneStrings = sourceSaplings.map(s => s.toString()).join('\n');
    const updated = StorageService.addSavedGeneSet(geneStrings, selectedPlant);
    setSavedGeneSets(updated);
  }, [sourceSaplings, selectedPlant]);

  const runSimulation = useCallback(async () => {
    if (sourceSaplings.length < 2) return;
    setIsCalculating(true);
    setProgress({
      isRunning: true,
      currentGeneration: 1,
      totalGenerations: options.numberOfGenerations || 2,
      progressPercent: 0,
      estimatedTimeRemainingSeconds: 3
    });
    setResults([]);

    if (options.autoSaveInputSets ?? true) {
      saveCurrentGeneSet();
    }

    // Yield to browser frame so button click & progress bar update instantly
    setTimeout(async () => {
      await orchestrator.simulateBestGenetics(sourceSaplings, options);
    }, 16);
  }, [orchestrator, sourceSaplings, options, saveCurrentGeneSet]);

  const cancelSimulation = useCallback(() => {
    orchestrator.cancelSimulation();
    setIsCalculating(false);
    setProgress(null);
    setResults(orchestrator.getSortedResults());
  }, [orchestrator]);

  const skipCurrentGeneration = useCallback(() => {
    orchestrator.skipToNextGeneration();
    setResults(orchestrator.getSortedResults());
  }, [orchestrator]);

  const loadSavedGeneSet = useCallback((set: StoredGeneSet) => {
    const rawTokens = (set.genes.toUpperCase().match(/[GHYWX]{6}/g) || []).filter(g => Sapling.isValidGeneString(g));
    const saplings = rawTokens.map((g, idx) => new Sapling(g, 0, idx));
    setSourceSaplings(saplings);
    setGeneInputText(rawTokens.join('\n'));
    if (set.selectedPlantType) {
      setSelectedPlant(set.selectedPlantType);
    }
  }, []);

  const deleteSavedGeneSet = useCallback((timestamp: number) => {
    const updated = StorageService.removeSavedGeneSet(timestamp);
    setSavedGeneSets(updated);
  }, []);

  // Scanner Methods
  const startScanner = useCallback(async () => {
    await scannerService.start();
  }, [scannerService]);

  const stopScanner = useCallback(() => {
    scannerService.stop();
  }, [scannerService]);

  const moveScannerRegion = useCallback((regionIdx: number, dx: number, dy: number) => {
    scannerService.moveRegion(regionIdx, dx, dy);
  }, [scannerService]);

  const scaleScannerRegion = useCallback((regionIdx: number, dw: number) => {
    scannerService.scaleRegion(regionIdx, dw);
  }, [scannerService]);

  const resetScannerRegions = useCallback(() => {
    scannerService.resetRegions();
  }, [scannerService]);

  const getScannerDiagnostics = useCallback(() => {
    return scannerService.getDiagnostics();
  }, [scannerService]);

  return (
    <AppContext.Provider
      value={{
        activeTab,
        setActiveTab,
        themeMode,
        setThemeMode,
        toggleTheme,
        selectedPlant,
        setSelectedPlant,
        options,
        updateOptions,
        consent,
        updateConsent,
        isOptionsModalOpen,
        setIsOptionsModalOpen,
        isAboutModalOpen,
        setIsAboutModalOpen,
        isConsentModalOpen,
        setIsConsentModalOpen,
        isScannerGuideOpen,
        setIsScannerGuideOpen,
        geneInputText,
        setGeneInputText,
        sourceSaplings,
        setSourceSaplings,
        results,
        highlightedGroup,
        setHighlightedGroup,
        progress,
        isCalculating,
        runSimulation,
        cancelSimulation,
        skipCurrentGeneration,
        savedGeneSets,
        saveCurrentGeneSet,
        loadSavedGeneSet,
        deleteSavedGeneSet,
        isScannerActive,
        isScannerInitializing,
        scannerPreviews,
        scannerStatusMessage,
        startScanner,
        stopScanner,
        moveScannerRegion,
        scaleScannerRegion,
        resetScannerRegions,
        getScannerDiagnostics
      }}
    >
      {children}
    </AppContext.Provider>
  );
};

export const useApp = () => {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp must be used within an AppProvider');
  return context;
};
