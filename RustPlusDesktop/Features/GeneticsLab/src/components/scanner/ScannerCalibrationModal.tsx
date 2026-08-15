import React, { useState } from 'react';
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
  Tooltip
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import { useScanner } from '../../context/ScannerContext.tsx';

export const ScannerCalibrationModal: React.FC = () => {
  const {
    isCalibrationModalOpen,
    setIsCalibrationModalOpen,
    profiles,
    activeProfileId,
    setActiveProfileId,
    scannerPreviews,
    moveScannerRegion,
    scaleScannerRegion,
    resetScannerRegions
  } = useScanner();

  const [selectedRegionIdx, setSelectedRegionIdx] = useState<number>(0);

  if (!isCalibrationModalOpen) return null;

  return (
    <Dialog
      open={isCalibrationModalOpen}
      onClose={() => setIsCalibrationModalOpen(false)}
      maxWidth="md"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: '#141414',
            border: '1px solid #333333',
            borderRadius: '6px',
            color: '#E0E0E0'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: '#FFFFFF' }}>
            Scanner Calibration & Resolution Profiles
          </Typography>
          <Typography variant="caption" sx={{ color: '#888' }}>
            Fine-tune tooltip OCR capture bounding boxes for your screen resolution
          </Typography>
        </Box>
        <IconButton size="small" onClick={() => setIsCalibrationModalOpen(false)} sx={{ color: '#888' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        {/* Profile Selector */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', p: 1.5, backgroundColor: '#1C1C1C', borderRadius: '4px', border: '1px solid #282828' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <Typography variant="body2" sx={{ fontWeight: 700, color: '#E0E0E0' }}>
              Resolution Preset:
            </Typography>
            <Select
              size="small"
              value={activeProfileId}
              onChange={(e) => setActiveProfileId(e.target.value)}
              sx={{ backgroundColor: '#141414', color: '#00E5FF', fontWeight: 700 }}
            >
              {profiles.map((p) => (
                <MenuItem key={p.id} value={p.id}>
                  {p.name} ({p.resolutionName})
                </MenuItem>
              ))}
            </Select>
          </Box>

          <Button
            size="small"
            variant="outlined"
            onClick={resetScannerRegions}
            startIcon={<RestartAltIcon sx={{ fontSize: 16 }} />}
            sx={{ borderColor: '#383838', color: '#AAA', fontSize: '0.72rem' }}
          >
            Reset Regions
          </Button>
        </Box>

        {/* Region Selector Tabs */}
        <Tabs
          value={selectedRegionIdx}
          onChange={(_, val) => setSelectedRegionIdx(val)}
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: '#00E5FF' } }}
        >
          <Tab value={0} label="Region 1: Inventory Tooltip" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value={1} label="Region 2: Planter Tooltip" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
        </Tabs>

        {/* Preview & Calibration Controls */}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1.2fr 1fr' }, gap: 2 }}>
          {/* Live Preview Box */}
          <Paper
            variant="outlined"
            sx={{
              p: 2,
              backgroundColor: '#101010',
              borderColor: '#282828',
              borderRadius: '4px',
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              minHeight: 180
            }}
          >
            {scannerPreviews[selectedRegionIdx] ? (
              <Box
                component="img"
                src={scannerPreviews[selectedRegionIdx]}
                alt={`Region ${selectedRegionIdx + 1} Preview`}
                sx={{
                  maxWidth: '100%',
                  maxHeight: 140,
                  border: '1.5px solid #00E5FF',
                  borderRadius: '3px',
                  backgroundColor: '#000'
                }}
              />
            ) : (
              <Typography variant="caption" sx={{ color: '#666', textAlign: 'center' }}>
                Preview appears here when scanner is actively running and captures frames.
              </Typography>
            )}
          </Paper>

          {/* Directional Nudge Pad */}
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 1, p: 2, backgroundColor: '#181818', borderRadius: '4px', border: '1px solid #282828' }}>
            <Typography variant="caption" sx={{ color: '#888', fontWeight: 800 }}>
              NUDGE BOUNDING BOX
            </Typography>

            <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0.5 }}>
              <IconButton size="small" onClick={() => moveScannerRegion(selectedRegionIdx, 0, -0.005)} sx={{ border: '1px solid #333' }}>
                <ArrowUpwardIcon sx={{ fontSize: 16 }} />
              </IconButton>
              <Box sx={{ display: 'flex', gap: 1 }}>
                <IconButton size="small" onClick={() => moveScannerRegion(selectedRegionIdx, -0.005, 0)} sx={{ border: '1px solid #333' }}>
                  <ArrowBackIcon sx={{ fontSize: 16 }} />
                </IconButton>
                <IconButton size="small" onClick={() => moveScannerRegion(selectedRegionIdx, 0.005, 0)} sx={{ border: '1px solid #333' }}>
                  <ArrowForwardIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Box>
              <IconButton size="small" onClick={() => moveScannerRegion(selectedRegionIdx, 0, 0.005)} sx={{ border: '1px solid #333' }}>
                <ArrowDownwardIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Box>

            <Box sx={{ display: 'flex', gap: 1, mt: 1 }}>
              <Button
                size="small"
                variant="outlined"
                onClick={() => scaleScannerRegion(selectedRegionIdx, -0.005)}
                startIcon={<ZoomOutIcon sx={{ fontSize: 14 }} />}
                sx={{ fontSize: '0.68rem', py: 0.2 }}
              >
                Narrow
              </Button>
              <Button
                size="small"
                variant="outlined"
                onClick={() => scaleScannerRegion(selectedRegionIdx, 0.005)}
                startIcon={<ZoomInIcon sx={{ fontSize: 14 }} />}
                sx={{ fontSize: '0.68rem', py: 0.2 }}
              >
                Widen
              </Button>
            </Box>
          </Box>
        </Box>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={() => setIsCalibrationModalOpen(false)} variant="contained" size="small">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
};
