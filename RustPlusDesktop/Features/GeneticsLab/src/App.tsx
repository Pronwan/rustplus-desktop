// Rust Genetics Lab - Standalone & Desktop Integration
import React, { useMemo } from 'react';
import { ThemeProvider, CssBaseline, Box } from '@mui/material';
import { getMuiTheme } from './theme/muiTheme.ts';
import { useApp } from './context/AppContext.tsx';
import { AppHeader } from './components/layout/AppHeader.tsx';
import { PromoBanner } from './components/layout/PromoBanner.tsx';
import { WorkspaceLayout } from './components/workspace/WorkspaceLayout.tsx';
import { FarmOutputPlanner } from './components/planner/FarmOutputPlanner.tsx';
import { GuidePage } from './components/guide/GuidePage.tsx';
import { RecipesPage } from './components/recipes/RecipesPage.tsx';
import { BreedingMode } from './components/breeding/BreedingMode.tsx';
import { CompactScannerStatus } from './components/scanner/CompactScannerStatus.tsx';
import { ScannerCalibrationModal } from './components/scanner/ScannerCalibrationModal.tsx';
import { GeneCorrectionModal } from './components/scanner/GeneCorrectionModal.tsx';
import { ProjectManagerModal } from './components/projects/ProjectManagerModal.tsx';
import { KeyboardShortcutsModal } from './components/layout/KeyboardShortcutsModal.tsx';
import { OptionsModal } from './components/modals/OptionsModal.tsx';
import { AboutModal } from './components/modals/AboutModal.tsx';
import { ScannerGuideModal } from './components/modals/ScannerGuideModal.tsx';
import { CookieConsentBanner } from './components/modals/CookieConsentBanner.tsx';

export const App: React.FC = () => {
  const {
    activeTab,
    themeMode,
    density,
    isKeyboardShortcutsOpen,
    setIsKeyboardShortcutsOpen,
    isProjectManagerOpen,
    setIsProjectManagerOpen
  } = useApp();

  const muiTheme = useMemo(() => getMuiTheme(themeMode, density), [themeMode, density]);

  return (
    <ThemeProvider theme={muiTheme}>
      <CssBaseline />
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: muiTheme.palette.background.default,
          color: muiTheme.palette.text.primary
        }}
      >
        {/* Global Navigation Header */}
        <AppHeader />

        {/* Dismissible promo slot (free web build only; hidden for Premium) */}
        <PromoBanner />

        {/* Main Content Body */}
        <Box component="main" sx={{ flex: 1, overflow: 'hidden' }}>
          {(activeTab === 'workspace' || (activeTab as any) === 'calculator') && <WorkspaceLayout />}
          {activeTab === 'planner' && <FarmOutputPlanner />}
          {activeTab === 'guide' && <GuidePage />}
          {activeTab === 'recipes' && <RecipesPage />}
        </Box>

        {/* Step-by-Step Breeding Mode Assistant */}
        <BreedingMode />

        {/* Floating Active Scanner Status Widget */}
        <CompactScannerStatus />

        {/* Scanner Modals */}
        <ScannerCalibrationModal />
        <GeneCorrectionModal />

        {/* Project & Farm Data Manager */}
        <ProjectManagerModal
          open={isProjectManagerOpen}
          onClose={() => setIsProjectManagerOpen(false)}
        />

        {/* Hotkeys Cheatsheet */}
        <KeyboardShortcutsModal
          open={isKeyboardShortcutsOpen}
          onClose={() => setIsKeyboardShortcutsOpen(false)}
        />

        {/* Global Modals */}
        <OptionsModal />
        <AboutModal />
        <ScannerGuideModal />
        <CookieConsentBanner />
      </Box>
    </ThemeProvider>
  );
};
