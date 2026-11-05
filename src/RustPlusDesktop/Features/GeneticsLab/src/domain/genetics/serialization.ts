import { Sapling } from './Sapling.ts';
import { GeneticsMap } from './GeneticsMap.ts';
import { GeneticsMapGroup } from './GeneticsMapGroup.ts';

export interface SaplingDTO {
  genes: string;
  generationIndex: number;
  index?: number;
}

export interface GeneticsMapDTO {
  resultSapling: SaplingDTO;
  baseSapling?: SaplingDTO;
  crossbreedingSaplings: SaplingDTO[];
  score: number;
  chance: number;
  sumOfComposingSaplingsGenerations: number;
  tieWinningCrossbreedingSaplingIndexes?: number[];
  tieLosingCrossbreedingSaplingIndexes?: number[];
}

export interface GeneticsMapGroupDTO {
  resultSaplingGeneString: string;
  mapList: GeneticsMapDTO[];
}

export function serializeSapling(sapling: Sapling): SaplingDTO {
  return {
    genes: sapling.toString(),
    generationIndex: sapling.generationIndex,
    index: sapling.index
  };
}

export function deserializeSapling(dto: SaplingDTO): Sapling {
  return new Sapling(dto.genes, dto.generationIndex, dto.index);
}

export function serializeGeneticsMap(map: GeneticsMap): GeneticsMapDTO {
  return {
    resultSapling: serializeSapling(map.resultSapling),
    baseSapling: map.baseSapling ? serializeSapling(map.baseSapling) : undefined,
    crossbreedingSaplings: map.crossbreedingSaplings.map(serializeSapling),
    score: map.score,
    chance: map.chance,
    sumOfComposingSaplingsGenerations: map.sumOfComposingSaplingsGenerations,
    tieWinningCrossbreedingSaplingIndexes: map.tieWinningCrossbreedingSaplingIndexes,
    tieLosingCrossbreedingSaplingIndexes: map.tieLosingCrossbreedingSaplingIndexes
  };
}

export function deserializeGeneticsMap(dto: GeneticsMapDTO): GeneticsMap {
  const map = new GeneticsMap(
    deserializeSapling(dto.resultSapling),
    dto.crossbreedingSaplings.map(deserializeSapling),
    dto.baseSapling ? deserializeSapling(dto.baseSapling) : undefined,
    dto.chance,
    dto.score,
    dto.tieWinningCrossbreedingSaplingIndexes,
    dto.tieLosingCrossbreedingSaplingIndexes
  );
  map.sumOfComposingSaplingsGenerations = dto.sumOfComposingSaplingsGenerations;
  return map;
}

export function serializeGeneticsMapGroup(group: GeneticsMapGroup): GeneticsMapGroupDTO {
  return {
    resultSaplingGeneString: group.resultSaplingGeneString,
    mapList: group.mapList.map(serializeGeneticsMap)
  };
}

export function deserializeGeneticsMapGroup(dto: GeneticsMapGroupDTO): GeneticsMapGroup {
  return new GeneticsMapGroup(
    dto.resultSaplingGeneString,
    dto.mapList.map(deserializeGeneticsMap)
  );
}
