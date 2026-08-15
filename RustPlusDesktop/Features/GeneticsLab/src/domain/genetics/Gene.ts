export type GeneType = 'G' | 'H' | 'Y' | 'W' | 'X';

export const VALID_GENES: readonly GeneType[] = ['G', 'H', 'Y', 'W', 'X'] as const;
export const GREEN_GENES: readonly GeneType[] = ['G', 'H', 'Y'] as const;
export const RED_GENES: readonly GeneType[] = ['W', 'X'] as const;

export const GREEN_GENE_WEIGHT = 0.6;
export const RED_GENE_WEIGHT = 1.0;

// Precomputed lookups so the crossbreeding hot path never re-scans arrays.
const GENE_WEIGHTS: Record<GeneType, number> = {
  G: GREEN_GENE_WEIGHT,
  H: GREEN_GENE_WEIGHT,
  Y: GREEN_GENE_WEIGHT,
  W: RED_GENE_WEIGHT,
  X: RED_GENE_WEIGHT
};
const GENE_IS_GREEN: Record<GeneType, boolean> = {
  G: true,
  H: true,
  Y: true,
  W: false,
  X: false
};

export class Gene {
  public readonly type: GeneType;
  public readonly weight: number;

  constructor(type: GeneType | string) {
    const upper = type.toUpperCase() as GeneType;
    if (!Gene.isValid(upper)) {
      throw new Error(`Invalid gene type: ${type}`);
    }
    this.type = upper;
    this.weight = GENE_WEIGHTS[upper];
  }

  public static isValid(gene: string): gene is GeneType {
    return (VALID_GENES as readonly string[]).includes(gene);
  }

  public static getWeight(type: GeneType): number {
    return GENE_WEIGHTS[type];
  }

  public getCrossbreedingWeight(): number {
    return this.weight;
  }

  public get isGreen(): boolean {
    return GENE_IS_GREEN[this.type];
  }

  public get isRed(): boolean {
    return !GENE_IS_GREEN[this.type];
  }

  public toString(): string {
    return this.type;
  }
}
