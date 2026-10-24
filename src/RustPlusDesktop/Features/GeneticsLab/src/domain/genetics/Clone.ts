import { Sapling } from './Sapling.ts';

export interface SavedClone {
  id: string;
  genetics: string;
  cropType: string;
  name?: string;
  quantity: number;
  favorite: boolean;
  tags: string[];
  notes?: string;
  source?: 'manual' | 'scanner' | 'import' | 'sample';
  createdAt: string;
  updatedAt: string;
}

export class CloneUtils {
  public static create(
    genetics: string,
    cropType: string,
    options: Partial<Omit<SavedClone, 'id' | 'genetics' | 'cropType' | 'createdAt' | 'updatedAt'>> = {}
  ): SavedClone {
    const cleanGenes = genetics.trim().toUpperCase();
    const now = new Date().toISOString();
    return {
      id: `clone_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`,
      genetics: cleanGenes,
      cropType,
      name: options.name || '',
      quantity: options.quantity !== undefined ? Math.max(1, options.quantity) : 1,
      favorite: options.favorite || false,
      tags: options.tags || [],
      notes: options.notes || '',
      source: options.source || 'manual',
      createdAt: now,
      updatedAt: now
    };
  }

  public static toSapling(clone: SavedClone, originalIndex?: number): Sapling {
    return new Sapling(clone.genetics, 0, originalIndex);
  }

  public static toSaplings(clones: SavedClone[]): Sapling[] {
    return clones.map((c, idx) => new Sapling(c.genetics, 0, idx));
  }

  public static isValid(clone: Partial<SavedClone>): boolean {
    return !!clone.genetics && Sapling.isValidGeneString(clone.genetics);
  }

  public static countGreenGenes(genetics: string): number {
    let count = 0;
    for (const char of genetics.toUpperCase()) {
      if (char === 'G' || char === 'Y' || char === 'H') {
        count++;
      }
    }
    return count;
  }

  public static countRedGenes(genetics: string): number {
    let count = 0;
    for (const char of genetics.toUpperCase()) {
      if (char === 'W' || char === 'X') {
        count++;
      }
    }
    return count;
  }
}
