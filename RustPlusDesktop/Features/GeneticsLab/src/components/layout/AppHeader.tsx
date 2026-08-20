import React, { useState } from 'react';
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
  Tooltip,
  Menu,
  ListItemIcon,
  ListItemText,
  useMediaQuery,
  useTheme
} from '@mui/material';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import SettingsIcon from '@mui/icons-material/Settings';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import LightModeIcon from '@mui/icons-material/LightMode';
import FolderIcon from '@mui/icons-material/Folder';
import KeyboardIcon from '@mui/icons-material/Keyboard';
import InfoIcon from '@mui/icons-material/Info';
import GitHubIcon from '@mui/icons-material/GitHub';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import { useApp, PLANT_TYPES } from '../../context/AppContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { useScanner } from '../../context/ScannerContext.tsx';

export const AppHeader: React.FC = () => {
  const theme = useTheme();
  const isCompact = useMediaQuery(theme.breakpoints.down('sm'));
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
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
  const { isScannerActive, isScannerInitializing, startScanner, stopScanner } = useScanner();
  const scannerBusy = isScannerActive || isScannerInitializing;
  const iconColor = themeMode === 'dark' ? '#AAA' : '#64748B';
  const closeMenuThen = (action: () => void) => () => {
    setMenuAnchor(null);
    action();
  };

  const utilityActions = (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
      <Tooltip title="Farm Projects & Data (Save/Load/Export)" arrow>
        <IconButton aria-label="Open farm projects" size="small" onClick={() => setIsProjectManagerOpen(true)} sx={{ color: iconColor }}>
          <FolderIcon sx={{ fontSize: 19 }} />
        </IconButton>
      </Tooltip>
      <Tooltip title={themeMode === 'dark' ? 'Switch to Light Mode' : 'Switch to Dark Mode'} arrow>
        <IconButton aria-label={themeMode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'} size="small" onClick={toggleTheme} sx={{ color: iconColor }}>
          {themeMode === 'dark' ? <LightModeIcon sx={{ fontSize: 19 }} /> : <DarkModeIcon sx={{ fontSize: 19 }} />}
        </IconButton>
      </Tooltip>
      <Tooltip title="Keyboard Shortcuts" arrow>
        <IconButton aria-label="Open keyboard shortcuts" size="small" onClick={() => setIsKeyboardShortcutsOpen(true)} sx={{ color: iconColor }}>
          <KeyboardIcon sx={{ fontSize: 19 }} />
        </IconButton>
      </Tooltip>
      <Tooltip title="Options & Performance" arrow>
        <IconButton aria-label="Open options" size="small" onClick={() => setIsOptionsModalOpen(true)} sx={{ color: iconColor }}>
          <SettingsIcon sx={{ fontSize: 19 }} />
        </IconButton>
      </Tooltip>
      <Tooltip title="About Genetics Lab" arrow>
        <IconButton aria-label="Open about Genetics Lab" size="small" onClick={() => setIsAboutModalOpen(true)} sx={{ color: iconColor }}>
          <InfoIcon sx={{ fontSize: 19 }} />
        </IconButton>
      </Tooltip>
      <Tooltip title="GitHub Repository & Contribute" arrow>
        <IconButton aria-label="Open Genetics Lab GitHub repository" size="small" component="a" href="https://github.com/JawadYzbk/rust-genetics-lab" target="_blank" rel="noopener noreferrer" sx={{ color: iconColor }}>
          <GitHubIcon sx={{ fontSize: 19 }} />
        </IconButton>
      </Tooltip>
    </Box>
  );

  return (
    <AppBar
      position="static"
      sx={{
        flexShrink: 0,
        backgroundColor: themeMode === 'dark' ? '#0A0A0A' : '#FFFFFF',
        color: themeMode === 'dark' ? '#FFFFFF' : '#1E293B',
        borderBottom: '1px solid',
        borderColor: themeMode === 'dark' ? '#222222' : '#E2E8F0',
        boxShadow: 'none',
        px: 1
      }}
    >
      <Toolbar
        variant="dense"
        sx={{
          minHeight: 48,
          display: 'flex',
          flexWrap: { xs: 'wrap', lg: 'nowrap' },
          justifyContent: 'space-between',
          gap: { xs: 0.75, sm: 1.5 },
          py: { xs: 0.5, lg: 0 }
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: { xs: 0.75, sm: 2 }, minWidth: 0 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexShrink: 0 }}>
            <Box
              component="img"
              src={`./img/items/${selectedPlant}.webp`}
              alt=""
              sx={{ width: 28, height: 28, borderRadius: '4px' }}
              onError={(e) => { (e.target as HTMLElement).style.display = 'none'; }}
            />
            <Typography
              component="span"
              variant="subtitle1"
              sx={{
                fontWeight: 900,
                fontFamily: '"Roboto Mono", monospace',
                letterSpacing: '1px',
                color: 'var(--gl-primary)',
                fontSize: '0.95rem',
                whiteSpace: 'nowrap'
              }}
            >
              {isCompact ? 'LAB' : 'RUST GENETICS LAB'}
            </Typography>
          </Box>

          <Select
            aria-label="Crop type"
            size="small"
            value={selectedPlant}
            onChange={(e) => setSelectedPlant(e.target.value)}
            sx={{
              height: 32,
              width: { xs: 104, sm: 150 },
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

        <Box component="nav" aria-label="Primary navigation" sx={{ order: { xs: 3, lg: 2 }, width: { xs: '100%', lg: 'auto' }, minWidth: 0 }}>
          <Tabs
            value={activeTab}
            onChange={(_, val) => setActiveTab(val)}
            variant={isCompact ? 'fullWidth' : 'standard'}
            sx={{ minHeight: 40, '& .MuiTabs-indicator': { backgroundColor: themeMode === 'dark' ? '#00E5FF' : '#0284C7', height: 2.5 } }}
          >
            <Tab value="workspace" label={isCompact ? 'Breed' : 'Breeding Workspace'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="planner" label={isCompact ? 'Planner' : 'Farm Planner'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="recipes" label={isCompact ? 'Recipes' : 'Tea Recipes'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="guide" label={isCompact ? 'Guide' : 'Genetics Guide'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
          </Tabs>
        </Box>

        <Box sx={{ order: { xs: 2, lg: 3 }, display: 'flex', alignItems: 'center', gap: { xs: 0.25, sm: 0.75 }, flexShrink: 0 }}>
          <Button
            aria-label={scannerBusy ? 'Stop Rust scanner' : 'Scan genetics from Rust'}
            size="small"
            variant={scannerBusy ? 'contained' : 'outlined'}
            color={scannerBusy ? 'error' : 'primary'}
            onClick={() => (scannerBusy ? stopScanner() : startScanner())}
            startIcon={!isCompact ? <AutoAwesomeIcon sx={{ fontSize: 16 }} /> : undefined}
            sx={{ minHeight: { xs: 44, sm: 32 }, minWidth: { xs: 58, sm: 0 }, px: { xs: 1, sm: 1.5 }, fontSize: '0.75rem', fontWeight: 800 }}
          >
            {isCompact ? (scannerBusy ? 'STOP' : 'SCAN') : isScannerInitializing ? 'STARTING…' : isScannerActive ? 'STOP SCANNER' : 'SCAN FROM RUST'}
          </Button>

          {!isCompact && utilityActions}
          {isCompact && (
            <>
              <IconButton
                aria-label="More actions"
                aria-controls={menuAnchor ? 'header-actions-menu' : undefined}
                aria-haspopup="menu"
                aria-expanded={Boolean(menuAnchor)}
                onClick={(event) => setMenuAnchor(event.currentTarget)}
                sx={{ minWidth: 44, minHeight: 44, color: iconColor }}
              >
                <MoreVertIcon />
              </IconButton>
              <Menu id="header-actions-menu" anchorEl={menuAnchor} open={Boolean(menuAnchor)} onClose={() => setMenuAnchor(null)}>
                <MenuItem onClick={closeMenuThen(() => setIsProjectManagerOpen(true))}><ListItemIcon><FolderIcon /></ListItemIcon><ListItemText>Farm projects</ListItemText></MenuItem>
                <MenuItem onClick={closeMenuThen(toggleTheme)}><ListItemIcon>{themeMode === 'dark' ? <LightModeIcon /> : <DarkModeIcon />}</ListItemIcon><ListItemText>{themeMode === 'dark' ? 'Light mode' : 'Dark mode'}</ListItemText></MenuItem>
                <MenuItem onClick={closeMenuThen(() => setIsKeyboardShortcutsOpen(true))}><ListItemIcon><KeyboardIcon /></ListItemIcon><ListItemText>Keyboard shortcuts</ListItemText></MenuItem>
                <MenuItem onClick={closeMenuThen(() => setIsOptionsModalOpen(true))}><ListItemIcon><SettingsIcon /></ListItemIcon><ListItemText>Options</ListItemText></MenuItem>
                <MenuItem onClick={closeMenuThen(() => setIsAboutModalOpen(true))}><ListItemIcon><InfoIcon /></ListItemIcon><ListItemText>About</ListItemText></MenuItem>
                <MenuItem component="a" href="https://github.com/JawadYzbk/rust-genetics-lab" target="_blank" rel="noopener noreferrer" onClick={() => setMenuAnchor(null)}><ListItemIcon><GitHubIcon /></ListItemIcon><ListItemText>GitHub repository</ListItemText></MenuItem>
              </Menu>
            </>
          )}
        </Box>
      </Toolbar>
    </AppBar>
  );
};
