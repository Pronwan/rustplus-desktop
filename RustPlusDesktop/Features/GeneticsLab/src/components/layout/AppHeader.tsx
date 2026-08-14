import React from 'react';
import {
  AppBar,
  Toolbar,
  Typography,
  Tabs,
  Tab,
  Box,
  IconButton,
  Tooltip,
  Select,
  MenuItem,
  Button
} from '@mui/material';
import SettingsIcon from '@mui/icons-material/Settings';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import { useApp, PLANT_TYPES } from '../../context/AppContext.tsx';

export const AppHeader: React.FC = () => {
  const {
    activeTab,
    setActiveTab,
    selectedPlant,
    setSelectedPlant,
    isCalculating,
    progress,
    setIsOptionsModalOpen,
    setIsAboutModalOpen
  } = useApp();

  return (
    <AppBar
      position="static"
      elevation={0}
      sx={{
        backgroundColor: '#141414',
        borderBottom: '1px solid #282828',
        color: '#E0E0E0'
      }}
    >
      <Toolbar sx={{ minHeight: 56, px: { xs: 2, sm: 4 }, display: 'flex', justifyContent: 'space-between' }}>
        {/* Brand & Plant Selector */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Box
            component="img"
            src={`./img/items/${selectedPlant}.webp`}
            alt={selectedPlant}
            sx={{ width: 28, height: 28, objectFit: 'contain' }}
          />

          <Typography
            variant="subtitle1"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              letterSpacing: '0.5px',
              color: '#FFFFFF'
            }}
          >
            Genetics Lab
          </Typography>

          <Select
            value={selectedPlant}
            onChange={(e) => setSelectedPlant(e.target.value as string)}
            size="small"
            variant="standard"
            disableUnderline
            sx={{
              fontSize: '0.8rem',
              fontWeight: 600,
              color: '#8E8E8E',
              fontFamily: 'monospace',
              '& .MuiSelect-select': { py: 0.25, px: 1 }
            }}
          >
            {PLANT_TYPES.map((plant: string) => (
              <MenuItem key={plant} value={plant}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Box
                    component="img"
                    src={`./img/items/${plant}.webp`}
                    alt={plant}
                    sx={{ width: 16, height: 16, objectFit: 'contain' }}
                  />
                  <Typography variant="body2" sx={{ textTransform: 'capitalize', fontSize: '0.8rem' }}>
                    {plant.replace(/-/g, ' ')}
                  </Typography>
                </Box>
              </MenuItem>
            ))}
          </Select>
        </Box>

        {/* Center Tabs */}
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          textColor="inherit"
          sx={{ minHeight: 48, '& .MuiTabs-indicator': { backgroundColor: '#00E5FF', height: 3 } }}
        >
          <Tab
            value="calculator"
            label="CALCULATOR"
            sx={{
              minHeight: 48,
              px: 2.5,
              fontWeight: 700,
              letterSpacing: '1px',
              fontSize: '0.85rem'
            }}
          />
          <Tab
            value="guide"
            label="GUIDE"
            sx={{
              minHeight: 48,
              px: 2.5,
              fontWeight: 700,
              letterSpacing: '1px',
              fontSize: '0.85rem'
            }}
          />
          <Tab
            value="recipes"
            label="RECIPES"
            sx={{
              minHeight: 48,
              px: 2.5,
              fontWeight: 700,
              letterSpacing: '1px',
              fontSize: '0.85rem'
            }}
          />
        </Tabs>

        {/* Right Action Links & EST Progress Time */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          {isCalculating && (
            <Tooltip title="Estimated calculation time remaining" arrow>
              <Box
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 0.75,
                  color: '#00E5FF',
                  backgroundColor: 'rgba(0, 229, 255, 0.08)',
                  border: '1px solid rgba(0, 229, 255, 0.3)',
                  borderRadius: '4px',
                  px: 1.25,
                  py: 0.25,
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.82rem',
                  fontWeight: 700
                }}
              >
                <span>⏱</span>
                <span>
                  {progress && progress.estimatedTimeRemainingSeconds !== null && progress.estimatedTimeRemainingSeconds !== undefined
                    ? (() => {
                        const totalSecs = Math.max(0, progress.estimatedTimeRemainingSeconds);
                        const m = Math.floor(totalSecs / 60);
                        const s = Math.floor(totalSecs % 60);
                        if (m >= 60) {
                          const h = Math.floor(m / 60);
                          const rm = m % 60;
                          return `${h}h:${rm.toString().padStart(2, '0')}m`;
                        }
                        return `${m}m:${s.toString().padStart(2, '0')}s`;
                      })()
                    : 'calculating...'}
                </span>
              </Box>
            </Tooltip>
          )}

          <Button
            size="small"
            onClick={() => setIsAboutModalOpen(true)}
            sx={{
              color: '#B0B0B0',
              fontWeight: 700,
              fontSize: '0.8rem',
              letterSpacing: '0.5px',
              '&:hover': { color: '#00E5FF', backgroundColor: 'transparent' }
            }}
          >
            ABOUT
          </Button>

          <Tooltip title="Options & Settings">
            <IconButton
              size="small"
              onClick={() => setIsOptionsModalOpen(true)}
              sx={{ color: '#8E8E8E', '&:hover': { color: '#00E5FF' } }}
            >
              <SettingsIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Toolbar>
    </AppBar>
  );
};
