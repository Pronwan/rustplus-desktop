import React from 'react';
import {
  AppBar,
  Toolbar,
  Box,
  Typography,
  Tabs,
  Tab,
  Select,
  MenuItem,
  Button,
  IconButton,
  Tooltip
} from '@mui/material';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import SettingsIcon from '@mui/icons-material/Settings';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import LightModeIcon from '@mui/icons-material/LightMode';
import HelpIcon from '@mui/icons-material/Help';
import FolderIcon from '@mui/icons-material/Folder';
import KeyboardIcon from '@mui/icons-material/Keyboard';
import InfoIcon from '@mui/icons-material/Info';
import GitHubIcon from '@mui/icons-material/GitHub';
import { useApp, PLANT_TYPES } from '../../context/AppContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { useScanner } from '../../context/ScannerContext.tsx';

export const AppHeader: React.FC = () => {
  const {
    activeTab,
    setActiveTab,
    themeMode,
    toggleTheme,
    setIsOptionsModalOpen,
    setIsAboutModalOpen,
    setIsKeyboardShortcutsOpen,
    setIsProjectManagerOpen
  } = useApp();

  const { selectedPlant, setSelectedPlant } = useWorkspace();
  const { isScannerActive, startScanner, stopScanner } = useScanner();

  return (
    <AppBar
      position="static"
      sx={{
        backgroundColor: themeMode === 'dark' ? '#0A0A0A' : '#FFFFFF',
        color: themeMode === 'dark' ? '#FFFFFF' : '#1E293B',
        borderBottom: '1px solid',
        borderColor: themeMode === 'dark' ? '#222222' : '#E2E8F0',
        boxShadow: 'none',
        px: 1
      }}
    >
      <Toolbar variant="dense" sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
        {/* Left: Brand Logo & Crop Selector */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Box
              component="img"
              src={`./img/items/${selectedPlant}.webp`}
              alt={selectedPlant}
              sx={{ width: 28, height: 28, borderRadius: '4px' }}
              onError={(e) => {
                (e.target as HTMLElement).style.display = 'none';
              }}
            />
            <Typography
              variant="subtitle1"
              sx={{
                fontWeight: 900,
                fontFamily: '"Roboto Mono", monospace',
                letterSpacing: '1px',
                color: themeMode === 'dark' ? '#00E5FF' : '#0284C7',
                fontSize: '0.95rem'
              }}
            >
              RUST GENETICS LAB
            </Typography>
          </Box>

          {/* Crop Selector */}
          <Select
            size="small"
            value={selectedPlant}
            onChange={(e) => setSelectedPlant(e.target.value)}
            sx={{
              height: 30,
              fontSize: '0.78rem',
              fontWeight: 800,
              backgroundColor: themeMode === 'dark' ? '#181818' : '#F1F5F9',
              color: themeMode === 'dark' ? '#FFFFFF' : '#1E293B',
              textTransform: 'capitalize',
              '& fieldset': { borderColor: themeMode === 'dark' ? '#333' : '#CBD5E1' }
            }}
          >
            {PLANT_TYPES.map((plant) => (
              <MenuItem key={plant} value={plant} sx={{ fontSize: '0.78rem', textTransform: 'capitalize' }}>
                {plant.replace(/-/g, ' ')}
              </MenuItem>
            ))}
          </Select>
        </Box>

        {/* Center: Main Navigation Tabs */}
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          sx={{
            minHeight: 44,
            '& .MuiTabs-indicator': { backgroundColor: themeMode === 'dark' ? '#00E5FF' : '#0284C7', height: 2.5 }
          }}
        >
          <Tab value="workspace" label="Breeding Workspace" sx={{ fontSize: '0.82rem', fontWeight: 800 }} />
          <Tab value="planner" label="Farm Planner" sx={{ fontSize: '0.82rem', fontWeight: 800 }} />
          <Tab value="recipes" label="Tea Recipes" sx={{ fontSize: '0.82rem', fontWeight: 800 }} />
          <Tab value="guide" label="Genetics Guide" sx={{ fontSize: '0.82rem', fontWeight: 800 }} />
        </Tabs>

        {/* Right: Actions & Tools */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {/* Scanner Button */}
          <Button
            size="small"
            variant={isScannerActive ? 'contained' : 'outlined'}
            color={isScannerActive ? 'error' : 'primary'}
            onClick={() => (isScannerActive ? stopScanner() : startScanner())}
            startIcon={<AutoAwesomeIcon sx={{ fontSize: 16 }} />}
            sx={{
              fontSize: '0.75rem',
              fontWeight: 800,
              py: 0.4,
              px: 1.5,
              borderColor: isScannerActive ? undefined : themeMode === 'dark' ? '#333' : '#CBD5E1'
            }}
          >
            {isScannerActive ? 'STOP SCANNER' : 'SCAN FROM RUST'}
          </Button>

          {/* Farm Projects */}
          <Tooltip title="Farm Projects & Data (Save/Load/Export)" arrow>
            <IconButton
              size="small"
              onClick={() => setIsProjectManagerOpen(true)}
              sx={{ color: themeMode === 'dark' ? '#AAA' : '#64748B' }}
            >
              <FolderIcon sx={{ fontSize: 19 }} />
            </IconButton>
          </Tooltip>

          {/* Theme Toggle */}
          <Tooltip title={themeMode === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode'} arrow>
            <IconButton size="small" onClick={toggleTheme} sx={{ color: themeMode === 'dark' ? '#AAA' : '#64748B' }}>
              {themeMode === 'dark' ? <LightModeIcon sx={{ fontSize: 19 }} /> : <DarkModeIcon sx={{ fontSize: 19 }} />}
            </IconButton>
          </Tooltip>

          {/* Keyboard Shortcuts */}
          <Tooltip title="Keyboard Shortcuts" arrow>
            <IconButton
              size="small"
              onClick={() => setIsKeyboardShortcutsOpen(true)}
              sx={{ color: themeMode === 'dark' ? '#AAA' : '#64748B' }}
            >
              <KeyboardIcon sx={{ fontSize: 19 }} />
            </IconButton>
          </Tooltip>

          {/* Settings / Options */}
          <Tooltip title="Options & Performance" arrow>
            <IconButton
              size="small"
              onClick={() => setIsOptionsModalOpen(true)}
              sx={{ color: themeMode === 'dark' ? '#AAA' : '#64748B' }}
            >
              <SettingsIcon sx={{ fontSize: 19 }} />
            </IconButton>
          </Tooltip>

          {/* About */}
          <Tooltip title="About Genetics Lab" arrow>
            <IconButton
              size="small"
              onClick={() => setIsAboutModalOpen(true)}
              sx={{ color: themeMode === 'dark' ? '#AAA' : '#64748B' }}
            >
              <InfoIcon sx={{ fontSize: 19 }} />
            </IconButton>
          </Tooltip>

          {/* GitHub Repository */}
          <Tooltip title="GitHub Repository & Contribute" arrow>
            <IconButton
              size="small"
              component="a"
              href="https://github.com/JawadYzbk/rust-genetics-lab"
              target="_blank"
              rel="noopener noreferrer"
              sx={{
                color: themeMode === 'dark' ? '#AAA' : '#64748B',
                '&:hover': { color: '#00E5FF' }
              }}
            >
              <GitHubIcon sx={{ fontSize: 19 }} />
            </IconButton>
          </Tooltip>
        </Box>
      </Toolbar>
    </AppBar>
  );
};
