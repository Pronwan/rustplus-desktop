import React, { useEffect, useMemo, useState } from 'react';
import {
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography
} from '@mui/material';
import {
  LivestockAnimal,
  LivestockGeneRow,
  LivestockSex,
  LivestockSpecies,
  createAnimal,
  displayName,
  suggestAnimalName
} from '../../domain/livestock/animal.ts';
import { LivestockGenePanel } from './LivestockGeneBadges.tsx';
import { SexToggleIcon } from './SexIcon.tsx';

interface AnimalEditorDialogProps {
  open: boolean;
  /** Animal to edit, or null to add a new one. */
  animal: LivestockAnimal | null;
  herd: LivestockAnimal[];
  onClose: () => void;
  onSave: (animal: LivestockAnimal) => void;
  onDelete?: (id: string) => void;
}

function parseMarker(text: string): number | null {
  if (text.trim() === '') return null;
  const value = Number(text);
  return Number.isInteger(value) && value >= 0 && value < 1000 ? value : null;
}

export const AnimalEditorDialog: React.FC<AnimalEditorDialogProps> = ({
  open,
  animal,
  herd,
  onClose,
  onSave,
  onDelete
}) => {
  const [draft, setDraft] = useState<LivestockAnimal>(() => animal ?? createAnimal());
  const [nameTouched, setNameTouched] = useState(false);

  useEffect(() => {
    if (!open) return;
    const initial = animal ?? createAnimal({ species: 'cattle', sex: 'female' });
    setDraft(animal ? initial : { ...initial, name: suggestAnimalName(initial.species, initial.sex, herd) });
    setNameTouched(!!animal);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, animal]);

  const patch = (changes: Partial<LivestockAnimal>) => {
    setDraft((current) => {
      const next = { ...current, ...changes };
      // Keep the suggested name in step with the kind until the player types their own.
      if (!nameTouched && (changes.species || changes.sex)) {
        next.name = suggestAnimalName(next.species, next.sex, herd.filter((a) => a.id !== next.id));
      }
      return next;
    });
  };

  const others = useMemo(
    () => herd.filter((a) => a.id !== draft.id && a.species === draft.species),
    [herd, draft.id, draft.species]
  );
  const mothers = others.filter((a) => a.sex !== 'male');
  const fathers = others.filter((a) => a.sex !== 'female');

  const setRows = (rows: LivestockGeneRow[]) => patch({ rows });
  const setMarkerValue = (rowIndex: number, text: string) => {
    patch({
      rows: draft.rows.map((row, i) =>
        i === rowIndex ? { ...row, marker: { ...row.marker, value: parseMarker(text) } } : row
      )
    });
  };

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ fontWeight: 800 }}>{animal ? `Edit ${displayName(animal)}` : 'Add animal'}</DialogTitle>
      <DialogContent dividers>
        <Stack spacing={2.25}>
          <TextField
            label="Name"
            size="small"
            value={draft.name}
            onChange={(e) => {
              setNameTouched(true);
              patch({ name: e.target.value.slice(0, 60) });
            }}
          />

          <TextField
            label="In-game name"
            size="small"
            value={draft.gameName ?? ''}
            onChange={(e) => patch({ gameName: e.target.value.slice(0, 40) || undefined })}
            helperText="As the animal's panel shows it. Scanning it again updates this animal instead of adding a new one."
          />

          <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
            <ToggleButtonGroup
              exclusive
              size="small"
              value={draft.species}
              onChange={(_, species: LivestockSpecies | null) => species && patch({ species, motherId: undefined, fatherId: undefined })}
              aria-label="Species"
            >
              <ToggleButton value="cattle">Cattle</ToggleButton>
              <ToggleButton value="sheep">Sheep</ToggleButton>
            </ToggleButtonGroup>
            <ToggleButtonGroup
              exclusive
              size="small"
              value={draft.sex}
              onChange={(_, sex: LivestockSex | null) => sex && patch({ sex })}
              aria-label="Sex"
            >
              <ToggleButton value="female"><SexToggleIcon sex="female" />{draft.species === 'cattle' ? 'Cow' : 'Ewe'}</ToggleButton>
              <ToggleButton value="male"><SexToggleIcon sex="male" />{draft.species === 'cattle' ? 'Bull' : 'Ram'}</ToggleButton>
              <ToggleButton value="unknown">Unknown</ToggleButton>
            </ToggleButtonGroup>
          </Box>

          <Box>
            <Typography variant="subtitle2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
              Genes
            </Typography>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', mb: 1 }}>
              Click a badge to change it: Ok (grey), then Good (green), then Bad (red). The last badge is the numbered marker; click it to change its colour.
            </Typography>
            <LivestockGenePanel rows={draft.rows} size="lg" editable onChange={setRows} />
            <Box sx={{ display: 'flex', gap: 1.5, mt: 1.5, alignItems: 'center', flexWrap: 'wrap' }}>
              {draft.rows.map((row, i) => (
                <TextField
                  key={i}
                  label={i === 0 ? 'Marker number' : 'Second row marker'}
                  size="small"
                  type="number"
                  value={row.marker.value ?? ''}
                  onChange={(e) => setMarkerValue(i, e.target.value)}
                  slotProps={{ htmlInput: { min: 0, max: 999 } }}
                  sx={{ width: 150 }}
                />
              ))}
              <FormControlLabel
                control={
                  <Checkbox
                    size="small"
                    checked={draft.rows.length > 1}
                    onChange={(e) =>
                      patch({
                        rows: e.target.checked
                          ? [draft.rows[0], { levels: [...draft.rows[0].levels], marker: { value: null, color: 'unknown' } }]
                          : [draft.rows[0]]
                      })
                    }
                  />
                }
                label="Second row"
              />
            </Box>
          </Box>

          <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
            <TextField
              label="Age (hours)"
              size="small"
              type="number"
              value={draft.observed?.ageSeconds === null || draft.observed?.ageSeconds === undefined ? '' : Math.round((draft.observed.ageSeconds / 3600) * 10) / 10}
              onChange={(e) => {
                const hours = e.target.value === '' ? null : Math.max(0, Number(e.target.value));
                patch({
                  observed: { ageSeconds: hours === null || !Number.isFinite(hours) ? null : hours * 3600, overall: draft.observed?.overall ?? null, at: Date.now() }
                });
              }}
              slotProps={{ htmlInput: { min: 0, step: 0.1 } }}
              sx={{ width: 140 }}
            />
            <TextField
              label="Overall condition %"
              size="small"
              type="number"
              value={draft.observed?.overall === null || draft.observed?.overall === undefined ? '' : Math.round(draft.observed.overall * 100)}
              onChange={(e) => {
                const pct = e.target.value === '' ? null : Math.min(100, Math.max(0, Number(e.target.value)));
                patch({
                  observed: { ageSeconds: draft.observed?.ageSeconds ?? null, overall: pct === null || !Number.isFinite(pct) ? null : pct / 100, at: Date.now() }
                });
              }}
              slotProps={{ htmlInput: { min: 0, max: 100 } }}
              sx={{ width: 170 }}
            />
          </Box>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', mt: -1 }}>
            From the AGE and CONDITIONS rows of the panel. Scanning fills these in; age keeps counting from when it was entered.
          </Typography>

          <FormControlLabel
            control={<Checkbox checked={draft.inbred} onChange={(e) => patch({ inbred: e.target.checked })} />}
            label="Inbred (sells for less and lives shorter)"
          />

          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 1.5 }}>
            <FormControl size="small">
              <InputLabel id="animal-mother">Mother</InputLabel>
              <Select
                labelId="animal-mother"
                label="Mother"
                value={draft.motherId ?? ''}
                onChange={(e) => patch({ motherId: e.target.value || undefined })}
              >
                <MenuItem value="">
                  <em>Not recorded</em>
                </MenuItem>
                {mothers.map((a) => (
                  <MenuItem key={a.id} value={a.id}>
                    {displayName(a)}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
            <FormControl size="small">
              <InputLabel id="animal-father">Father</InputLabel>
              <Select
                labelId="animal-father"
                label="Father"
                value={draft.fatherId ?? ''}
                onChange={(e) => patch({ fatherId: e.target.value || undefined })}
              >
                <MenuItem value="">
                  <em>Not recorded</em>
                </MenuItem>
                {fathers.map((a) => (
                  <MenuItem key={a.id} value={a.id}>
                    {displayName(a)}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          </Box>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', mt: -1 }}>
            Recording parents lets pair suggestions warn you before you inbreed.
          </Typography>

          <TextField
            label="Notes"
            size="small"
            multiline
            minRows={2}
            value={draft.notes ?? ''}
            onChange={(e) => patch({ notes: e.target.value.slice(0, 500) || undefined })}
          />
        </Stack>
      </DialogContent>
      <DialogActions sx={{ justifyContent: 'space-between', px: 3 }}>
        <Box>
          {animal && onDelete && (
            <Button color="error" onClick={() => onDelete(animal.id)}>
              Remove
            </Button>
          )}
        </Box>
        <Box sx={{ display: 'flex', gap: 1 }}>
          <Button onClick={onClose}>Cancel</Button>
          <Button variant="contained" onClick={() => onSave({ ...draft, name: draft.name.trim() })}>
            {animal ? 'Save' : 'Add to herd'}
          </Button>
        </Box>
      </DialogActions>
    </Dialog>
  );
};
