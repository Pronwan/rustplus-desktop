import React, { useState, useEffect } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Typography,
  Tabs,
  Tab,
  Box,
  Slider,
  Button,
  Checkbox,
  Switch,
  FormControlLabel,
  Divider
} from '@mui/material';
import { useApp } from '../../context/AppContext.tsx';
import { ApplicationOptions } from '../../services/orchestrator.ts';
import { DEFAULT_OPTIONS, StorageService } from '../../services/storageService.ts';
import { DEFAULT_GENE_SCORES } from '../../domain/genetics/Sapling.ts';

export const OptionsModal: React.FC = () => {
  const { isOptionsModalOpen, setIsOptionsModalOpen, options, updateOptions } = useApp();

  const [activeTab, setActiveTab] = useState<'crossbreeding' | 'ui'>('crossbreeding');
  const [localOptions, setLocalOptions] = useState<ApplicationOptions>({ ...options });

  // Sync with options whenever modal opens
  useEffect(() => {
    if (isOptionsModalOpen) {
      setLocalOptions({ ...options, geneScores: { ...options.geneScores } });
    }
  }, [isOptionsModalOpen, options]);

  const handleReset = () => {
    const reset = {
      ...DEFAULT_OPTIONS,
      geneScores: { ...DEFAULT_GENE_SCORES }
    };
    setLocalOptions(reset);
  };

  const handleApplySet = () => {
    updateOptions(localOptions);
  };

  const handleSaveAndClose = () => {
    updateOptions(localOptions);
    StorageService.saveOptions(localOptions);
    setIsOptionsModalOpen(false);
  };

  return (
    <Dialog
      open={isOptionsModalOpen}
      onClose={() => setIsOptionsModalOpen(false)}
      maxWidth="sm"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: '#181818',
            border: '1px solid #282828',
            borderRadius: '4px',
            color: '#E0E0E0',
            maxHeight: '90vh'
          }
        }
      }}
    >
      {/* Header */}
      <DialogTitle
        sx={{
          m: 0,
          p: '16px 24px 8px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between'
        }}
      >
        <Typography
          variant="h5"
          sx={{
            fontWeight: 800,
            fontFamily: '"Roboto Mono", monospace',
            color: '#FFFFFF',
            fontSize: '1.25rem'
          }}
        >
          Options
        </Typography>

        <Button
          size="small"
          onClick={handleReset}
          sx={{
            color: '#00E5FF',
            fontWeight: 700,
            fontSize: '0.8rem',
            fontFamily: 'monospace',
            p: 0,
            minWidth: 'auto',
            '&:hover': { backgroundColor: 'transparent', textDecoration: 'underline' }
          }}
        >
          RESET
        </Button>
      </DialogTitle>

      {/* Tabs: CROSSBREEDING | UI & SOUNDS */}
      <Box sx={{ px: 3, borderBottom: '1px solid #282828' }}>
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          textColor="inherit"
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: '#00E5FF', height: 2 } }}
        >
          <Tab
            value="crossbreeding"
            label="CROSSBREEDING"
            sx={{
              minHeight: 36,
              py: 0.5,
              px: 1.5,
              fontSize: '0.8rem',
              fontWeight: 700,
              color: activeTab === 'crossbreeding' ? '#00E5FF' : '#888888'
            }}
          />
          <Tab
            value="ui"
            label="UI &amp; SOUNDS"
            sx={{
              minHeight: 36,
              py: 0.5,
              px: 1.5,
              fontSize: '0.8rem',
              fontWeight: 700,
              color: activeTab === 'ui' ? '#00E5FF' : '#888888'
            }}
          />
        </Tabs>
      </Box>

      {/* Dialog Body */}
      <DialogContent sx={{ p: '20px 24px', display: 'flex', flexDirection: 'column', gap: 3 }}>
        {/* TAB 1: CROSSBREEDING */}
        {activeTab === 'crossbreeding' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
            {/* 1. Number of Workers */}
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', mb: 1, fontFamily: 'monospace' }}>
                Number of Workers
              </Typography>
              <Slider
                value={localOptions.numberOfWorkers || 4}
                min={2}
                max={16}
                step={2}
                marks={[
                  { value: 2, label: '2' },
                  { value: 4, label: '4' },
                  { value: 6, label: '6' },
                  { value: 8, label: '8' },
                  { value: 10, label: '10' },
                  { value: 12, label: '12' },
                  { value: 14, label: '14' },
                  { value: 16, label: '16' }
                ]}
                onChange={(_, val) => setLocalOptions({ ...localOptions, numberOfWorkers: val as number })}
                sx={{
                  color: '#00E5FF',
                  '& .MuiSlider-markLabel': {
                    color: '#8E8E8E',
                    fontFamily: 'monospace',
                    fontSize: '0.75rem'
                  }
                }}
              />
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 1.5, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Controls how many background workers are spawned during the calculation. A higher number means the calculation will be finished quicker, but it may cause your processor to be overloaded. If your device is struggling just lower the number.
              </Typography>
            </Box>

            {/* 2. Crossbreeding Plants Range */}
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', mb: 1, fontFamily: 'monospace' }}>
                Crossbreeding Plants Range
              </Typography>
              <Slider
                value={[
                  localOptions.minCrossbreedingSaplingsNumber ?? 2,
                  localOptions.maxCrossbreedingSaplingsNumber ?? 5
                ]}
                min={2}
                max={8}
                step={1}
                marks={[
                  { value: 2, label: '2' },
                  { value: 3, label: '3' },
                  { value: 4, label: '4' },
                  { value: 5, label: '5' },
                  { value: 6, label: '6' },
                  { value: 7, label: '7' },
                  { value: 8, label: '8' }
                ]}
                onChange={(_, val) => {
                  const arr = val as number[];
                  setLocalOptions({
                    ...localOptions,
                    minCrossbreedingSaplingsNumber: arr[0],
                    maxCrossbreedingSaplingsNumber: arr[1]
                  });
                }}
                sx={{
                  color: '#00E5FF',
                  '& .MuiSlider-markLabel': {
                    color: '#8E8E8E',
                    fontFamily: 'monospace',
                    fontSize: '0.75rem'
                  }
                }}
              />
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 1.5, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Controls the range of Plants that can be used for a single Crossbreeding session. It seems that range from 2 to 5 is a sweet spot between effectiveness and calculation speed. It is possible that we are missing some results if this value is not set to the extremes, but it saves a lot of processing time.
              </Typography>
            </Box>

            {/* 3. Number of Generations */}
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', mb: 1, fontFamily: 'monospace' }}>
                Number of Generations
              </Typography>
              <Slider
                value={localOptions.numberOfGenerations || 2}
                min={1}
                max={3}
                step={1}
                marks={[
                  { value: 1, label: 'one' },
                  { value: 2, label: 'two' },
                  { value: 3, label: 'three' }
                ]}
                onChange={(_, val) => setLocalOptions({ ...localOptions, numberOfGenerations: val as number })}
                sx={{
                  color: '#00E5FF',
                  '& .MuiSlider-markLabel': {
                    color: '#8E8E8E',
                    fontFamily: 'monospace',
                    fontSize: '0.8rem'
                  }
                }}
              />
            </Box>

            {/* 4. Plants added to next Generation */}
            <Box>
              <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
                Plants added to next Generation
              </Typography>
              <input
                type="number"
                min={1}
                max={100}
                value={localOptions.numberOfSaplingsAddedBetweenGenerations ?? 20}
                onChange={(e) =>
                  setLocalOptions({
                    ...localOptions,
                    numberOfSaplingsAddedBetweenGenerations: parseInt(e.target.value, 10) || 20
                  })
                }
                className="filter-underline-input"
                style={{ textAlign: 'left', fontSize: '0.95rem' }}
              />
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 0.75, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Number of best result Plants from current Generation that are added to calculation for next Generation.
              </Typography>
            </Box>

            {/* 5. Check combinations with repetitions */}
            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={localOptions.withRepetitions ?? true}
                    onChange={(e) => setLocalOptions({ ...localOptions, withRepetitions: e.target.checked })}
                    sx={{ color: '#00E5FF', '&.Mui-checked': { color: '#00E5FF' }, p: 0.5 }}
                  />
                }
                label={
                  <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                    Check combinations with repetitions
                  </Typography>
                }
              />
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 0.5, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Additionally, checks combinations where one plant is used more than once in one crossbreeding session. Slightly increases processing time.
              </Typography>
            </Box>

            {/* 6. Gene Scores */}
            <Box>
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', mb: 1, fontFamily: 'monospace' }}>
                Gene Scores
              </Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, 1fr)', gap: 2 }}>
                {(['G', 'Y', 'H', 'X', 'W'] as const).map((gene) => (
                  <Box key={gene}>
                    <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
                      {gene}
                    </Typography>
                    <input
                      type="number"
                      step={0.1}
                      value={localOptions.geneScores[gene] ?? 0}
                      onChange={(e) =>
                        setLocalOptions({
                          ...localOptions,
                          geneScores: {
                            ...localOptions.geneScores,
                            [gene]: parseFloat(e.target.value) || 0
                          }
                        })
                      }
                      className="filter-underline-input"
                    />
                  </Box>
                ))}
              </Box>
            </Box>

            {/* 7. Manual Minimum Tracked Score */}
            <Box>
              <FormControlLabel
                control={
                  <Checkbox
                    checked={localOptions.modifyMinimumTrackedScoreManually ?? false}
                    onChange={(e) =>
                      setLocalOptions({
                        ...localOptions,
                        modifyMinimumTrackedScoreManually: e.target.checked
                      })
                    }
                    sx={{ color: '#00E5FF', '&.Mui-checked': { color: '#00E5FF' }, p: 0.5 }}
                  />
                }
                label={
                  <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                    Manual Minimum Tracked Score
                  </Typography>
                }
              />
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 0.5, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Setting a lower Minimum Tracked Score can increase memory consumption.
              </Typography>

              {localOptions.modifyMinimumTrackedScoreManually && (
                <Box sx={{ mt: 1 }}>
                  <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
                    Minimum Tracked Score
                  </Typography>
                  <input
                    type="number"
                    value={localOptions.minimumTrackedScore ?? 4}
                    onChange={(e) =>
                      setLocalOptions({
                        ...localOptions,
                        minimumTrackedScore: parseFloat(e.target.value) || 4
                      })
                    }
                    className="filter-underline-input"
                    style={{ textAlign: 'left', width: 100 }}
                  />
                </Box>
              )}
            </Box>
          </Box>
        )}

        {/* TAB 2: UI & SOUNDS */}
        {activeTab === 'ui' && (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
            {/* Switch to Light Mode */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Switch
                checked={!localOptions.darkMode}
                onChange={(e) => setLocalOptions({ ...localOptions, darkMode: !e.target.checked })}
                sx={{
                  '& .MuiSwitch-switchBase.Mui-checked': { color: '#00E5FF' },
                  '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: '#00E5FF' }
                }}
              />
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                Switch to Light Mode
              </Typography>
            </Box>

            {/* Sounds */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Switch
                checked={localOptions.sounds ?? false}
                onChange={(e) => setLocalOptions({ ...localOptions, sounds: e.target.checked })}
                sx={{
                  '& .MuiSwitch-switchBase.Mui-checked': { color: '#00E5FF' },
                  '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: '#00E5FF' }
                }}
              />
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                Sounds: {localOptions.sounds ? 'On' : 'Off'}
              </Typography>
            </Box>

            {/* Skip Scanning Guide */}
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
              <Switch
                checked={localOptions.skipScannerGuide ?? false}
                onChange={(e) => setLocalOptions({ ...localOptions, skipScannerGuide: e.target.checked })}
                sx={{
                  '& .MuiSwitch-switchBase.Mui-checked': { color: '#00E5FF' },
                  '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: '#00E5FF' }
                }}
              />
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                Skip Scanning Guide: {localOptions.skipScannerGuide ? 'Yes' : 'No'}
              </Typography>
            </Box>

            {/* Automatically save calculated input genes */}
            <Box>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Switch
                  checked={localOptions.autoSaveInputSets ?? true}
                  onChange={(e) => setLocalOptions({ ...localOptions, autoSaveInputSets: e.target.checked })}
                  sx={{
                    '& .MuiSwitch-switchBase.Mui-checked': { color: '#00E5FF' },
                    '& .MuiSwitch-switchBase.Mui-checked + .MuiSwitch-track': { backgroundColor: '#00E5FF' }
                  }}
                />
                <Typography variant="body2" sx={{ fontWeight: 700, color: '#FFFFFF', fontFamily: 'monospace' }}>
                  Automatically save calculated input genes: {localOptions.autoSaveInputSets ? 'Yes' : 'No'}
                </Typography>
              </Box>
              <Typography variant="caption" sx={{ color: '#888888', display: 'block', mt: 0.5, ml: 7, lineHeight: 1.4, fontFamily: 'monospace' }}>
                Turning this off enables a save button that allows you to decide which genes you want to save.
              </Typography>
            </Box>
          </Box>
        )}
      </DialogContent>

      {/* Dialog Footer Actions */}
      <DialogActions sx={{ p: '12px 24px', display: 'flex', justifyContent: 'flex-end', gap: 1.5 }}>
        <Button
          onClick={() => setIsOptionsModalOpen(false)}
          sx={{
            color: '#8E8E8E',
            fontWeight: 700,
            fontFamily: 'monospace',
            '&:hover': { color: '#FFFFFF', backgroundColor: 'transparent' }
          }}
        >
          CLOSE
        </Button>

        <Button
          variant="contained"
          onClick={handleApplySet}
          sx={{
            backgroundColor: '#00E5FF',
            color: '#000000',
            fontWeight: 800,
            fontFamily: 'monospace',
            px: 2,
            '&:hover': { backgroundColor: '#33EBFF' }
          }}
        >
          SET
        </Button>

        <Button
          variant="contained"
          onClick={handleSaveAndClose}
          sx={{
            backgroundColor: '#00E5FF',
            color: '#000000',
            fontWeight: 800,
            fontFamily: 'monospace',
            px: 2,
            '&:hover': { backgroundColor: '#33EBFF' }
          }}
        >
          SAVE
        </Button>
      </DialogActions>
    </Dialog>
  );
};
