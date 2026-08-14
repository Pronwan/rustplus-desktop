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

self.onmessage = (e: MessageEvent<CrossbreedingWorkerRequest>) => {
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
