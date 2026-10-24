import { GeneticsMap } from './GeneticsMap.ts';

export class GeneticsMapGroup {
  public resultSaplingGeneString: string;
  public mapList: GeneticsMap[];

  constructor(resultSaplingGeneString: string, mapList: GeneticsMap[] = []) {
    this.resultSaplingGeneString = resultSaplingGeneString;
    this.mapList = mapList;
  }

  public clone(): GeneticsMapGroup {
    return new GeneticsMapGroup(
      this.resultSaplingGeneString,
      this.mapList.map(m => m.clone())
    );
  }
}
