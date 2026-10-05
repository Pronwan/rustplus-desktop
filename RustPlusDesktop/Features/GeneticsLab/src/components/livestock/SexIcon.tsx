import React from 'react';
import { Box, IconButton, Tooltip } from '@mui/material';
import { Mars, Venus } from 'lucide-react';
import { LivestockAnimal, LivestockSex } from '../../domain/livestock/animal.ts';

export const SEX_COLOR: Record<'male' | 'female', string> = { male: '#4FA3FF', female: '#FF6FB5' };

function sexLabel(species: LivestockAnimal['species'], sex: 'male' | 'female'): string {
  if (species === 'cattle') return sex === 'male' ? 'Bull (male)' : 'Cow (female)';
  return sex === 'male' ? 'Ram (male)' : 'Ewe (female)';
}

/** Mars or Venus for a known sex; nothing for unknown. */
export const SexIcon: React.FC<{ animal: Pick<LivestockAnimal, 'species' | 'sex'>; size?: number }> = ({ animal, size = 16 }) => {
  if (animal.sex === 'unknown') return null;
  const Icon = animal.sex === 'male' ? Mars : Venus;
  return (
    <Tooltip title={sexLabel(animal.species, animal.sex)} arrow disableInteractive>
      <Box component="span" sx={{ display: 'inline-flex', color: SEX_COLOR[animal.sex], flex: 'none' }}>
        <Icon size={size} strokeWidth={2.5} aria-label={sexLabel(animal.species, animal.sex)} />
      </Box>
    </Tooltip>
  );
};

/**
 * One-click sex for an animal the scan could not sex (calves, lambs, sheep): a Venus and a
 * Mars button side by side. Clicks do not reach the row underneath.
 */
export const SexQuickPick: React.FC<{
  animal: LivestockAnimal;
  onPick: (sex: LivestockSex) => void;
  size?: number;
}> = ({ animal, onPick, size = 16 }) => (
  <Box
    component="span"
    sx={{ display: 'inline-flex', gap: 0.25, flex: 'none' }}
    onClick={(e) => e.stopPropagation()}
  >
    {(['female', 'male'] as const).map((sex) => {
      const Icon = sex === 'male' ? Mars : Venus;
      return (
        <Tooltip key={sex} title={`Set as ${sexLabel(animal.species, sex)}`} arrow disableInteractive>
          <IconButton
            size="small"
            aria-label={`Set as ${sexLabel(animal.species, sex)}`}
            onClick={() => onPick(sex)}
            sx={{
              p: 0.25,
              color: SEX_COLOR[sex],
              border: '1px dashed',
              borderColor: SEX_COLOR[sex],
              opacity: 0.75,
              '&:hover': { opacity: 1, backgroundColor: 'transparent' }
            }}
          >
            <Icon size={size} strokeWidth={2.5} />
          </IconButton>
        </Tooltip>
      );
    })}
  </Box>
);

/** Icons for the sex toggle in the editor. */
export const SexToggleIcon: React.FC<{ sex: LivestockSex }> = ({ sex }) => {
  if (sex === 'unknown') return null;
  const Icon = sex === 'male' ? Mars : Venus;
  return <Icon size={15} strokeWidth={2.5} color={SEX_COLOR[sex]} style={{ marginRight: 4 }} />;
};
