import React, { useMemo } from 'react';
import { Box, ButtonBase, Popover, Typography } from '@mui/material';
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from './GeneticsSequence.tsx';
import { PlantIcon, plantLabel } from './PlantPicker.tsx';
import { PLANT_GROUPS } from '../../domain/plants.ts';

interface MoveCloneMenuProps {
  /** Where to open: an element, or a point for a right-click. */
  anchor: { element: HTMLElement } | { top: number; left: number } | null;
  gene: string;
  onMove: (plant: string) => void;
  onClose: () => void;
}

/** "Move to…" for one clone: every other plant as an icon tile with its clone count. */
export const MoveCloneMenu: React.FC<MoveCloneMenuProps> = ({ anchor, gene, onMove, onClose }) => {
  const { allClones, selectedPlant } = useWorkspace();
  const counts = useMemo(() => {
    const map: Record<string, number> = {};
    for (const clone of allClones) map[clone.cropType] = (map[clone.cropType] ?? 0) + 1;
    return map;
  }, [allClones]);

  return (
    <Popover
      open={anchor !== null}
      onClose={onClose}
      anchorEl={anchor && 'element' in anchor ? anchor.element : undefined}
      anchorReference={anchor && 'element' in anchor ? 'anchorEl' : 'anchorPosition'}
      anchorPosition={anchor && 'top' in anchor ? anchor : undefined}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
      slotProps={{
        paper: {
          role: 'dialog',
          'aria-label': `Move ${gene} to another plant`,
          sx: {
            p: 1.25,
            width: 'min(320px, calc(100vw - 32px))',
            borderRadius: '10px',
            border: '1px solid var(--gl-border)',
            backgroundColor: 'var(--gl-panel-bg)',
            boxShadow: '0 18px 40px rgba(0,0,0,0.45)'
          }
        } as object
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, mb: 1 }}>
        <DriveFileMoveIcon sx={{ fontSize: 18, color: 'var(--gl-primary)' }} />
        <Typography sx={{ fontSize: '0.74rem', fontWeight: 800 }}>Move</Typography>
        <GeneticsSequence genes={gene} size="small" />
        <Typography sx={{ fontSize: '0.74rem', fontWeight: 800 }}>to</Typography>
      </Box>
      {PLANT_GROUPS.map((group) => (
        <Box key={group.title} sx={{ '&:not(:last-of-type)': { mb: 0.75 } }}>
          <Typography sx={{ fontSize: '0.6rem', fontWeight: 800, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--gl-text-muted)', mb: 0.5 }}>
            {group.title}
          </Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 0.5 }}>
            {group.plants.map((plant) => {
              const current = plant === selectedPlant;
              const n = counts[plant] ?? 0;
              return (
                <ButtonBase
                  key={plant}
                  disabled={current}
                  onClick={() => onMove(plant)}
                  aria-label={`${plantLabel(plant)}${current ? ' (here now)' : `, ${n} clone${n === 1 ? '' : 's'}`}`}
                  sx={{
                    justifyContent: 'flex-start',
                    gap: 0.5,
                    px: 0.5,
                    py: 0.5,
                    borderRadius: '6px',
                    border: '1px solid var(--gl-border)',
                    opacity: current ? 0.4 : 1,
                    '&:hover, &:focus-visible': { borderColor: 'var(--gl-primary)', backgroundColor: 'rgba(var(--gl-primary-rgb), 0.08)' }
                  }}
                >
                  <PlantIcon plant={plant} size={20} />
                  <Box sx={{ minWidth: 0, textAlign: 'left' }}>
                    <Typography sx={{ fontSize: '0.64rem', fontWeight: 800, lineHeight: 1.1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
                      {plantLabel(plant)}
                    </Typography>
                    <Typography sx={{ fontSize: '0.56rem', fontWeight: 700, color: n > 0 ? 'var(--gl-success)' : 'var(--gl-text-muted)' }}>
                      {current ? 'here' : n}
                    </Typography>
                  </Box>
                </ButtonBase>
              );
            })}
          </Box>
        </Box>
      ))}
    </Popover>
  );
};
