import { Sapling, GeneScores } from '../domain/genetics/Sapling.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { resultMapGroupsSortingFunction, appendAndOrganizeResults } from '../domain/genetics/sorting.ts';
import { getBestSaplingsForNextGeneration, linkGenerationTree } from '../domain/genetics/generationSelection.ts';
import {
  serializeSapling,
  deserializeGeneticsMap,
  deserializeGeneticsMapGroup
} from '../domain/genetics/serialization.ts';
import {
  getNumberOfCrossbreedingCombinations,
  getWorkChunks,
  getMaxPositionsCount,
  setNextPosition,
  WorkChunk,
  GenerationInfo
} from '../domain/genetics/combinations.ts';
import { evaluateCombination } from '../domain/genetics/crossbreeding.ts';

/**
 * Max result groups emitted to the UI. Caps main-thread route-analysis + render
 * work so heavy Thorough / Gen-3 runs don't freeze the UI. Results are sorted
 * best-first before slicing, and the UI groups/paginates further.
 */
const MAX_RETURNED_RESULTS = 500;

export interface ApplicationOptions {
  withRepetitions: boolean;
  modifyMinimumTrackedScoreManually: boolean;
  minCrossbreedingSaplingsNumber: number;
  maxCrossbreedingSaplingsNumber: number;
  numberOfGenerations: number;
  numberOfSaplingsAddedBetweenGenerations: number;
  minimumTrackedScore: number;
  geneScores: GeneScores;
  darkMode: boolean;
  skipScannerGuide: boolean;
  autoSaveInputSets: boolean;
  sounds: boolean;
  numberOfWorkers: number;
  cpuLimitPercent: number;
}

export type SimulatorEventType =
  | 'PROGRESS_UPDATE'
  | 'PARTIAL_RESULTS'
  | 'DONE_GENERATION'
  | 'DONE';

export interface SimulatorEvent {
  type: SimulatorEventType;
  generationIndex?: number;
  progressPercent?: number;
  estimatedTimeMs?: number | null;
  mapGroups?: GeneticsMapGroup[];
  totalTimeMs?: number;
  stage?: string;
  processedCombinations?: number;
  totalCombinations?: number;
}

export type SimulatorEventListener = (event: SimulatorEvent) => void;

interface ProgressRecord {
  timestamp: number;
  combinationsProcessed: number;
}

export class CrossbreedingOrchestrator {
  private listeners: SimulatorEventListener[] = [];
  private activeWorkers: Worker[] = [];
  private currentRunId = '';
  private isRunning = false;
  private isCancelled = false;

  private allAccumulatedGroupsMap = new Map<string, GeneticsMapGroup>();
  private currentGenGroupsMap = new Map<string, GeneticsMapGroup>();

  private totalCombinationsInGen = 0;
  private processedCombinationsInGen = 0;
  private progressHistory: ProgressRecord[] = [];

  private startTime = 0;
  private lastProgressSentTime = 0;
  private lastPartialResultSentTime = 0;
  private partialResultIntervalMs = 1200;

  private currentGenerationIndex = 0;
  private maxGenerations = 1;
  private currentSourceSaplings: Sapling[] = [];
  private originalSourceSaplings: Sapling[] = [];
  private currentOptions!: ApplicationOptions;

  public addEventListener(listener: SimulatorEventListener): () => void {
    this.listeners.push(listener);
    return () => {
      this.listeners = this.listeners.filter(l => l !== listener);
    };
  }

  private sendEvent(event: SimulatorEvent): void {
    if (this.isCancelled) return;
    for (const listener of this.listeners) {
      listener(event);
    }
  }

  public async simulateBestGenetics(
    sourceSaplings: Sapling[],
    options: ApplicationOptions
  ): Promise<void> {
    this.cancelSimulation();

    this.isRunning = true;
    this.isCancelled = false;
    this.currentRunId = `run_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
    this.startTime = Date.now();
    this.originalSourceSaplings = sourceSaplings.map(s => s.clone());
    this.currentSourceSaplings = sourceSaplings.map(s => s.clone());
    this.currentOptions = { ...options };
    this.maxGenerations = Math.min(3, Math.max(1, options.numberOfGenerations));
    this.allAccumulatedGroupsMap.clear();
    this.currentGenerationIndex = 0;

    await this.startGeneration(0, 0);
  }

  private async startGeneration(genIndex: number, addedSaplingsCount: number): Promise<void> {
    if (!this.isRunning || this.isCancelled) return;

    this.currentGenerationIndex = genIndex;
    this.currentGenGroupsMap.clear();
    this.progressHistory = [];
    this.lastProgressSentTime = 0;
    this.lastPartialResultSentTime = Date.now();
    this.partialResultIntervalMs = 1200;
    this.sendStage(`Building combinations for generation ${genIndex + 1}…`);

    const generationInfo: GenerationInfo | undefined =
      genIndex > 0 ? { generationIndex: genIndex, addedSaplings: addedSaplingsCount } : undefined;

    const sourceDTOs = this.currentSourceSaplings.map(serializeSapling);
    const workerCount = Math.max(1, Math.min(this.currentOptions.numberOfWorkers || 4, 32));

    const combinatoricsOpts = {
      withRepetitions: this.currentOptions.withRepetitions,
      minCrossbreedingSaplingsNumber: this.currentOptions.minCrossbreedingSaplingsNumber,
      maxCrossbreedingSaplingsNumber: this.currentOptions.maxCrossbreedingSaplingsNumber,
      numberOfWorkers: workerCount
    };

    this.totalCombinationsInGen = getNumberOfCrossbreedingCombinations(
      this.currentSourceSaplings.length,
      combinatoricsOpts,
      generationInfo
    );
    this.processedCombinationsInGen = 0;

    if (this.totalCombinationsInGen === 0) {
      this.finishGeneration();
      return;
    }

    const workChunks = getWorkChunks(
      this.currentSourceSaplings.length,
      combinatoricsOpts,
      generationInfo
    );

    const hasWorkerSupport = typeof Worker !== 'undefined';

    if (hasWorkerSupport) {
      await this.runWithWebWorkers(workChunks, workerCount, generationInfo, sourceDTOs);
    } else {
      await this.runSynchronously(workChunks, generationInfo);
    }
  }

  private runWithWebWorkers(
    workChunks: WorkChunk[],
    workerCount: number,
    generationInfo: GenerationInfo | undefined,
    sourceDTOs: ReturnType<typeof serializeSapling>[]
  ): Promise<void> {
    return new Promise(resolve => {
      const runId = this.currentRunId;
      this.terminateAllWorkers();

      let completedWorkers = 0;
      const actualWorkerCount = Math.min(workerCount, workChunks.length || 1);

      // Distribute work chunks round-robin
      const chunksPerWorker: WorkChunk[][] = Array.from({ length: actualWorkerCount }, () => []);
      for (let i = 0; i < workChunks.length; i++) {
        chunksPerWorker[i % actualWorkerCount].push(workChunks[i]);
      }

      for (let w = 0; w < actualWorkerCount; w++) {
        const workerChunks = chunksPerWorker[w];
        if (workerChunks.length === 0) {
          completedWorkers++;
          if (completedWorkers >= actualWorkerCount) {
            this.finishGeneration();
            resolve();
          }
          continue;
        }

        try {
          const worker = new Worker(
            new URL('../workers/crossbreeding.worker.ts', import.meta.url),
            { type: 'module' }
          );
          this.activeWorkers.push(worker);

          worker.onmessage = (e: MessageEvent) => {
            if (this.isCancelled || e.data.runId !== runId) return;

            const { type, combinationsProcessed, newMaps, finalMapGroups } = e.data;

            if (combinationsProcessed) {
              this.handleProgress(combinationsProcessed);
            }

            if (newMaps && newMaps.length > 0) {
              const maps = newMaps.map(deserializeGeneticsMap);
              appendAndOrganizeResults(this.currentGenGroupsMap, maps);
              appendAndOrganizeResults(this.allAccumulatedGroupsMap, maps);
              this.checkAndSendPartialResults();
            }

            if (finalMapGroups && finalMapGroups.length > 0) {
              for (const groupDTO of finalMapGroups) {
                const group = deserializeGeneticsMapGroup(groupDTO);
                appendAndOrganizeResults(this.currentGenGroupsMap, group.mapList);
                appendAndOrganizeResults(this.allAccumulatedGroupsMap, group.mapList);
              }
              this.checkAndSendPartialResults();
            }

            if (type === 'DONE') {
              completedWorkers++;
              if (completedWorkers >= actualWorkerCount) {
                this.finishGeneration();
                resolve();
              }
            }
          };

          worker.onerror = () => {
            completedWorkers++;
            if (completedWorkers >= actualWorkerCount) {
              this.finishGeneration();
              resolve();
            }
          };

          worker.postMessage({
            runId,
            workerIndex: w,
            sourceSaplings: sourceDTOs,
            workChunks: workerChunks,
            generationInfo,
            options: {
              withRepetitions: this.currentOptions.withRepetitions,
              minCrossbreedingSaplingsNumber: this.currentOptions.minCrossbreedingSaplingsNumber,
              maxCrossbreedingSaplingsNumber: this.currentOptions.maxCrossbreedingSaplingsNumber,
              geneScores: this.currentOptions.geneScores,
              minimumTrackedScore: this.currentOptions.minimumTrackedScore,
              cpuLimitPercent: this.currentOptions.cpuLimitPercent,
              workerCount: actualWorkerCount
            }
          });
        } catch {
          // Fallback if worker instantiation fails
          this.runSynchronously(workerChunks, generationInfo).then(() => {
            completedWorkers++;
            if (completedWorkers >= actualWorkerCount) {
              this.finishGeneration();
              resolve();
            }
          });
        }
      }
    });
  }

  private async runSynchronously(
    workChunks: WorkChunk[],
    generationInfo: GenerationInfo | undefined
  ): Promise<void> {
    const allSourceSaplings = this.currentSourceSaplings;
    const existingGenotypes = new Set(allSourceSaplings.map(s => s.toString()));
    const genIndex = (generationInfo?.generationIndex ?? 0) + 1;

    const crossOptions = {
      geneScores: this.currentOptions.geneScores,
      minimumTrackedScore: this.currentOptions.minimumTrackedScore
    };

    const itemsCount = allSourceSaplings.length;
    const maxPos = getMaxPositionsCount(
      itemsCount,
      this.currentOptions.maxCrossbreedingSaplingsNumber,
      this.currentOptions.withRepetitions
    );
    const minPos = this.currentOptions.minCrossbreedingSaplingsNumber;

    let lastYieldTime = performance.now();

    for (const chunk of workChunks) {
      if (this.isCancelled) return;
      const positions = [...chunk.startingPositions];

      for (let c = 0; c < chunk.combinationsToProcess; c++) {
        const surrounding = positions.map(idx => allSourceSaplings[idx]);
        const maps = evaluateCombination(
          surrounding,
          allSourceSaplings,
          existingGenotypes,
          crossOptions,
          genIndex
        );

        if (maps.length > 0) {
          appendAndOrganizeResults(this.currentGenGroupsMap, maps);
          appendAndOrganizeResults(this.allAccumulatedGroupsMap, maps);
        }

        this.handleProgress(1);

        // Cooperative asynchronous yield to keep UI silky smooth
        if (performance.now() - lastYieldTime > 20) {
          await new Promise(r => setTimeout(r, 0));
          lastYieldTime = performance.now();
        }

        if (c < chunk.combinationsToProcess - 1) {
          const ok = setNextPosition(
            positions,
            itemsCount,
            this.currentOptions.withRepetitions,
            minPos,
            maxPos,
            generationInfo
          );
          if (!ok) break;
        }
      }
    }

    this.finishGeneration();
  }

  private handleProgress(processedDelta: number, force = false): void {
    this.processedCombinationsInGen += processedDelta;
    const now = Date.now();

    // Throttle progress events to at most once every 120ms to avoid overwhelming React render cycle
    if (!force && now - this.lastProgressSentTime < 120) {
      return;
    }
    this.lastProgressSentTime = now;

    const elapsed = now - this.startTime;
    let estimatedTimeMs: number | null = null;

    if (elapsed >= 400 && this.processedCombinationsInGen > 0) {
      const ratePerMs = this.processedCombinationsInGen / elapsed;
      if (ratePerMs > 0) {
        const remaining = Math.max(0, this.totalCombinationsInGen - this.processedCombinationsInGen);
        estimatedTimeMs = Math.round(remaining / ratePerMs);
      }
    }

    const progressPercent = this.totalCombinationsInGen > 0
      ? Math.min(100, Math.round((this.processedCombinationsInGen / this.totalCombinationsInGen) * 1000) / 10)
      : 100;

    this.sendEvent({
      type: 'PROGRESS_UPDATE',
      generationIndex: this.currentGenerationIndex + 1,
      progressPercent,
      estimatedTimeMs,
      stage: `Evaluating crossbreeding combinations (gen ${this.currentGenerationIndex + 1})`,
      processedCombinations: this.processedCombinationsInGen,
      totalCombinations: this.totalCombinationsInGen
    });
  }

  private sendStage(stage: string): void {
    this.sendEvent({
      type: 'PROGRESS_UPDATE',
      generationIndex: this.currentGenerationIndex + 1,
      progressPercent: this.totalCombinationsInGen > 0
        ? Math.min(100, Math.round((this.processedCombinationsInGen / this.totalCombinationsInGen) * 1000) / 10)
        : 0,
      estimatedTimeMs: null,
      stage,
      processedCombinations: this.processedCombinationsInGen,
      totalCombinations: this.totalCombinationsInGen
    });
  }

  private checkAndSendPartialResults(): void {
    const now = Date.now();
    if (now - this.lastPartialResultSentTime >= this.partialResultIntervalMs) {
      this.lastPartialResultSentTime = now;
      this.partialResultIntervalMs = Math.min(this.partialResultIntervalMs + 600, 3000);

      const currentSorted = this.getSortedResults();
      this.sendEvent({
        type: 'PARTIAL_RESULTS',
        generationIndex: this.currentGenerationIndex + 1,
        mapGroups: currentSorted
      });
    }
  }

  private finishGeneration(): void {
    if (this.isCancelled) return;

    this.terminateAllWorkers();
    const sorted = this.getSortedResults();

    this.sendEvent({
      type: 'DONE_GENERATION',
      generationIndex: this.currentGenerationIndex + 1,
      mapGroups: sorted
    });

    const nextGenIndex = this.currentGenerationIndex + 1;
    if (nextGenIndex < this.maxGenerations) {
      this.sendStage(`Selecting best plants for generation ${nextGenIndex + 1}…`);
      const bestCandidates = getBestSaplingsForNextGeneration(
        Array.from(this.currentGenGroupsMap.values()),
        this.currentSourceSaplings,
        this.currentOptions.geneScores,
        this.currentOptions.numberOfSaplingsAddedBetweenGenerations,
        nextGenIndex
      );

      if (bestCandidates.length > 0) {
        this.currentSourceSaplings = [...bestCandidates, ...this.originalSourceSaplings];
        this.startGeneration(nextGenIndex, bestCandidates.length);
        return;
      }
    }

    // Done all generations
    this.finishSimulation();
  }

  public skipToNextGeneration(): void {
    if (!this.isRunning || this.isCancelled) return;
    this.terminateAllWorkers();

    const nextGenIndex = this.currentGenerationIndex + 1;
    if (nextGenIndex < this.maxGenerations) {
      const bestCandidates = getBestSaplingsForNextGeneration(
        Array.from(this.currentGenGroupsMap.values()),
        this.currentSourceSaplings,
        this.currentOptions.geneScores,
        this.currentOptions.numberOfSaplingsAddedBetweenGenerations,
        nextGenIndex
      );

      if (bestCandidates.length > 0) {
        this.currentSourceSaplings = [...bestCandidates, ...this.originalSourceSaplings];
        this.startGeneration(nextGenIndex, bestCandidates.length);
        return;
      }
    }

    this.finishSimulation();
  }

  private finishSimulation(): void {
    if (this.isCancelled) return;

    this.terminateAllWorkers();
    this.isRunning = false;

    // Link tree relationships across generations
    linkGenerationTree(
      Array.from(this.allAccumulatedGroupsMap.values()),
      this.allAccumulatedGroupsMap
    );

    const sortedResults = this.getSortedResults();
    const totalTimeMs = Date.now() - this.startTime;

    this.sendEvent({
      type: 'DONE',
      mapGroups: sortedResults,
      totalTimeMs
    });
  }

  public cancelSimulation(): void {
    this.isCancelled = true;
    this.isRunning = false;
    this.terminateAllWorkers();
  }

  private terminateAllWorkers(): void {
    for (const w of this.activeWorkers) {
      try {
        w.terminate();
      } catch {
        // ignore
      }
    }
    this.activeWorkers = [];
  }

  public getSortedResults(): GeneticsMapGroup[] {
    const groups = Array.from(this.allAccumulatedGroupsMap.values());
    groups.sort(resultMapGroupsSortingFunction);
    // Only emit the top routes. Thorough/Gen-3 runs can accumulate thousands of
    // distinct result strings; returning them all forces the main thread to run
    // the recursive route analysis + render on every partial/final update, which
    // freezes the UI. The user only ever views a small, grouped/paginated subset,
    // so cap the payload to the best results by score.
    return groups.length > MAX_RETURNED_RESULTS
      ? groups.slice(0, MAX_RETURNED_RESULTS)
      : groups;
  }
}
