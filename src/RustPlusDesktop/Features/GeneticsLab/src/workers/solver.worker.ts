/**
 * Crossbreeding solver worker.
 *
 * Pull-based: the worker is initialised once per generation, then asks for work
 * whenever it finishes a batch. That replaces the old fixed round-robin split,
 * so an unlucky worker that draws several large slices no longer sets the
 * wall-clock time for the whole generation.
 *
 * Results stay in the worker's own `ResultStore` (top 3 per genotype) and are
 * shipped as one packed `Int32Array`, throttled while running and once at the
 * end. The store's structural de-duplication makes repeated snapshots
 * idempotent on the receiving side.
 */

import {
  Evaluator,
  ResultStore,
  packSource,
  SourcePlantInput
} from '../domain/genetics/fastCore.ts';
import { packRecords } from '../domain/genetics/fastCodec.ts';
import { buildRejectTable, TargetConstraint } from '../domain/genetics/targetFilter.ts';
import { GeneScores } from '../domain/genetics/Sapling.ts';

export interface SolverInitMessage {
  type: 'INIT';
  runId: string;
  workerIndex: number;
  source: SourcePlantInput[];
  ownedGenotypes: string[];
  /**
   * Present only for the final generation. Each worker builds its own reject
   * table from it rather than receiving a 256KB byte array per worker.
   */
  target?: TargetConstraint | null;
  config: {
    minK: number;
    maxK: number;
    withRepetitions: boolean;
    minimumTrackedScore: number;
    geneScores: GeneScores;
    generationIndex: number;
    cpuLimitPercent?: number;
    workerCount?: number;
  };
}

/** Each entry is `[k, p0]` - one closed-form combination slice. */
export interface SolverWorkMessage {
  type: 'WORK';
  runId: string;
  slices: number[];
}

export interface SolverStopMessage {
  type: 'STOP';
}

/** Sent when the queue is drained; the worker replies with its remaining delta. */
export interface SolverFlushMessage {
  type: 'FLUSH';
  runId: string;
}

export type SolverRequest =
  | SolverInitMessage
  | SolverWorkMessage
  | SolverStopMessage
  | SolverFlushMessage;

export interface SolverResponse {
  type: 'READY' | 'PROGRESS' | 'BATCH_DONE' | 'FLUSHED' | 'FAILED';
  runId: string;
  workerIndex: number;
  combos?: number;
  records?: Int32Array;
  message?: string;
}

const sleep = (ms: number): Promise<void> => new Promise(r => setTimeout(r, ms));

/**
 * Fraction of a core this worker may stay busy for, so that
 * (workers x duty) / cores approximates the requested CPU ceiling.
 */
function computeDutyFraction(cpuLimitPercent: number | undefined, workerCount: number | undefined): number {
  if (!cpuLimitPercent || cpuLimitPercent >= 100) return 1;
  const cores = (self.navigator && self.navigator.hardwareConcurrency) || workerCount || 4;
  const workers = Math.max(1, workerCount || 1);
  const duty = ((cpuLimitPercent / 100) * cores) / workers;
  return Math.min(1, Math.max(0.05, duty));
}

let runId = '';
let workerIndex = 0;
let evaluator: Evaluator | null = null;
let store: ResultStore | null = null;
let dutyFraction = 1;
let stopped = false;

const PROGRESS_INTERVAL_MS = 200;
const SNAPSHOT_INTERVAL_MS = 1200;
const WORK_SLICE_MS = 25;

/** Packs everything retained since the last delta was sent. */
function takeDelta(): Int32Array {
  return packRecords(store!, store!.takeDirty());
}

self.onmessage = async (e: MessageEvent<SolverRequest>) => {
  const msg = e.data;

  if (msg.type === 'STOP') {
    stopped = true;
    return;
  }

  if (msg.type === 'FLUSH') {
    if (!store || msg.runId !== runId) return;
    const records = takeDelta();
    post({ type: 'FLUSHED', runId, workerIndex, records }, [records.buffer]);
    return;
  }

  if (msg.type === 'INIT') {
    runId = msg.runId;
    workerIndex = msg.workerIndex;
    stopped = false;

    const reject = buildRejectTable(msg.ownedGenotypes, msg.target);

    store = new ResultStore();
    evaluator = new Evaluator(packSource(msg.source), { ...msg.config, reject }, store);
    dutyFraction = computeDutyFraction(msg.config.cpuLimitPercent, msg.config.workerCount);

    post({ type: 'READY', runId, workerIndex });
    return;
  }

  if (msg.type === 'WORK') {
    if (!evaluator || !store || msg.runId !== runId) return;

    let lastProgress = Date.now();
    let lastSnapshot = Date.now();
    let sliceStart = performance.now();
    evaluator.resetProcessed();

    try {
      for (let i = 0; i < msg.slices.length; i += 2) {
        if (stopped) break;
        evaluator.runSlice(msg.slices[i], msg.slices[i + 1]);

        const now = Date.now();
        if (now - lastProgress >= PROGRESS_INTERVAL_MS) {
          const combos = evaluator.processed;
          evaluator.resetProcessed();
          lastProgress = now;

          if (now - lastSnapshot >= SNAPSHOT_INTERVAL_MS) {
            lastSnapshot = now;
            const records = takeDelta();
            post({ type: 'PROGRESS', runId, workerIndex, combos, records }, [records.buffer]);
          } else {
            post({ type: 'PROGRESS', runId, workerIndex, combos });
          }
        }

        if (dutyFraction < 1) {
          const worked = performance.now() - sliceStart;
          if (worked >= WORK_SLICE_MS) {
            await sleep(worked * (1 / dutyFraction - 1));
            sliceStart = performance.now();
          }
        }
      }

      // Batch completion carries progress only; results ride the throttled
      // delta above and the final FLUSH, so finishing a batch never re-ships
      // work the main thread has already merged.
      post({ type: 'BATCH_DONE', runId, workerIndex, combos: evaluator.processed });
    } catch (err) {
      post({
        type: 'FAILED',
        runId,
        workerIndex,
        combos: evaluator.processed,
        message: err instanceof Error ? err.message : String(err)
      });
    }
  }
};

function post(response: SolverResponse, transfer?: Transferable[]): void {
  if (transfer) {
    (self as unknown as Worker).postMessage(response, transfer);
  } else {
    (self as unknown as Worker).postMessage(response);
  }
}
