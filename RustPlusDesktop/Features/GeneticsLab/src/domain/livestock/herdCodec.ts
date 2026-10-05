import { GENE_LEVELS, GeneLevel } from './livestockGenes.ts';
import {
  LivestockAnimal,
  LivestockGeneRow,
  LivestockSex,
  LivestockSpecies,
  MarkerColor,
  createAnimal
} from './animal.ts';

/**
 * Validation for herds loaded from storage or pasted in by the player. Anything that does
 * not look like an animal is dropped rather than repaired into something misleading.
 */

const SPECIES: LivestockSpecies[] = ['cattle', 'sheep'];
const SEXES: LivestockSex[] = ['male', 'female', 'unknown'];
const MARKERS: MarkerColor[] = ['pink', 'blue', 'purple', 'teal', 'lime', 'red', 'green', 'grey', 'unknown'];

function sanitizeRow(raw: any): LivestockGeneRow | null {
  if (!raw || !Array.isArray(raw.levels) || raw.levels.length !== 5) return null;
  const levels = raw.levels.map((level: unknown) =>
    (GENE_LEVELS as readonly string[]).includes(level as string) ? (level as GeneLevel) : null
  );
  const value = raw.marker?.value;
  return {
    levels,
    marker: {
      value: Number.isInteger(value) && value >= 0 && value < 1000 ? value : null,
      color: MARKERS.includes(raw.marker?.color) ? raw.marker.color : 'unknown'
    }
  };
}

function sanitizeObserved(raw: any): LivestockAnimal['observed'] {
  if (!raw || typeof raw !== 'object' || !Number.isFinite(raw.at)) return undefined;
  const age = Number.isFinite(raw.ageSeconds) && raw.ageSeconds >= 0 ? raw.ageSeconds : null;
  const overall = Number.isFinite(raw.overall) && raw.overall >= 0 && raw.overall <= 1 ? raw.overall : null;
  return age === null && overall === null ? undefined : { ageSeconds: age, overall, at: raw.at };
}

export function sanitizeAnimal(raw: any): LivestockAnimal | null {
  if (!raw || typeof raw !== 'object' || typeof raw.id !== 'string' || !raw.id) return null;
  if (!Array.isArray(raw.rows) || raw.rows.length < 1 || raw.rows.length > 2) return null;
  const rows = raw.rows.map(sanitizeRow);
  if (rows.some((row: LivestockGeneRow | null) => !row)) return null;
  return createAnimal({
    id: raw.id,
    name: typeof raw.name === 'string' ? raw.name.slice(0, 60) : '',
    species: SPECIES.includes(raw.species) ? raw.species : 'cattle',
    sex: SEXES.includes(raw.sex) ? raw.sex : 'unknown',
    rows: rows as LivestockGeneRow[],
    inbred: raw.inbred === true,
    motherId: typeof raw.motherId === 'string' ? raw.motherId : undefined,
    fatherId: typeof raw.fatherId === 'string' ? raw.fatherId : undefined,
    notes: typeof raw.notes === 'string' ? raw.notes.slice(0, 500) : undefined,
    observed: sanitizeObserved(raw.observed),
    gameName: typeof raw.gameName === 'string' && raw.gameName.trim() ? raw.gameName.trim().slice(0, 40) : undefined,
    source: raw.source === 'scan' ? 'scan' : 'manual',
    createdAt: Number.isFinite(raw.createdAt) ? raw.createdAt : Date.now()
  });
}

export function sanitizeHerd(raw: unknown): LivestockAnimal[] {
  const list = Array.isArray(raw) ? raw : Array.isArray((raw as any)?.animals) ? (raw as any).animals : [];
  const seen = new Set<string>();
  const herd: LivestockAnimal[] = [];
  for (const entry of list) {
    const animal = sanitizeAnimal(entry);
    if (!animal || seen.has(animal.id)) continue;
    seen.add(animal.id);
    herd.push(animal);
  }
  // Parent links that point outside the herd are kept: the parent may simply have been sold.
  return herd;
}

export const HERD_EXPORT_FORMAT = 'genetics-lab/livestock-herd';

export function exportHerd(herd: LivestockAnimal[]): string {
  return JSON.stringify({ format: HERD_EXPORT_FORMAT, version: 1, animals: herd }, null, 2);
}
