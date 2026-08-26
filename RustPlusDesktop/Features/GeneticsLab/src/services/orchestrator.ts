import { Sapling, GeneScores } from '../domain/genetics/Sapling.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { resultMapGroupsSortingFunction } from '../domain/genetics/sorting.ts';
import {
  getBestSaplingsForNextGeneration,
  linkGenerationTree
} from '../domain/genetics/generationSelection.ts';
import {
  getNumberOfCrossbreedingCombinations,
  getWorkChunks,
  WorkChunk,
  GenerationInfo
} from '../domain/genetics/combinations.ts';
import {
  Evaluator,
  ResultStore,
  SourcePlantInput,
  packSource
} from '../domain/genetics/fastCore.ts';
import { buildRejectTable, TargetConstraint, targetPrunes } from '../domain/genetics/targetFilter.ts';
import { materializeGroups, GroupMaterializer } from '../domain/genetics/fastGeneration.ts';
import { unpackRecords } from '../domain/genetics/fastCodec.ts';
import type { SolverResponse } from '../workers/solver.worker.ts';

/**
 * Max result groups emitted to the UI. Caps main-thread route-analysis + render
 * work so heavy Thorough / Gen-3 runs don't freeze the UI. Results are sorted
 * best-first before slicing, and the UI groups/paginates further.
 */
export const MAX_RETURNED_RESULTS = 500;

/**
 * Target combinations per worker batch, expressed as a divisor of the
 * generation total. Enough batches per worker that a slow slice cannot skew the
 * finish time, few enough that message overhead stays negligible.
 */
const BATCHES_PER_WORKER = 30;

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
  /** Set when part of the search space could not be completed. */
  incomplete?: boolean;
}

export type SimulatorEventListener = (event: SimulatorEvent) => void;

interface PoolWorker {
  worker: Worker;
  index: number;
  /**
   * Queue indexes currently assigned. Indexes rather than raw `[k, p0]` pairs so
   * a requeue can restore each slice's combination count, which batch sizing and
   * inline progress both depend on.
   */
  inFlight: number[];
  dead: boolean;
  /**
   * `starting` until the worker acknowledges INIT. A starting worker must not
   * be counted as idle, or a queue that drains before every worker reports
   * ready would end the generation early.
   */
  state: 'starting' | 'idle' | 'busy' | 'flushing';
}

export class CrossbreedingOrchestrator {
  private listeners: SimulatorEventListener[] = [];
  private pool: PoolWorker[] = [];
  private currentRunId = '';
  private isRunning = false;
  private isCancelled = false;
  private skipRequested = false;

  private allAccumulatedStore = new ResultStore();
  private currentGenStore = new ResultStore();
  /** Keeps unchanged result groups identity-stable across streaming updates. */
  private materializer = new GroupMaterializer();

  private totalCombinationsInGen = 0;
  private processedCombinationsInGen = 0;

  private startTime = 0;
  private lastProgressSentTime = 0;
  private lastPartialResultSentTime = 0;
  private partialResultIntervalMs = 1200;

  private currentGenerationIndex = 0;
  private maxGenerations = 1;
  private currentSourceSaplings: Sapling[] = [];
  private originalSourceSaplings: Sapling[] = [];
  private currentOptions!: ApplicationOptions;

  /** Flat `[k, p0, ...]` queue plus the per-slice combination counts. */
  private sliceQueue: number[] = [];
  private sliceCounts: number[] = [];
  private queueCursor = 0;
  private batchTarget = 1;
  private generationFailed = false;
  private resolveGeneration: (() => void) | null = null;

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
    options: ApplicationOptions,
    target?: TargetConstraint | null
  ): Promise<void> {
    this.cancelSimulation();
    this.target = target ?? null;

    this.isRunning = true;
    this.isCancelled = false;
    this.skipRequested = false;
    this.currentRunId = `run_${Date.now()}_${Math.random().toString(36).slice(2, 7)}`;
    this.startTime = Date.now();
    this.originalSourceSaplings = sourceSaplings.map(s => s.clone());
    this.currentSourceSaplings = sourceSaplings.map(s => s.clone());
    this.currentOptions = { ...options };
    this.maxGenerations = Math.min(3, Math.max(1, options.numberOfGenerations));
    this.allAccumulatedStore = new ResultStore();
    this.materializer.clear();
    this.linkedGroups = null;
    this.currentGenerationIndex = 0;

    let genIndex = 0;
    let addedSaplings = 0;

    // Generations run as a sequential loop so the returned promise settles only
    // when the whole simulation is finished.
    for (;;) {
      if (!this.isRunning || this.isCancelled) return;

      await this.runGeneration(genIndex, addedSaplings);
      if (this.isCancelled) return;

      this.emitGenerationDone();

      const nextGenIndex = genIndex + 1;
      if (nextGenIndex >= this.maxGenerations) break;

      const candidates = this.selectNextGeneration(nextGenIndex);
      if (candidates.length === 0) break;

      this.currentSourceSaplings = [...candidates, ...this.originalSourceSaplings];
      genIndex = nextGenIndex;
      addedSaplings = candidates.length;
      this.skipRequested = false;
    }

    this.finishSimulation();
  }

  private selectNextGeneration(nextGenIndex: number): Sapling[] {
    this.sendStage(`Selecting best plants for generation ${nextGenIndex + 1}…`);
    return getBestSaplingsForNextGeneration(
      Array.from(materializeGroups(this.currentGenStore).values()),
      this.currentSourceSaplings,
      this.currentOptions.geneScores,
      this.currentOptions.numberOfSaplingsAddedBetweenGenerations,
      nextGenIndex
    );
  }

  private async runGeneration(genIndex: number, addedSaplingsCount: number): Promise<void> {
    this.currentGenerationIndex = genIndex;
    this.currentGenStore = new ResultStore();
    this.generationFailed = false;
    this.lastProgressSentTime = 0;
    this.lastPartialResultSentTime = Date.now();
    this.partialResultIntervalMs = 1200;
    this.sendStage(`Building combinations for generation ${genIndex + 1}…`);

    const generationInfo: GenerationInfo | undefined =
      genIndex > 0 ? { generationIndex: genIndex, addedSaplings: addedSaplingsCount } : undefined;

    const combinatoricsOpts = {
      withRepetitions: this.currentOptions.withRepetitions,
      minCrossbreedingSaplingsNumber: this.currentOptions.minCrossbreedingSaplingsNumber,
      maxCrossbreedingSaplingsNumber: this.currentOptions.maxCrossbreedingSaplingsNumber
    };

    this.totalCombinationsInGen = getNumberOfCrossbreedingCombinations(
      this.currentSourceSaplings.length,
      combinatoricsOpts,
      generationInfo
    );
    this.processedCombinationsInGen = 0;

    if (this.totalCombinationsInGen === 0) return;

    const chunks = getWorkChunks(
      this.currentSourceSaplings.length,
      combinatoricsOpts,
      generationInfo
    );
    this.loadQueue(chunks);

    const workerCount = Math.max(1, Math.min(this.currentOptions.numberOfWorkers || 4, 32));
    this.batchTarget = Math.max(
      1,
      Math.ceil(this.totalCombinationsInGen / (workerCount * BATCHES_PER_WORKER))
    );

    if (typeof Worker === 'undefined') {
      this.runInline();
    } else {
      await this.runPool(workerCount, genIndex + 1);
    }

    // Progress events are throttled, so the last increments of a fast
    // generation would otherwise never reach the UI and the bar would stall
    // short of 100%.
    this.handleProgress(0, true);
  }

  private loadQueue(chunks: WorkChunk[]): void {
    this.sliceQueue = [];
    this.sliceCounts = [];
    for (const chunk of chunks) {
      this.sliceQueue.push(chunk.startingPositions.length, chunk.startingPositions[0]);
      this.sliceCounts.push(chunk.combinationsToProcess);
    }
    this.queueCursor = 0;
  }

  /** Pops the next batch of queue indexes, or null when the queue is drained. */
  private takeBatch(): number[] | null {
    if (this.queueCursor >= this.sliceCounts.length) return null;
    const indexes: number[] = [];
    let acc = 0;
    while (this.queueCursor < this.sliceCounts.length && acc < this.batchTarget) {
      const i = this.queueCursor++;
      indexes.push(i);
      acc += this.sliceCounts[i];
    }
    return indexes;
  }

  private slicesFor(indexes: number[]): number[] {
    const slices: number[] = [];
    for (const i of indexes) slices.push(this.sliceQueue[i * 2], this.sliceQueue[i * 2 + 1]);
    return slices;
  }

  /**
   * Returns a dead worker's unfinished slices to the front of the queue, keeping
   * their combination counts so later batches are still sized correctly.
   */
  private requeue(indexes: number[]): void {
    if (indexes.length === 0) return;
    const queue: number[] = [];
    const counts: number[] = [];
    const push = (i: number) => {
      queue.push(this.sliceQueue[i * 2], this.sliceQueue[i * 2 + 1]);
      counts.push(this.sliceCounts[i]);
    };
    for (const i of indexes) push(i);
    for (let i = this.queueCursor; i < this.sliceCounts.length; i++) push(i);
    this.sliceQueue = queue;
    this.sliceCounts = counts;
    this.queueCursor = 0;
  }

  private buildSourcePayload(): { source: SourcePlantInput[]; ownedGenotypes: string[] } {
    const source = this.currentSourceSaplings.map(s => ({
      genes: s.toString(),
      generationIndex: s.generationIndex,
      index: s.index
    }));
    return { source, ownedGenotypes: this.currentSourceSaplings.map(s => s.toString()) };
  }

  private runPool(workerCount: number, generationIndex: number): Promise<void> {
    return new Promise<void>(resolve => {
      this.resolveGeneration = resolve;
      const runId = this.currentRunId;
      const { source, ownedGenotypes } = this.buildSourcePayload();
      const target = this.targetForGeneration(generationIndex - 1);
      const config = {
        minK: this.currentOptions.minCrossbreedingSaplingsNumber,
        maxK: this.currentOptions.maxCrossbreedingSaplingsNumber,
        withRepetitions: this.currentOptions.withRepetitions,
        minimumTrackedScore: this.currentOptions.minimumTrackedScore,
        geneScores: this.currentOptions.geneScores,
        generationIndex,
        cpuLimitPercent: this.currentOptions.cpuLimitPercent,
        workerCount
      };

      this.pool = [];
      for (let w = 0; w < workerCount; w++) {
        let worker: Worker;
        try {
          worker = new Worker(new URL('../workers/solver.worker.ts', import.meta.url), {
            type: 'module'
          });
        } catch {
          // A worker that cannot be constructed simply reduces the pool size.
          // Generation completion stays owned by the pool coordinator, so a
          // construction failure can no longer end the generation early.
          continue;
        }

        const entry: PoolWorker = {
          worker,
          index: w,
          inFlight: [],
          dead: false,
          state: 'starting'
        };
        this.pool.push(entry);

        worker.onmessage = (e: MessageEvent<SolverResponse>) => {
          this.handleWorkerMessage(entry, e.data, runId);
        };
        worker.onerror = () => {
          this.handleWorkerLoss(entry);
        };

        worker.postMessage({
          type: 'INIT',
          runId,
          workerIndex: w,
          source,
          ownedGenotypes,
          target,
          config
        });
      }

      if (this.pool.length === 0) {
        // No workers at all: do the work on this thread, then complete once.
        this.runInline();
        this.settleGeneration();
      }
    });
  }

  private handleWorkerMessage(entry: PoolWorker, msg: SolverResponse, runId: string): void {
    if (this.isCancelled || msg.runId !== runId || entry.dead) return;

    if (msg.type === 'READY') {
      this.dispatch(entry);
      return;
    }

    if (msg.type === 'PROGRESS') {
      if (msg.combos) this.handleProgress(msg.combos);
      if (msg.records) this.ingest(msg.records);
      return;
    }

    if (msg.type === 'BATCH_DONE') {
      if (msg.combos) this.handleProgress(msg.combos);
      entry.inFlight = [];
      this.dispatch(entry);
      return;
    }

    if (msg.type === 'FLUSHED') {
      if (msg.records) this.ingest(msg.records);
      entry.state = 'idle';
      if (this.queueCursor < this.sliceCounts.length) {
        // Work reappeared while this worker was flushing (a peer died and its
        // slices were requeued), so pick it up rather than finishing.
        this.dispatch(entry);
      } else if (this.pool.every(p => p.dead || p.state === 'idle')) {
        this.finishPool();
      }
      return;
    }

    if (msg.type === 'FAILED') {
      if (msg.combos) this.handleProgress(msg.combos);
      this.handleWorkerLoss(entry);
    }
  }

  /**
   * A worker died or reported failure. Its unfinished slices go back on the
   * queue so the remaining workers cover them; the generation is only reported
   * incomplete if no worker is left to pick them up.
   */
  private handleWorkerLoss(entry: PoolWorker): void {
    if (entry.dead) return;
    entry.dead = true;
    try {
      entry.worker.terminate();
    } catch {
      // ignore
    }

    this.requeue(entry.inFlight);
    entry.inFlight = [];
    entry.state = 'idle';

    const alive = this.pool.filter(p => !p.dead);
    if (alive.length === 0) {
      if (this.queueCursor < this.sliceCounts.length) {
        // Nothing left to run the remaining work in parallel: finish it here so
        // the result set stays complete rather than silently truncated.
        try {
          this.runInline();
        } catch {
          this.generationFailed = true;
        }
      }
      this.settleGeneration();
      return;
    }

    // Wake any idle survivor so the requeued work is picked up promptly.
    for (const p of alive) {
      if (p.state === 'idle') this.dispatch(p);
    }
  }

  private dispatch(entry: PoolWorker): void {
    if (this.isCancelled || entry.dead) return;

    if (this.skipRequested) {
      this.finishPool();
      return;
    }

    const batch = this.takeBatch();
    if (batch === null) {
      // Queue drained: ask for whatever this worker has not shipped yet, and
      // only count it idle once that delta has arrived.
      entry.inFlight = [];
      if (entry.state !== 'flushing') {
        entry.state = 'flushing';
        entry.worker.postMessage({ type: 'FLUSH', runId: this.currentRunId });
      }
      return;
    }

    const slices = this.slicesFor(batch);
    entry.inFlight = batch;
    entry.state = 'busy';
    entry.worker.postMessage({ type: 'WORK', runId: this.currentRunId, slices });
  }

  private finishPool(): void {
    this.terminateAllWorkers();
    this.settleGeneration();
  }

  private settleGeneration(): void {
    const resolve = this.resolveGeneration;
    this.resolveGeneration = null;
    if (resolve) resolve();
  }

  /** Single-threaded execution of whatever is left in the queue. */
  private runInline(): void {
    const packed = packSource(
      this.currentSourceSaplings.map(s => ({
        genes: s.toString(),
        generationIndex: s.generationIndex,
        index: s.index
      }))
    );
    const reject = buildRejectTable(
      this.currentSourceSaplings.map(s => s.toString()),
      this.targetForGeneration(this.currentGenerationIndex)
    );

    const store = new ResultStore();
    const evaluator = new Evaluator(
      packed,
      {
        minK: this.currentOptions.minCrossbreedingSaplingsNumber,
        maxK: this.currentOptions.maxCrossbreedingSaplingsNumber,
        withRepetitions: this.currentOptions.withRepetitions,
        minimumTrackedScore: this.currentOptions.minimumTrackedScore,
        geneScores: this.currentOptions.geneScores,
        generationIndex: this.currentGenerationIndex + 1,
        reject
      },
      store
    );

    while (this.queueCursor < this.sliceCounts.length) {
      if (this.isCancelled || this.skipRequested) break;
      const i = this.queueCursor++;
      evaluator.runSlice(this.sliceQueue[i * 2], this.sliceQueue[i * 2 + 1]);
      this.handleProgress(this.sliceCounts[i]);
    }

    this.currentGenStore.mergeFrom(store);
    this.allAccumulatedStore.mergeFrom(store);
  }

  private ingest(records: Int32Array): void {
    const maps = unpackRecords(records, this.currentOptions.geneScores);
    for (const map of maps) {
      this.currentGenStore.insert(map);
      this.allAccumulatedStore.insert(map);
    }
    this.checkAndSendPartialResults();
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

      this.sendEvent({
        type: 'PARTIAL_RESULTS',
        generationIndex: this.currentGenerationIndex + 1,
        mapGroups: this.getSortedResults()
      });
    }
  }

  private emitGenerationDone(): void {
    this.sendEvent({
      type: 'DONE_GENERATION',
      generationIndex: this.currentGenerationIndex + 1,
      mapGroups: this.getSortedResults(),
      incomplete: this.generationFailed
    });
  }

  public skipToNextGeneration(): void {
    if (!this.isRunning || this.isCancelled) return;
    // The generation loop owns advancement; flagging is enough to drain the
    // queue and settle the current generation.
    this.skipRequested = true;
    this.finishPool();
  }

  private finishSimulation(): void {
    if (this.isCancelled) return;

    this.terminateAllWorkers();
    this.isRunning = false;

    this.materializer.sync(this.allAccumulatedStore);
    const groups = this.materializer.asMap();
    linkGenerationTree(Array.from(groups.values()), groups);
    this.linkedGroups = groups;

    this.sendEvent({
      type: 'DONE',
      mapGroups: this.selectTop(Array.from(groups.values())),
      totalTimeMs: Date.now() - this.startTime,
      incomplete: this.generationFailed
    });
  }

  /** Set once the final tree linking has run, so results carry real chances. */
  private linkedGroups: Map<string, GeneticsMapGroup> | null = null;

  /**
   * The user's target, applied as a solver-side filter on the FINAL generation
   * only. Earlier generations must stay unfiltered because their results feed
   * beam selection, which deliberately picks intermediates that do not match
   * the target.
   */
  private target: TargetConstraint | null = null;

  /** True when `genIndex` is the last generation this run will execute. */
  private isFinalGeneration(genIndex: number): boolean {
    return genIndex + 1 >= this.maxGenerations;
  }

  private targetForGeneration(genIndex: number): TargetConstraint | null {
    return this.isFinalGeneration(genIndex) && targetPrunes(this.target) ? this.target : null;
  }

  public cancelSimulation(): void {
    this.isCancelled = true;
    this.isRunning = false;
    this.skipRequested = false;
    this.settleGeneration();
    this.terminateAllWorkers();
  }

  private terminateAllWorkers(): void {
    for (const entry of this.pool) {
      entry.dead = true;
      try {
        entry.worker.terminate();
      } catch {
        // ignore
      }
    }
    this.pool = [];
  }

  public getSortedResults(): GeneticsMapGroup[] {
    const groups = this.linkedGroups
      ? Array.from(this.linkedGroups.values())
      : this.materializer.sync(this.allAccumulatedStore);
    return this.selectTop(groups);
  }

  /**
   * Returns the best `MAX_RETURNED_RESULTS` groups. Uses selection rather than a
   * full sort: only the retained head has to be ordered, and the comparator is
   * the expensive part because it walks the recursive chance product.
   */
  private selectTop(groups: GeneticsMapGroup[]): GeneticsMapGroup[] {
    if (groups.length <= MAX_RETURNED_RESULTS) {
      return groups.sort(resultMapGroupsSortingFunction);
    }
    const head = groups.slice(0, MAX_RETURNED_RESULTS).sort(resultMapGroupsSortingFunction);
    let worst = head[head.length - 1];
    for (let i = MAX_RETURNED_RESULTS; i < groups.length; i++) {
      const candidate = groups[i];
      if (resultMapGroupsSortingFunction(candidate, worst) >= 0) continue;
      let lo = 0;
      let hi = head.length - 1;
      while (lo < hi) {
        const mid = (lo + hi) >> 1;
        if (resultMapGroupsSortingFunction(candidate, head[mid]) < 0) hi = mid;
        else lo = mid + 1;
      }
      head.splice(lo, 0, candidate);
      head.length = MAX_RETURNED_RESULTS;
      worst = head[head.length - 1];
    }
    return head;
  }
}
