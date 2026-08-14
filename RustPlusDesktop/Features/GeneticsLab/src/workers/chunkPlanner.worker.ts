import { getWorkChunks, getNumberOfCrossbreedingCombinations, CombinatoricsOptions, GenerationInfo } from '../domain/genetics/combinations.ts';
import { SaplingDTO } from '../domain/genetics/serialization.ts';

export interface ChunkPlannerRequest {
  runId: string;
  sourceSaplings: SaplingDTO[];
  options: CombinatoricsOptions;
  generationInfo?: GenerationInfo;
}

export interface ChunkPlannerResponse {
  runId: string;
  type: 'CHUNKS_READY';
  workChunks: ReturnType<typeof getWorkChunks>;
  allCombinationsCount: number;
}

self.onmessage = (e: MessageEvent<ChunkPlannerRequest>) => {
  const { runId, sourceSaplings, options, generationInfo } = e.data;
  const itemsCount = sourceSaplings.length;

  const allCombinationsCount = getNumberOfCrossbreedingCombinations(itemsCount, options, generationInfo);
  const workChunks = getWorkChunks(itemsCount, options, generationInfo);

  const response: ChunkPlannerResponse = {
    runId,
    type: 'CHUNKS_READY',
    workChunks,
    allCombinationsCount
  };

  self.postMessage(response);
};
