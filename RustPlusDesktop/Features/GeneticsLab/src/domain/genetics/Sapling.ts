import { Gene, GeneType } from './Gene.ts';

export type GeneScores = Record<GeneType, number>;

export const DEFAULT_GENE_SCORES: GeneScores = {
  G: 1,
  Y: 1,
  H: 0.5,
  X: 0,
  W: 0
};

export class Sapling {
  public genes: Gene[];
  public generationIndex: number;
  public index?: number;
  private cachedString?: string;

  constructor(
    genesInput?: string | Gene[] | GeneType[],
    generationIndex = 0,
    index?: number
  ) {
    this.generationIndex = generationIndex;
    this.index = index;
    this.genes = [];

    if (typeof genesInput === 'string') {
      const clean = genesInput.trim().toUpperCase();
      for (let i = 0; i < clean.length; i++) {
        this.genes.push(new Gene(clean[i]));
      }
    } else if (Array.isArray(genesInput)) {
      for (const g of genesInput) {
        if (g instanceof Gene) {
          this.genes.push(g);
        } else {
          this.genes.push(new Gene(g));
        }
      }
    }
  }

  public addGene(gene: Gene | GeneType): void {
    if (gene instanceof Gene) {
      this.genes.push(gene);
    } else {
      this.genes.push(new Gene(gene));
    }
    this.cachedString = undefined;
  }

  public numberOfGs(): number {
    return this.genes.filter(g => g.type === 'G').length;
  }

  public numberOfYs(): number {
    return this.genes.filter(g => g.type === 'Y').length;
  }

  public numberOfHs(): number {
    return this.genes.filter(g => g.type === 'H').length;
  }

  public numberOfXs(): number {
    return this.genes.filter(g => g.type === 'X').length;
  }

  public numberOfWs(): number {
    return this.genes.filter(g => g.type === 'W').length;
  }

  public getScore(geneScores: GeneScores = DEFAULT_GENE_SCORES): number {
    let sum = 0;
    for (const gene of this.genes) {
      const score = geneScores[gene.type] ?? 0;
      sum += score;
    }
    return Math.round(sum * 100) / 100;
  }

  public toString(): string {
    // Genes only change through the constructor or addGene (which clears the cache), so the
    // genotype string can be memoized. This is called heavily during grouping/dedup/sort.
    if (this.cachedString === undefined) {
      let s = '';
      for (const g of this.genes) {
        s += g.type;
      }
      this.cachedString = s;
    }
    return this.cachedString;
  }

  public clone(): Sapling {
    const clone = new Sapling(
      this.genes.map(g => new Gene(g.type)),
      this.generationIndex,
      this.index
    );
    return clone;
  }

  public static isValidGeneString(str: string): boolean {
    return /^[GHYWX]{6}$/i.test(str.trim());
  }
}
