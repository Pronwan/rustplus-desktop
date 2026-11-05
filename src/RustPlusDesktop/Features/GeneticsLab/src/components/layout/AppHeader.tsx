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
import NewReleasesIcon from '@mui/icons-material/NewReleases';
import SchoolIcon from '@mui/icons-material/School';
import GitHubIcon from '@mui/icons-material/GitHub';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import PhotoCameraIcon from '@mui/icons-material/PhotoCamera';
import FlashOnIcon from '@mui/icons-material/FlashOn';
import { useApp } from '../../context/AppContext.tsx';
import { PlantPicker } from '../common/PlantPicker.tsx';
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
    setIsPatchNotesOpen,
    setIsTourOpen,
    setIsReflexNoticeOpen,
    setIsKeyboardShortcutsOpen,
    setIsProjectManagerOpen
  } = useApp();
  const { selectedPlant, setSelectedPlant } = useWorkspace();
  const {
    isScannerActive,
    isScannerInitializing,
    startScanner,
    stopScanner,
    isCameraScannerSupported,
    openCameraScanner
  } = useScanner();
  const scannerBusy = isScannerActive || isScannerInitializing;
  // Phone camera mode is an explicit action offered on compact layouts only; the desktop
  // screen scanner above stays exactly as it was.
  const showPhoneCameraAction = isCompact && isCameraScannerSupported;
  const iconColor = themeMode === 'dark' ? '#AAA' : '#64748B';
  const closeMenuThen = (action: () => void) => () => {
    setMenuAnchor(null);
    action();
  };

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
                fontFamily: 'var(--font-family-mono)',
                letterSpacing: '1px',
                color: 'var(--gl-primary)',
                fontSize: '0.95rem',
                whiteSpace: 'nowrap'
              }}
            >
              {isCompact ? 'LAB' : 'RUST GENETICS LAB'}
            </Typography>
          </Box>

          <Box data-tour="plant-picker" sx={{ display: 'flex' }}>
            <PlantPicker value={selectedPlant} onChange={setSelectedPlant} label="Crop type" compact={isCompact} />
          </Box>
        </Box>

        <Box component="nav" data-tour="nav" aria-label="Primary navigation" sx={{ order: { xs: 3, lg: 2 }, width: { xs: '100%', lg: 'auto' }, minWidth: 0 }}>
          <Tabs
            value={activeTab}
            onChange={(_, val) => setActiveTab(val)}
            variant={isCompact ? 'fullWidth' : 'standard'}
            sx={{ minHeight: 40, '& .MuiTabs-indicator': { backgroundColor: themeMode === 'dark' ? 'var(--gl-primary)' : '#0284C7', height: 2.5 } }}
          >
            <Tab value="workspace" label={isCompact ? 'Breed' : 'Breeding Workspace'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="livestock" label={<span>{isCompact ? 'Animals' : 'Livestock'}<Box component="span" sx={{ ml: 0.5, px: 0.5, fontSize: '0.58rem', lineHeight: 1.6, borderRadius: '3px', backgroundColor: '#F59E0B', color: '#111827', fontWeight: 900, verticalAlign: 'middle' }}>BETA</Box></span>} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="planner" label={isCompact ? 'Planner' : 'Farm Planner'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="recipes" label={isCompact ? 'Recipes' : 'Tea Recipes'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
            <Tab value="guide" label={isCompact ? 'Guide' : 'Genetics Guide'} sx={{ minWidth: 0, px: { xs: 0.5, sm: 2 } }} />
          </Tabs>
        </Box>

        <Box sx={{ order: { xs: 2, lg: 3 }, display: 'flex', alignItems: 'center', gap: { xs: 0.25, sm: 0.75 }, flexShrink: 0 }}>
          <Button
            data-tour="scan"
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

          {!isCompact && (
            <Tooltip title="Farm projects & data" arrow>
              <IconButton data-tour="projects" aria-label="Open farm projects" size="small" onClick={() => setIsProjectManagerOpen(true)} sx={{ color: iconColor }}>
                <FolderIcon sx={{ fontSize: 19 }} />
              </IconButton>
            </Tooltip>
          )}
          <Tooltip title={themeMode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'} arrow>
            <IconButton
              aria-label={themeMode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
              size="small"
              onClick={toggleTheme}
              sx={{ color: iconColor, '& svg': { transition: 'transform 300ms ease' }, '&:hover svg': { transform: 'rotate(25deg)' } }}
            >
              {themeMode === 'dark' ? <LightModeIcon sx={{ fontSize: 19 }} /> : <DarkModeIcon sx={{ fontSize: 19 }} />}
            </IconButton>
          </Tooltip>
          <Tooltip title="More actions" arrow>
            <IconButton
              data-tour="menu"
              aria-label="More actions"
              aria-controls={menuAnchor ? 'header-actions-menu' : undefined}
              aria-haspopup="menu"
              aria-expanded={Boolean(menuAnchor)}
              onClick={(event) => setMenuAnchor(event.currentTarget)}
              sx={{ minWidth: { xs: 44, sm: 32 }, minHeight: { xs: 44, sm: 32 }, color: iconColor }}
            >
              <MoreVertIcon />
            </IconButton>
          </Tooltip>
          <Menu id="header-actions-menu" anchorEl={menuAnchor} open={Boolean(menuAnchor)} onClose={() => setMenuAnchor(null)}>
            {showPhoneCameraAction && <MenuItem onClick={closeMenuThen(openCameraScanner)}><ListItemIcon><PhotoCameraIcon /></ListItemIcon><ListItemText>Use phone camera (Beta)</ListItemText></MenuItem>}
            {isCompact && <MenuItem onClick={closeMenuThen(() => setIsProjectManagerOpen(true))}><ListItemIcon><FolderIcon /></ListItemIcon><ListItemText>Farm projects</ListItemText></MenuItem>}
            <MenuItem onClick={closeMenuThen(() => setIsTourOpen(true))}><ListItemIcon><SchoolIcon /></ListItemIcon><ListItemText>Tutorial</ListItemText></MenuItem>
            <MenuItem onClick={closeMenuThen(() => setIsKeyboardShortcutsOpen(true))}><ListItemIcon><KeyboardIcon /></ListItemIcon><ListItemText>Keyboard shortcuts</ListItemText></MenuItem>
            <MenuItem onClick={closeMenuThen(() => setIsReflexNoticeOpen(true))}><ListItemIcon><FlashOnIcon sx={{ color: '#F59E0B' }} /></ListItemIcon><ListItemText>Scanner &amp; Reflex Guide</ListItemText></MenuItem>
            <MenuItem onClick={closeMenuThen(() => setIsOptionsModalOpen(true))}><ListItemIcon><SettingsIcon /></ListItemIcon><ListItemText>Options</ListItemText></MenuItem>
            <MenuItem onClick={closeMenuThen(() => setIsPatchNotesOpen(true))}><ListItemIcon><NewReleasesIcon /></ListItemIcon><ListItemText>Patch notes</ListItemText></MenuItem>
            <MenuItem onClick={closeMenuThen(() => setIsAboutModalOpen(true))}><ListItemIcon><InfoIcon /></ListItemIcon><ListItemText>About</ListItemText></MenuItem>
            <MenuItem component="a" href="https://github.com/JawadYzbk/rust-genetics-lab" target="_blank" rel="noopener noreferrer" onClick={() => setMenuAnchor(null)}><ListItemIcon><GitHubIcon /></ListItemIcon><ListItemText>GitHub repository</ListItemText></MenuItem>
          </Menu>
        </Box>
      </Toolbar>
    </AppBar>
  );
};
