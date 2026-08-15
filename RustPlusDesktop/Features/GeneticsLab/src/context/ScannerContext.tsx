import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { ScannerService, ScannerEvent } from '../services/scannerService.ts';
import { StorageService, ScannerProfile, ScannerRegion } from '../services/storageService.ts';
import { AudioService } from '../services/audioService.ts';
import { useWorkspace } from './WorkspaceContext.tsx';
import { useNotification } from './NotificationContext.tsx';
import { Sapling } from '../domain/genetics/Sapling.ts';

interface ScannerContextType {
  isScannerActive: boolean;
  isScannerInitializing: boolean;
  scannerStatusMessage: string;
  lastScannedGenes: string | null;
  lastConfidence: number;
  scannerPreviews: Record<number, string>;

  // Profiles
  profiles: ScannerProfile[];
  activeProfileId: string;
  activeProfile: ScannerProfile;
  setActiveProfileId: (id: string) => void;
  saveProfile: (profile: ScannerProfile) => void;
  deleteProfile: (id: string) => void;

  // Calibration Modal
  isCalibrationModalOpen: boolean;
  setIsCalibrationModalOpen: (open: boolean) => void;

  // Single Slot Correction
  correctionCandidate: { genes: string; confidence: number; slotConfidences?: number[] } | null;
  setCorrectionCandidate: (cand: { genes: string; confidence: number; slotConfidences?: number[] } | null) => void;

  // Actions
  startScanner: () => Promise<void>;
  stopScanner: () => void;
  moveScannerRegion: (regionIdx: number, dx: number, dy: number) => void;
  scaleScannerRegion: (regionIdx: number, dw: number) => void;
  resetScannerRegions: () => void;
  setScannerPreviewEnabled: (enabled: boolean) => void;
  getScannerDiagnostics: () => import('../services/scanner/scannerTypes.ts').ScannerDiagnostics;
}

const ScannerContext = createContext<ScannerContextType | null>(null);

export const ScannerProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { addClone, selectedPlant } = useWorkspace();
  const { notifySuccess, notifyWarning, notifyError, notifyInfo } = useNotification();

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

  const setActiveProfileId = useCallback((id: string) => {
    setActiveProfileIdState(id);
    StorageService.saveActiveScannerProfileId(id);
    const prof = profiles.find(p => p.id === id);
    if (prof) {
      StorageService.saveScannerRegions(prof.regions);
    }
  }, [profiles]);

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

  const deleteProfile = useCallback((id: string) => {
    setProfiles(prev => {
      if (prev.length <= 1) {
        notifyWarning('You must keep at least one profile.');
        return prev;
      }
      const updated = prev.filter(p => p.id !== id);
      StorageService.saveScannerProfiles(updated);
      if (activeProfileId === id) {
        setActiveProfileId(updated[0].id);
      }
      notifyInfo('Profile deleted');
      return updated;
    });
  }, [activeProfileId, setActiveProfileId, notifyWarning, notifyInfo]);

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
        notifyInfo('Scanner stopped.');
      } else if (evt.type === 'PREVIEW') {
        if (evt.regionIndex !== undefined && evt.previewDataUrl) {
          setScannerPreviews(prev => ({ ...prev, [evt.regionIndex!]: evt.previewDataUrl! }));
        }
      } else if (evt.type === 'SAPLING-FOUND') {
        if (evt.geneString) {
          const found = evt.geneString.toUpperCase();
          const conf = Math.round((evt.confidence || 0.95) * 100);
          setLastScannedGenes(found);
          setLastConfidence(conf);

          if (Sapling.isValidGeneString(found)) {
            // Fresh sound preference read to prevent stale closure
            const currentOpts = StorageService.getOptions();
            if (currentOpts.sounds) {
              AudioService.playPop(true);
            }
            scannerService.acknowledgeGeneHandled(found);
            addClone(found, { source: 'scanner' });
          } else if (found.length === 6) {
            // Uncertain gene - prompt for single slot correction if needed
            setCorrectionCandidate({
              genes: found,
              confidence: conf
            });
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
  }, [scannerService, addClone, notifySuccess, notifyInfo, notifyError]);

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

  const setScannerPreviewEnabled = useCallback((enabled: boolean) => {
    scannerService.setPreviewEnabled(enabled);
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
        setScannerPreviewEnabled,
        getScannerDiagnostics
      }}
    >
      {children}
    </ScannerContext.Provider>
  );
};

export const useScanner = () => {
  const context = useContext(ScannerContext);
  if (!context) throw new Error('useScanner must be used within a ScannerProvider');
  return context;
};
