import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { flushSync } from 'react-dom';
import { StorageService, TargetConfiguration, BreedingSession, BreedingSessionStep, FarmProject, StoredGeneSet } from '../services/storageService.ts';
import { SavedClone, CloneUtils } from '../domain/genetics/Clone.ts';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import { buildBreedingPlan } from '../domain/genetics/breedingPlan.ts';
import { useNotification } from './NotificationContext.tsx';
import { PLANT_ORDER, plantLabel } from '../domain/plants.ts';

const DEFAULT_SAMPLE_GENES: Record<string, string[]> = {
  hemp: ['GGYYHH', 'YGGYHH', 'HHGGYY', 'WYGGYX', 'GHGYHW', 'YYGGHH', 'XGYYHH'],
  'red-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  'blue-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  'yellow-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  'green-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  'white-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  'mixed-berry': ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX', 'HWYYGG'],
  potato: ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX'],
  pumpkin: ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX'],
  corn: ['GGYYHH', 'YYGGHH', 'GGGGYY', 'YYGGXX']
};

export interface GeneGroup {
  plant: string;
  /** Valid gene strings in the list, in order. */
  genes: string[];
}

interface WorkspaceContextType {
  selectedPlant: string;
  setSelectedPlant: (plant: string) => void;

  // Direct Manual Input State
  geneInputText: string;
  setGeneInputText: (text: string) => void;
  sourceSaplings: Sapling[]; // Valid saplings from current input text with line indices
  clones: SavedClone[]; // Clones for current crop
  allClones: SavedClone[];
  /** Every plant with genes in its list (plus the selected one), in plant order. */
  geneGroups: GeneGroup[];
  /** Empties one plant's list; the notice offers Undo. Returns the undo, if anything was cleared. */
  clearPlantGroup: (plant: string) => (() => void) | undefined;
  /**
   * Moves line `lineIndex` of the open plant's list to `toPlant`'s list. `currentText` is the
   * list as the editor shows it, which can be a keystroke ahead of the stored one.
   */
  moveGeneLine: (currentText: string, lineIndex: number, toPlant: string) => void;
  /** Replaces a scanned gene that turned out to be misread; true if it was found. */
  replaceScannedGene: (from: string, to: string, plant?: string) => boolean;
  /** Empties every plant's list; the notice offers Undo. Returns the undo, if anything was cleared. */
  clearAllGroups: () => (() => void) | undefined;
  
  // Clone operations
  addClone: (genetics: string, options?: Partial<Omit<SavedClone, 'id' | 'genetics' | 'cropType' | 'createdAt' | 'updatedAt'>> & { cropType?: string }) => SavedClone | null;
  addBatchClones: (geneStrings: string[], source?: 'manual' | 'scanner' | 'import') => number;
  appendScannedGene: (geneString: string, plant?: string) => void;
  updateClone: (id: string, updates: Partial<SavedClone>) => void;
  removeClone: (id: string) => void;
  duplicateClone: (id: string) => void;
  toggleFavorite: (id: string) => void;
  clearGeneInput: () => void;
  loadSampleGenes: () => void;

  // Saved Sets History (Auto-saved on calculate)
  savedGeneSets: StoredGeneSet[];
  saveCurrentGeneSet: () => void;
  loadSavedGeneSet: (set: StoredGeneSet) => void;
  deleteSavedGeneSet: (timestamp: number) => void;

  // Target Designer
  targetConfig: TargetConfiguration;
  setTargetConfig: React.Dispatch<React.SetStateAction<TargetConfiguration>>;
  setTargetSlot: (slotIndex: number, gene: string) => void;
  setTargetPreset: (targetString: string, mode?: 'exact' | 'at-least' | 'best-possible') => void;

  // Active Breeding Session
  activeSession: BreedingSession | null;
  startBreedingSession: (routeMap: GeneticsMap, targetGenetics: string) => void;
  updateSessionStepPlanted: (stepIndex: number, type: 'center' | 'surrounding', planted: boolean) => void;
  completeBreedingStep: (stepIndex: number) => void;
  abandonBreedingSession: () => void;
  /** Changes the plant type of the active session; saved with it and kept in its history. */
  setSessionCropType: (cropType: string) => void;
  breedingHistory: BreedingSession[];

  // Projects / Saved Workspaces
  projects: FarmProject[];
  saveCurrentAsProject: (name: string) => void;
  loadProject: (projectId: string) => void;
  deleteProject: (projectId: string) => void;

  // Import / Export
  exportWorkspaceJson: () => string;
  importWorkspaceJson: (jsonString: string) => { success: boolean; error?: string; count?: number };
}

const WorkspaceContext = createContext<WorkspaceContextType | null>(null);

export const WorkspaceProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { notifySuccess, notifyInfo, notifyError } = useNotification();

  const [selectedPlant, setSelectedPlantState] = useState<string>(() => StorageService.getSelectedPlantType());
  // Always start with empty gene lists. Users load plants via Scan, the SAMPLE
  // button, or the SAVED sets tab — we no longer auto-restore the previous input.
  //
  // One list per plant type: clones of several plants can be collected side by side (the
  // scanner files each under the plant it detects), and the selected plant's list is the one
  // shown, edited and calculated.
  const [geneInputs, setGeneInputs] = useState<Record<string, string>>({});
  const geneInputText = geneInputs[selectedPlant] ?? '';
  // Updated synchronously by setSelectedPlant, so a handler that switches plant and then
  // writes genes writes them to the new plant.
  const selectedPlantRef = useRef(selectedPlant);

  /** Writes a plant's list (the selected plant's unless `plant` is given). */
  const setGeneInputTextState = useCallback(
    (value: string | ((previous: string) => string), plant?: string) => {
      const key = plant ?? selectedPlantRef.current;
      setGeneInputs((all) => {
        const previous = all[key] ?? '';
        const next = typeof value === 'function' ? value(previous) : value;
        return next === previous ? all : { ...all, [key]: next };
      });
    },
    []
  );

  const [savedGeneSets, setSavedGeneSets] = useState<StoredGeneSet[]>(() => StorageService.getSavedGeneSets());
  const [targetConfig, setTargetConfig] = useState<TargetConfiguration>(() => StorageService.getTargetConfig());
  const [activeSession, setActiveSession] = useState<BreedingSession | null>(() => StorageService.getActiveBreedingSession());
  const [breedingHistory, setBreedingHistory] = useState<BreedingSession[]>(() => StorageService.getBreedingSessionHistory());
  const [projects, setProjects] = useState<FarmProject[]>(() => StorageService.getProjects());

  // Derive sourceSaplings and clones from geneInputText
  const { sourceSaplings, clones } = useMemo(() => {
    const lines = geneInputText.split('\n');
    const validSaplings: Sapling[] = [];
    const validClones: SavedClone[] = [];

    lines.forEach((rawLine, idx) => {
      const clean = rawLine.trim().toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);
      if (clean.length === 6 && Sapling.isValidGeneString(clean)) {
        validSaplings.push(new Sapling(clean, 0, idx));
        validClones.push(
          CloneUtils.create(clean, selectedPlant, {
            name: `Plant #${idx + 1}`,
            quantity: 1,
            source: 'manual'
          })
        );
      }
    });

    return { sourceSaplings: validSaplings, clones: validClones };
  }, [geneInputText, selectedPlant]);

  // Every plant's clones, for per-plant counts (the plant picker, the gene groups).
  const allClones = useMemo(() => {
    const out: SavedClone[] = [];
    for (const [plant, text] of Object.entries(geneInputs)) {
      if (plant === selectedPlant) continue;
      text.split('\n').forEach((line, idx) => {
        const clean = line.trim().toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);
        if (clean.length === 6 && Sapling.isValidGeneString(clean)) {
          out.push(CloneUtils.create(clean, plant, { name: `Plant #${idx + 1}`, quantity: 1, source: 'manual' }));
        }
      });
    }
    return [...clones, ...out];
  }, [geneInputs, selectedPlant, clones]);

  const geneGroups = useMemo<GeneGroup[]>(() => {
    const byPlant: Record<string, string[]> = {};
    for (const clone of allClones) (byPlant[clone.cropType] ??= []).push(clone.genetics);
    return PLANT_ORDER.filter((plant) => byPlant[plant]?.length || plant === selectedPlant).map((plant) => ({
      plant,
      genes: byPlant[plant] ?? []
    }));
  }, [allClones, selectedPlant]);

  const setSelectedPlant = useCallback((plant: string) => {
    selectedPlantRef.current = plant;
    setSelectedPlantState(plant);
    StorageService.saveSelectedPlantType(plant);
  }, []);

  const setGeneInputText = useCallback((text: string) => {
    setGeneInputTextState(text);
  }, []);

  // Always-current mirror of the input text so scan-dedup can read it without
  // making addClone/appendScannedGene change identity on every keystroke (which
  // would churn the scanner's event subscription).
  const geneInputsRef = useRef(geneInputs);
  useEffect(() => {
    geneInputsRef.current = geneInputs;
  }, [geneInputs]);

  const normalizeGene = (s: string) =>
    s.trim().toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);

  // Save target config on change
  useEffect(() => {
    StorageService.saveTargetConfig(targetConfig);
  }, [targetConfig]);

  // --- MANUAL INPUT & CLONE ACTIONS ---

  const clearPlantGroup = useCallback(
    (plant: string) => {
      const previous = geneInputsRef.current[plant] ?? '';
      if (!previous.trim()) return undefined;
      setGeneInputTextState('', plant);
      const undo = () => setGeneInputTextState(previous, plant);
      notifyInfo(`Cleared ${plantLabel(plant)}`, { label: 'Undo', onClick: undo });
      return undo;
    },
    [notifyInfo, setGeneInputTextState]
  );

  const moveGeneLine = useCallback(
    (currentText: string, lineIndex: number, toPlant: string) => {
      const fromPlant = selectedPlantRef.current;
      if (toPlant === fromPlant) return;
      const lines = currentText.split('\n');
      const gene = normalizeGene(lines[lineIndex] ?? '');
      if (!Sapling.isValidGeneString(gene)) return;

      const previousTarget = geneInputsRef.current[toPlant] ?? '';
      const remaining = lines.filter((_, i) => i !== lineIndex).join('\n');
      const targetGenes = previousTarget.split('\n').map(normalizeGene).filter(Boolean);
      const alreadyThere = targetGenes.includes(gene);
      const nextTarget = alreadyThere ? previousTarget : previousTarget.trim() ? `${previousTarget.trim()}\n${gene}` : gene;

      geneInputsRef.current = { ...geneInputsRef.current, [fromPlant]: remaining, [toPlant]: nextTarget };
      setGeneInputTextState(remaining, fromPlant);
      setGeneInputTextState(nextTarget, toPlant);
      notifyInfo(
        alreadyThere
          ? `${gene} was already in ${plantLabel(toPlant)}: removed from ${plantLabel(fromPlant)}`
          : `Moved ${gene} to ${plantLabel(toPlant)}`,
        {
          label: 'Undo',
          onClick: () => {
            setGeneInputTextState(currentText, fromPlant);
            setGeneInputTextState(previousTarget, toPlant);
          }
        }
      );
    },
    [notifyInfo, setGeneInputTextState]
  );

  const replaceScannedGene = useCallback(
    (from: string, to: string, plant?: string): boolean => {
      const wrong = normalizeGene(from);
      const right = normalizeGene(to);
      const plants = plant ? [plant, ...Object.keys(geneInputsRef.current)] : Object.keys(geneInputsRef.current);
      for (const key of plants) {
        const lines = (geneInputsRef.current[key] ?? '').split('\n');
        // The newest entry is the one just scanned.
        const index = lines.map(normalizeGene).lastIndexOf(wrong);
        if (index < 0) continue;
        const hasRight = lines.some((line) => normalizeGene(line) === right);
        const next = hasRight ? lines.filter((_, i) => i !== index) : lines.map((line, i) => (i === index ? right : line));
        const text = next.join('\n');
        geneInputsRef.current = { ...geneInputsRef.current, [key]: text };
        flushSync(() => setGeneInputTextState(text, key));
        return true;
      }
      return false;
    },
    [setGeneInputTextState]
  );

  // The toolbar's CLEAR: the open group only.
  const clearGeneInput = useCallback(() => {
    clearPlantGroup(selectedPlantRef.current);
  }, [clearPlantGroup]);

  const clearAllGroups = useCallback(() => {
    const previous = geneInputsRef.current;
    const filled = Object.values(previous).filter((text) => text.trim()).length;
    if (filled === 0) return undefined;
    setGeneInputs({});
    const undo = () => setGeneInputs(previous);
    notifyInfo(`Cleared ${filled} plant group${filled === 1 ? '' : 's'}`, { label: 'Undo', onClick: undo });
    return undo;
  }, [notifyInfo]);

  const loadSampleGenes = useCallback(() => {
    const samples = DEFAULT_SAMPLE_GENES[selectedPlant] || DEFAULT_SAMPLE_GENES['hemp'];
    setGeneInputTextState(samples.join('\n'));
    notifyInfo(`Loaded ${samples.length} sample plants for ${selectedPlant.replace(/-/g, ' ')}`);
  }, [selectedPlant, notifyInfo]);

  // Append a scanned gene string, de-duplicating against what's already in the
  // list. Returns true only if it was newly added (false when it's a duplicate).
  /** Adds a gene to a plant's list (the selected plant's by default); false if already there. */
  const appendScannedGene = useCallback((geneString: string, plant?: string): boolean => {
    const clean = normalizeGene(geneString);
    if (!Sapling.isValidGeneString(clean)) return false;
    const key = plant ?? selectedPlantRef.current;
    const current = geneInputsRef.current[key] ?? '';

    const existing = current.split('\n').map(normalizeGene).filter(Boolean);
    if (existing.includes(clean)) return false; // dedup: already scanned

    // Immediately update ref so any immediate subsequent frame knows it's already in the bank!
    const trimmed = current.trim();
    const updated = trimmed ? `${trimmed}\n${clean}` : clean;
    geneInputsRef.current = { ...geneInputsRef.current, [key]: updated };

    // flushSync guarantees immediate synchronous DOM repaint even if the window is in the background
    flushSync(() => {
      setGeneInputTextState(updated, key);
    });
    return true;
  }, [setGeneInputTextState]);

  const addClone = useCallback(
    (
      genetics: string,
      _options?: any
    ): SavedClone | null => {
      const clean = normalizeGene(genetics);
      if (!Sapling.isValidGeneString(clean)) {
        notifyError(`Invalid gene string: "${genetics}"`);
        return null;
      }
      // `cropType` files the clone under another plant's list (a scanner read of a
      // different plant); otherwise it joins the selected plant's.
      const plant: string = _options?.cropType ?? selectedPlant;
      // Null signals a duplicate (already in the list) so callers can react.
      if (!appendScannedGene(clean, plant)) return null;
      return CloneUtils.create(clean, plant);
    },
    [appendScannedGene, selectedPlant, notifyError]
  );

  const addBatchClones = useCallback(
    (geneStrings: string[], _source: any = 'manual'): number => {
      const valid = geneStrings
        .map(g => g.trim().toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6))
        .filter(g => g.length === 6 && Sapling.isValidGeneString(g));

      if (valid.length > 0) {
        setGeneInputTextState((prev) => {
          const trimmed = prev.trim();
          return trimmed ? `${trimmed}\n${valid.join('\n')}` : valid.join('\n');
        });
        notifySuccess(`Added ${valid.length} clone${valid.length > 1 ? 's' : ''}`);
      }
      return valid.length;
    },
    [notifySuccess]
  );

  const updateClone = useCallback((id: string, updates: Partial<SavedClone>) => {
    // If updating genetics, modify the text
    if (updates.genetics) {
      setGeneInputTextState(prev => {
        const lines = prev.split('\n');
        const idx = clones.findIndex(c => c.id === id);
        if (idx >= 0 && lines[idx]) {
          lines[idx] = updates.genetics!;
          return lines.join('\n');
        }
        return prev;
      });
    }
  }, [clones]);

  const removeClone = useCallback((id: string) => {
    setGeneInputTextState(prev => {
      const lines = prev.split('\n');
      const idx = clones.findIndex(c => c.id === id);
      if (idx >= 0) {
        lines.splice(idx, 1);
        return lines.join('\n');
      }
      return prev;
    });
  }, [clones]);

  const duplicateClone = useCallback((id: string) => {
    const target = clones.find(c => c.id === id);
    if (target) {
      setGeneInputTextState(prev => prev ? `${prev}\n${target.genetics}` : target.genetics);
      notifySuccess(`Duplicated [${target.genetics}]`);
    }
  }, [clones, notifySuccess]);

  const toggleFavorite = useCallback((_id: string) => {}, []);

  // --- SAVED SETS HISTORY ---

  const saveCurrentGeneSet = useCallback(() => {
    if (!geneInputText.trim()) return;
    const updated = StorageService.addSavedGeneSet(geneInputText, selectedPlant);
    setSavedGeneSets(updated);
    notifySuccess(`Saved set (${sourceSaplings.length} plants) to history`);
  }, [geneInputText, selectedPlant, sourceSaplings.length, notifySuccess]);

  const loadSavedGeneSet = useCallback(
    (set: StoredGeneSet) => {
      if (set.selectedPlantType) {
        setSelectedPlant(set.selectedPlantType);
      }
      setGeneInputTextState(set.genes, set.selectedPlantType || undefined);
      notifySuccess(`Loaded saved set (${set.genes.split('\n').filter(Boolean).length} plants)`);
    },
    [setSelectedPlant, notifySuccess]
  );

  const deleteSavedGeneSet = useCallback(
    (timestamp: number) => {
      const updated = StorageService.removeSavedGeneSet(timestamp);
      setSavedGeneSets(updated);
      notifyInfo('Deleted saved set');
    },
    [notifyInfo]
  );

  // --- TARGET DESIGNER ACTIONS ---

  const setTargetSlot = useCallback((slotIndex: number, gene: string) => {
    setTargetConfig(prev => {
      const chars = prev.targetGenetics.padEnd(6, '*').split('');
      chars[slotIndex] = gene.toUpperCase();
      return {
        ...prev,
        targetGenetics: chars.join('')
      };
    });
  }, []);

  const setTargetPreset = useCallback((targetString: string, mode: 'exact' | 'at-least' | 'best-possible' = 'exact') => {
    setTargetConfig({
      targetGenetics: targetString.toUpperCase(),
      matchMode: mode
    });
    notifyInfo(`Target preset set to [${targetString}]`);
  }, [notifyInfo]);

  // --- BREEDING SESSION ACTIONS ---

  const startBreedingSession = useCallback(
    (routeMap: GeneticsMap, targetGenetics: string) => {
      const steps: BreedingSessionStep[] = buildBreedingPlan(routeMap).map(step => ({
        ...step,
        isCenterPlanted: false,
        isSurroundingPlanted: false,
        isCompleted: false
      }));

      const newSession: BreedingSession = {
        id: `session_${Date.now()}`,
        cropType: selectedPlant,
        targetGenetics,
        steps,
        currentStepIndex: 0,
        status: 'active',
        startedAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };

      StorageService.saveActiveBreedingSession(newSession);
      setActiveSession(newSession);
      notifySuccess(`Started Breeding Session for [${targetGenetics}]`);
    },
    [selectedPlant, notifySuccess]
  );

  const updateSessionStepPlanted = useCallback(
    (stepIndex: number, type: 'center' | 'surrounding', planted: boolean) => {
      if (!activeSession) return;
      // A new step object, not an edit of the old one: views memoised on the step (the 3D
      // planter) only see the change when the reference changes.
      const updatedSteps = activeSession.steps.map((s, i) =>
        i !== stepIndex
          ? s
          : {
              ...s,
              ...(type === 'center' ? { isCenterPlanted: planted } : {}),
              ...(type === 'surrounding' ? { isSurroundingPlanted: planted } : {})
            }
      );
      const updatedSession: BreedingSession = {
        ...activeSession,
        steps: updatedSteps,
        updatedAt: new Date().toISOString()
      };
      StorageService.saveActiveBreedingSession(updatedSession);
      setActiveSession(updatedSession);
    },
    [activeSession]
  );

  const setSessionCropType = useCallback(
    (cropType: string) => {
      if (!activeSession || activeSession.cropType === cropType) return;
      const updatedSession: BreedingSession = {
        ...activeSession,
        cropType,
        updatedAt: new Date().toISOString()
      };
      StorageService.saveActiveBreedingSession(updatedSession);
      setActiveSession(updatedSession);
    },
    [activeSession]
  );

  const completeBreedingStep = useCallback(
    (stepIndex: number) => {
      if (!activeSession) return;
      const updatedSteps = activeSession.steps.map((s, i) =>
        i === stepIndex ? { ...s, isCompleted: true, isCenterPlanted: true, isSurroundingPlanted: true } : s
      );

      const nextStepIdx = stepIndex + 1;
      const isAllComplete = nextStepIdx >= updatedSteps.length;

      const updatedSession: BreedingSession = {
        ...activeSession,
        steps: updatedSteps,
        currentStepIndex: Math.min(nextStepIdx, updatedSteps.length - 1),
        status: isAllComplete ? 'completed' : 'active',
        completedAt: isAllComplete ? new Date().toISOString() : undefined,
        updatedAt: new Date().toISOString()
      };

      if (isAllComplete) {
        StorageService.saveActiveBreedingSession(null);
        StorageService.addBreedingSessionToHistory(updatedSession);
        setActiveSession(null);
        setBreedingHistory(prev => [updatedSession, ...prev]);
        notifySuccess(`🎉 Breeding complete for [${activeSession.targetGenetics}]!`);
      } else {
        StorageService.saveActiveBreedingSession(updatedSession);
        setActiveSession(updatedSession);
        notifySuccess(`Generation ${stepIndex + 1} marked complete. Moving to step ${nextStepIdx + 1}`);
      }
    },
    [activeSession, notifySuccess]
  );

  const abandonBreedingSession = useCallback(() => {
    if (!activeSession) return;
    const abandoned: BreedingSession = {
      ...activeSession,
      status: 'abandoned',
      updatedAt: new Date().toISOString()
    };
    StorageService.saveActiveBreedingSession(null);
    StorageService.addBreedingSessionToHistory(abandoned);
    setActiveSession(null);
    setBreedingHistory(prev => [abandoned, ...prev]);
    notifyInfo('Breeding session closed.');
  }, [activeSession, notifyInfo]);

  // --- PROJECTS ---

  const saveCurrentAsProject = useCallback(
    (name: string) => {
      const project: FarmProject = {
        id: `project_${Date.now()}`,
        name: name.trim() || `${selectedPlant} Project`,
        cropType: selectedPlant,
        targetGenetics: targetConfig.targetGenetics,
        clones: [...clones],
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString()
      };
      const updated = StorageService.addProject(project);
      setProjects(updated);
      notifySuccess(`Saved project "${project.name}"`);
    },
    [selectedPlant, targetConfig, clones, notifySuccess]
  );

  const loadProject = useCallback(
    (projectId: string) => {
      const proj = projects.find(p => p.id === projectId);
      if (!proj) return;

      setSelectedPlant(proj.cropType);
      setTargetConfig({
        targetGenetics: proj.targetGenetics,
        matchMode: 'best-possible'
      });
      setGeneInputTextState(proj.clones.map(c => c.genetics).join('\n'), proj.cropType);
      notifySuccess(`Loaded project "${proj.name}" (${proj.clones.length} clones)`);
    },
    [projects, setSelectedPlant, notifySuccess]
  );

  const deleteProject = useCallback(
    (projectId: string) => {
      const updated = StorageService.deleteProject(projectId);
      setProjects(updated);
      notifyInfo('Project deleted');
    },
    [notifyInfo]
  );

  // --- IMPORT / EXPORT ---

  const exportWorkspaceJson = useCallback((): string => {
    const payload = {
      version: '3.1.0',
      exportedAt: new Date().toISOString(),
      cropType: selectedPlant,
      targetConfig,
      clones: clones
    };
    return JSON.stringify(payload, null, 2);
  }, [selectedPlant, targetConfig, clones]);

  const importWorkspaceJson = useCallback(
    (jsonString: string): { success: boolean; error?: string; count?: number } => {
      try {
        const data = JSON.parse(jsonString);
        if (!data || typeof data !== 'object') {
          return { success: false, error: 'Invalid JSON format' };
        }

        const cropType = data.cropType || selectedPlant;
        const importedGeneLines: string[] = [];

        if (Array.isArray(data.clones)) {
          for (const item of data.clones) {
            const genes = typeof item === 'string' ? item : item.genetics;
            if (genes && Sapling.isValidGeneString(genes)) {
              importedGeneLines.push(genes.toUpperCase());
            }
          }
        }

        if (data.targetConfig?.targetGenetics) {
          setTargetConfig({
            targetGenetics: data.targetConfig.targetGenetics,
            matchMode: data.targetConfig.matchMode || 'best-possible'
          });
        }

        if (importedGeneLines.length > 0) {
          setSelectedPlant(cropType);
          setGeneInputTextState(importedGeneLines.join('\n'), cropType);
          notifySuccess(`Successfully imported ${importedGeneLines.length} clones for ${cropType}`);
          return { success: true, count: importedGeneLines.length };
        }

        return { success: false, error: 'No valid clones found in import data' };
      } catch (err: any) {
        return { success: false, error: err.message || 'Corrupt JSON file' };
      }
    },
    [selectedPlant, setSelectedPlant, notifySuccess]
  );

  return (
    <WorkspaceContext.Provider
      value={{
        selectedPlant,
        setSelectedPlant,
        geneInputText,
        setGeneInputText,
        clones,
        allClones,
        geneGroups,
        clearPlantGroup,
        clearAllGroups,
        moveGeneLine,
        replaceScannedGene,
        sourceSaplings,
        addClone,
        addBatchClones,
        appendScannedGene,
        updateClone,
        removeClone,
        duplicateClone,
        toggleFavorite,
        clearGeneInput,
        loadSampleGenes,
        savedGeneSets,
        saveCurrentGeneSet,
        loadSavedGeneSet,
        deleteSavedGeneSet,
        targetConfig,
        setTargetConfig,
        setTargetSlot,
        setTargetPreset,
        activeSession,
        startBreedingSession,
        updateSessionStepPlanted,
        completeBreedingStep,
        abandonBreedingSession,
        setSessionCropType,
        breedingHistory,
        projects,
        saveCurrentAsProject,
        loadProject,
        deleteProject,
        exportWorkspaceJson,
        importWorkspaceJson
      }}
    >
      {children}
    </WorkspaceContext.Provider>
  );
};

export const useWorkspace = () => {
  const context = useContext(WorkspaceContext);
  if (!context) throw new Error('useWorkspace must be used within a WorkspaceProvider');
  return context;
};
