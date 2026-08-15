import { ApplicationOptions } from './orchestrator.ts';
import { DEFAULT_GENE_SCORES } from '../domain/genetics/Sapling.ts';

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

// Logical CPU cores available to the browser/WebView.
const CPU_CORES = (typeof navigator !== 'undefined' && navigator.hardwareConcurrency) || 4;

// Hard ceiling for worker threads: always leave at least one core free so the OS, the
// UI thread and the game stay responsive. Saturating every core is what makes the whole
// PC stutter during a heavy (high gene count) calculation.
export const MAX_WORKER_COUNT = Math.max(1, CPU_CORES - 1);

// Recommended default: leave 2 cores free on larger CPUs, 1 on small ones. This keeps the
// machine smooth by default while still using most of the CPU for the simulation.
export const RECOMMENDED_WORKER_COUNT = (() => {
  if (CPU_CORES <= 2) return 1;
  if (CPU_CORES <= 4) return CPU_CORES - 1;
  return CPU_CORES - 2;
})();

export const DEFAULT_OPTIONS: ApplicationOptions = {
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
  sounds: false,
  numberOfWorkers: RECOMMENDED_WORKER_COUNT,
  cpuLimitPercent: 50
};

const CONSENT_PREFIX = 'rb-cookie-pref-v1';
const KEY_PREF_DECIDED = `${CONSENT_PREFIX}-storage-preference-decided`;
const KEY_FUNCTIONAL = `${CONSENT_PREFIX}-functional-cookies-and-storage`;
const KEY_ANALYTICS = `${CONSENT_PREFIX}-analytics-cookies`;
const KEY_ADVERTISEMENT = `${CONSENT_PREFIX}-advertisement-cookies`;

const OPTIONS_KEY = 'options-v4';
const MAX_CB_MIGRATION_KEY = 'gl-migrated-maxcb-v1';
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
        // Revoked: clean up functional data
        this.clearFunctionalData();
      }
    } catch {
      // storage unavailable
    }
  }

  public static clearFunctionalData(): void {
    try {
      localStorage.removeItem(OPTIONS_KEY);
      localStorage.removeItem(PREVIOUS_GENE_SETS_KEY);
      localStorage.removeItem(SCANNER_REGIONS_KEY);
      localStorage.removeItem(SELECTED_PLANT_KEY);
    } catch {
      // ignore
    }
  }

  public static getOptions(): ApplicationOptions {
    try {
      const raw = localStorage.getItem(OPTIONS_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        // The previous default set numberOfWorkers to the full core count, which pegged
        // every core and stuttered the whole PC. If the stored value is that old
        // all-cores value (i.e. never deliberately lowered), adopt the smoother
        // recommended default. Otherwise honour the user's choice, but never allow more
        // than MAX_WORKER_COUNT so at least one core always stays free.
        const storedWorkers =
          typeof parsed.numberOfWorkers === 'number' ? parsed.numberOfWorkers : DEFAULT_OPTIONS.numberOfWorkers;
        const wasOldAllCoresDefault = storedWorkers >= CPU_CORES;
        const numberOfWorkers = wasOldAllCoresDefault
          ? RECOMMENDED_WORKER_COUNT
          : Math.min(storedWorkers, MAX_WORKER_COUNT);

        const cpuLimitPercent = Math.min(
          100,
          Math.max(10, typeof parsed.cpuLimitPercent === 'number' ? parsed.cpuLimitPercent : DEFAULT_OPTIONS.cpuLimitPercent)
        );

        // One-time migration: the old default max crossbreeding plants (5) explodes the
        // combination count and memory. Lower untouched (old-default) configs to the new
        // default of 3. Done once via a flag so a deliberately chosen value still sticks.
        let maxCrossbreedingSaplingsNumber =
          typeof parsed.maxCrossbreedingSaplingsNumber === 'number'
            ? parsed.maxCrossbreedingSaplingsNumber
            : DEFAULT_OPTIONS.maxCrossbreedingSaplingsNumber;
        let migratedMax = false;
        if (!localStorage.getItem(MAX_CB_MIGRATION_KEY)) {
          if (maxCrossbreedingSaplingsNumber === 5) {
            maxCrossbreedingSaplingsNumber = DEFAULT_OPTIONS.maxCrossbreedingSaplingsNumber;
            migratedMax = true;
          }
          try {
            localStorage.setItem(MAX_CB_MIGRATION_KEY, '1');
          } catch {
            // ignore
          }
        }

        const merged = {
          ...DEFAULT_OPTIONS,
          ...parsed,
          geneScores: {
            ...DEFAULT_GENE_SCORES,
            ...(parsed.geneScores || {})
          },
          numberOfWorkers,
          cpuLimitPercent,
          maxCrossbreedingSaplingsNumber
        };

        // Persist the migrated value so it is not undone on the next load.
        if (migratedMax) {
          try {
            localStorage.setItem(OPTIONS_KEY, JSON.stringify(merged));
          } catch {
            // ignore
          }
        }

        return merged;
      }
    } catch {
      // ignore
    }
    return { ...DEFAULT_OPTIONS };
  }

  public static saveOptions(options: ApplicationOptions): void {
    const consent = this.getConsent();
    if (!consent.functional) return;

    try {
      localStorage.setItem(OPTIONS_KEY, JSON.stringify(options));
    } catch {
      // ignore
    }
  }

  public static getSavedGeneSets(): StoredGeneSet[] {
    try {
      const raw = localStorage.getItem(PREVIOUS_GENE_SETS_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed)) {
          return parsed.slice(0, 200);
        }
      }
    } catch {
      // ignore
    }
    return [];
  }

  public static saveGeneSets(sets: StoredGeneSet[]): void {
    const consent = this.getConsent();
    if (!consent.functional) return;

    try {
      localStorage.setItem(PREVIOUS_GENE_SETS_KEY, JSON.stringify(sets.slice(0, 200)));
    } catch {
      // ignore
    }
  }

  public static addSavedGeneSet(genes: string, selectedPlantType: string | null): StoredGeneSet[] {
    const existing = this.getSavedGeneSets();
    const cleanGenes = genes.trim().toUpperCase();
    if (!cleanGenes) return existing;

    // Deduplication rule - if exact same set exists, return existing
    const duplicate = existing.some(
      s => s.genes === cleanGenes && s.selectedPlantType === selectedPlantType
    );
    if (duplicate) return existing;

    const newSet: StoredGeneSet = {
      timestamp: Date.now(),
      selectedPlantType,
      genes: cleanGenes
    };

    const updated = [newSet, ...existing].slice(0, 200);
    this.saveGeneSets(updated);
    return updated;
  }

  public static removeSavedGeneSet(timestamp: number): StoredGeneSet[] {
    const existing = this.getSavedGeneSets();
    const updated = existing.filter(s => s.timestamp !== timestamp);
    this.saveGeneSets(updated);
    return updated;
  }

  public static getScannerRegions(): ScannerRegion[] {
    try {
      const raw = localStorage.getItem(SCANNER_REGIONS_KEY);
      if (raw) {
        const parsed = JSON.parse(raw);
        if (Array.isArray(parsed) && parsed.length === 2) {
          return parsed;
        }
      }
    } catch {
      // ignore
    }
    return JSON.parse(JSON.stringify(DEFAULT_SCANNER_REGIONS));
  }

  public static saveScannerRegions(regions: ScannerRegion[]): void {
    const consent = this.getConsent();
    if (!consent.functional) return;

    try {
      localStorage.setItem(SCANNER_REGIONS_KEY, JSON.stringify(regions));
    } catch {
      // ignore
    }
  }

  public static resetScannerRegions(): ScannerRegion[] {
    try {
      localStorage.removeItem(SCANNER_REGIONS_KEY);
    } catch {
      // ignore
    }
    return JSON.parse(JSON.stringify(DEFAULT_SCANNER_REGIONS));
  }

  public static getSelectedPlantType(): string {
    try {
      const stored = localStorage.getItem(SELECTED_PLANT_KEY);
      if (stored) return stored;
    } catch {
      // ignore
    }
    return 'mixed-berry';
  }

  public static saveSelectedPlantType(plantType: string): void {
    const consent = this.getConsent();
    if (!consent.functional) return;

    try {
      localStorage.setItem(SELECTED_PLANT_KEY, plantType);
    } catch {
      // ignore
    }
  }
}
