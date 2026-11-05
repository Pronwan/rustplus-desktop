import { Sapling } from './Sapling.ts';
import type { GeneticsMapGroup } from './GeneticsMapGroup.ts';

export class GeneticsMap {
  public resultSapling: Sapling;
  public baseSapling?: Sapling;
  public baseSaplingVariants?: GeneticsMapGroup;
  public crossbreedingSaplings: Sapling[];
  public crossbreedingSaplingsVariants?: GeneticsMapGroup[];
  public score: number;
  public chance: number;
  public sumOfComposingSaplingsGenerations: number;
  public tieWinningCrossbreedingSaplingIndexes?: number[];
  public tieLosingCrossbreedingSaplingIndexes?: number[];

  constructor(
    resultSapling: Sapling,
    crossbreedingSaplings: Sapling[],
    baseSapling?: Sapling,
    chance = 1,
    score = 0,
    tieWinningCrossbreedingSaplingIndexes?: number[],
    tieLosingCrossbreedingSaplingIndexes?: number[]
  ) {
    this.resultSapling = resultSapling;
    this.crossbreedingSaplings = crossbreedingSaplings;
    this.baseSapling = baseSapling;
    this.chance = chance;
    this.score = score;
    this.tieWinningCrossbreedingSaplingIndexes = tieWinningCrossbreedingSaplingIndexes;
    this.tieLosingCrossbreedingSaplingIndexes = tieLosingCrossbreedingSaplingIndexes;

    let genSum = 0;
    for (const sapling of crossbreedingSaplings) {
      genSum += sapling.generationIndex;
    }
    if (baseSapling) {
      genSum += baseSapling.generationIndex;
    }
    this.sumOfComposingSaplingsGenerations = genSum;
  }

  /**
   * Memoized recursive chance. The comparator in `resultMapGroupsSortingFunction`
   * calls this O(n log n) times per sort and route analysis calls it again, so
   * without a cache the whole dependency tree is re-walked on every comparison.
   * Invalidated by `linkGenerationTree` when parent links change.
   */
  private chanceProductCache?: number;

  public invalidateChanceProduct(): void {
    this.chanceProductCache = undefined;
  }

  public getChanceProduct(): number {
    if (this.chanceProductCache !== undefined) {
      return this.chanceProductCache;
    }
    let product = this.chance;

    if (this.baseSaplingVariants && this.baseSaplingVariants.mapList.length > 0) {
      product *= this.baseSaplingVariants.mapList[0].getChanceProduct();
    }

    if (this.crossbreedingSaplingsVariants) {
      for (const variant of this.crossbreedingSaplingsVariants) {
        if (variant && variant.mapList.length > 0) {
          product *= variant.mapList[0].getChanceProduct();
        }
      }
    }

    this.chanceProductCache = Math.round(product * 10000) / 10000;
    return this.chanceProductCache;
  }

  public clone(): GeneticsMap {
    const clone = new GeneticsMap(
      this.resultSapling.clone(),
      this.crossbreedingSaplings.map(s => s.clone()),
      this.baseSapling ? this.baseSapling.clone() : undefined,
      this.chance,
      this.score,
      this.tieWinningCrossbreedingSaplingIndexes ? [...this.tieWinningCrossbreedingSaplingIndexes] : undefined,
      this.tieLosingCrossbreedingSaplingIndexes ? [...this.tieLosingCrossbreedingSaplingIndexes] : undefined
    );

    clone.sumOfComposingSaplingsGenerations = this.sumOfComposingSaplingsGenerations;

    if (this.baseSaplingVariants) {
      clone.baseSaplingVariants = this.baseSaplingVariants.clone();
    }

    if (this.crossbreedingSaplingsVariants) {
      clone.crossbreedingSaplingsVariants = this.crossbreedingSaplingsVariants.map(v => v.clone());
    }

    return clone;
  }
}
