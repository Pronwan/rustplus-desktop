import React, { useMemo, useRef, useState } from 'react';
import { Box, ButtonBase, Popover, Tooltip, Typography } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { PLANT_GROUPS, PLANT_ORDER as ORDERED, plantLabel } from '../../domain/plants.ts';

// Re-exported: other components import the label from here.
export { plantLabel };

export const PlantIcon: React.FC<{ plant: string; size: number }> = ({ plant, size }) => (
  <Box
    component="img"
    src={`./img/items/${plant}.webp`}
    alt=""
    draggable={false}
    sx={{ width: size, height: size, objectFit: 'contain', flexShrink: 0, transition: 'transform 160ms ease' }}
    onError={(e: React.SyntheticEvent<HTMLImageElement>) => {
      e.currentTarget.style.visibility = 'hidden';
    }}
  />
);

interface PlantPickerProps {
  value: string;
  onChange: (plant: string) => void;
  /** Accessible name of the trigger, e.g. "Crop type". */
  label: string;
  /** Icon-only trigger for narrow headers. */
  compact?: boolean;
  /** `add`: a small "+" trigger for starting another plant group; the grid is the same. */
  variant?: 'select' | 'add';
  /** Text next to the "+" of the `add` trigger; icon only when omitted. */
  addText?: string;
}

/**
 * Plant chooser with the game's item icons: a trigger showing the current plant and how many
 * of its clones are saved, opening a grid of every plant grouped by kind.
 */
export const PlantPicker: React.FC<PlantPickerProps> = ({ value, onChange, label, compact = false, variant = 'select', addText }) => {
  const { allClones } = useWorkspace();
  const anchorRef = useRef<HTMLButtonElement | null>(null);
  const tileRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const [open, setOpen] = useState(false);

  const counts = useMemo(() => {
    const map: Record<string, number> = {};
    for (const clone of allClones) map[clone.cropType] = (map[clone.cropType] ?? 0) + 1;
    return map;
  }, [allClones]);

  const choose = (plant: string) => {
    onChange(plant);
    setOpen(false);
    anchorRef.current?.focus();
  };

  // Arrow keys move through the grid (3 tiles per row), Home/End jump to the ends.
  const handleTileKey = (event: React.KeyboardEvent, plant: string) => {
    const index = ORDERED.indexOf(plant);
    const moves: Record<string, number> = { ArrowRight: 1, ArrowLeft: -1, ArrowDown: 3, ArrowUp: -3 };
    let next = index;
    if (event.key in moves) next = Math.min(ORDERED.length - 1, Math.max(0, index + moves[event.key]));
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = ORDERED.length - 1;
    else return;
    event.preventDefault();
    tileRefs.current[ORDERED[next]]?.focus();
  };

  const count = counts[value] ?? 0;

  return (
    <>
      {variant === 'add' ? (
        <Tooltip title={label}>
          <ButtonBase
            ref={anchorRef}
            onClick={() => setOpen(true)}
            aria-label={label}
            aria-haspopup="dialog"
            aria-expanded={open}
            sx={{
              flexShrink: 0,
              height: 26,
              minWidth: 26,
              px: addText ? 0.75 : 0,
              gap: 0.25,
              borderRadius: '999px',
              border: '1px dashed var(--gl-border-strong)',
              color: 'var(--gl-text-secondary)',
              fontSize: '0.68rem',
              fontWeight: 800,
              transition: 'border-color 120ms ease, color 120ms ease',
              '&:hover, &:focus-visible': { borderColor: 'var(--gl-primary)', color: 'var(--gl-primary)' }
            }}
          >
            <AddIcon sx={{ fontSize: 16 }} />
            {addText}
          </ButtonBase>
        </Tooltip>
      ) : (
        <ButtonBase
          ref={anchorRef}
          onClick={() => setOpen(true)}
          aria-label={`${label}: ${plantLabel(value)}`}
          aria-haspopup="dialog"
          aria-expanded={open}
          sx={{
            height: 36,
            px: compact ? 0.75 : 1,
            gap: 1,
            borderRadius: '8px',
            border: '1px solid var(--gl-border-strong)',
            backgroundColor: 'var(--gl-input-bg)',
            color: 'var(--gl-text-primary)',
            transition: 'border-color 140ms ease, background-color 140ms ease, transform 140ms ease',
            '&:hover': { borderColor: 'var(--gl-primary)', backgroundColor: 'rgba(var(--gl-primary-rgb), 0.08)' },
            '&:hover img': { transform: 'scale(1.12) rotate(-4deg)' },
            '&:focus-visible': { outline: '2px solid var(--gl-primary)', outlineOffset: 2 }
          }}
        >
          <PlantIcon plant={value} size={26} />
          {!compact && (
            <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-start', lineHeight: 1.05, minWidth: 0 }}>
              <Typography component="span" sx={{ fontSize: '0.8rem', fontWeight: 800, whiteSpace: 'nowrap' }}>
                {plantLabel(value)}
              </Typography>
              <Typography component="span" sx={{ fontSize: '0.62rem', fontWeight: 700, color: 'var(--gl-text-muted)', whiteSpace: 'nowrap' }}>
                {count === 0 ? 'No clones yet' : `${count} clone${count === 1 ? '' : 's'}`}
              </Typography>
            </Box>
          )}
          <ExpandMoreIcon
            sx={{ fontSize: 18, color: 'var(--gl-text-muted)', transition: 'transform 160ms ease', transform: open ? 'rotate(180deg)' : 'none' }}
          />
        </ButtonBase>
      )}

      <Popover
        open={open}
        anchorEl={anchorRef.current}
        onClose={() => setOpen(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        transformOrigin={{ vertical: 'top', horizontal: 'left' }}
        slotProps={{
          transition: { onEntered: () => tileRefs.current[value]?.focus() },
          paper: {
            role: 'dialog',
            'aria-label': `Choose ${label.toLowerCase()}`,
            sx: {
              mt: 0.75,
              p: 1.5,
              width: 'min(420px, calc(100vw - 32px))',
              borderRadius: '10px',
              border: '1px solid var(--gl-border)',
              backgroundColor: 'var(--gl-panel-bg)',
              boxShadow: '0 18px 40px rgba(0,0,0,0.45)'
            }
          } as object
        }}
      >
        {PLANT_GROUPS.map((group) => (
          <Box key={group.title} sx={{ '&:not(:last-of-type)': { mb: 1.25 } }}>
            <Typography sx={{ fontSize: '0.64rem', fontWeight: 800, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--gl-text-muted)', mb: 0.75, px: 0.25 }}>
              {group.title}
            </Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: 0.75 }}>
              {group.plants.map((plant) => {
                const selected = plant === value;
                const n = counts[plant] ?? 0;
                return (
                  <ButtonBase
                    key={plant}
                    ref={(el: HTMLButtonElement | null) => {
                      tileRefs.current[plant] = el;
                    }}
                    onClick={() => choose(plant)}
                    onKeyDown={(event) => handleTileKey(event, plant)}
                    aria-pressed={selected}
                    aria-label={`${plantLabel(plant)}, ${n} clone${n === 1 ? '' : 's'}`}
                    sx={{
                      position: 'relative',
                      flexDirection: 'column',
                      gap: 0.5,
                      py: 1,
                      px: 0.5,
                      borderRadius: '8px',
                      border: '1.5px solid',
                      borderColor: selected ? 'var(--gl-primary)' : 'var(--gl-border)',
                      backgroundColor: selected ? 'rgba(var(--gl-primary-rgb), 0.12)' : 'var(--gl-panel-header-bg)',
                      transition: 'transform 140ms ease, border-color 140ms ease, background-color 140ms ease, box-shadow 140ms ease',
                      '&:hover, &:focus-visible': {
                        borderColor: 'var(--gl-primary)',
                        transform: 'translateY(-2px)',
                        boxShadow: '0 6px 16px rgba(0,0,0,0.3)'
                      },
                      '&:hover img, &:focus-visible img': { transform: 'scale(1.15)' },
                      '&:focus-visible': { outline: 'none' }
                    }}
                  >
                    {selected && (
                      <CheckCircleIcon sx={{ position: 'absolute', top: 4, right: 4, fontSize: 15, color: 'var(--gl-primary)' }} />
                    )}
                    <PlantIcon plant={plant} size={40} />
                    <Typography sx={{ fontSize: '0.72rem', fontWeight: 800, color: 'var(--gl-text-primary)', lineHeight: 1.1, textAlign: 'center' }}>
                      {plantLabel(plant)}
                    </Typography>
                    <Typography sx={{ fontSize: '0.6rem', fontWeight: 700, color: n > 0 ? 'var(--gl-success)' : 'var(--gl-text-muted)' }}>
                      {n > 0 ? `${n} clone${n === 1 ? '' : 's'}` : 'none saved'}
                    </Typography>
                  </ButtonBase>
                );
              })}
            </Box>
          </Box>
        ))}
      </Popover>
    </>
  );
};
