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

export const DEFAULT_OPTIONS: ApplicationOptions = {
  withRepetitions: true,
  modifyMinimumTrackedScoreManually: false,
  minCrossbreedingSaplingsNumber: 2,
  maxCrossbreedingSaplingsNumber: 5,
  numberOfGenerations: 2,
  numberOfSaplingsAddedBetweenGenerations: 20,
  minimumTrackedScore: 4,
  geneScores: { ...DEFAULT_GENE_SCORES },
  darkMode: true,
  skipScannerGuide: false,
  autoSaveInputSets: true,
  sounds: false,
  numberOfWorkers: typeof navigator !== 'undefined' ? Math.max(1, navigator.hardwareConcurrency || 4) : 4
};

const CONSENT_PREFIX = 'rb-cookie-pref-v1';
const KEY_PREF_DECIDED = `${CONSENT_PREFIX}-storage-preference-decided`;
const KEY_FUNCTIONAL = `${CONSENT_PREFIX}-functional-cookies-and-storage`;
const KEY_ANALYTICS = `${CONSENT_PREFIX}-analytics-cookies`;
const KEY_ADVERTISEMENT = `${CONSENT_PREFIX}-advertisement-cookies`;

const OPTIONS_KEY = 'options-v4';
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
        const maxWorkers = typeof navigator !== 'undefined' ? navigator.hardwareConcurrency || 4 : 4;
        return {
          ...DEFAULT_OPTIONS,
          ...parsed,
          geneScores: {
            ...DEFAULT_GENE_SCORES,
            ...(parsed.geneScores || {})
          },
          numberOfWorkers: Math.min(parsed.numberOfWorkers || DEFAULT_OPTIONS.numberOfWorkers, maxWorkers)
        };
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
