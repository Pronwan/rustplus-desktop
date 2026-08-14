export type GeneType = 'G' | 'H' | 'Y' | 'W' | 'X';

export const VALID_GENES: readonly GeneType[] = ['G', 'H', 'Y', 'W', 'X'] as const;
export const GREEN_GENES: readonly GeneType[] = ['G', 'H', 'Y'] as const;
export const RED_GENES: readonly GeneType[] = ['W', 'X'] as const;

export const GREEN_GENE_WEIGHT = 0.6;
export const RED_GENE_WEIGHT = 1.0;

export class Gene {
  public readonly type: GeneType;

  constructor(type: GeneType | string) {
    const upper = type.toUpperCase() as GeneType;
    if (!Gene.isValid(upper)) {
      throw new Error(`Invalid gene type: ${type}`);
    }
    this.type = upper;
  }

  public static isValid(gene: string): gene is GeneType {
    return (VALID_GENES as readonly string[]).includes(gene);
  }

  public static getWeight(type: GeneType): number {
    return (GREEN_GENES as readonly string[]).includes(type) ? GREEN_GENE_WEIGHT : RED_GENE_WEIGHT;
  }

  public getCrossbreedingWeight(): number {
    return Gene.getWeight(this.type);
  }

  public get isGreen(): boolean {
    return (GREEN_GENES as readonly string[]).includes(this.type);
  }

  public get isRed(): boolean {
    return (RED_GENES as readonly string[]).includes(this.type);
  }

  public toString(): string {
    return this.type;
  }
}
