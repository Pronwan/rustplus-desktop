/**
 * Livestock genetics as Rust implements them (October 2026 livestock update).
 *
 * Every animal carries TWO copies of each of five genes -- D, L, Y, F, H -- and the better
 * copy is the one expressed. The in-game panel shows the expressed value of each gene, in
 * that fixed order, as the colour of its badge: red Bad, grey Ok, green Good. A newborn gets
 * one random copy of each gene from each parent.
 *
 * Values verified against the live game on 2026-10-01. Every effect is a base value scaled by the gene's
 * multiplier, so a balance change is a one-line edit here.
 */

export const LIVESTOCK_GENES = ['D', 'L', 'Y', 'F', 'H'] as const;
export type LivestockGene = (typeof LIVESTOCK_GENES)[number];

export const GENE_LEVELS = ['low', 'mid', 'high'] as const;
export type GeneLevel = (typeof GENE_LEVELS)[number];

/** Date the gene values were last checked against the game. */
export const LIVESTOCK_DATA_AS_OF = '2026-10-02';

export interface LivestockGeneInfo {
  gene: LivestockGene;
  name: string;
  summary: string;
  multipliers: Record<GeneLevel, number>;
  /**
   * Inbred animals' genes act weaker: every effect is scaled by 0.7 (an inbred Good sheep
   * gives 11 wool instead of 16, inbred lifespans are 21.8h / 33.6h / 50.4h). Inbred animals
   * still pass on their Good copies.
   */
  inbredMultipliers: Record<GeneLevel, number>;
}

/** Inbreeding scales every gene effect by this. */
export const INBRED_EFFECT = 0.7;

function gene(
  gene: LivestockGene,
  name: string,
  summary: string,
  bad: number,
  good: number
): LivestockGeneInfo {
  return {
    gene,
    name,
    summary,
    multipliers: { low: bad, mid: 1, high: good },
    inbredMultipliers: { low: bad * INBRED_EFFECT, mid: INBRED_EFFECT, high: good * INBRED_EFFECT }
  };
}

export const LIVESTOCK_GENE_INFO: Record<LivestockGene, LivestockGeneInfo> = {
  D: gene('D', 'Dung', 'How often a tame animal drops dung', 0.6, 1.6),
  L: gene('L', 'Longevity', 'How long the adult lives', 0.65, 1.5),
  Y: gene('Y', 'Yield', 'Milk or wool per gather, and how fast it regrows', 0.6, 1.6),
  F: gene('F', 'Fertility', 'Breeding cooldowns and twin chance', 0.65, 1.6),
  H: gene('H', 'Hardiness', 'How low its needs can drop and it still counts as full condition', 0.7, 1.5)
};

/** Odds of each copy in a wild animal: Good 20%, Ok 50%, Bad 30%. */
export const WILD_COPY_ODDS: Record<GeneLevel, number> = { low: 0.3, mid: 0.5, high: 0.2 };

/** Wild and vendor animals are born inbred 1 time in 16. */
export const WILD_INBRED_CHANCE = 1 / 16;

/**
 * Base values at an Ok gene. A gene divides cooldowns and intervals by its multiplier and
 * multiplies amounts and lifespan by it.
 */
export const LIVESTOCK_BASE = {
  /** Adult lifespan, hours (±10%). Animals only age while tame. */
  lifespanHours: 48,
  /** Calf or lamb to adult, seconds (±10%). */
  growUpSeconds: 3600,
  pregnancySeconds: 600,
  /** Milking cooldown and fleece regrow time, seconds. */
  gatherCooldownSeconds: 300,
  milkPerGather: 1,
  woolPerFleece: 10,
  maleBreedingCooldownSeconds: 600,
  femaleCooldownAfterBirthSeconds: 3960,
  /**
   * Dung interval at full condition, seconds (±20%). Tame adults only: cows, bulls and sheep
   * all drop dung, calves and lambs do not. One dung is 36s of biofuel.
   */
  dungIntervalSeconds: 1200,
  /** Chance of twins with a Good fertility gene. */
  goodFertilityTwinChance: 0.6
} as const;

/** The Biofuel Generator burns one dung every this many seconds (100 an hour). */
export const BIOFUEL_SECONDS_PER_DUNG = 36;

/** Lowest need level that still counts as full condition, per Hardiness level. */
export const HARDINESS_FULL_CONDITION: Record<GeneLevel, number> = { low: 0.71, mid: 0.5, high: 0.33 };

/** What the Livestock Vendor charges for young, already bonded and on a lead. */
export const VENDOR_PRICES = { calf: 300, lamb: 150 } as const;

export function geneMultiplier(gene: LivestockGene, level: GeneLevel | null, inbred = false): number {
  // An unread badge counts as Ok: it neither flatters nor punishes the animal.
  const info = LIVESTOCK_GENE_INFO[gene];
  return (inbred ? info.inbredMultipliers : info.multipliers)[level ?? 'mid'];
}

export const LEVEL_LABEL: Record<GeneLevel, string> = {
  low: 'Bad',
  mid: 'Ok',
  high: 'Good'
};

/** Next level when a badge is clicked in the editor: Ok -> Good -> Bad -> Ok. */
export function cycleLevel(level: GeneLevel | null): GeneLevel {
  if (level === 'mid' || level === null) return 'high';
  if (level === 'high') return 'low';
  return 'mid';
}
