import { ApplicationOptions } from './orchestrator.ts';
import { DEFAULT_GENE_SCORES, Sapling } from '../domain/genetics/Sapling.ts';
import { SavedClone, CloneUtils } from '../domain/genetics/Clone.ts';

export interface CookieConsentState {
  isPreferenceDecided: boolean;
  functional: boolean;
  analytics: boolean;
  advertisement: boolean;
}

export interface StoredGeneSet {
  timestamp: number;
  selectedPlantType: string | null;
  genes: string; // Newline-separated 6-gene strings
}

export interface ScannerRegion {
  TOP_LEFT_X: number;
  TOP_LEFT_Y: number;
  WIDTH: number;
  HEIGHT_TO_WIDTH_RATIO: number;
  GENE_WIDTH_TO_WIDTH_RATIO: number;
}

export interface ScannerProfile {
  id: string;
  name: string;
  resolutionName: string;
  regions: ScannerRegion[];
  scale: number;
}

export interface TargetConfiguration {
  targetGenetics: string; // 6 chars (G, Y, H, W, X, or *)
  matchMode: 'exact' | 'at-least' | 'best-possible';
  minGs?: number;
  minYs?: number;
  minHs?: number;
}

export interface BreedingSessionStep {
  generationIndex: number;
  targetGeneString: string;
  centerSaplingString?: string;
  surroundingSaplingsStrings: string[];
  priorityWinningIndices?: number[];
  priorityLosingIndices?: number[];
  chance: number;
  isCenterPlanted: boolean;
  isSurroundingPlanted: boolean;
  isCompleted: boolean;
}

export interface BreedingSession {
  id: string;
  cropType: string;
  targetGenetics: string;
  steps: BreedingSessionStep[];
  currentStepIndex: number;
  status: 'active' | 'completed' | 'abandoned';
  startedAt: string;
  updatedAt: string;
  completedAt?: string;
}

export interface FarmProject {
  id: string;
  name: string;
  cropType: string;
  targetGenetics: string;
  clones: SavedClone[];
  createdAt: string;
  updatedAt: string;
}

export const DEFAULT_SCANNER_REGIONS: ScannerRegion[] = [
  {
    TOP_LEFT_X: 0.4156,
    TOP_LEFT_Y: 0.2772,
    WIDTH: 0.079,
    HEIGHT_TO_WIDTH_RATIO: 0.18,
    GENE_WIDTH_TO_WIDTH_RATIO: 0.11
  },
  {
    TOP_LEFT_X: 0.6116,
    TOP_LEFT_Y: 0.3422,
    WIDTH: 0.1305,
    HEIGHT_TO_WIDTH_RATIO: 0.125,
    GENE_WIDTH_TO_WIDTH_RATIO: 0.08
  }
];

export const DEFAULT_SCANNER_PROFILES: ScannerProfile[] = [
  {
    id: 'profile_1440p',
    name: '1440p Windowed / Borderless',
    resolutionName: '2560 x 1440',
    regions: [...DEFAULT_SCANNER_REGIONS],
    scale: 1
  },
  {
    id: 'profile_1080p',
    name: '1080p Standard',
    resolutionName: '1920 x 1080',
    regions: [
      {
        TOP_LEFT_X: 0.4156,
        TOP_LEFT_Y: 0.2772,
        WIDTH: 0.082,
        HEIGHT_TO_WIDTH_RATIO: 0.18,
        GENE_WIDTH_TO_WIDTH_RATIO: 0.11
      },
      {
        TOP_LEFT_X: 0.6116,
        TOP_LEFT_Y: 0.3422,
        WIDTH: 0.132,
        HEIGHT_TO_WIDTH_RATIO: 0.125,
        GENE_WIDTH_TO_WIDTH_RATIO: 0.08
      }
    ],
    scale: 1
  },
  {
    id: 'profile_ultrawide',
    name: 'Ultrawide 21:9',
    resolutionName: '3440 x 1440',
    regions: [...DEFAULT_SCANNER_REGIONS],
    scale: 1
  }
];

// Logical CPU cores available to the browser/WebView.
const CPU_CORES = (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) || 4;

export const MAX_WORKER_COUNT = Math.max(1, CPU_CORES - 1);

export const RECOMMENDED_WORKER_COUNT = (() => {
  if (CPU_CORES <= 2) return 1;
  if (CPU_CORES <= 4) return CPU_CORES - 1;
  return CPU_CORES - 2;
})();

export interface ExtendedApplicationOptions extends ApplicationOptions {
  calculationPreset: 'fast' | 'balanced' | 'thorough';
  density: 'comfortable' | 'compact';
  inventoryMode: 'ignore' | 'prefer' | 'require';
  targetStopMode: 'continue' | 'exact' | 'threshold';
  targetStopThresholdPercent: number;
}

export const DEFAULT_OPTIONS: ExtendedApplicationOptions = {
  withRepetitions: true,
  modifyMinimumTrackedScoreManually: false,
  minCrossbreedingSaplingsNumber: 2,
  maxCrossbreedingSaplingsNumber: 3,
  numberOfGenerations: 2,
  numberOfSaplingsAddedBetweenGenerations: 20,
  minimumTrackedScore: 4,
  geneScores: { ...DEFAULT_GENE_SCORES },
  darkMode: true,
  skipScannerGuide: false,
  autoSaveInputSets: true,
  sounds: true,
  numberOfWorkers: RECOMMENDED_WORKER_COUNT,
  cpuLimitPercent: 50,
  calculationPreset: 'balanced',
  density: 'comfortable',
  inventoryMode: 'prefer',
  targetStopMode: 'continue',
  targetStopThresholdPercent: 100
};

const CONSENT_PREFIX = 'rb-cookie-pref-v1';
const KEY_PREF_DECIDED = `${CONSENT_PREFIX}-storage-preference-decided`;
const KEY_FUNCTIONAL = `${CONSENT_PREFIX}-functional-cookies-and-storage`;
const KEY_ANALYTICS = `${CONSENT_PREFIX}-analytics-cookies`;
const KEY_ADVERTISEMENT = `${CONSENT_PREFIX}-advertisement-cookies`;

const OPTIONS_KEY = 'options-v5';
const CLONE_BANK_KEY = 'GL_CLONE_BANK_V1';
const TARGET_CONFIG_KEY = 'GL_TARGET_CONFIG_V1';
const PROJECTS_KEY = 'GL_FARM_PROJECTS_V1';
const ACTIVE_SESSION_KEY = 'GL_ACTIVE_BREEDING_SESSION_V1';
const SESSION_HISTORY_KEY = 'GL_BREEDING_SESSIONS_HISTORY_V1';
const SCANNER_PROFILES_KEY = 'GL_SCANNER_PROFILES_V1';
const ACTIVE_PROFILE_ID_KEY = 'GL_ACTIVE_SCANNER_PROFILE_ID_V1';
const PREVIOUS_GENE_SETS_KEY = 'PREVIOUS_GENE_SETS';
const SCANNER_REGIONS_KEY = 'SCANNER_REGIONS';
const SELECTED_PLANT_KEY = 'SELECTED_PLANT_TYPE';

export class StorageService {
  public static getConsent(): CookieConsentState {
    try {
      const decided = localStorage.getItem(KEY_PREF_DECIDED) === 'true';
      const functional = localStorage.getItem(KEY_FUNCTIONAL) === 'true';
      const analytics = localStorage.getItem(KEY_ANALYTICS) === 'true';
      const advertisement = localStorage.getItem(KEY_ADVERTISEMENT) === 'true';

      return {
        isPreferenceDecided: decided,
        functional: decided ? functional : true,
        analytics: decided ? analytics : false,
        advertisement: decided ? advertisement : false
      };
    } catch {
      return {
        isPreferenceDecided: false,
        functional: true,
        analytics: false,
        advertisement: false
      };
    }
  }

  public static saveConsent(consent: CookieConsentState): void {
    try {
      localStorage.setItem(KEY_PREF_DECIDED, String(consent.isPreferenceDecided));
      localStorage.setItem(KEY_FUNCTIONAL, String(consent.functional));
      localStorage.setItem(KEY_ANALYTICS, String(consent.analytics));
      localStorage.setItem(KEY_ADVERTISEMENT, String(consent.advertisement));

      if (!consent.functional) {
        this.clearFunctionalData();
      }
    } catch {
      // storage unavailable
    }
  }

  public static clearFunctionalData(): void {
    try {
      localStorage.removeItem(OPTIONS_KEY);
      localStorage.removeItem(CLONE_BANK_KEY);
      localStorage.removeItem(TARGET_CONFIG_KEY);
      localStorage.removeItem(PROJECTS_KEY);
      localStorage.removeItem(ACTIVE_SESSION_KEY);
      localStorage.removeItem(SESSION_HISTORY_KEY);
      localStorage.removeItem(SCANNER_PROFILES_KEY);
      localStorage.removeItem(ACTIVE_PROFILE_ID_KEY);
      localStorage.removeItem(PREVIOUS_GENE_SETS_KEY);
      localStorage.removeItem(SCANNER_REGIONS_KEY);
      localStorage.removeItem(SELECTED_PLANT_KEY);
    } catch {
      // ignore
    }
  }

  public static getOptions(): ExtendedApplicationOptions {
    try {
      const raw = localStorage.getItem(OPTIONS_KEY) || localStorage.getItem('options-v4');
      if (raw) {
        const parsed = JSON.parse(raw);
        const storedWorkers =
          typeof parsed.numberOfWorkers === 'number' ? parsed.numberOfWorkers : DEFAULT_OPTIONS.numberOfWorkers;
        const wasOldAllCoresDefault = storedWorkers >= CPU_CORES;
        const safeWorkers = wasOldAllCoresDefault
          ? RECOMMENDED_WORKER_COUNT
          : Math.min(MAX_WORKER_COUNT, Math.max(1, storedWorkers));

        return {
          ...DEFAULT_OPTIONS,
          ...parsed,
          numberOfWorkers: safeWorkers,
          geneScores: {
            ...DEFAULT_GENE_SCORES,
            ...(parsed.geneScores || {})
          }
        };
      }
    } catch {
      // fallback
    }
    return { ...DEFAULT_OPTIONS };
  }

  public static saveOptions(options: Partial<ExtendedApplicationOptions>): void {
    try {
      const current = this.getOptions();
      const updated = { ...current, ...options };
      localStorage.setItem(OPTIONS_KEY, JSON.stringify(updated));
    } catch {
      // ignore
    }
  }

  // --- CLONE BANK ---

  public static getClones(cropType?: string): SavedClone[] {
    try {
      const raw = localStorage.getItem(CLONE_BANK_KEY);
      if (raw) {
        const parsed: SavedClone[] = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length > 0) {
          return cropType ? parsed.filter(c => c.cropType === cropType) : parsed;
        }
      }

      // Auto-migrate from older PREVIOUS_GENE_SETS if clone bank is empty
      const legacySets = this.getSavedGeneSets();
      if (legacySets.length > 0) {
        const migrated: SavedClone[] = [];
        const currentPlant = this.getSelectedPlantType();
        for (const set of legacySets) {
          const lines = (set.genes.toUpperCase().match(/[GHYWX]{6}/g) || []).filter(g => Sapling.isValidGeneString(g));
          for (const g of lines) {
            migrated.push(
              CloneUtils.create(g, set.selectedPlantType || currentPlant, {
                source: 'manual',
                quantity: 1
              })
            );
          }
        }
        if (migrated.length > 0) {
          this.saveClones(migrated);
          return cropType ? migrated.filter(c => c.cropType === cropType) : migrated;
        }
      }
    } catch {
      // fallback
    }
    return [];
  }

  public static saveClones(clones: SavedClone[]): void {
    try {
      localStorage.setItem(CLONE_BANK_KEY, JSON.stringify(clones));
    } catch {
      // ignore
    }
  }

  public static addClone(clone: SavedClone): SavedClone[] {
    const all = this.getClones();
    const existingIdx = all.findIndex(c => c.genetics === clone.genetics && c.cropType === clone.cropType);
    if (existingIdx >= 0) {
      all[existingIdx].quantity += clone.quantity || 1;
      all[existingIdx].updatedAt = new Date().toISOString();
      if (clone.name && !all[existingIdx].name) all[existingIdx].name = clone.name;
      if (clone.tags.length > 0) {
        all[existingIdx].tags = Array.from(new Set([...all[existingIdx].tags, ...clone.tags]));
      }
    } else {
      all.push(clone);
    }
    this.saveClones(all);
    return all;
  }

  public static updateClone(cloneId: string, updates: Partial<SavedClone>): SavedClone[] {
    const all = this.getClones();
    const idx = all.findIndex(c => c.id === cloneId);
    if (idx >= 0) {
      all[idx] = { ...all[idx], ...updates, updatedAt: new Date().toISOString() };
      this.saveClones(all);
    }
    return all;
  }

  public static removeClone(cloneId: string): SavedClone[] {
    const all = this.getClones().filter(c => c.id !== cloneId);
    this.saveClones(all);
    return all;
  }

  public static clearClonesForCrop(cropType: string): SavedClone[] {
    const all = this.getClones().filter(c => c.cropType !== cropType);
    this.saveClones(all);
    return all;
  }

  // --- TARGET CONFIGURATION ---

  public static getTargetConfig(): TargetConfiguration {
    try {
      const raw = localStorage.getItem(TARGET_CONFIG_KEY);
      if (raw) {
        return JSON.parse(raw);
      }
    } catch {
      // ignore
    }
    return {
      targetGenetics: 'GGGYYY',
      matchMode: 'exact'
    };
  }

  public static saveTargetConfig(config: TargetConfiguration): void {
    try {
      localStorage.setItem(TARGET_CONFIG_KEY, JSON.stringify(config));
    } catch {
      // ignore
    }
  }

  // --- BREEDING SESSIONS ---

  public static getActiveBreedingSession(): BreedingSession | null {
    try {
      const raw = localStorage.getItem(ACTIVE_SESSION_KEY);
      if (raw) {
        const session: BreedingSession = JSON.parse(raw);
        if (session.status === 'active') return session;
      }
    } catch {
      // ignore
    }
    return null;
  }

  public static saveActiveBreedingSession(session: BreedingSession | null): void {
    try {
      if (!session) {
        localStorage.removeItem(ACTIVE_SESSION_KEY);
      } else {
        localStorage.setItem(ACTIVE_SESSION_KEY, JSON.stringify(session));
      }
    } catch {
      // ignore
    }
  }

  public static getBreedingSessionHistory(): BreedingSession[] {
    try {
      const raw = localStorage.getItem(SESSION_HISTORY_KEY);
      if (raw) {
        return JSON.parse(raw);
      }
    } catch {
      // ignore
    }
    return [];
  }

  public static addBreedingSessionToHistory(session: BreedingSession): void {
    try {
      const history = this.getBreedingSessionHistory().filter(s => s.id !== session.id);
      history.unshift(session);
      if (history.length > 25) history.length = 25;
      localStorage.setItem(SESSION_HISTORY_KEY, JSON.stringify(history));
    } catch {
      // ignore
    }
  }

  // --- FARM PROJECTS ---

  public static getProjects(): FarmProject[] {
    try {
      const raw = localStorage.getItem(PROJECTS_KEY);
      if (raw) {
        return JSON.parse(raw);
      }
    } catch {
      // ignore
    }
    return [];
  }

  public static saveProjects(projects: FarmProject[]): void {
    try {
      localStorage.setItem(PROJECTS_KEY, JSON.stringify(projects));
    } catch {
      // ignore
    }
  }

  public static addProject(project: FarmProject): FarmProject[] {
    const projects = this.getProjects().filter(p => p.id !== project.id);
    projects.unshift(project);
    this.saveProjects(projects);
    return projects;
  }

  public static deleteProject(projectId: string): FarmProject[] {
    const projects = this.getProjects().filter(p => p.id !== projectId);
    this.saveProjects(projects);
    return projects;
  }

  // --- SCANNER PROFILES ---

  public static getScannerProfiles(): ScannerProfile[] {
    try {
      const raw = localStorage.getItem(SCANNER_PROFILES_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length > 0) return parsed;
      }
    } catch {
      // ignore
    }
    return [...DEFAULT_SCANNER_PROFILES];
  }

  public static saveScannerProfiles(profiles: ScannerProfile[]): void {
    try {
      localStorage.setItem(SCANNER_PROFILES_KEY, JSON.stringify(profiles));
    } catch {
      // ignore
    }
  }

  public static getActiveScannerProfileId(): string {
    try {
      return localStorage.getItem(ACTIVE_PROFILE_ID_KEY) || 'profile_1440p';
    } catch {
      return 'profile_1440p';
    }
  }

  public static saveActiveScannerProfileId(id: string): void {
    try {
      localStorage.setItem(ACTIVE_PROFILE_ID_KEY, id);
    } catch {
      // ignore
    }
  }

  // --- LEGACY GENE SETS & SCANNER REGIONS ---

  public static getSavedGeneSets(): StoredGeneSet[] {
    try {
      const raw = localStorage.getItem(PREVIOUS_GENE_SETS_KEY);
      if (raw) {
        return JSON.parse(raw);
      }
    } catch {
      // ignore
    }
    return [];
  }

  public static addSavedGeneSet(genes: string, selectedPlantType: string | null): StoredGeneSet[] {
    const list = this.getSavedGeneSets();
    const cleanGenes = genes.trim().toUpperCase();
    if (!cleanGenes) return list;
    const existingIndex = list.findIndex(s => s.genes === cleanGenes && s.selectedPlantType === selectedPlantType);
    if (existingIndex >= 0) {
      list.splice(existingIndex, 1);
    }
    list.unshift({
      timestamp: Date.now(),
      selectedPlantType,
      genes: cleanGenes
    });
    if (list.length > 20) list.length = 20;
    try {
      localStorage.setItem(PREVIOUS_GENE_SETS_KEY, JSON.stringify(list));
    } catch {
      // ignore
    }
    return list;
  }

  public static removeSavedGeneSet(timestamp: number): StoredGeneSet[] {
    const list = this.getSavedGeneSets().filter(s => s.timestamp !== timestamp);
    try {
      localStorage.setItem(PREVIOUS_GENE_SETS_KEY, JSON.stringify(list));
    } catch {
      // ignore
    }
    return list;
  }

  public static resetScannerRegions(): ScannerRegion[] {
    this.saveScannerRegions(DEFAULT_SCANNER_REGIONS);
    return [...DEFAULT_SCANNER_REGIONS];
  }

  public static getScannerRegions(): ScannerRegion[] {
    try {
      const raw = localStorage.getItem(SCANNER_REGIONS_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length >= 2) {
          return parsed;
        }
      }
    } catch {
      // ignore
    }
    return [...DEFAULT_SCANNER_REGIONS];
  }

  public static saveScannerRegions(regions: ScannerRegion[]): void {
    try {
      localStorage.setItem(SCANNER_REGIONS_KEY, JSON.stringify(regions));
    } catch {
      // ignore
    }
  }

  public static getSelectedPlantType(): string {
    try {
      return localStorage.getItem(SELECTED_PLANT_KEY) || 'hemp';
    } catch {
      return 'hemp';
    }
  }

  public static saveSelectedPlantType(plantType: string): void {
    try {
      localStorage.setItem(SELECTED_PLANT_KEY, plantType);
    } catch {
      // ignore
    }
  }
}
