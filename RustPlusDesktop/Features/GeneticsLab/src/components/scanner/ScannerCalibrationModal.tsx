import React, { useState, useEffect, useRef } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  Select,
  MenuItem,
  IconButton,
  Tabs,
  Tab,
  Paper,
  TextField,
  Tooltip,
  Divider,
  Stack
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import FileUploadIcon from '@mui/icons-material/FileUpload';
import AddIcon from '@mui/icons-material/Add';
import DeleteIcon from '@mui/icons-material/Delete';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { useScanner } from '../../context/ScannerContext.tsx';
import { useNotification } from '../../context/NotificationContext.tsx';

export const ScannerCalibrationModal: React.FC = () => {
  const {
    isCalibrationModalOpen,
    setIsCalibrationModalOpen,
    profiles,
    activeProfileId,
    setActiveProfileId,
    createCustomProfile,
    exportProfileJson,
    importProfileJson,
    deleteProfile,
    scannerPreviews,
    setScannerPreviewEnabled,
    moveScannerRegion,
    scaleScannerRegion,
    resetScannerRegions
  } = useScanner();

  const { notifySuccess, notifyError } = useNotification();

  const [selectedRegionIdx, setSelectedRegionIdx] = useState<number>(0);
  const [isSaveModalOpen, setIsSaveModalOpen] = useState(false);
  const [newPresetName, setNewPresetName] = useState('');
  const [isImportModalOpen, setIsImportModalOpen] = useState(false);
  const [importJsonText, setImportJsonText] = useState('');
  const [isExportModalOpen, setIsExportModalOpen] = useState(false);
  const [exportJsonText, setExportJsonText] = useState('');

  const holdTimerRef = useRef<any>(null);
  const repeatTimerRef = useRef<any>(null);

  // Enable live preview streaming while calibration modal is open & add arrow key listener
  useEffect(() => {
    if (isCalibrationModalOpen) {
      setScannerPreviewEnabled(true);
    }

    const handleKeyDown = (e: KeyboardEvent) => {
      if (!isCalibrationModalOpen || isSaveModalOpen || isImportModalOpen || isExportModalOpen) return;
      // Ultra-fine 1px step (0.0005) or fast 5px step with Shift (0.0025)
      const step = e.shiftKey ? 0.0025 : 0.0006;
      if (e.key === 'ArrowUp') {
        e.preventDefault();
        moveScannerRegion(selectedRegionIdx, 0, -step);
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        moveScannerRegion(selectedRegionIdx, 0, step);
      } else if (e.key === 'ArrowLeft') {
        e.preventDefault();
        moveScannerRegion(selectedRegionIdx, -step, 0);
      } else if (e.key === 'ArrowRight') {
        e.preventDefault();
        moveScannerRegion(selectedRegionIdx, step, 0);
      } else if (e.key === '-' || e.key === '_') {
        e.preventDefault();
        scaleScannerRegion(selectedRegionIdx, -step);
      } else if (e.key === '=' || e.key === '+') {
        e.preventDefault();
        scaleScannerRegion(selectedRegionIdx, step);
      }
    };

    window.addEventListener('keydown', handleKeyDown);

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      setScannerPreviewEnabled(false);
      stopHold();
    };
  }, [
    isCalibrationModalOpen,
    isSaveModalOpen,
    isImportModalOpen,
    isExportModalOpen,
    selectedRegionIdx,
    moveScannerRegion,
    scaleScannerRegion,
    setScannerPreviewEnabled
  ]);

  const startHold = (action: () => void) => {
    action();
    holdTimerRef.current = setTimeout(() => {
      repeatTimerRef.current = setInterval(() => {
        action();
      }, 50);
    }, 220);
  };

  const stopHold = () => {
    if (holdTimerRef.current) clearTimeout(holdTimerRef.current);
    if (repeatTimerRef.current) clearInterval(repeatTimerRef.current);
    holdTimerRef.current = null;
    repeatTimerRef.current = null;
  };

  const handleSavePreset = () => {
    if (!newPresetName.trim()) return;
    createCustomProfile(newPresetName.trim());
    setNewPresetName('');
    setIsSaveModalOpen(false);
  };

  const handleOpenExport = () => {
    const json = exportProfileJson();
    setExportJsonText(json);
    setIsExportModalOpen(true);
  };

  const handleCopyExport = () => {
    navigator.clipboard.writeText(exportJsonText);
    notifySuccess('Preset configuration copied to clipboard!');
  };

  const handleDownloadExport = () => {
    const blob = new Blob([exportJsonText], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `rust_scanner_presets_${Date.now()}.json`;
    a.click();
    URL.revokeObjectURL(url);
    notifySuccess('Downloaded preset JSON file!');
  };

  const handleImport = () => {
    if (!importJsonText.trim()) return;
    const ok = importProfileJson(importJsonText.trim());
    if (ok) {
      setImportJsonText('');
      setIsImportModalOpen(false);
    }
  };

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = (event) => {
      const text = event.target?.result as string;
      if (text) {
        setImportJsonText(text);
      }
    };
    reader.readAsText(file);
  };

  if (!isCalibrationModalOpen) return null;

  const isCustomProfile = activeProfileId.startsWith('custom_');

  return (
    <>
      <Dialog
        open={isCalibrationModalOpen}
        onClose={() => setIsCalibrationModalOpen(false)}
        maxWidth="md"
        fullWidth
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-panel-bg)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '6px',
              color: 'var(--gl-text-primary)'
            }
          }
        }}
      >
        <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
              Scanner Calibration & Resolution Profiles
            </Typography>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
              Align the 6 numbered guide stripes over your in-game tooltip letters
            </Typography>
          </Box>
          <IconButton aria-label="Close scanner calibration" size="small" onClick={() => setIsCalibrationModalOpen(false)} sx={{ color: 'var(--gl-text-muted)' }}>
            <CloseIcon sx={{ fontSize: 18 }} />
          </IconButton>
        </DialogTitle>

        <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
          {/* Profile Selector Toolbar */}
          <Box
            sx={{
              display: 'flex',
              flexWrap: 'wrap',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: 1.5,
              p: 1.5,
              backgroundColor: 'var(--gl-card-hover-bg)',
              borderRadius: '4px',
              border: '1px solid var(--gl-border)'
            }}
          >
            {/* Left: Preset Selector */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Typography variant="body2" sx={{ fontWeight: 700, color: 'var(--gl-text-primary)', fontSize: '0.8rem' }}>
                Preset:
              </Typography>
              <Select
                inputProps={{ 'aria-label': 'Scanner calibration profile' }}
                size="small"
                value={activeProfileId}
                onChange={(e) => setActiveProfileId(e.target.value)}
                sx={{
                  backgroundColor: 'var(--gl-panel-bg)',
                  color: 'var(--gl-primary)',
                  fontWeight: 700,
                  fontSize: '0.8rem',
                  minWidth: 200
                }}
              >
                {profiles.map((p) => (
                  <MenuItem key={p.id} value={p.id} sx={{ fontSize: '0.8rem' }}>
                    {p.name} ({p.resolutionName})
                  </MenuItem>
                ))}
              </Select>

              {isCustomProfile && (
                <Tooltip title="Delete this custom preset">
                  <IconButton
                    aria-label="Delete scanner profile"
                    size="small"
                    onClick={() => deleteProfile(activeProfileId)}
                    sx={{ color: 'var(--gl-error)', border: '1px solid var(--gl-tint-green)', p: 0.5 }}
                  >
                    <DeleteIcon sx={{ fontSize: 16 }} />
                  </IconButton>
                </Tooltip>
              )}
            </Box>

            {/* Right: Actions (Save As, Export, Import, Reset) */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Button
                size="small"
                variant="outlined"
                onClick={() => setIsSaveModalOpen(true)}
                startIcon={<AddIcon sx={{ fontSize: 15 }} />}
                sx={{ borderColor: 'var(--gl-primary)', color: 'var(--gl-primary)', fontSize: '0.72rem', fontWeight: 700 }}
              >
                Save As Preset
              </Button>

              <Button
                size="small"
                variant="outlined"
                onClick={handleOpenExport}
                startIcon={<FileUploadIcon sx={{ fontSize: 15 }} />}
                sx={{ borderColor: 'var(--gl-border-strong)', color: 'var(--gl-text-secondary)', fontSize: '0.72rem' }}
              >
                Export
              </Button>

              <Button
                size="small"
                variant="outlined"
                onClick={() => setIsImportModalOpen(true)}
                startIcon={<FileDownloadIcon sx={{ fontSize: 15 }} />}
                sx={{ borderColor: 'var(--gl-border-strong)', color: 'var(--gl-text-secondary)', fontSize: '0.72rem' }}
              >
                Import
              </Button>

              <Button
                size="small"
                variant="outlined"
                onClick={resetScannerRegions}
                startIcon={<RestartAltIcon sx={{ fontSize: 15 }} />}
                sx={{ borderColor: 'var(--gl-border-strong)', color: 'var(--gl-text-secondary)', fontSize: '0.72rem' }}
              >
                Reset
              </Button>
            </Box>
          </Box>

          {/* Region Selector Tabs */}
          <Tabs
            value={selectedRegionIdx}
            onChange={(_, val) => setSelectedRegionIdx(val)}
            sx={{ minHeight: 32, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)' } }}
          >
            <Tab value={0} label="Region 1: Inventory Tooltip" sx={{ minHeight: 32, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
            <Tab value={1} label="Region 2: Planter Tooltip" sx={{ minHeight: 32, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          </Tabs>

          {/* Preview & Calibration Controls */}
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1.4fr 1fr' }, gap: 2 }}>
            {/* Zoomed Surround Live Preview Box */}
            <Paper
              variant="outlined"
              sx={{
                p: 1.5,
                backgroundColor: 'var(--gl-app-bg)',
                borderColor: 'var(--gl-border)',
                borderRadius: '4px',
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                minHeight: 200,
                position: 'relative',
                overflow: 'hidden'
              }}
            >
              {scannerPreviews[selectedRegionIdx] ? (
                <Box
                  sx={{
                    width: '100%',
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    gap: 1
                  }}
                >
                  <Box
                    sx={{
                      width: '100%',
                      maxWidth: 440,
                      overflow: 'hidden',
                      borderRadius: '4px',
                      border: '1px solid var(--gl-surface-hover)',
                      backgroundColor: '#000000',
                      display: 'flex',
                      justifyContent: 'center'
                    }}
                  >
                    <Box
                      component="img"
                      src={scannerPreviews[selectedRegionIdx]}
                      alt={`Region ${selectedRegionIdx + 1} Preview`}
                      sx={{
                        width: '100%',
                        height: 'auto',
                        display: 'block',
                        imageRendering: 'auto'
                      }}
                    />
                  </Box>

                  <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontSize: '0.72rem', fontWeight: 700, textAlign: 'center' }}>
                    Center each letter inside the 6 numbered stripes
                  </Typography>
                </Box>
              ) : (
                <Box sx={{ textAlign: 'center', p: 2 }}>
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', mb: 0.5 }}>
                    Live Zoom Preview
                  </Typography>
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontSize: '0.7rem' }}>
                    Hover over a plant clone in Rust with scanner active to see real-time alignment.
                  </Typography>
                </Box>
              )}
            </Paper>

            {/* Directional Nudge Pad */}
            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 1,
                p: 2,
                backgroundColor: 'var(--gl-panel-header-bg)',
                borderRadius: '4px',
                border: '1px solid var(--gl-border)'
              }}
            >
              <Typography variant="caption" sx={{ color: 'var(--gl-text-primary)', fontWeight: 800, fontSize: '0.75rem', letterSpacing: '0.5px' }}>
                NUDGE BOUNDING BOX
              </Typography>

              <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0.5, my: 0.5 }}>
                <IconButton
                  aria-label="Move selected scanner region up"
                  size="small"
                  sx={{ border: '1px solid var(--gl-surface-hover)', backgroundColor: 'var(--gl-surface)' }}
                  onMouseDown={() => startHold(() => moveScannerRegion(selectedRegionIdx, 0, -0.0006))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  <ArrowUpwardIcon sx={{ fontSize: 16 }} />
                </IconButton>

                <Box sx={{ display: 'flex', gap: 1.5 }}>
                  <IconButton
                    aria-label="Move selected scanner region left"
                    size="small"
                    sx={{ border: '1px solid var(--gl-surface-hover)', backgroundColor: 'var(--gl-surface)' }}
                    onMouseDown={() => startHold(() => moveScannerRegion(selectedRegionIdx, -0.0006, 0))}
                    onMouseUp={stopHold}
                    onMouseLeave={stopHold}
                  >
                    <ArrowBackIcon sx={{ fontSize: 16 }} />
                  </IconButton>

                  <IconButton
                    aria-label="Move selected scanner region right"
                    size="small"
                    sx={{ border: '1px solid var(--gl-surface-hover)', backgroundColor: 'var(--gl-surface)' }}
                    onMouseDown={() => startHold(() => moveScannerRegion(selectedRegionIdx, 0.0006, 0))}
                    onMouseUp={stopHold}
                    onMouseLeave={stopHold}
                  >
                    <ArrowForwardIcon sx={{ fontSize: 16 }} />
                  </IconButton>
                </Box>

                <IconButton
                  aria-label="Move selected scanner region down"
                  size="small"
                  sx={{ border: '1px solid var(--gl-surface-hover)', backgroundColor: 'var(--gl-surface)' }}
                  onMouseDown={() => startHold(() => moveScannerRegion(selectedRegionIdx, 0, 0.0006))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                >
                  <ArrowDownwardIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Box>

              <Box sx={{ display: 'flex', gap: 1, mt: 0.5 }}>
                <Button
                  size="small"
                  variant="outlined"
                  startIcon={<ZoomOutIcon sx={{ fontSize: 14 }} />}
                  onMouseDown={() => startHold(() => scaleScannerRegion(selectedRegionIdx, -0.0006))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                  sx={{ fontSize: '0.7rem', py: 0.3, borderColor: 'var(--gl-text-faint)', color: 'var(--gl-text-secondary)' }}
                >
                  Narrow
                </Button>
                <Button
                  size="small"
                  variant="outlined"
                  startIcon={<ZoomInIcon sx={{ fontSize: 14 }} />}
                  onMouseDown={() => startHold(() => scaleScannerRegion(selectedRegionIdx, 0.0006))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                  sx={{ fontSize: '0.7rem', py: 0.3, borderColor: 'var(--gl-text-faint)', color: 'var(--gl-text-secondary)' }}
                >
                  Widen
                </Button>
              </Box>

              {/* Keyboard Shortcut Hint */}
              <Box
                sx={{
                  mt: 1,
                  p: 1,
                  backgroundColor: 'rgba(0, 229, 255, 0.05)',
                  borderRadius: '4px',
                  border: '1px dashed rgba(0, 229, 255, 0.25)',
                  width: '100%',
                  textAlign: 'center'
                }}
              >
                <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontSize: '0.7rem', display: 'block', fontWeight: 700 }}>
                  ⌨️ Tip: Use Keyboard Arrow Keys (↑ ↓ ← →)
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.65rem', display: 'block', mt: 0.2 }}>
                  Hold <span style={{ color: 'var(--gl-text-secondary)' }}>Shift</span> for faster movement • <span style={{ color: 'var(--gl-text-secondary)' }}>-</span> / <span style={{ color: 'var(--gl-text-secondary)' }}>+</span> to resize
                </Typography>
              </Box>
            </Box>
          </Box>
        </DialogContent>

        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button
            onClick={() => setIsCalibrationModalOpen(false)}
            variant="contained"
            size="small"
            sx={{ backgroundColor: 'var(--gl-primary)', color: 'var(--gl-on-accent)', fontWeight: 800 }}
          >
            Done
          </Button>
        </DialogActions>
      </Dialog>

      {/* Save Custom Preset Dialog */}
      <Dialog
        open={isSaveModalOpen}
        onClose={() => setIsSaveModalOpen(false)}
        maxWidth="xs"
        fullWidth
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-panel-header-bg)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '6px',
              color: 'var(--gl-text-primary)'
            }
          }
        }}
      >
        <DialogTitle sx={{ fontWeight: 800, fontSize: '0.95rem' }}>Save As Custom Preset</DialogTitle>
        <DialogContent sx={{ pt: 1, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
            Save current coordinates as a named profile for easy switching and sharing.
          </Typography>
          <TextField
            autoFocus
            size="small"
            label="Preset Name"
            placeholder="e.g. My 1440p Custom or 4K Ultrawide"
            value={newPresetName}
            onChange={(e) => setNewPresetName(e.target.value)}
            fullWidth
            sx={{
              '& .MuiInputBase-input': { color: 'var(--gl-text-primary)', fontSize: '0.85rem' },
              '& .MuiOutlinedInput-root': { borderColor: 'var(--gl-text-faint)' }
            }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setIsSaveModalOpen(false)} size="small" sx={{ color: 'var(--gl-text-muted)' }}>
            Cancel
          </Button>
          <Button
            onClick={handleSavePreset}
            variant="contained"
            size="small"
            disabled={!newPresetName.trim()}
            sx={{ backgroundColor: 'var(--gl-primary)', color: 'var(--gl-on-accent)', fontWeight: 800 }}
          >
            Save Preset
          </Button>
        </DialogActions>
      </Dialog>

      {/* Export Presets Dialog */}
      <Dialog
        open={isExportModalOpen}
        onClose={() => setIsExportModalOpen(false)}
        maxWidth="sm"
        fullWidth
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-panel-header-bg)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '6px',
              color: 'var(--gl-text-primary)'
            }
          }
        }}
      >
        <DialogTitle sx={{ fontWeight: 800, fontSize: '0.95rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span>Export Presets (JSON)</span>
          <IconButton aria-label="Close scanner profile export" size="small" onClick={() => setIsExportModalOpen(false)} sx={{ color: 'var(--gl-text-muted)' }}>
            <CloseIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </DialogTitle>
        <DialogContent sx={{ pt: 1, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
            Share this configuration with friends or across devices.
          </Typography>
          <TextField
            aria-label="Scanner profile JSON to import"
            multiline
            rows={8}
            value={exportJsonText}
            fullWidth
            slotProps={{ input: { readOnly: true } }}
            sx={{
              '& .MuiInputBase-input': { color: 'var(--gl-primary)', fontFamily: 'monospace', fontSize: '0.72rem' },
              backgroundColor: 'var(--gl-app-bg)'
            }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2, justifyContent: 'space-between' }}>
          <Button
            onClick={handleDownloadExport}
            variant="outlined"
            size="small"
            startIcon={<FileDownloadIcon sx={{ fontSize: 16 }} />}
            sx={{ borderColor: 'var(--gl-text-faint)', color: 'var(--gl-text-secondary)' }}
          >
            Download .json
          </Button>
          <Button
            onClick={handleCopyExport}
            variant="contained"
            size="small"
            startIcon={<ContentCopyIcon sx={{ fontSize: 16 }} />}
            sx={{ backgroundColor: 'var(--gl-primary)', color: 'var(--gl-on-accent)', fontWeight: 800 }}
          >
            Copy to Clipboard
          </Button>
        </DialogActions>
      </Dialog>

      {/* Import Presets Dialog */}
      <Dialog
        open={isImportModalOpen}
        onClose={() => setIsImportModalOpen(false)}
        maxWidth="sm"
        fullWidth
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-panel-header-bg)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '6px',
              color: 'var(--gl-text-primary)'
            }
          }
        }}
      >
        <DialogTitle sx={{ fontWeight: 800, fontSize: '0.95rem', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <span>Import Presets (JSON)</span>
          <IconButton aria-label="Close scanner profile import" size="small" onClick={() => setIsImportModalOpen(false)} sx={{ color: 'var(--gl-text-muted)' }}>
            <CloseIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </DialogTitle>
        <DialogContent sx={{ pt: 1, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
            Paste preset JSON code or upload a exported `.json` file from another player.
          </Typography>
          <TextField
            multiline
            rows={8}
            placeholder="Paste preset JSON here..."
            value={importJsonText}
            onChange={(e) => setImportJsonText(e.target.value)}
            fullWidth
            sx={{
              '& .MuiInputBase-input': { color: 'var(--gl-text-primary)', fontFamily: 'monospace', fontSize: '0.72rem' },
              backgroundColor: 'var(--gl-app-bg)'
            }}
          />
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Button
              component="label"
              size="small"
              variant="outlined"
              startIcon={<FileUploadIcon sx={{ fontSize: 16 }} />}
              sx={{ borderColor: 'var(--gl-text-faint)', color: 'var(--gl-text-secondary)', fontSize: '0.72rem' }}
            >
              Upload .json File
              <input type="file" accept=".json,application/json" hidden onChange={handleFileUpload} />
            </Button>
          </Box>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setIsImportModalOpen(false)} size="small" sx={{ color: 'var(--gl-text-muted)' }}>
            Cancel
          </Button>
          <Button
            onClick={handleImport}
            variant="contained"
            size="small"
            disabled={!importJsonText.trim()}
            sx={{ backgroundColor: 'var(--gl-primary)', color: 'var(--gl-on-accent)', fontWeight: 800 }}
          >
            Import & Apply
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
};
