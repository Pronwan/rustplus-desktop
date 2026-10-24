import { GeneLevel, LIVESTOCK_GENES, WILD_COPY_ODDS } from './livestockGenes.ts';
import { LivestockAnimal } from './animal.ts';

/**
 * What an animal really carries, as far as it can be known.
 *
 * The panel shows each gene's EXPRESSED value, which is the better of the animal's two
 * copies. The other copy is hidden. A grey (Ok) badge can hide a Bad copy; a green (Good)
 * badge can hide anything. Breeding passes on one random copy from each parent, so the
 * hidden copies matter as much as the visible ones.
 *
 * For every animal and gene this keeps a probability for each possible pair of copies:
 * the prior comes from the parents when they are recorded (otherwise from the odds of a
 * wild animal), and is then narrowed to the pairs whose better copy matches what the panel
 * shows. Inference runs forward from parents to young only; a calf's genes do not, here,
 * sharpen what is known about its parents.
 */

/** Copy quality as an index: 0 Bad, 1 Ok, 2 Good. */
export type Copy = 0 | 1 | 2;

const LEVEL_COPY: Record<GeneLevel, Copy> = { low: 0, mid: 1, high: 2 };
const COPY_LEVEL: GeneLevel[] = ['low', 'mid', 'high'];

/** The six unordered pairs of copies, in a fixed order. */
export const COPY_PAIRS: ReadonlyArray<readonly [Copy, Copy]> = [
  [0, 0],
  [0, 1],
  [0, 2],
  [1, 1],
  [1, 2],
  [2, 2]
];

/** Probability of each entry of COPY_PAIRS; sums to 1. */
export type GenotypeDist = number[];

/** Probability of each copy quality, indexed by Copy. */
export type CopyDist = [number, number, number];

export const WILD_COPY: CopyDist = [WILD_COPY_ODDS.low, WILD_COPY_ODDS.mid, WILD_COPY_ODDS.high];

function pairIndex(a: Copy, b: Copy): number {
  const lo = Math.min(a, b);
  const hi = Math.max(a, b);
  return COPY_PAIRS.findIndex(([x, y]) => x === lo && y === hi);
}

/** Pair distribution of two independent copies. */
export function pairFromCopies(a: CopyDist, b: CopyDist): GenotypeDist {
  const dist = new Array(COPY_PAIRS.length).fill(0);
  for (let i = 0 as Copy; i <= 2; i++) {
    for (let j = 0 as Copy; j <= 2; j++) {
      dist[pairIndex(i as Copy, j as Copy)] += a[i] * b[j];
    }
  }
  return dist;
}

export const WILD_GENOTYPE: GenotypeDist = pairFromCopies(WILD_COPY, WILD_COPY);

/** One known pair of copies. */
export function exactGenotype(a: GeneLevel, b: GeneLevel): GenotypeDist {
  const dist = new Array(COPY_PAIRS.length).fill(0);
  dist[pairIndex(LEVEL_COPY[a], LEVEL_COPY[b])] = 1;
  return dist;
}

function normalise(dist: GenotypeDist): GenotypeDist | null {
  const total = dist.reduce((sum, p) => sum + p, 0);
  return total > 1e-12 ? dist.map((p) => p / total) : null;
}

/** Keep only the pairs whose better copy is the shown level. */
export function conditionOnExpressed(dist: GenotypeDist, shown: GeneLevel | null): GenotypeDist {
  if (shown === null) return dist;
  const target = LEVEL_COPY[shown];
  const narrowed = normalise(dist.map((p, i) => (Math.max(...COPY_PAIRS[i]) === target ? p : 0)));
  // A shown value the recorded parents cannot produce means the records are wrong (or the
  // badge was misread); fall back to what the badge alone says.
  return narrowed ?? normalise(WILD_GENOTYPE.map((p, i) => (Math.max(...COPY_PAIRS[i]) === target ? p : 0)))!;
}

/** Odds of each copy quality being the one passed to a newborn. */
export function passedCopy(dist: GenotypeDist): CopyDist {
  const out: CopyDist = [0, 0, 0];
  dist.forEach((p, i) => {
    const [a, b] = COPY_PAIRS[i];
    out[a] += p / 2;
    out[b] += p / 2;
  });
  return out;
}

export function offspringGenotype(mother: GenotypeDist, father: GenotypeDist): GenotypeDist {
  return pairFromCopies(passedCopy(mother), passedCopy(father));
}

/** Odds of each level being the expressed (better) one. */
export function expressedLevels(dist: GenotypeDist): Record<GeneLevel, number> {
  const out = { low: 0, mid: 0, high: 0 };
  dist.forEach((p, i) => {
    out[COPY_LEVEL[Math.max(...COPY_PAIRS[i]) as Copy]] += p;
  });
  return out;
}

/** Chance both copies are Good, i.e. the gene always passes on Good. */
export function breedsTrue(dist: GenotypeDist): number {
  return dist[COPY_PAIRS.length - 1];
}

/** Chance the hidden (worse) copy is Bad -- a carrier of Bad. */
export function hidesBad(dist: GenotypeDist): number {
  return dist.reduce((sum, p, i) => (COPY_PAIRS[i][0] === 0 ? sum + p : sum), 0);
}

export type HerdGenotypes = Map<string, GenotypeDist[]>;

/**
 * Genotype estimates for a whole herd. Animals recorded with two rows of badges (the
 * earlier two-row panel showed both copies) are known exactly.
 */
export function inferHerdGenotypes(herd: LivestockAnimal[]): HerdGenotypes {
  const byId = new Map(herd.map((a) => [a.id, a]));
  const result: HerdGenotypes = new Map();
  const inProgress = new Set<string>();

  const parentPassing = (id: string | undefined, geneIndex: number): GenotypeDist | null => {
    if (!id) return null;
    const parent = byId.get(id);
    if (!parent) return null;
    const dists = solve(parent);
    return dists ? dists[geneIndex] : null;
  };

  function solve(animal: LivestockAnimal): GenotypeDist[] | null {
    const cached = result.get(animal.id);
    if (cached) return cached;
    // A parent loop in the records: break it by treating this ancestor as wild.
    if (inProgress.has(animal.id)) return null;
    inProgress.add(animal.id);

    const dists = LIVESTOCK_GENES.map((_, i) => {
      const shown = animal.rows[0]?.levels[i] ?? null;
      const second = animal.rows[1]?.levels[i] ?? null;
      if (animal.rows.length > 1 && shown && second) return exactGenotype(shown, second);

      const mother = parentPassing(animal.motherId, i);
      const father = parentPassing(animal.fatherId, i);
      const prior =
        mother || father ? offspringGenotype(mother ?? WILD_GENOTYPE, father ?? WILD_GENOTYPE) : WILD_GENOTYPE;
      return conditionOnExpressed(prior, shown);
    });

    inProgress.delete(animal.id);
    result.set(animal.id, dists);
    return dists;
  }

  for (const animal of herd) solve(animal);
  return result;
}
