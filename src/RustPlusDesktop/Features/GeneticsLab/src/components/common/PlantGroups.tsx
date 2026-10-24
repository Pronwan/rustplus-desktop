import React, { useState } from 'react';
import {
  Box,
  Button,
  ButtonBase,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Tooltip,
  Typography
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import DeleteSweepIcon from '@mui/icons-material/DeleteSweep';
import ViewListIcon from '@mui/icons-material/ViewList';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { PLANT_ORDER } from '../../domain/plants.ts';
import { GeneticsSequence } from './GeneticsSequence.tsx';
import { PlantIcon, PlantPicker, plantLabel } from './PlantPicker.tsx';

const PREVIEW_GENES = 4;

const countLabel = (n: number) => `${n} clone${n === 1 ? '' : 's'}`;

/**
 * The gene list's plant groups: one list per plant, the selected one shown and calculated.
 *
 * Compact chips that wrap (icon and count; the open one also named, with a clear button), a "+" that opens the plant grid to start or open a group, and a "Groups" dialog
 * that lists every group with a preview of its genes. Clearing is undoable from the notice.
 */
export const PlantGroups: React.FC = () => {
  const { geneGroups, selectedPlant, setSelectedPlant, clearPlantGroup, clearAllGroups } = useWorkspace();
  const [isManagerOpen, setIsManagerOpen] = useState(false);
  // Groups listed when the dialog opened stay listed while it is open, so one cleared there
  // shows as empty (with Undo in the notice) instead of vanishing from under the cursor.
  const [listedPlants, setListedPlants] = useState<string[]>([]);
  // Undo for groups cleared in the dialog, offered on their row (the notice under a modal
  // dialog is out of reach of the keyboard and screen readers).
  const [undoByPlant, setUndoByPlant] = useState<Record<string, () => void>>({});
  const [undoAll, setUndoAll] = useState<(() => void) | null>(null);
  const openManager = () => {
    setListedPlants(geneGroups.map((g) => g.plant));
    setUndoByPlant({});
    setUndoAll(null);
    setIsManagerOpen(true);
  };
  const clearInDialog = (plant: string) => {
    const undo = clearPlantGroup(plant);
    if (undo) setUndoByPlant((all) => ({ ...all, [plant]: undo }));
  };
  const undoInDialog = (plant: string) => {
    undoByPlant[plant]?.();
    setUndoByPlant(({ [plant]: _done, ...rest }) => rest);
  };
  const managerGroups = PLANT_ORDER.filter(
    (plant) => listedPlants.includes(plant) || geneGroups.some((g) => g.plant === plant)
  ).map((plant) => geneGroups.find((g) => g.plant === plant) ?? { plant, genes: [] as string[] });

  const filledGroups = geneGroups.filter((g) => g.genes.length > 0);

  return (
    <Box data-tour="plant-groups" sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 0.5, mb: 1, minWidth: 0 }}>
      <Box
        role="tablist"
        aria-label="Plant groups"
        sx={{ display: 'contents' }}
      >
        {geneGroups.map(({ plant, genes }) => {
          const selected = plant === selectedPlant;
          return (
            <Box
              key={plant}
              sx={{
                flexShrink: 0,
                display: 'flex',
                alignItems: 'center',
                borderRadius: '999px',
                border: '1px solid',
                borderColor: selected ? 'var(--gl-primary)' : 'var(--gl-border-strong)',
                backgroundColor: selected ? 'rgba(var(--gl-primary-rgb), 0.14)' : 'transparent',
                transition: 'border-color 120ms ease, background-color 120ms ease',
                '&:hover': { borderColor: 'var(--gl-primary)' }
              }}
            >
              <Tooltip title={selected ? `${plantLabel(plant)}: shown and calculated now` : `${plantLabel(plant)} · ${countLabel(genes.length)}`}>
                <ButtonBase
                  role="tab"
                  aria-selected={selected}
                  aria-label={`${plantLabel(plant)}, ${countLabel(genes.length)}`}
                  onClick={() => setSelectedPlant(plant)}
                  sx={{ height: 24, gap: 0.5, pl: 0.5, pr: selected && genes.length > 0 ? 0.25 : 0.75, borderRadius: '999px' }}
                >
                  <PlantIcon plant={plant} size={18} />
                  {selected && (
                    <Typography sx={{ fontSize: '0.68rem', fontWeight: 800, color: 'var(--gl-primary)', whiteSpace: 'nowrap' }}>
                      {plantLabel(plant)}
                    </Typography>
                  )}
                  <Typography
                    sx={{
                      fontSize: '0.62rem',
                      fontWeight: 800,
                      fontFamily: 'var(--font-family-mono)',
                      color: selected ? 'var(--gl-primary)' : 'var(--gl-text-secondary)'
                    }}
                  >
                    {genes.length}
                  </Typography>
                </ButtonBase>
              </Tooltip>
              {selected && genes.length > 0 && (
                <Tooltip title={`Clear ${plantLabel(plant)} (${countLabel(genes.length)})`}>
                  <IconButton
                    size="small"
                    aria-label={`Clear ${plantLabel(plant)}`}
                    onClick={() => clearPlantGroup(plant)}
                    sx={{ p: 0.25, mr: 0.25, color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-error)' } }}
                  >
                    <CloseIcon sx={{ fontSize: 13 }} />
                  </IconButton>
                </Tooltip>
              )}
            </Box>
          );
        })}
      </Box>

      <PlantPicker variant="add" value={selectedPlant} onChange={setSelectedPlant} label="Add or open a plant group" />
      <Tooltip title="View and manage all groups">
        <Button
          size="small"
          onClick={openManager}
          startIcon={<ViewListIcon sx={{ fontSize: 15 }} />}
          sx={{ flexShrink: 0, minWidth: 0, height: 26, px: 0.75, fontSize: '0.66rem', fontWeight: 800, color: 'var(--gl-text-secondary)' }}
        >
          Groups
        </Button>
      </Tooltip>

      <Dialog open={isManagerOpen} onClose={() => setIsManagerOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 6 }}>
          <ViewListIcon sx={{ color: 'var(--gl-primary)' }} />
          Plant groups
          <Typography component="span" sx={{ fontSize: '0.8rem', color: 'var(--gl-text-muted)', fontWeight: 600 }}>
            {filledGroups.length} with clones · {filledGroups.reduce((n, g) => n + g.genes.length, 0)} clones
          </Typography>
          <IconButton aria-label="Close" onClick={() => setIsManagerOpen(false)} sx={{ position: 'absolute', right: 8, top: 8 }}>
            <CloseIcon />
          </IconButton>
        </DialogTitle>
        <DialogContent dividers sx={{ p: 1 }}>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', px: 1, pb: 1 }}>
            Each plant keeps its own list. The scanner files clones under the plant it reads; only the open group is
            calculated.
          </Typography>
          {managerGroups.map(({ plant, genes }) => {
            const selected = plant === selectedPlant;
            return (
              <Box
                key={plant}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 1.25,
                  p: 1,
                  borderRadius: '8px',
                  border: '1px solid',
                  borderColor: selected ? 'var(--gl-primary)' : 'transparent',
                  backgroundColor: selected ? 'rgba(var(--gl-primary-rgb), 0.08)' : 'transparent',
                  '&:hover': { backgroundColor: selected ? 'rgba(var(--gl-primary-rgb), 0.1)' : 'var(--gl-panel-header-bg)' }
                }}
              >
                <PlantIcon plant={plant} size={34} />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1 }}>
                    <Typography sx={{ fontWeight: 800, fontSize: '0.86rem' }}>{plantLabel(plant)}</Typography>
                    <Typography sx={{ fontSize: '0.72rem', fontWeight: 700, color: genes.length ? 'var(--gl-success)' : 'var(--gl-text-muted)' }}>
                      {genes.length ? countLabel(genes.length) : 'empty'}
                    </Typography>
                    {selected && (
                      <Typography sx={{ fontSize: '0.66rem', fontWeight: 800, color: 'var(--gl-primary)', textTransform: 'uppercase' }}>
                        open
                      </Typography>
                    )}
                  </Box>
                  {genes.length > 0 && (
                    <Box sx={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', columnGap: 1.25, rowGap: 0.5, mt: 0.5 }}>
                      {genes.slice(0, PREVIEW_GENES).map((gene, i) => (
                        <GeneticsSequence key={`${gene}-${i}`} genes={gene} size="small" />
                      ))}
                      {genes.length > PREVIEW_GENES && (
                        <Typography sx={{ fontSize: '0.7rem', fontWeight: 700, color: 'var(--gl-text-muted)' }}>
                          +{genes.length - PREVIEW_GENES} more
                        </Typography>
                      )}
                    </Box>
                  )}
                </Box>
                {!selected && (
                  <Button
                    size="small"
                    variant="outlined"
                    onClick={() => {
                      setSelectedPlant(plant);
                      setIsManagerOpen(false);
                    }}
                    sx={{ flexShrink: 0, fontSize: '0.68rem', fontWeight: 800 }}
                  >
                    Open
                  </Button>
                )}
                {undoByPlant[plant] && genes.length === 0 ? (
                  <Button size="small" onClick={() => undoInDialog(plant)} sx={{ flexShrink: 0, fontSize: '0.68rem', fontWeight: 800 }}>
                    Undo
                  </Button>
                ) : (
                  <Tooltip title={genes.length ? `Clear ${plantLabel(plant)}` : 'Already empty'}>
                    <span>
                      <IconButton
                        aria-label={`Clear ${plantLabel(plant)}`}
                        disabled={genes.length === 0}
                        onClick={() => clearInDialog(plant)}
                        sx={{ color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-error)' } }}
                      >
                        <DeleteSweepIcon fontSize="small" />
                      </IconButton>
                    </span>
                  </Tooltip>
                )}
              </Box>
            );
          })}
        </DialogContent>
        <DialogActions sx={{ gap: 1 }}>
          <Box sx={{ mr: 'auto', display: 'flex', alignItems: 'center' }}>
            <PlantPicker
              variant="add"
              addText="Add plant"
              value={selectedPlant}
              onChange={(plant) => {
                setSelectedPlant(plant);
                setIsManagerOpen(false);
              }}
              label="Add or open a plant group"
            />
          </Box>
          {undoAll && filledGroups.length === 0 ? (
            <Button
              onClick={() => {
                undoAll();
                setUndoAll(null);
                setUndoByPlant({});
              }}
              sx={{ fontWeight: 800 }}
            >
              Undo clear all
            </Button>
          ) : (
            <Button
              color="error"
              disabled={filledGroups.length === 0}
              onClick={() => {
                const undo = clearAllGroups();
                // Wrapped: a bare function passed to a state setter would be called as an updater.
                if (undo) setUndoAll(() => undo);
                setUndoByPlant({});
              }}
              startIcon={<DeleteSweepIcon />}
              sx={{ fontWeight: 800 }}
            >
              Clear all
            </Button>
          )}
          <Button onClick={() => setIsManagerOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
};
