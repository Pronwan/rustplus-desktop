import React, { useMemo, useRef, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  IconButton,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Tab,
  Tabs,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import StopIcon from '@mui/icons-material/Stop';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import { useLivestock } from '../../context/LivestockContext.tsx';
import { useNotification } from '../../context/NotificationContext.tsx';
import {
  LivestockAnimal,
  LivestockSpecies,
  animalKindLabel,
  displayName
} from '../../domain/livestock/animal.ts';
import { estimateAnimalPrice } from '../../domain/livestock/pricing.ts';
import { LIVESTOCK_DATA_AS_OF } from '../../domain/livestock/livestockGenes.ts';
import { AnimalPortrait, kindLabel } from './LivestockImages.tsx';
import { SexIcon, SexQuickPick } from './SexIcon.tsx';
import { exportHerd, sanitizeHerd } from '../../domain/livestock/herdCodec.ts';
import { isCameraSupported, isDesktopCaptureSupported } from '../../services/livestock/livestockScanSession.ts';
import { LivestockGenePanel } from './LivestockGeneBadges.tsx';
import { AnimalEditorDialog } from './AnimalEditorDialog.tsx';
import { AnimalDetail } from './AnimalDetail.tsx';
import { PairSuggestionsPanel } from './PairSuggestionsPanel.tsx';
import { LivestockRulesPanel } from './LivestockRulesPanel.tsx';
import { LivestockCameraDialog } from './LivestockCameraDialog.tsx';
import { ScanKindSelect } from './ScanKindSelect.tsx';
import { ConfirmDialog } from '../common/ConfirmDialog.tsx';
import { formatDuration } from '../../domain/livestock/stats.ts';

const panelSx = { backgroundColor: 'var(--gl-panel-bg)', borderColor: 'var(--gl-border)', borderRadius: '6px' };

type SpeciesFilter = 'all' | LivestockSpecies;
type RightTab = 'animal' | 'pairs' | 'rules';

export const LivestockPage: React.FC = () => {
  const {
    herd,
    addAnimal,
    updateAnimal,
    removeAnimal,
    replaceHerd,
    clearHerd,
    selectedId,
    setSelectedId,
    scan,
    startDesktopScan,
    stopScan
  } = useLivestock();
  const { notifySuccess, notifyError } = useNotification();

  const [filter, setFilter] = useState<SpeciesFilter>('all');
  const [rightTab, setRightTab] = useState<RightTab>('animal');
  const [editorOpen, setEditorOpen] = useState(false);
  const [editing, setEditing] = useState<LivestockAnimal | null>(null);
  const [cameraOpen, setCameraOpen] = useState(false);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const [confirmClear, setConfirmClear] = useState(false);
  const fileInput = useRef<HTMLInputElement | null>(null);

  const visible = useMemo(() => herd.filter((a) => filter === 'all' || a.species === filter), [herd, filter]);
  const selected = herd.find((a) => a.id === selectedId) ?? null;
  const desktopScanning = scan.source === 'desktop' && (scan.status === 'scanning' || scan.status === 'starting');
  const counts = useMemo(
    () => ({ cattle: herd.filter((a) => a.species === 'cattle').length, sheep: herd.filter((a) => a.species === 'sheep').length }),
    [herd]
  );

  const openEditor = (animal: LivestockAnimal | null) => {
    setEditing(animal);
    setEditorOpen(true);
  };

  const save = (animal: LivestockAnimal) => {
    if (herd.some((a) => a.id === animal.id)) updateAnimal(animal.id, animal);
    else addAnimal(animal);
    setSelectedId(animal.id);
    setRightTab('animal');
    setEditorOpen(false);
  };

  const download = () => {
    const blob = new Blob([exportHerd(herd)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'livestock-herd.json';
    link.click();
    URL.revokeObjectURL(url);
  };

  const importFile = async (file: File) => {
    try {
      const imported = sanitizeHerd(JSON.parse(await file.text()));
      if (imported.length === 0) {
        notifyError('No animals found in that file.');
        return;
      }
      const known = new Set(herd.map((a) => a.id));
      const fresh = imported.filter((a) => !known.has(a.id));
      replaceHerd([...herd, ...fresh]);
      notifySuccess(`Imported ${fresh.length} animal${fresh.length === 1 ? '' : 's'}.`);
    } catch {
      notifyError('That file is not a livestock herd export.');
    }
  };

  return (
    <Box sx={{ maxWidth: 1440, mx: 'auto', p: { xs: 1.5, sm: 2, lg: 3 }, minWidth: 0 }}>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 1.5, mb: 2, flexWrap: 'wrap' }}>
        <Box>
          <Typography component="h1" variant="h5" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
            Livestock Genetics
            <Chip size="small" label="BETA" sx={{ ml: 1, height: 20, fontSize: '0.65rem', fontWeight: 900, backgroundColor: '#F59E0B', color: '#111827', verticalAlign: 'middle' }} />
          </Typography>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', mt: 0.5 }}>
            Scan cows, bulls and sheep straight from Rust, see what their genes do, and find the pairs worth breeding.
          </Typography>
        </Box>
        <Tooltip title="Gene effects and breeding rules checked against the live game. The sale formula is a community measurement.">
          <Chip size="small" variant="outlined" label={`Game data, ${LIVESTOCK_DATA_AS_OF}`} sx={{ color: 'var(--gl-text-secondary)' }} />
        </Tooltip>
      </Box>

      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 }, mb: 2 }}>
        <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <ScanKindSelect />
          {isDesktopCaptureSupported() && (
            desktopScanning ? (
              <Button variant="contained" color="error" startIcon={<StopIcon />} onClick={stopScan}>
                Stop scanning
              </Button>
            ) : (
              <Button variant="contained" startIcon={<AutoAwesomeIcon />} onClick={() => void startDesktopScan()} disabled={scan.status === 'starting'}>
                Scan from Rust
              </Button>
            )
          )}
          {isCameraSupported() && (
            <Button variant="outlined" startIcon={<PhotoCameraIcon />} onClick={() => setCameraOpen(true)}>
              Phone camera
            </Button>
          )}
          <Button variant="outlined" startIcon={<AddIcon />} onClick={() => openEditor(null)}>
            Add manually
          </Button>
        </Box>

        {desktopScanning && (
          <Box sx={{ mt: 1.5, display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
            {scan.live ? (
              <>
                <LivestockGenePanel rows={scan.live.rows} size="md" />
                {scan.liveName && (
                  <Typography variant="body2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
                    {scan.liveName}
                  </Typography>
                )}
                {scan.livePortrait && (
                  <Chip
                    size="small"
                    color="primary"
                    variant="outlined"
                    label={`Detected: ${
                      scan.livePortrait.kind
                        ? scan.livePortrait.kind[0].toUpperCase() + scan.livePortrait.kind.slice(1)
                        : scan.livePortrait.species === 'cattle'
                          ? 'Cattle (cow or calf?)'
                          : 'Sheep (adult or lamb?)'
                    }`}
                  />
                )}
                {scan.liveConditions && (
                  <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', fontFamily: 'monospace' }}>
                    Age {scan.liveConditions.ageSeconds === null ? '?' : formatDuration(scan.liveConditions.ageSeconds)} · Overall{' '}
                    {scan.liveConditions.overall === null ? '?' : `${Math.round(scan.liveConditions.overall * 100)}%`}
                  </Typography>
                )}
                <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
                  Reading this animal. It is added as soon as two frames agree.
                </Typography>
              </>
            ) : (
              <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
                Scanning. In Rust, look at an animal so its gene panel is on screen; its genes are read automatically.
              </Typography>
            )}
          </Box>
        )}
        {!desktopScanning && (
          <Typography variant="caption" sx={{ display: 'block', mt: 1, color: 'var(--gl-text-muted)' }}>
            No calibration needed: the gene panel is found anywhere on screen. With Auto-detect, the panel's portrait sets the
            species, and cow or bull for adult cattle. The game shows one portrait for both sexes of calves, lambs and sheep, so
            set their sex on the animal (or pick a kind in "Add scans as").
          </Typography>
        )}

      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'minmax(0, 1fr)', lg: 'minmax(340px, 440px) minmax(0, 1fr)' }, gap: 2, alignItems: 'start' }}>
        <Paper component="section" variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 }, minWidth: 0 }}>
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1, mb: 1.5 }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
              Herd ({herd.length})
            </Typography>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
              <ToggleButtonGroup exclusive size="small" value={filter} onChange={(_, v: SpeciesFilter | null) => v && setFilter(v)} aria-label="Filter herd">
                <ToggleButton value="all">All</ToggleButton>
                <ToggleButton value="cattle">Cattle {counts.cattle}</ToggleButton>
                <ToggleButton value="sheep">Sheep {counts.sheep}</ToggleButton>
              </ToggleButtonGroup>
              <IconButton size="small" aria-label="Herd actions" onClick={(e) => setMenuAnchor(e.currentTarget)}>
                <MoreVertIcon fontSize="small" />
              </IconButton>
              <Menu anchorEl={menuAnchor} open={!!menuAnchor} onClose={() => setMenuAnchor(null)}>
                <MenuItem disabled={herd.length === 0} onClick={() => { setMenuAnchor(null); download(); }}>
                  Export herd (JSON)
                </MenuItem>
                <MenuItem onClick={() => { setMenuAnchor(null); fileInput.current?.click(); }}>Import herd</MenuItem>
                <MenuItem
                  disabled={herd.length === 0}
                  onClick={() => { setMenuAnchor(null); setConfirmClear(true); }}
                  sx={{ color: 'var(--gl-error)' }}
                >
                  Clear herd
                </MenuItem>
              </Menu>
              <input
                ref={fileInput}
                type="file"
                accept="application/json,.json"
                hidden
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) void importFile(file);
                  e.target.value = '';
                }}
              />
            </Box>
          </Box>

          {visible.length === 0 ? (
            <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', py: 3, textAlign: 'center' }}>
              No animals yet. Scan one from Rust, or add it manually.
            </Typography>
          ) : (
            <Stack spacing={0.75}>
              {visible.map((animal) => {
                const isSelected = animal.id === selectedId;
                return (
                  <Box
                    key={animal.id}
                    component="button"
                    type="button"
                    onClick={() => {
                      setSelectedId(animal.id);
                      setRightTab('animal');
                    }}
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: 1,
                      width: '100%',
                      textAlign: 'left',
                      p: 1,
                      borderRadius: '6px',
                      border: '1px solid',
                      borderColor: isSelected ? 'var(--gl-primary)' : 'var(--gl-border)',
                      backgroundColor: isSelected ? 'var(--gl-tint-cyan)' : 'var(--gl-card-bg)',
                      color: 'inherit',
                      cursor: 'pointer',
                      '&:hover': { backgroundColor: 'var(--gl-card-hover-bg)' }
                    }}
                  >
                    <Box sx={{ minWidth: 0, display: 'flex', alignItems: 'center', gap: 1 }}>
                      <AnimalPortrait animal={animal} size={36} />
                      <Box sx={{ minWidth: 0 }}>
                      <Typography variant="body2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {displayName(animal)}
                      </Typography>
                      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                        {animal.sex === 'unknown' ? (
                          <SexQuickPick animal={animal} size={13} onPick={(sex) => updateAnimal(animal.id, { sex })} />
                        ) : (
                          <SexIcon animal={animal} size={14} />
                        )}
                        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                          {kindLabel(animal)}
                          {animal.sex !== 'unknown' && kindLabel(animal) !== animalKindLabel(animal.species, animal.sex) ? ` · ${animal.sex}` : ''}
                          {animal.inbred ? ' · inbred' : ''}
                        </Typography>
                      </Box>
                      </Box>
                    </Box>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
                      <LivestockGenePanel rows={animal.rows} size="sm" />
                      <Typography variant="caption" sx={{ fontFamily: 'monospace', fontWeight: 800, color: 'var(--gl-text-secondary)', minWidth: 34, textAlign: 'right' }}>
                        ~{estimateAnimalPrice(animal).price}
                      </Typography>
                    </Box>
                  </Box>
                );
              })}
            </Stack>
          )}
        </Paper>

        <Box component="section" sx={{ minWidth: 0 }}>
          <Tabs value={rightTab} onChange={(_, v: RightTab) => setRightTab(v)} sx={{ mb: 1.5, minHeight: 40 }}>
            <Tab value="animal" label="Animal" sx={{ minHeight: 40 }} />
            <Tab value="pairs" label="Pair suggestions" sx={{ minHeight: 40 }} />
            <Tab value="rules" label="Rules" sx={{ minHeight: 40 }} />
          </Tabs>
          {rightTab === 'animal' &&
            (selected ? (
              <AnimalDetail
                animal={selected}
                herd={herd}
                onSetSex={(sex) => updateAnimal(selected.id, { sex })}
                onEdit={() => openEditor(selected)}
                onRemove={() => {
                  const removed = selected;
                  removeAnimal(removed.id);
                  notifySuccess(`Removed ${displayName(removed)}`, { label: 'Undo', onClick: () => addAnimal(removed) });
                }}
              />
            ) : (
              <Paper variant="outlined" sx={{ ...panelSx, p: 3 }}>
                <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)' }}>
                  Select an animal to see what its genes do and what it would sell for.
                </Typography>
              </Paper>
            ))}
          {rightTab === 'pairs' && (
            <PairSuggestionsPanel
              herd={herd}
              onSelect={(id) => {
                setSelectedId(id);
                setRightTab('animal');
              }}
            />
          )}
          {rightTab === 'rules' && <LivestockRulesPanel />}
        </Box>
      </Box>

      <AnimalEditorDialog
        open={editorOpen}
        animal={editing}
        herd={herd}
        onClose={() => setEditorOpen(false)}
        onSave={save}
        onDelete={(id) => {
          removeAnimal(id);
          setEditorOpen(false);
        }}
      />
      <LivestockCameraDialog open={cameraOpen} onClose={() => setCameraOpen(false)} />
      <ConfirmDialog
        open={confirmClear}
        title="Clear herd?"
        message={`Remove all ${herd.length} animals from this device. You can undo straight after, or export the herd first from the same menu.`}
        confirmLabel="Clear herd"
        isDestructive
        onConfirm={() => {
          setConfirmClear(false);
          clearHerd();
        }}
        onCancel={() => setConfirmClear(false)}
      />
    </Box>
  );
};

export default LivestockPage;
