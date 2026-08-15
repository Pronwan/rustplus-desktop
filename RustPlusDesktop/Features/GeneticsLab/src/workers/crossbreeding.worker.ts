import { evaluateCombination, CrossbreedingOptions } from '../domain/genetics/crossbreeding.ts';
import { setNextPositionInChunk, getMaxPositionsCount, WorkChunk, GenerationInfo } from '../domain/genetics/combinations.ts';
import { appendAndOrganizeResults } from '../domain/genetics/sorting.ts';
import { GeneticsMapGroup } from '../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../domain/genetics/GeneticsMap.ts';
import {
  SaplingDTO,
  GeneticsMapDTO,
  GeneticsMapGroupDTO,
  deserializeSapling,
  serializeGeneticsMap,
  serializeGeneticsMapGroup
} from '../domain/genetics/serialization.ts';

export interface CrossbreedingWorkerRequest {
  runId: string;
  workerIndex: number;
  sourceSaplings: SaplingDTO[];
  workChunks: WorkChunk[];
  generationInfo?: GenerationInfo;
  options: {
    withRepetitions: boolean;
    minCrossbreedingSaplingsNumber: number;
    maxCrossbreedingSaplingsNumber: number;
    geneScores: Record<'G' | 'Y' | 'H' | 'X' | 'W', number>;
    minimumTrackedScore: number;
    cpuLimitPercent?: number;
    workerCount?: number;
  };
}

export interface CrossbreedingWorkerResponse {
  runId: string;
  workerIndex: number;
  type: 'PROGRESS' | 'DONE';
  combinationsProcessed: number;
  newMaps?: GeneticsMapDTO[];
  finalMapGroups?: GeneticsMapGroupDTO[];
}

const sleep = (ms: number): Promise<void> => new Promise(resolve => setTimeout(resolve, ms));

/**
 * Fraction of a single core each worker is allowed to use (0..1).
 *
 * The number of workers caps how many cores run in parallel, but each worker otherwise
 * pins its core at 100%. To honour a total-CPU ceiling we duty-cycle: a worker computes
 * how busy it may stay so that (workerCount x duty) / totalCores ~= cpuLimitPercent.
 * When the requested cap is already satisfied by the worker count alone, duty is 1 (no
 * throttling and no added latency).
 */
function computeDutyFraction(cpuLimitPercent: number | undefined, workerCount: number | undefined): number {
  if (!cpuLimitPercent || cpuLimitPercent >= 100) return 1;
  const cores = (self.navigator && self.navigator.hardwareConcurrency) || workerCount || 4;
  const workers = Math.max(1, workerCount || 1);
  const targetCoreEquivalents = (cpuLimitPercent / 100) * cores;
  const duty = targetCoreEquivalents / workers;
  return Math.min(1, Math.max(0.05, duty));
}

self.onmessage = async (e: MessageEvent<CrossbreedingWorkerRequest>) => {
  const { runId, workerIndex, sourceSaplings: sourceDTOs, workChunks, generationInfo, options } = e.data;

  const allSourceSaplings = sourceDTOs.map(deserializeSapling);
  const existingGenotypeStrings = new Set<string>(allSourceSaplings.map(s => s.toString()));
  const generationIndex = generationInfo ? generationInfo.generationIndex + 1 : 1;

  const crossbreedingOptions: CrossbreedingOptions = {
    geneScores: options.geneScores,
    minimumTrackedScore: options.minimumTrackedScore
  };

  const groupMap = new Map<string, GeneticsMapGroup>();
  const itemsCount = allSourceSaplings.length;
  const maxPos = getMaxPositionsCount(
    itemsCount,
    options.maxCrossbreedingSaplingsNumber,
    options.withRepetitions
  );
  const minPos = options.minCrossbreedingSaplingsNumber;

  let totalProcessedSinceReport = 0;
  let lastReportTime = Date.now();
  const REPORT_INTERVAL_MS = 250;
  const newMapsDelta: GeneticsMap[] = [];

  // CPU throttling: work for a short slice, then sleep proportionally so this worker only
  // uses `dutyFraction` of its core. WORK_SLICE_MS keeps slices short enough that the cap
  // is smooth rather than bursty.
  const dutyFraction = computeDutyFraction(options.cpuLimitPercent, options.workerCount);
  const WORK_SLICE_MS = 25;
  let sliceStart = performance.now();

  for (const chunk of workChunks) {
    const positions = [...chunk.startingPositions];
    const combinationsToProcess = chunk.combinationsToProcess;

    for (let c = 0; c < combinationsToProcess; c++) {
      const surroundingSaplings = positions.map(idx => allSourceSaplings[idx]);

      const maps = evaluateCombination(
        surroundingSaplings,
        allSourceSaplings,
        existingGenotypeStrings,
        crossbreedingOptions,
        generationIndex
      );

      if (maps.length > 0) {
        for (const m of maps) {
          newMapsDelta.push(m);
        }
        appendAndOrganizeResults(groupMap, maps);
      }

      totalProcessedSinceReport++;

      const now = Date.now();
      if (now - lastReportTime >= REPORT_INTERVAL_MS) {
        const deltaDTOs = newMapsDelta.splice(0, newMapsDelta.length).map(serializeGeneticsMap);
        const progressMsg: CrossbreedingWorkerResponse = {
          runId,
          workerIndex,
          type: 'PROGRESS',
          combinationsProcessed: totalProcessedSinceReport,
          newMaps: deltaDTOs
        };
        self.postMessage(progressMsg);
        totalProcessedSinceReport = 0;
        lastReportTime = now;
      }

      // Duty-cycle throttle: after each busy slice, sleep so average core usage stays at
      // dutyFraction. Skipped entirely when dutyFraction >= 1 (no cap).
      if (dutyFraction < 1) {
        const workedMs = performance.now() - sliceStart;
        if (workedMs >= WORK_SLICE_MS) {
          const sleepMs = workedMs * (1 / dutyFraction - 1);
          await sleep(sleepMs);
          sliceStart = performance.now();
        }
      }

      if (c < combinationsToProcess - 1) {
        setNextPositionInChunk(positions, itemsCount, options.withRepetitions);
      }
    }
  }

  const finalGroupsDTO = Array.from(groupMap.values()).map(serializeGeneticsMapGroup);
  const doneMsg: CrossbreedingWorkerResponse = {
    runId,
    workerIndex,
    type: 'DONE',
    combinationsProcessed: totalProcessedSinceReport,
    finalMapGroups: finalGroupsDTO
  };
  self.postMessage(doneMsg);
};
