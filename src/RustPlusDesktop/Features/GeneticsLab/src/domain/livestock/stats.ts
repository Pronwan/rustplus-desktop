import { BIOFUEL_SECONDS_PER_DUNG, GeneLevel, HARDINESS_FULL_CONDITION, INBRED_EFFECT, LIVESTOCK_BASE, geneMultiplier } from './livestockGenes.ts';
import { LivestockAnimal } from './animal.ts';
import { currentCondition } from './pricing.ts';

/**
 * What an animal's genes mean in play, from its expressed genes (the panel's row). Inbred
 * animals use the weaker inbred multipliers (every effect x0.7). Each entry is ready to show: the value for this
 * animal and the Ok-gene baseline.
 */

export interface AnimalStat {
  gene: 'D' | 'L' | 'Y' | 'F' | 'H';
  label: string;
  value: string;
  baseline: string;
  level: GeneLevel | null;
  /** Icon of the product, when the stat is about one. */
  icon?: 'milk' | 'wool' | 'dung';
  /** False when the stat does not apply to this animal at all. */
  applies: boolean;
  /** A short line shown under the stat. */
  note?: string;
}

export function formatDuration(seconds: number): string {
  if (seconds < 90) return `${Math.round(seconds * 10) / 10}s`;
  if (seconds < 3600) {
    let m = Math.floor(seconds / 60);
    let s = Math.round(seconds - m * 60);
    if (s === 60) {
      m++;
      s = 0;
    }
    return s === 0 ? `${m}m` : `${m}m ${s}s`;
  }
  const minutes = Math.round(seconds / 60);
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h}h` : `${h}h ${String(m).padStart(2, '0')}m`;
}

function percent(value: number): string {
  return `${Math.round(value * 100)}%`;
}

function round1(value: number): string {
  return String(Math.round(value * 10) / 10);
}

export function animalStats(animal: LivestockAnimal): AnimalStat[] {
  const [d, l, y, f, h] = animal.rows[0].levels;
  const inbred = animal.inbred;
  const B = LIVESTOCK_BASE;
  const isCattle = animal.species === 'cattle';
  const isMale = animal.sex === 'male';
  const isFemale = animal.sex === 'female';
  const mult = (gene: 'D' | 'L' | 'Y' | 'F' | 'H', level: GeneLevel | null) => geneMultiplier(gene, level, inbred);
  const stats: AnimalStat[] = [];

  if (isCattle) {
    stats.push({
      gene: 'Y',
      label: 'Milk per milking',
      // Only Good yield doubles the milk (Bad only slows the regrow); inbred Good's 1.12 rounds to 1.
      value: String(Math.max(1, Math.round(B.milkPerGather * mult('Y', y)))),
      baseline: String(B.milkPerGather),
      level: y,
      icon: 'milk',
      applies: !isMale
    });
    stats.push({
      gene: 'Y',
      label: 'Milking cooldown',
      value: formatDuration(B.gatherCooldownSeconds / mult('Y', y)),
      baseline: formatDuration(B.gatherCooldownSeconds),
      level: y,
      icon: 'milk',
      applies: !isMale
    });
  } else {
    stats.push({
      gene: 'Y',
      label: 'Wool per full fleece',
      // Whole wool only: an inbred Good sheep's 16 x 0.7 is 11.
      value: String(Math.floor(B.woolPerFleece * mult('Y', y) + 1e-9)),
      baseline: String(B.woolPerFleece),
      level: y,
      icon: 'wool',
      applies: true
    });
    stats.push({
      gene: 'Y',
      label: 'Fleece regrow',
      value: formatDuration(B.gatherCooldownSeconds / mult('Y', y)),
      baseline: formatDuration(B.gatherCooldownSeconds),
      level: y,
      icon: 'wool',
      applies: true
    });
  }

  if (!isFemale) {
    stats.push({
      gene: 'F',
      label: 'Male breeding cooldown',
      value: formatDuration(B.maleBreedingCooldownSeconds / mult('F', f)),
      baseline: formatDuration(B.maleBreedingCooldownSeconds),
      level: f,
      applies: true
    });
  }
  if (!isMale) {
    stats.push({
      gene: 'F',
      label: 'Cooldown after giving birth',
      value: formatDuration(B.femaleCooldownAfterBirthSeconds / mult('F', f)),
      baseline: formatDuration(B.femaleCooldownAfterBirthSeconds),
      level: f,
      applies: true
    });
    stats.push({
      gene: 'F',
      label: 'Twins chance',
      value: percent((f ?? 'mid') === 'high' ? B.goodFertilityTwinChance * (inbred ? INBRED_EFFECT : 1) : 0),
      baseline: '0%',
      level: f,
      applies: true
    });
  }

  stats.push({
    gene: 'H',
    label: 'Lowest need for full condition',
    value: percent(HARDINESS_FULL_CONDITION[h ?? 'mid']),
    baseline: percent(HARDINESS_FULL_CONDITION.mid),
    level: h,
    applies: true
  });

  stats.push({
    gene: 'L',
    label: inbred ? 'Adult lifespan (inbred)' : 'Adult lifespan',
    value: formatDuration(B.lifespanHours * 3600 * mult('L', l)),
    baseline: `${B.lifespanHours}h`,
    level: l,
    applies: true
  });

  const dungInterval = B.dungIntervalSeconds / mult('D', d);
  const dungPerHour = 3600 / dungInterval;
  // Calves and lambs drop no dung.
  const baby = currentCondition(animal).isBaby === true;
  stats.push({
    gene: 'D',
    label: 'Dung interval',
    value: `${formatDuration(dungInterval)} (${round1(dungPerHour)}/h)`,
    baseline: formatDuration(B.dungIntervalSeconds),
    level: d,
    icon: 'dung',
    applies: !baby,
    note: baby
      ? 'Adults only: calves and lambs drop no dung.'
      : `${Math.ceil(3600 / BIOFUEL_SECONDS_PER_DUNG / dungPerHour - 1e-9)} adults like this keep one Biofuel Generator running.`
  });

  return stats;
}
