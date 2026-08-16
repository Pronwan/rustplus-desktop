import React, { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import { StorageService, CookieConsentState, ExtendedApplicationOptions } from '../services/storageService.ts';
import { NotificationProvider } from './NotificationContext.tsx';
import { WorkspaceProvider, useWorkspace } from './WorkspaceContext.tsx';
import { CalculationProvider, useCalculation } from './CalculationContext.tsx';
import { ScannerProvider, useScanner } from './ScannerContext.tsx';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';

export type ActiveTab = 'workspace' | 'planner' | 'guide' | 'recipes';

export const PLANT_TYPES = [
  'hemp',
  'mixed-berry',
  'red-berry',
  'blue-berry',
  'yellow-berry',
  'green-berry',
  'white-berry',
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
  stage: string;
  processedCombinations: number;
  totalCombinations: number;
}

interface AppContextType {
  activeTab: ActiveTab;
  setActiveTab: (tab: ActiveTab) => void;

  themeMode: 'dark' | 'light';
  setThemeMode: (mode: 'dark' | 'light') => void;
  toggleTheme: () => void;

  density: 'comfortable' | 'compact';
  setDensity: (density: 'comfortable' | 'compact') => void;

  selectedPlant: string;
  setSelectedPlant: (plant: string) => void;

  options: ExtendedApplicationOptions;
  updateOptions: (opts: Partial<ExtendedApplicationOptions>) => void;

  consent: CookieConsentState;
  updateConsent: (consent: CookieConsentState) => void;

  // Global Modals
  isOptionsModalOpen: boolean;
  setIsOptionsModalOpen: (open: boolean) => void;
  isAboutModalOpen: boolean;
  setIsAboutModalOpen: (open: boolean) => void;
  isConsentModalOpen: boolean;
  setIsConsentModalOpen: (open: boolean) => void;
  isScannerGuideOpen: boolean;
  setIsScannerGuideOpen: (open: boolean) => void;
  isKeyboardShortcutsOpen: boolean;
  setIsKeyboardShortcutsOpen: (open: boolean) => void;
  isProjectManagerOpen: boolean;
  setIsProjectManagerOpen: (open: boolean) => void;

  // Facade access
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
  savedGeneSets: import('../services/storageService.ts').StoredGeneSet[];
  saveCurrentGeneSet: () => void;
  loadSavedGeneSet: (set: import('../services/storageService.ts').StoredGeneSet) => void;
  deleteSavedGeneSet: (timestamp: number) => void;
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
  setScannerPreviewEnabled: (enabled: boolean) => void;
  isStarved: boolean;
  starvationReason?: string;
}

const AppContext = createContext<AppContextType | null>(null);

const AppInternalBridge: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const workspace = useWorkspace();
  const calculation = useCalculation();
  const scanner = useScanner();

  const [activeTab, setActiveTab] = useState<ActiveTab>('workspace');
  const [consent, setConsent] = useState<CookieConsentState>(() => StorageService.getConsent());
  const [themeMode, setThemeModeState] = useState<'dark' | 'light'>(() => (calculation.options.darkMode ? 'dark' : 'light'));
  const [density, setDensityState] = useState<'comfortable' | 'compact'>(() => calculation.options.density || 'comfortable');

  // Modals
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);
  const [isAboutModalOpen, setIsAboutModalOpen] = useState(false);
  const [isConsentModalOpen, setIsConsentModalOpen] = useState(false);
  const [isScannerGuideOpen, setIsScannerGuideOpen] = useState(false);
  const [isKeyboardShortcutsOpen, setIsKeyboardShortcutsOpen] = useState(false);
  const [isProjectManagerOpen, setIsProjectManagerOpen] = useState(false);

  // Sync theme
  useEffect(() => {
    document.documentElement.setAttribute('data-theme', themeMode);
  }, [themeMode]);

  // Sync favicon
  useEffect(() => {
    const faviconLink = document.querySelector<HTMLLinkElement>("link[rel~='icon']");
    if (faviconLink) {
      faviconLink.href = `./img/items/${workspace.selectedPlant}.webp`;
    }
  }, [workspace.selectedPlant]);

  const setThemeMode = useCallback((mode: 'dark' | 'light') => {
    setThemeModeState(mode);
    calculation.updateOptions({ darkMode: mode === 'dark' });
  }, [calculation]);

  const toggleTheme = useCallback(() => {
    setThemeMode(themeMode === 'dark' ? 'light' : 'dark');
  }, [themeMode, setThemeMode]);

  const setDensity = useCallback((newDensity: 'comfortable' | 'compact') => {
    setDensityState(newDensity);
    calculation.updateOptions({ density: newDensity });
  }, [calculation]);

  const updateConsent = useCallback((newConsent: CookieConsentState) => {
    setConsent(newConsent);
    StorageService.saveConsent(newConsent);
  }, []);

  // Global Keyboard Shortcuts (Rule #47)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore when inside input/textarea
      const targetTag = (e.target as HTMLElement)?.tagName;
      const isInput = targetTag === 'INPUT' || targetTag === 'TEXTAREA' || (e.target as HTMLElement)?.isContentEditable;

      if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
        e.preventDefault();
        if (!scanner.isScannerActive && !scanner.isScannerInitializing) {
          calculation.runSimulation();
        }
        return;
      }

      if ((e.ctrlKey || e.metaKey) && e.shiftKey && (e.key === 'S' || e.key === 's')) {
        e.preventDefault();
        // Treat "initializing" as running too, so the toggle can cancel a scan that is
        // still starting up (share granted but not yet active) instead of no-opping.
        if (scanner.isScannerActive || scanner.isScannerInitializing) {
          scanner.stopScanner();
        } else {
          scanner.startScanner();
        }
        return;
      }

      if (e.key === 'Escape') {
        setIsOptionsModalOpen(false);
        setIsAboutModalOpen(false);
        setIsScannerGuideOpen(false);
        setIsKeyboardShortcutsOpen(false);
        setIsProjectManagerOpen(false);
        calculation.setIsCompareModalOpen(false);
        scanner.setIsCalibrationModalOpen(false);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [calculation, scanner]);

  const contextValue: AppContextType = {
    activeTab,
    setActiveTab,
    themeMode,
    setThemeMode,
    toggleTheme,
    density,
    setDensity,
    selectedPlant: workspace.selectedPlant,
    setSelectedPlant: workspace.setSelectedPlant,
    options: calculation.options,
    updateOptions: calculation.updateOptions,
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
    isKeyboardShortcutsOpen,
    setIsKeyboardShortcutsOpen,
    isProjectManagerOpen,
    setIsProjectManagerOpen,

    // Facade
    geneInputText: workspace.clones.map(c => c.genetics).join('\n'),
    setGeneInputText: (txt: string) => {
      const lines = txt.toUpperCase().split('\n').map(l => l.replace(/[^GHYWX]/g, '').slice(0, 6)).filter(l => l.length === 6);
      workspace.addBatchClones(lines);
    },
    sourceSaplings: workspace.sourceSaplings,
    setSourceSaplings: (_s) => {},
    results: calculation.results,
    highlightedGroup: calculation.highlightedGroup,
    setHighlightedGroup: calculation.setHighlightedGroup,
    progress: calculation.progress,
    isCalculating: calculation.isCalculating,
    runSimulation: calculation.runSimulation,
    cancelSimulation: calculation.cancelSimulation,
    skipCurrentGeneration: calculation.skipCurrentGeneration,
    savedGeneSets: StorageService.getSavedGeneSets(),
    saveCurrentGeneSet: () => {},
    loadSavedGeneSet: (set) => {
      const tokens = (set.genes.toUpperCase().match(/[GHYWX]{6}/g) || []);
      workspace.addBatchClones(tokens);
      if (set.selectedPlantType) {
        workspace.setSelectedPlant(set.selectedPlantType);
      }
    },
    deleteSavedGeneSet: (ts) => StorageService.removeSavedGeneSet(ts),
    isScannerActive: scanner.isScannerActive,
    isScannerInitializing: scanner.isScannerInitializing,
    scannerPreviews: scanner.scannerPreviews,
    scannerStatusMessage: scanner.scannerStatusMessage,
    startScanner: scanner.startScanner,
    stopScanner: scanner.stopScanner,
    moveScannerRegion: scanner.moveScannerRegion,
    scaleScannerRegion: scanner.scaleScannerRegion,
    resetScannerRegions: scanner.resetScannerRegions,
    getScannerDiagnostics: scanner.getScannerDiagnostics,
    setScannerPreviewEnabled: scanner.setScannerPreviewEnabled,
    isStarved: scanner.isStarved,
    starvationReason: scanner.starvationReason
  };

  return (
    <AppContext.Provider value={contextValue}>
      {children}
    </AppContext.Provider>
  );
};

export const AppProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  return (
    <NotificationProvider>
      <WorkspaceProvider>
        <CalculationProvider>
          <ScannerProvider>
            <AppInternalBridge>
              {children}
            </AppInternalBridge>
          </ScannerProvider>
        </CalculationProvider>
      </WorkspaceProvider>
    </NotificationProvider>
  );
};

export const useApp = () => {
  const context = useContext(AppContext);
  if (!context) throw new Error('useApp must be used within an AppProvider');
  return context;
};
