import React from 'react';
import { Box } from '@mui/material';
import { LivestockAnimal, animalKindLabel } from '../../domain/livestock/animal.ts';
import { currentCondition } from '../../domain/livestock/pricing.ts';

/**
 * Portraits and product icons, from the game's own art, shipped under
 * public/img/livestock.
 */

export type LivestockPortrait = 'cow' | 'bull' | 'calf' | 'sheep' | 'lamb';

export function portraitFor(animal: LivestockAnimal): LivestockPortrait {
  // Young until an hour old; otherwise the adult of its kind.
  const young = currentCondition(animal).isBaby === true;
  if (animal.species === 'sheep') return young ? 'lamb' : 'sheep';
  if (young) return 'calf';
  return animal.sex === 'male' ? 'bull' : 'cow';
}

/** "Calf" or "Lamb" while young, else Cow / Bull / Ewe / Ram. */
export function kindLabel(animal: LivestockAnimal): string {
  if (currentCondition(animal).isBaby) return animal.species === 'sheep' ? 'Lamb' : 'Calf';
  return animalKindLabel(animal.species, animal.sex);
}

export const AnimalPortrait: React.FC<{ animal: LivestockAnimal; size?: number }> = ({ animal, size = 40 }) => (
  <Box
    component="img"
    src={`./img/livestock/${portraitFor(animal)}.webp`}
    alt={animalKindLabel(animal.species, animal.sex)}
    sx={{
      width: size,
      height: size,
      flex: 'none',
      borderRadius: '6px',
      objectFit: 'cover',
      backgroundColor: 'var(--gl-item-slot-bg)',
      border: '1px solid var(--gl-border)'
    }}
  />
);

export const ProductIcon: React.FC<{ name: 'milk' | 'wool' | 'dung' | 'scrap'; size?: number }> = ({ name, size = 18 }) => (
  <Box
    component="img"
    src={`./img/livestock/${name === 'dung' ? 'horsedung' : name}.webp`}
    alt=""
    aria-hidden
    sx={{ width: size, height: size, flex: 'none', objectFit: 'contain' }}
  />
);
