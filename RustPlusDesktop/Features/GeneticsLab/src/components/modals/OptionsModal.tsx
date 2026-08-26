import React, { useState, useEffect } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  Tabs,
  Tab,
  Switch,
  FormControlLabel,
  Slider,
  IconButton,
  Select,
  MenuItem,
  Divider,
  Paper
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import { useApp } from '../../context/AppContext.tsx';
import { useCalculation } from '../../context/CalculationContext.tsx';
import { useNotification } from '../../context/NotificationContext.tsx';
import {
  DEFAULT_OPTIONS,
  MAX_WORKER_COUNT,
  RECOMMENDED_WORKER_COUNT,
  ExtendedApplicationOptions
} from '../../services/storageService.ts';

export const OptionsModal: React.FC = () => {
  const {
    isOptionsModalOpen,
    setIsOptionsModalOpen,
    themeMode,
    setThemeMode,
    density,
    setDensity
  } = useApp();

  const { options, updateOptions } = useCalculation();
  const { notifySuccess, notifyInfo } = useNotification();

  const [tab, setTab] = useState<'solver' | 'ui' | 'scanner'>('solver');
  const [localOptions, setLocalOptions] = useState<ExtendedApplicationOptions>({ ...options });

  useEffect(() => {
    if (isOptionsModalOpen) {
      setLocalOptions({ ...options });
    }
  }, [isOptionsModalOpen, options]);

  const handleSave = () => {
    updateOptions(localOptions);
    notifySuccess('Saved options');
    setIsOptionsModalOpen(false);
  };

  const handleResetDefaults = () => {
    setLocalOptions({ ...DEFAULT_OPTIONS });
    notifyInfo('Reset options to recommended defaults');
  };

  if (!isOptionsModalOpen) return null;

  return (
    <Dialog
      open={isOptionsModalOpen}
      onClose={() => setIsOptionsModalOpen(false)}
      maxWidth="sm"
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
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Settings & Performance
        </Typography>
        <IconButton aria-label="Close settings" size="small" onClick={() => setIsOptionsModalOpen(false)} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <Box sx={{ borderBottom: '1px solid var(--gl-border)', px: 3 }}>
        <Tabs
          value={tab}
          onChange={(_, val) => setTab(val)}
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)' } }}
        >
          <Tab value="solver" label="Genetics Solver" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value="ui" label="Theme & Display" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value="scanner" label="Audio & Scanner" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
        </Tabs>
      </Box>

      <DialogContent sx={{ pt: 2.5, minHeight: 320, display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        {tab === 'solver' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {/* Worker Threads Slider */}
            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                <Typography variant="body2" sx={{ fontWeight: 700 }}>
                  Worker CPU Threads
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontFamily: 'monospace' }}>
                  {localOptions.numberOfWorkers} threads (Rec: {RECOMMENDED_WORKER_COUNT})
                </Typography>
              </Box>
              <Slider
                aria-label="Worker CPU threads"
                value={localOptions.numberOfWorkers}
                onChange={(_, val) => setLocalOptions({ ...localOptions, numberOfWorkers: val as number })}
                min={1}
                max={MAX_WORKER_COUNT}
                step={1}
                marks
                valueLabelDisplay="auto"
              />
            </Box>

            {/* Max Generations */}
            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                <Typography variant="body2" sx={{ fontWeight: 700 }}>
                  Number of Breeding Generations
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontFamily: 'monospace' }}>
                  {localOptions.numberOfGenerations} Generation{localOptions.numberOfGenerations > 1 ? 's' : ''}
                </Typography>
              </Box>
              <Slider
                aria-label="Number of breeding generations"
                value={localOptions.numberOfGenerations}
                onChange={(_, val) => setLocalOptions({ ...localOptions, numberOfGenerations: val as number })}
                min={1}
                max={3}
                step={1}
                marks
              />
            </Box>

            {/* Max Crossbreeding Parents */}
            <Box>
              <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                <Typography variant="body2" sx={{ fontWeight: 700 }}>
                  Max Surrounding Plants Per Planter
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, fontFamily: 'monospace' }}>
                  {localOptions.maxCrossbreedingSaplingsNumber} plants
                </Typography>
              </Box>
              <Slider
                aria-label="Maximum surrounding plants per planter"
                value={localOptions.maxCrossbreedingSaplingsNumber}
                onChange={(_, val) => setLocalOptions({ ...localOptions, maxCrossbreedingSaplingsNumber: val as number })}
                min={2}
                max={4}
                step={1}
                marks
              />
            </Box>
          </Box>
        )}

        {tab === 'ui' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2.5 }}>
            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <Box>
                <Typography variant="body2" sx={{ fontWeight: 700 }}>
                  Theme Mode
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                  Industrial dark mode or bright day light theme
                </Typography>
              </Box>
              <Select
                inputProps={{ 'aria-label': 'Theme mode' }}
                size="small"
                value={themeMode}
                onChange={(e) => setThemeMode(e.target.value as any)}
                sx={{ backgroundColor: 'var(--gl-card-hover-bg)', color: 'var(--gl-primary)' }}
              >
                <MenuItem value="dark">Dark Theme</MenuItem>
                <MenuItem value="light">Light Theme</MenuItem>
              </Select>
            </Box>

            <Divider sx={{ borderColor: 'var(--gl-border)' }} />

            <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <Box>
                <Typography variant="body2" sx={{ fontWeight: 700 }}>
                  Display Density
                </Typography>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                  Adjust padding and spacing for high resolution displays
                </Typography>
              </Box>
              <Select
                inputProps={{ 'aria-label': 'Display density' }}
                size="small"
                value={density}
                onChange={(e) => setDensity(e.target.value as any)}
                sx={{ backgroundColor: 'var(--gl-card-hover-bg)', color: 'var(--gl-primary)' }}
              >
                <MenuItem value="comfortable">Comfortable</MenuItem>
                <MenuItem value="compact">Compact</MenuItem>
              </Select>
            </Box>
          </Box>
        )}

        {tab === 'scanner' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <FormControlLabel
              control={
                <Switch
                  checked={localOptions.sounds}
                  onChange={(e) => setLocalOptions({ ...localOptions, sounds: e.target.checked })}
                  color="primary"
                />
              }
              label={
                <Box>
                  <Typography variant="body2" sx={{ fontWeight: 700 }}>
                    Sound Effects
                  </Typography>
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                    Play audio pop notification when clone is successfully scanned in Rust
                  </Typography>
                </Box>
              }
            />

            <FormControlLabel
              control={
                <Switch
                  checked={localOptions.skipScannerGuide}
                  onChange={(e) => setLocalOptions({ ...localOptions, skipScannerGuide: e.target.checked })}
                  color="primary"
                />
              }
              label={
                <Box>
                  <Typography variant="body2" sx={{ fontWeight: 700 }}>
                    Skip Scanner Onboarding
                  </Typography>
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                    Don't show the initial guide modal when launching screen share
                  </Typography>
                </Box>
              }
            />
          </Box>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2, justifyContent: 'space-between' }}>
        <Button
          onClick={handleResetDefaults}
          color="inherit"
          size="small"
          startIcon={<RestartAltIcon sx={{ fontSize: 16 }} />}
          sx={{ color: 'var(--gl-text-muted)' }}
        >
          Reset Defaults
        </Button>
        <Box sx={{ display: 'flex', gap: 1 }}>
          <Button onClick={() => setIsOptionsModalOpen(false)} color="inherit" size="small">
            Cancel
          </Button>
          <Button onClick={handleSave} variant="contained" size="small">
            Save
          </Button>
        </Box>
      </DialogActions>
    </Dialog>
  );
};
