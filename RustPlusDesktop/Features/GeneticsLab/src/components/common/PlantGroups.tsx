import React, { useMemo } from 'react';
import { Box, ButtonBase, Tooltip, Typography } from '@mui/material';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { PLANT_TYPES } from '../../context/AppContext.tsx';
import { plantLabel } from './PlantPicker.tsx';

/**
 * The gene list's plant groups: every plant that has clones, as a chip with its icon and
 * count. Clicking one shows (and calculates) that plant's list. Hidden while there is only
 * one group.
 */
export const PlantGroups: React.FC = () => {
  const { allClones, selectedPlant, setSelectedPlant } = useWorkspace();

  const groups = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const clone of allClones) counts[clone.cropType] = (counts[clone.cropType] ?? 0) + 1;
    return PLANT_TYPES.filter((plant) => (counts[plant] ?? 0) > 0 || plant === selectedPlant).map((plant) => ({
      plant,
      count: counts[plant] ?? 0
    }));
  }, [allClones, selectedPlant]);

  if (groups.length < 2) return null;

  return (
    <Box role="tablist" aria-label="Plant groups" sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', mb: 1 }}>
      {groups.map(({ plant, count }) => {
        const selected = plant === selectedPlant;
        return (
          <Tooltip key={plant} title={selected ? 'Shown and calculated now' : `Show and calculate ${plantLabel(plant)}`}>
            <ButtonBase
              role="tab"
              aria-selected={selected}
              onClick={() => setSelectedPlant(plant)}
              sx={{
                gap: 0.5,
                pl: 0.5,
                pr: 0.75,
                py: 0.25,
                borderRadius: '999px',
                border: '1px solid',
                borderColor: selected ? 'var(--gl-primary)' : 'var(--gl-border-strong)',
                backgroundColor: selected ? 'rgba(var(--gl-primary-rgb), 0.14)' : 'transparent',
                transition: 'border-color 120ms ease, background-color 120ms ease',
                '&:hover': { borderColor: 'var(--gl-primary)' }
              }}
            >
              <Box component="img" src={`./img/items/${plant}.webp`} alt="" sx={{ width: 16, height: 16, objectFit: 'contain' }} />
              <Typography sx={{ fontSize: '0.68rem', fontWeight: 800, color: selected ? 'var(--gl-primary)' : 'var(--gl-text-secondary)' }}>
                {plantLabel(plant)}
              </Typography>
              <Typography sx={{ fontSize: '0.62rem', fontWeight: 800, fontFamily: 'monospace', color: 'var(--gl-text-muted)' }}>
                {count}
              </Typography>
            </ButtonBase>
          </Tooltip>
        );
      })}
    </Box>
  );
};
