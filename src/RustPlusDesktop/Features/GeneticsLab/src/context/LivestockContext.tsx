import React, { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { StorageService } from '../services/storageService.ts';
import { sanitizeHerd } from '../domain/livestock/herdCodec.ts';
import {
  LivestockAnimal,
  LivestockGeneRow,
  ObservedCondition,
  LivestockSex,
  LivestockSpecies,
  createAnimal,
  displayName,
  encodeAnimalGenes,
  findScannedAnimal,
  suggestAnimalName
} from '../domain/livestock/animal.ts';
import {
  IDLE_LIVESTOCK_SCAN_STATE,
  LivestockScanSession,
  LivestockScanState
} from '../services/livestock/livestockScanSession.ts';
import { StableRead } from '../services/livestock/livestockReadStabilizer.ts';
import { PortraitMatch, portraitSex } from '../services/livestock/portraitClassifier.ts';
import { useNotification } from './NotificationContext.tsx';


interface LivestockContextValue {
  herd: LivestockAnimal[];
  addAnimal: (animal: LivestockAnimal) => void;
  updateAnimal: (id: string, patch: Partial<LivestockAnimal>) => void;
  removeAnimal: (id: string) => void;
  replaceHerd: (herd: LivestockAnimal[]) => void;
  clearHerd: () => void;

  selectedId: string | null;
  setSelectedId: (id: string | null) => void;

  /**
   * What a scanned animal is added as. With `scanAuto`, the panel's portrait decides the
   * species (and the sex of adult cattle); the manual kind is the fallback.
   */
  scanAuto: boolean;
  setScanAuto: (auto: boolean) => void;
  scanSpecies: LivestockSpecies;
  scanSex: LivestockSex;
  setScanKind: (species: LivestockSpecies, sex: LivestockSex) => void;

  scan: LivestockScanState;
  startDesktopScan: () => Promise<void>;
  startCameraScan: (video: HTMLVideoElement) => Promise<void>;
  stopScan: () => void;
}

const LivestockContext = createContext<LivestockContextValue | null>(null);

function rowsFromRead(rows: LivestockGeneRow[]): LivestockGeneRow[] {
  return rows.map((row) => ({ levels: [...row.levels], marker: { ...row.marker } }));
}

export const LivestockProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { notifySuccess, notifyError } = useNotification();
  const [herd, setHerd] = useState<LivestockAnimal[]>(() => sanitizeHerd(StorageService.getLivestockHerdRaw()));
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [scanSpecies, setScanSpecies] = useState<LivestockSpecies>('cattle');
  const [scanSex, setScanSex] = useState<LivestockSex>('female');
  const [scanAuto, setScanAuto] = useState(true);
  const [scan, setScan] = useState<LivestockScanState>(IDLE_LIVESTOCK_SCAN_STATE);

  // The session outlives renders; refs give its callbacks the current herd and settings.
  const herdRef = useRef(herd);
  const kindRef = useRef({ species: scanSpecies, sex: scanSex, auto: scanAuto });
  herdRef.current = herd;
  kindRef.current = { species: scanSpecies, sex: scanSex, auto: scanAuto };

  useEffect(() => {
    StorageService.saveLivestockHerdRaw(herd);
  }, [herd]);

  const addAnimal = useCallback((animal: LivestockAnimal) => {
    setHerd((current) => [...current, animal]);
  }, []);

  const updateAnimal = useCallback((id: string, patch: Partial<LivestockAnimal>) => {
    setHerd((current) => current.map((a) => (a.id === id ? { ...a, ...patch, id } : a)));
  }, []);

  const removeAnimal = useCallback((id: string) => {
    setHerd((current) =>
      current
        .filter((a) => a.id !== id)
        // Children keep existing; they just lose the link to a parent that is gone.
        .map((a) => ({
          ...a,
          motherId: a.motherId === id ? undefined : a.motherId,
          fatherId: a.fatherId === id ? undefined : a.fatherId
        }))
    );
    setSelectedId((current) => (current === id ? null : current));
  }, []);

  const replaceHerd = useCallback((next: LivestockAnimal[]) => {
    setHerd(next);
    setSelectedId(null);
  }, []);

  const clearHerd = useCallback(() => {
    const previous = herdRef.current;
    setHerd([]);
    setSelectedId(null);
    notifySuccess(`Removed ${previous.length} animal${previous.length === 1 ? '' : 's'}`, {
      label: 'Undo',
      onClick: () => setHerd(previous)
    });
  }, [notifySuccess]);

  const setScanKind = useCallback((species: LivestockSpecies, sex: LivestockSex) => {
    setScanSpecies(species);
    setScanSex(sex);
    setScanAuto(false);
  }, []);

  const addScanned = useCallback(
    (read: StableRead, observed: ObservedCondition | null, portrait: PortraitMatch | null, gameName: string | null) => {
      const manual = kindRef.current;
      let species = manual.species;
      let sex = manual.sex;
      if (manual.auto && portrait) {
        species = portrait.species;
        // Calves, lambs and sheep share one portrait for both sexes, and an uncertain
        // cow-or-calf says nothing about sex: the panel cannot tell.
        sex = (portrait.kind && portraitSex(portrait.kind)) ?? 'unknown';
      }
      const animal = createAnimal({
        species,
        sex,
        name: gameName ?? suggestAnimalName(species, sex, herdRef.current),
        gameName: gameName ?? undefined,
        rows: rowsFromRead(read.rows),
        observed: observed ?? undefined,
        source: 'scan'
      });
      setHerd((current) => [...current, animal]);
      setSelectedId(animal.id);
      notifySuccess(`Added ${displayName(animal)}`, {
        label: 'Undo',
        onClick: () => setHerd((current) => current.filter((a) => a.id !== animal.id))
      });
    },
    [notifySuccess]
  );

  const sessionRef = useRef<LivestockScanSession | null>(null);
  const getSession = useCallback(() => {
    if (!sessionRef.current) {
      sessionRef.current = new LivestockScanSession((event) => {
        if (event.type === 'state') {
          setScan(event.state);
        } else if (event.type === 'confirmed') {
          const existing = findScannedAnimal(herdRef.current, {
            rows: event.read.rows,
            name: event.name,
            species: event.portrait?.species ?? null
          });
          if (!existing) {
            addScanned(event.read, event.observed, event.portrait, event.name);
            return;
          }
          // The same animal again: refresh it in place, never add a second copy.
          const rows = rowsFromRead(event.read.rows);
          const genesChanged = encodeAnimalGenes(rows) !== encodeAnimalGenes(existing.rows);
          const named = !existing.gameName && !!event.name;
          const sexFromPortrait = event.portrait?.kind ? portraitSex(event.portrait.kind) : null;
          const updated: LivestockAnimal = {
            ...existing,
            rows,
            observed: event.observed ?? existing.observed,
            gameName: existing.gameName ?? event.name ?? undefined,
            name: named && /^(Cow|Bull|Calf|Cattle|Ewe|Ram|Sheep|Lamb) \d+$/.test(existing.name) ? event.name! : existing.name,
            sex: existing.sex === 'unknown' && sexFromPortrait ? sexFromPortrait : existing.sex
          };
          setHerd((current) => current.map((a) => (a.id === existing.id ? updated : a)));
          setSelectedId(existing.id);
          // A rescan that changes nothing but age and condition needs no announcement.
          if (genesChanged || named) {
            notifySuccess(`Updated ${displayName(updated)}${genesChanged ? ': genes changed' : ''}`, {
              label: 'Undo',
              onClick: () => setHerd((current) => current.map((a) => (a.id === existing.id ? existing : a)))
            });
          }
        }
      });
    }
    return sessionRef.current;
  }, [addScanned, notifySuccess]);

  useEffect(() => () => sessionRef.current?.stop(), []);

  const startDesktopScan = useCallback(async () => {
    const session = getSession();
    await session.startDesktop();
    const state = session.getState();
    if (state.status === 'error' && state.error) notifyError(state.error);
  }, [getSession, notifyError]);

  const startCameraScan = useCallback(
    async (video: HTMLVideoElement) => {
      const session = getSession();
      await session.startCamera(video);
      const state = session.getState();
      if (state.status === 'error' && state.error) notifyError(state.error);
    },
    [getSession, notifyError]
  );

  const stopScan = useCallback(() => {
    sessionRef.current?.stop();
  }, []);


  const value = useMemo<LivestockContextValue>(
    () => ({
      herd,
      addAnimal,
      updateAnimal,
      removeAnimal,
      replaceHerd,
      clearHerd,
      selectedId,
      setSelectedId,
      scanAuto,
      setScanAuto,
      scanSpecies,
      scanSex,
      setScanKind,
      scan,
      startDesktopScan,
      startCameraScan,
      stopScan
    }),
    [
      herd,
      addAnimal,
      updateAnimal,
      removeAnimal,
      replaceHerd,
      clearHerd,
      selectedId,
      scanAuto,
      scanSpecies,
      scanSex,
      setScanKind,
      scan,
      startDesktopScan,
      startCameraScan,
      stopScan
    ]
  );

  return <LivestockContext.Provider value={value}>{children}</LivestockContext.Provider>;
};

export function useLivestock(): LivestockContextValue {
  const context = useContext(LivestockContext);
  if (!context) throw new Error('useLivestock must be used inside LivestockProvider');
  return context;
}
