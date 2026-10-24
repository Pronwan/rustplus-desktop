import { GeneLevel, LIVESTOCK_GENES } from './livestockGenes.ts';

export type LivestockSpecies = 'cattle' | 'sheep';
export type LivestockSex = 'male' | 'female' | 'unknown';

/**
 * Colour of the sixth badge, which carries a number. Its meaning is not documented yet
 * (captures show 0 on pink, 2 on green, 4 on teal, 6 on purple, 10 on lime, 14 on blue), so it is stored exactly as
 * read and never interpreted.
 */
export type MarkerColor = 'pink' | 'blue' | 'purple' | 'teal' | 'lime' | 'red' | 'green' | 'grey' | 'unknown';

export interface LivestockGeneRow {
  /** One level per gene in D, L, Y, F, H order; `null` where the badge could not be read. */
  levels: Array<GeneLevel | null>;
  marker: { value: number | null; color: MarkerColor };
}

/** AGE and CONDITIONS > OVERALL as last read from the game (or entered by hand). */
export interface ObservedCondition {
  /** Age shown in the panel, in seconds, at `at`. */
  ageSeconds: number | null;
  /** Overall condition, 0..1. */
  overall: number | null;
  /** When it was read, ms since epoch. Age keeps counting from here. */
  at: number;
}

export interface LivestockAnimal {
  id: string;
  name: string;
  species: LivestockSpecies;
  sex: LivestockSex;
  /**
   * Badge rows exactly as the game draws them. The live panel shows one row; an earlier
   * panel showed a large row over a small one, kept as `rows[1]`. `rows[0]` is always the
   * animal's own genes.
   */
  rows: LivestockGeneRow[];
  /** Set by the player; the game's own inbreeding indicator is not decoded yet. */
  inbred: boolean;
  motherId?: string;
  fatherId?: string;
  notes?: string;
  /** The name the game gives the animal, as read from its panel. Used to recognise it again. */
  gameName?: string;
  observed?: ObservedCondition;
  source: 'scan' | 'manual';
  createdAt: number;
}

export const SPECIES_LABEL: Record<LivestockSpecies, string> = {
  cattle: 'Cattle',
  sheep: 'Sheep'
};

export function animalKindLabel(species: LivestockSpecies, sex: LivestockSex): string {
  if (species === 'cattle') {
    if (sex === 'male') return 'Bull';
    if (sex === 'female') return 'Cow';
    return 'Cattle';
  }
  if (sex === 'male') return 'Ram';
  if (sex === 'female') return 'Ewe';
  return 'Sheep';
}

export function emptyGeneRow(): LivestockGeneRow {
  return {
    levels: LIVESTOCK_GENES.map(() => 'mid' as GeneLevel),
    marker: { value: null, color: 'unknown' }
  };
}

let idCounter = 0;
export function newAnimalId(): string {
  idCounter = (idCounter + 1) % 1_000_000;
  return `animal-${Date.now().toString(36)}-${idCounter.toString(36)}`;
}

export function createAnimal(partial: Partial<LivestockAnimal> = {}): LivestockAnimal {
  return {
    id: partial.id ?? newAnimalId(),
    name: partial.name ?? '',
    species: partial.species ?? 'cattle',
    sex: partial.sex ?? 'female',
    rows: partial.rows && partial.rows.length > 0 ? partial.rows.slice(0, 2) : [emptyGeneRow()],
    inbred: partial.inbred ?? false,
    motherId: partial.motherId,
    fatherId: partial.fatherId,
    notes: partial.notes,
    gameName: partial.gameName,
    observed: partial.observed,
    source: partial.source ?? 'manual',
    createdAt: partial.createdAt ?? Date.now()
  };
}

const LEVEL_CODE: Record<GeneLevel, string> = { low: 'r', mid: 'n', high: 'g' };
const CODE_LEVEL: Record<string, GeneLevel> = { r: 'low', n: 'mid', g: 'high' };
const MARKER_CODE: Record<MarkerColor, string> = {
  pink: 'p',
  blue: 'b',
  purple: 'v',
  teal: 't',
  lime: 'l',
  red: 'r',
  green: 'g',
  grey: 'n',
  unknown: '?'
};
const CODE_MARKER: Record<string, MarkerColor> = {
  p: 'pink',
  b: 'blue',
  v: 'purple',
  t: 'teal',
  l: 'lime',
  r: 'red',
  g: 'green',
  n: 'grey',
  '?': 'unknown'
};

/**
 * Compact, stable text form of a row: one letter per gene (`r` red, `n` neutral, `g` green,
 * `?` unread), then the marker's value and colour. `rrgnn|0p` reads "D red, L red, Y green,
 * F neutral, H neutral, marker 0 pink". Used for de-duplicating scans and for export.
 */
export function encodeGeneRow(row: LivestockGeneRow): string {
  const genes = row.levels.map((level) => (level ? LEVEL_CODE[level] : '?')).join('');
  const value = row.marker.value === null ? '?' : String(row.marker.value);
  return `${genes}|${value}${MARKER_CODE[row.marker.color]}`;
}

export function decodeGeneRow(code: string): LivestockGeneRow | null {
  const match = /^([rng?]{5})\|(\?|\d{1,3})([pbvtlrgn?])$/i.exec(code.trim());
  if (!match) return null;
  const levels = match[1]
    .toLowerCase()
    .split('')
    .map((ch) => (ch === '?' ? null : CODE_LEVEL[ch]));
  return {
    levels,
    marker: {
      value: match[2] === '?' ? null : Number(match[2]),
      color: CODE_MARKER[match[3].toLowerCase()]
    }
  };
}

/** Every row; identical for two reads of the same animal's panel. */
export function encodeAnimalGenes(rows: LivestockGeneRow[]): string {
  return rows.map(encodeGeneRow).join('/');
}

export function countLevels(row: LivestockGeneRow): Record<GeneLevel | 'unknown', number> {
  const counts = { low: 0, mid: 0, high: 0, unknown: 0 };
  for (const level of row.levels) {
    if (level) counts[level]++;
    else counts.unknown++;
  }
  return counts;
}

/** Default display name, e.g. "Cow 3", numbered within its kind. */
export function suggestAnimalName(
  species: LivestockSpecies,
  sex: LivestockSex,
  herd: LivestockAnimal[]
): string {
  const kind = animalKindLabel(species, sex);
  const used = new Set(herd.map((a) => a.name));
  let n = herd.filter((a) => a.species === species && a.sex === sex).length + 1;
  while (used.has(`${kind} ${n}`)) n++;
  return `${kind} ${n}`;
}

export function displayName(animal: LivestockAnimal): string {
  return animal.name.trim() || animalKindLabel(animal.species, animal.sex);
}

/* Cyrillic letters drawn like Latin ones, and Latin letters a small reader mixes up. */
const FOLD: Record<string, string> = {
  а: 'a', в: 'b', е: 'e', ё: 'e', к: 'k', м: 'm', н: 'h', о: 'o', р: 'p', с: 'c', т: 't', у: 'y', х: 'x',
  j: 'i', l: 'i', f: 't'
};

/** A name reduced to what survives reading errors: case, look-alike letters, script. */
export function nameKey(name: string): string {
  return [...name.trim().toLowerCase()]
    .map((ch) => FOLD[ch] ?? ch)
    .join('')
    .replace(/rn/g, 'm');
}

function editDistance(a: string, b: string): number {
  const row = Array.from({ length: b.length + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    let prev = row[0];
    row[0] = i;
    for (let j = 1; j <= b.length; j++) {
      const temp = row[j];
      row[j] = Math.min(row[j] + 1, row[j - 1] + 1, prev + (a[i - 1] === b[j - 1] ? 0 : 1));
      prev = temp;
    }
  }
  return row[b.length];
}

/**
 * Whether two read names are the same animal's. Exact after folding; one letter off is
 * forgiven in names of five letters or more, where it is far likelier a misread than a
 * different animal.
 */
export function namesMatch(a: string, b: string): boolean {
  const ka = nameKey(a);
  const kb = nameKey(b);
  if (!ka || !kb) return false;
  if (ka === kb) return true;
  return Math.min(ka.length, kb.length) >= 5 && editDistance(ka, kb) <= 1;
}

/**
 * The herd animal a scan belongs to, so one animal is never added twice.
 *
 * - With a name: the animal of that name (look-alike letters and one misread forgiven), even
 *   if its genes or marker read differently this time. Failing that, an animal with the same
 *   genes that has no name on record yet -- the same animal, scanned before names were read.
 * - Without a name: an animal with exactly the same genes and marker.
 *
 * Species must agree whenever the scan knows it.
 */
export function findScannedAnimal(
  herd: LivestockAnimal[],
  scan: { rows: LivestockGeneRow[]; name: string | null; species: LivestockSpecies | null }
): LivestockAnimal | null {
  const sameSpecies = (a: LivestockAnimal) => !scan.species || a.species === scan.species;
  const genes = encodeAnimalGenes(scan.rows);
  if (scan.name) {
    const byName = herd.find((a) => a.gameName && sameSpecies(a) && namesMatch(a.gameName, scan.name!));
    if (byName) return byName;
    return herd.find((a) => !a.gameName && sameSpecies(a) && encodeAnimalGenes(a.rows) === genes) ?? null;
  }
  return herd.find((a) => sameSpecies(a) && encodeAnimalGenes(a.rows) === genes) ?? null;
}
