import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { ScannerService, ScannerEvent } from '../services/scannerService.ts';
import { StorageService, ScannerProfile, ScannerRegion, DEFAULT_SCANNER_REGIONS } from '../services/storageService.ts';
import { CloneUtils } from '../domain/genetics/Clone.ts';
import { useNotification } from './NotificationContext.tsx';
import { useWorkspace } from './WorkspaceContext.tsx';

export interface ScannerContextValue {
  isScannerActive: boolean;
  isScannerInitializing: boolean;
  scannerStatusMessage: string;
  lastScannedGenes: string | null;
  lastConfidence: number;
  scannerPreviews: Record<number, string>;
  profiles: ScannerProfile[];
  activeProfileId: string;
  activeProfile: ScannerProfile;
  setActiveProfileId: (id: string) => void;
  saveProfile: (profile: ScannerProfile) => void;
  createCustomProfile: (name: string, resolutionName?: string) => ScannerProfile;
  exportProfileJson: (profileId?: string) => string;
  importProfileJson: (jsonStr: string) => boolean;
  deleteProfile: (id: string) => void;
  isCalibrationModalOpen: boolean;
  setIsCalibrationModalOpen: (open: boolean) => void;
  correctionCandidate: { genes: string; confidence: number; slotConfidences?: number[] } | null;
  setCorrectionCandidate: (cand: { genes: string; confidence: number; slotConfidences?: number[] } | null) => void;
  startScanner: () => Promise<void>;
  stopScanner: () => void;
  moveScannerRegion: (regionIdx: number, dx: number, dy: number) => void;
  scaleScannerRegion: (regionIdx: number, dw: number) => void;
  resetScannerRegions: () => void;
  getScannerDiagnostics: () => any;
  setScannerPreviewEnabled: (enabled: boolean) => void;
  acknowledgeGeneHandled: (geneString: string) => void;
}

const ScannerContext = createContext<ScannerContextValue | null>(null);

export const ScannerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { notifySuccess, notifyInfo, notifyWarning, notifyError } = useNotification();
  const { addClone, selectedPlant } = useWorkspace();

  const [isScannerActive, setIsScannerActive] = useState(false);
  const [isScannerInitializing, setIsScannerInitializing] = useState(false);
  const [scannerStatusMessage, setScannerStatusMessage] = useState('');
  const [lastScannedGenes, setLastScannedGenes] = useState<string | null>(null);
  const [lastConfidence, setLastConfidence] = useState(0);
  const [scannerPreviews, setScannerPreviews] = useState<Record<number, string>>({});

  const [profiles, setProfiles] = useState<ScannerProfile[]>(() => StorageService.getScannerProfiles());
  const [activeProfileId, setActiveProfileIdState] = useState<string>(() => StorageService.getActiveScannerProfileId());
  const [isCalibrationModalOpen, setIsCalibrationModalOpen] = useState(false);
  const [correctionCandidate, setCorrectionCandidate] = useState<{ genes: string; confidence: number; slotConfidences?: number[] } | null>(null);

  const scannerService = useMemo(() => new ScannerService(), []);

  // Sound preference ref to avoid stale closures (Rule #28)
  const soundPrefRef = useRef(true);
  useEffect(() => {
    const opts = StorageService.getOptions();
    soundPrefRef.current = !!opts.sounds;
  }, []);

  const activeProfile = useMemo(() => {
    return profiles.find(p => p.id === activeProfileId) || profiles[0];
  }, [profiles, activeProfileId]);

  // When active profile changes, update scannerService regions immediately
  const setActiveProfileId = useCallback((id: string) => {
    setActiveProfileIdState(id);
    StorageService.saveActiveScannerProfileId(id);
    const prof = profiles.find(p => p.id === id);
    if (prof && prof.regions) {
      scannerService.setRegions(prof.regions);
      notifyInfo(`Switched to "${prof.name}" preset`);
    }
  }, [profiles, scannerService, notifyInfo]);

  const saveProfile = useCallback((profile: ScannerProfile) => {
    setProfiles(prev => {
      const idx = prev.findIndex(p => p.id === profile.id);
      let updated: ScannerProfile[];
      if (idx >= 0) {
        updated = [...prev];
        updated[idx] = profile;
      } else {
        updated = [...prev, profile];
      }
      StorageService.saveScannerProfiles(updated);
      return updated;
    });
    notifySuccess(`Saved scanner profile "${profile.name}"`);
  }, [notifySuccess]);

  const createCustomProfile = useCallback((name: string, resolutionName?: string): ScannerProfile => {
    const currentRegions = scannerService.getRegions();
    const newProfile: ScannerProfile = {
      id: `custom_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
      name: name.trim() || 'Custom Preset',
      resolutionName: resolutionName || `${window.screen.width} x ${window.screen.height}`,
      regions: currentRegions.map(r => ({ ...r })),
      scale: 1
    };

    setProfiles(prev => {
      const updated = [...prev, newProfile];
      StorageService.saveScannerProfiles(updated);
      return updated;
    });

    setActiveProfileIdState(newProfile.id);
    StorageService.saveActiveScannerProfileId(newProfile.id);
    notifySuccess(`Created custom preset "${newProfile.name}"`);
    return newProfile;
  }, [scannerService, notifySuccess]);

  const exportProfileJson = useCallback((profileId?: string): string => {
    if (profileId) {
      const target = profiles.find(p => p.id === profileId) || activeProfile;
      return JSON.stringify(target, null, 2);
    }
    return JSON.stringify(profiles, null, 2);
  }, [profiles, activeProfile]);

  const importProfileJson = useCallback((jsonStr: string): boolean => {
    try {
      const parsed = JSON.parse(jsonStr);
      let importedProfiles: ScannerProfile[] = [];

      if (Array.isArray(parsed)) {
        importedProfiles = parsed.filter(p => p.name && Array.isArray(p.regions) && p.regions.length >= 2);
      } else if (parsed && parsed.name && Array.isArray(parsed.regions)) {
        importedProfiles = [{
          id: `custom_${Date.now()}_${Math.random().toString(36).slice(2, 6)}`,
          name: parsed.name,
          resolutionName: parsed.resolutionName || 'Custom',
          regions: parsed.regions,
          scale: parsed.scale || 1
        }];
      }

      if (importedProfiles.length === 0) {
        notifyError('Invalid preset JSON format');
        return false;
      }

      setProfiles(prev => {
        const merged = [...prev];
        for (const imp of importedProfiles) {
          const existingIdx = merged.findIndex(p => p.id === imp.id || p.name === imp.name);
          if (existingIdx >= 0) {
            merged[existingIdx] = imp;
          } else {
            merged.push(imp);
          }
        }
        StorageService.saveScannerProfiles(merged);
        return merged;
      });

      const firstImported = importedProfiles[0];
      setActiveProfileIdState(firstImported.id);
      StorageService.saveActiveScannerProfileId(firstImported.id);
      scannerService.setRegions(firstImported.regions);
      notifySuccess(`Successfully imported ${importedProfiles.length} preset(s)!`);
      return true;
    } catch {
      notifyError('Failed to parse preset JSON');
      return false;
    }
  }, [scannerService, notifySuccess, notifyError]);

  const deleteProfile = useCallback((id: string) => {
    setProfiles(prev => {
      if (prev.length <= 1) {
        notifyWarning('You must keep at least one profile.');
        return prev;
      }
      const updated = prev.filter(p => p.id !== id);
      StorageService.saveScannerProfiles(updated);
      if (activeProfileId === id) {
        const nextActive = updated[0];
        setActiveProfileIdState(nextActive.id);
        StorageService.saveActiveScannerProfileId(nextActive.id);
        scannerService.setRegions(nextActive.regions);
      }
      notifyInfo('Profile deleted');
      return updated;
    });
  }, [activeProfileId, scannerService, notifyWarning, notifyInfo]);

  // Scanner Event Listener
  useEffect(() => {
    const postScannerState = (active: boolean) => {
      try {
        (window as any).chrome?.webview?.postMessage({ type: 'scanner-state', active });
      } catch {
        // ignore
      }
    };

    const unsubscribe = scannerService.addEventListener((evt: ScannerEvent) => {
      if (evt.type === 'INITIALIZING') {
        setIsScannerInitializing(true);
        setScannerStatusMessage('Initializing OCR engine...');
        postScannerState(true);
      } else if (evt.type === 'STARTED') {
        setIsScannerInitializing(false);
        setIsScannerActive(true);
        setScannerStatusMessage('Scanner active. Hover over plant clones in Rust.');
        postScannerState(true);
        notifySuccess('Scanner started. Hover over clones in Rust.');
      } else if (evt.type === 'STOPPED') {
        setIsScannerActive(false);
        setIsScannerInitializing(false);
        setScannerStatusMessage('');
        postScannerState(false);
      } else if (evt.type === 'PREVIEW') {
        if (evt.previewDataUrl && typeof evt.regionIndex === 'number') {
          setScannerPreviews(prev => ({ ...prev, [evt.regionIndex!]: evt.previewDataUrl! }));
        }
      } else if (evt.type === 'SAPLING-FOUND') {
        if (evt.geneString) {
          setLastScannedGenes(evt.geneString);
          setLastConfidence(evt.confidence || 90);

          if (soundPrefRef.current) {
            try {
              const audio = new Audio('./assets/scan-beep.mp3');
              audio.volume = 0.4;
              audio.play().catch(() => {});
            } catch {
              // ignore
            }
          }

          const added = addClone(evt.geneString, {
            source: 'scanner',
            quantity: 1
          });

          if (added) {
            notifySuccess(`Scanned Clone: ${evt.geneString} (${Math.round(evt.confidence || 0)}% conf)`);
          } else {
            notifyInfo(`Clone ${evt.geneString} quantity incremented.`);
          }
        }
      } else if (evt.type === 'ERROR') {
        setIsScannerActive(false);
        setIsScannerInitializing(false);
        setScannerStatusMessage(`Error: ${evt.error}`);
        postScannerState(false);
        if (evt.error && !evt.error.toLowerCase().includes('cancel')) {
          notifyError(`Scanner error: ${evt.error}`);
        }
      }
    });

    return () => {
      unsubscribe();
    };
  }, [scannerService, addClone, selectedPlant, notifySuccess, notifyInfo, notifyError]);

  // Non-destructive startScanner (Rule #27: DO NOT DESTROY RESULTS ON START/CANCEL)
  const startScanner = useCallback(async () => {
    try {
      await scannerService.start();
    } catch (err: any) {
      setIsScannerActive(false);
      setIsScannerInitializing(false);
      if (err.name !== 'NotAllowedError') {
        notifyError(`Could not start scanner: ${err.message}`);
      }
    }
  }, [scannerService, notifyError]);

  const stopScanner = useCallback(() => {
    scannerService.stop();
  }, [scannerService]);

  const moveScannerRegion = useCallback((regionIdx: number, dx: number, dy: number) => {
    scannerService.moveRegion(regionIdx, dx, dy);
    // Sync active profile in context
    const currentRegions = scannerService.getRegions();
    setProfiles(prev => {
      const idx = prev.findIndex(p => p.id === activeProfileId);
      if (idx >= 0) {
        const updated = [...prev];
        updated[idx] = { ...updated[idx], regions: currentRegions.map(r => ({ ...r })) };
        StorageService.saveScannerProfiles(updated);
        return updated;
      }
      return prev;
    });
  }, [scannerService, activeProfileId]);

  const scaleScannerRegion = useCallback((regionIdx: number, dw: number) => {
    scannerService.scaleRegion(regionIdx, dw);
    // Sync active profile in context
    const currentRegions = scannerService.getRegions();
    setProfiles(prev => {
      const idx = prev.findIndex(p => p.id === activeProfileId);
      if (idx >= 0) {
        const updated = [...prev];
        updated[idx] = { ...updated[idx], regions: currentRegions.map(r => ({ ...r })) };
        StorageService.saveScannerProfiles(updated);
        return updated;
      }
      return prev;
    });
  }, [scannerService, activeProfileId]);

  const resetScannerRegions = useCallback(() => {
    const defaultRegions = scannerService.resetRegions();
    setProfiles(prev => {
      const idx = prev.findIndex(p => p.id === activeProfileId);
      if (idx >= 0) {
        const updated = [...prev];
        updated[idx] = { ...updated[idx], regions: defaultRegions.map(r => ({ ...r })) };
        StorageService.saveScannerProfiles(updated);
        return updated;
      }
      return prev;
    });
    notifyInfo('Reset region coordinates to default');
  }, [scannerService, activeProfileId, notifyInfo]);

  const getScannerDiagnostics = useCallback(() => {
    return scannerService.getDiagnostics();
  }, [scannerService]);

  const setScannerPreviewEnabled = useCallback((enabled: boolean) => {
    scannerService.setPreviewEnabled(enabled);
  }, [scannerService]);

  const acknowledgeGeneHandled = useCallback((geneString: string) => {
    scannerService.acknowledgeGeneHandled(geneString);
  }, [scannerService]);

  return (
    <ScannerContext.Provider
      value={{
        isScannerActive,
        isScannerInitializing,
        scannerStatusMessage,
        lastScannedGenes,
        lastConfidence,
        scannerPreviews,
        profiles,
        activeProfileId,
        activeProfile,
        setActiveProfileId,
        saveProfile,
        createCustomProfile,
        exportProfileJson,
        importProfileJson,
        deleteProfile,
        isCalibrationModalOpen,
        setIsCalibrationModalOpen,
        correctionCandidate,
        setCorrectionCandidate,
        startScanner,
        stopScanner,
        moveScannerRegion,
        scaleScannerRegion,
        resetScannerRegions,
        getScannerDiagnostics,
        setScannerPreviewEnabled,
        acknowledgeGeneHandled
      }}
    >
      {children}
    </ScannerContext.Provider>
  );
};

export const useScanner = (): ScannerContextValue => {
  const context = useContext(ScannerContext);
  if (!context) {
    throw new Error('useScanner must be used within a ScannerProvider');
  }
  return context;
};
