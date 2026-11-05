/**
 * Target-matching semantics for the three Match Modes. A target is a 6-slot
 * string of gene letters (G/Y/H/W/X) where '*' or '?' (or a missing slot) is a
 * wildcard that imposes no constraint.
 *
 *  - Exact:         result must match the target slot-by-slot (wildcards match anything).
 *  - At Least:      result must contain AT LEAST the target's count of each concrete
 *                   gene (position ignored; wildcards impose no minimum).
 *  - Best Possible: never filters; ranks by how close a result is to the target —
 *                   exact matches first, then by how many of the target's genes are
 *                   present (multiset overlap), then by positional matches.
 */

export const GENE_TYPES = ['G', 'Y', 'H', 'W', 'X'] as const;
type GeneType = (typeof GENE_TYPES)[number];

const isWildcard = (c: string | undefined): boolean =>
  c === undefined || c === '' || c === '*' || c === '?';

export const normalizeTarget = (target: string): string =>
  (target || '').toUpperCase();

/** True if the target constrains anything at all (has ≥1 concrete gene). */
export const targetHasConstraint = (target: string): boolean =>
  /[GYHWX]/.test(normalizeTarget(target));

const geneCounts = (s: string): Record<GeneType, number> => {
  const counts = { G: 0, Y: 0, H: 0, W: 0, X: 0 };
  for (const c of s.toUpperCase()) {
    if (c in counts) counts[c as GeneType]++;
  }
  return counts;
};

/** Counts of each concrete (non-wildcard) gene the target requires. */
export const requiredGeneCounts = (target: string): Record<GeneType, number> => {
  const t = normalizeTarget(target);
  const counts = { G: 0, Y: 0, H: 0, W: 0, X: 0 };
  for (let i = 0; i < 6; i++) {
    const c = t[i];
    if (!isWildcard(c) && c in counts) counts[c as GeneType]++;
  }
  return counts;
};

/** Slot-by-slot match; wildcard target slots match any result gene. */
export const isExactMatch = (result: string, target: string): boolean => {
  const t = normalizeTarget(target);
  const r = result.toUpperCase();
  for (let i = 0; i < 6; i++) {
    const req = t[i];
    if (!isWildcard(req) && r[i] !== req) return false;
  }
  return true;
};

/** Result contains at least the required count of every concrete target gene. */
export const meetsAtLeast = (result: string, target: string): boolean => {
  const req = requiredGeneCounts(target);
  const rc = geneCounts(result);
  return GENE_TYPES.every((g) => rc[g] >= req[g]);
};

/**
 * How many of the target's required genes are present in the result (multiset
 * overlap). Higher = closer. Ranges 0..(number of concrete target genes).
 */
export const targetCloseness = (result: string, target: string): number => {
  const req = requiredGeneCounts(target);
  const rc = geneCounts(result);
  return GENE_TYPES.reduce((sum, g) => sum + Math.min(rc[g], req[g]), 0);
};

/** Count of non-wildcard slots where the result gene matches the target's. */
export const positionMatches = (result: string, target: string): number => {
  const t = normalizeTarget(target);
  const r = result.toUpperCase();
  let n = 0;
  for (let i = 0; i < 6; i++) {
    if (!isWildcard(t[i]) && r[i] === t[i]) n++;
  }
  return n;
};
