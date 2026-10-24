/**
 * Result-genotype admission tables.
 *
 * The solver rejects a result for two independent reasons: the genotype is
 * already owned by the source pool, or it cannot satisfy the user's target.
 * Both are pure functions of the 18-bit result code, so both collapse into one
 * direct-indexed byte table and a single array read on the hot path.
 *
 * Target pruning is only ever applied to the FINAL generation. Earlier
 * generations feed the beam, whose intermediates are deliberately not required
 * to match the target, so pruning them would change which routes exist. The
 * last generation's results only ever reach the display, which already applies
 * exactly this filter - so pruning there removes work without removing any
 * route the user could have seen.
 */

import { GENOTYPE_SPACE, encodeGenotype, decodeGenotype } from './fastCore.ts';
import { isExactMatch, meetsAtLeast, targetHasConstraint } from '../../utils/targetMatch.ts';

export type TargetMatchMode = 'exact' | 'at-least' | 'best-possible';

export interface TargetConstraint {
  targetGenetics: string;
  matchMode: TargetMatchMode;
}

/**
 * Byte table over the genotype space: 1 = the solver must discard this result.
 * `target` is optional; when absent (or in best-possible mode, which never
 * filters) only owned genotypes are rejected.
 */
export function buildRejectTable(
  ownedGenotypes: Iterable<string>,
  target?: TargetConstraint | null
): Uint8Array {
  const table = new Uint8Array(GENOTYPE_SPACE);

  if (target && target.matchMode !== 'best-possible' && targetHasConstraint(target.targetGenetics)) {
    // Start from "reject everything", then clear the matching genotypes.
    table.fill(1);
    for (let code = 0; code < GENOTYPE_SPACE; code++) {
      if (isValidCode(code) && matches(code, target)) table[code] = 0;
    }
  }

  for (const genotype of ownedGenotypes) {
    const code = encodeGenotype(genotype);
    if (code >= 0) table[code] = 1;
  }

  return table;
}

/** Codes whose 3-bit fields all fall inside the five real gene types. */
function isValidCode(code: number): boolean {
  for (let c = 0; c < 6; c++) {
    if (((code >> (c * 3)) & 7) > 4) return false;
  }
  return true;
}

function matches(code: number, target: TargetConstraint): boolean {
  const genotype = decodeGenotype(code);
  return target.matchMode === 'exact'
    ? isExactMatch(genotype, target.targetGenetics)
    : meetsAtLeast(genotype, target.targetGenetics);
}

/** True when the constraint would actually remove anything. */
export function targetPrunes(target?: TargetConstraint | null): boolean {
  return (
    !!target &&
    target.matchMode !== 'best-possible' &&
    targetHasConstraint(target.targetGenetics)
  );
}
