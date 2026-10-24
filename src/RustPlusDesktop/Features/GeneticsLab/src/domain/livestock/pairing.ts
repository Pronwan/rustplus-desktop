import { GeneLevel, LIVESTOCK_GENES, geneMultiplier } from './livestockGenes.ts';
import { LivestockAnimal, LivestockSex } from './animal.ts';
import { BASE_SALE_PRICE, SPECIES_PRICE_FACTOR } from './pricing.ts';
import {
  GenotypeDist,
  HerdGenotypes,
  breedsTrue,
  expressedLevels,
  inferHerdGenotypes,
  offspringGenotype,
  WILD_GENOTYPE
} from './genotype.ts';

/**
 * Pair suggestions from Rust's inheritance rule: a newborn gets one random copy of each gene
 * from each parent, and expresses the better of its two copies.
 *
 * The rule itself is documented. What is uncertain is the parents' hidden copies, which
 * `genotype.ts` estimates from the panel, the wild odds and any recorded ancestry. The
 * numbers here are exact for that estimate, and as good as the records behind it.
 */

export type Relation = 'parent-child' | 'siblings' | 'half-siblings' | 'shared-ancestor';

export interface GeneOutlook {
  /** Expected multiplier of the newborn's expressed gene. */
  expectedMultiplier: number;
  /** Chance the newborn shows Good. */
  high: number;
  /** Chance the newborn shows Bad. */
  low: number;
  /** Chance the newborn carries two Good copies (and so always passes on Good). */
  breedsTrue: number;
}

export interface PairSuggestion {
  male: LivestockAnimal;
  female: LivestockAnimal;
  /** Expected gene factor of the newborn (mean expected multiplier). */
  expectedGeneFactor: number;
  /** Expected sale value of the newborn as a young adult at full condition. */
  expectedValue: number;
  /** Chance every gene shows Good. */
  allHighChance: number;
  /** Chance no gene shows Bad. */
  noLowChance: number;
  /** Chance of a god clone: all ten copies Good. */
  godCloneChance: number;
  /** Genes where the newborn can show Good at all. */
  genesWithHighSource: number;
  perGene: GeneOutlook[];
  relation: Relation | null;
  /** True when one or both sexes were never recorded. */
  sexAssumed: boolean;
}

function ancestors(animal: LivestockAnimal, byId: Map<string, LivestockAnimal>, depth: number): Set<string> {
  const found = new Set<string>();
  let frontier: LivestockAnimal[] = [animal];
  for (let level = 0; level < depth && frontier.length > 0; level++) {
    const next: LivestockAnimal[] = [];
    for (const current of frontier) {
      for (const parentId of [current.motherId, current.fatherId]) {
        if (!parentId || found.has(parentId)) continue;
        found.add(parentId);
        const parent = byId.get(parentId);
        if (parent) next.push(parent);
      }
    }
    frontier = next;
  }
  return found;
}

/**
 * How two animals are related, from the parents the player recorded. Unrecorded parents
 * mean "not known to be related", which is not the same as unrelated.
 */
export function relationBetween(
  a: LivestockAnimal,
  b: LivestockAnimal,
  herd: LivestockAnimal[]
): Relation | null {
  if (a.motherId === b.id || a.fatherId === b.id || b.motherId === a.id || b.fatherId === a.id) {
    return 'parent-child';
  }
  const sameMother = !!a.motherId && a.motherId === b.motherId;
  const sameFather = !!a.fatherId && a.fatherId === b.fatherId;
  if (sameMother && sameFather) return 'siblings';
  if (sameMother || sameFather) return 'half-siblings';

  const byId = new Map(herd.map((animal) => [animal.id, animal]));
  const ancestorsA = ancestors(a, byId, 3);
  const ancestorsB = ancestors(b, byId, 3);
  if (ancestorsA.has(b.id) || ancestorsB.has(a.id)) return 'shared-ancestor';
  for (const id of ancestorsA) {
    if (ancestorsB.has(id)) return 'shared-ancestor';
  }
  return null;
}

export const RELATION_LABEL: Record<Relation, string> = {
  'parent-child': 'Parent and child',
  siblings: 'Siblings',
  'half-siblings': 'Half-siblings',
  'shared-ancestor': 'Shared ancestor'
};

function canPair(a: LivestockSex, b: LivestockSex): boolean {
  if (a === 'unknown' || b === 'unknown') return true;
  return a !== b;
}

export function geneOutlook(
  geneIndex: number,
  mother: GenotypeDist,
  father: GenotypeDist,
  inbred: boolean
): GeneOutlook {
  const gene = LIVESTOCK_GENES[geneIndex];
  const child = offspringGenotype(mother, father);
  const shown = expressedLevels(child);
  const levels: GeneLevel[] = ['low', 'mid', 'high'];
  return {
    expectedMultiplier: levels.reduce((sum, level) => sum + shown[level] * geneMultiplier(gene, level, inbred), 0),
    high: shown.high,
    low: shown.low,
    breedsTrue: breedsTrue(child)
  };
}

export function evaluatePair(
  male: LivestockAnimal,
  female: LivestockAnimal,
  herd: LivestockAnimal[],
  genotypes: HerdGenotypes = inferHerdGenotypes(herd)
): PairSuggestion {
  const relation = relationBetween(male, female, herd);
  // Close relatives can produce an inbred newborn; plan for the worse case.
  const inbred = relation !== null;
  const maleDists = genotypes.get(male.id) ?? LIVESTOCK_GENES.map(() => WILD_GENOTYPE);
  const femaleDists = genotypes.get(female.id) ?? LIVESTOCK_GENES.map(() => WILD_GENOTYPE);
  const perGene = LIVESTOCK_GENES.map((_, i) => geneOutlook(i, femaleDists[i], maleDists[i], inbred));
  const meanMultiplier = perGene.reduce((sum, g) => sum + g.expectedMultiplier, 0) / perGene.length;
  // Inbred multipliers already carry the x0.7 the sale formula applies to inbred animals.
  const saleFactor = meanMultiplier;

  return {
    male,
    female,
    expectedGeneFactor: meanMultiplier,
    expectedValue: BASE_SALE_PRICE * SPECIES_PRICE_FACTOR[male.species] * saleFactor,
    allHighChance: perGene.reduce((p, g) => p * g.high, 1),
    noLowChance: perGene.reduce((p, g) => p * (1 - g.low), 1),
    godCloneChance: perGene.reduce((p, g) => p * g.breedsTrue, 1),
    genesWithHighSource: perGene.filter((g) => g.high > 0).length,
    perGene,
    relation,
    sexAssumed: male.sex === 'unknown' || female.sex === 'unknown'
  };
}

export type PairSortKey = 'expected' | 'allHigh' | 'godClone' | 'noLow';

/**
 * Every eligible pairing in the herd, best first. Same species only; known sexes must
 * differ. Related pairs are kept (the player may want to see them) but are scored as
 * inbred, so they sink.
 */
export function suggestPairs(herd: LivestockAnimal[], sortBy: PairSortKey = 'expected'): PairSuggestion[] {
  const genotypes = inferHerdGenotypes(herd);
  const suggestions: PairSuggestion[] = [];

  for (let i = 0; i < herd.length; i++) {
    for (let j = i + 1; j < herd.length; j++) {
      const a = herd[i];
      const b = herd[j];
      if (a.species !== b.species || !canPair(a.sex, b.sex)) continue;
      const aIsMale = a.sex === 'male' || b.sex === 'female';
      suggestions.push(evaluatePair(aIsMale ? a : b, aIsMale ? b : a, herd, genotypes));
    }
  }

  const primary: Record<PairSortKey, (s: PairSuggestion) => number> = {
    expected: (s) => s.expectedGeneFactor,
    allHigh: (s) => s.allHighChance,
    godClone: (s) => s.godCloneChance,
    noLow: (s) => s.noLowChance
  };
  const key = primary[sortBy];

  return suggestions.sort(
    (x, y) =>
      key(y) - key(x) ||
      y.expectedGeneFactor - x.expectedGeneFactor ||
      y.allHighChance - x.allHighChance ||
      y.noLowChance - x.noLowChance ||
      Number(!!x.relation) - Number(!!y.relation)
  );
}
