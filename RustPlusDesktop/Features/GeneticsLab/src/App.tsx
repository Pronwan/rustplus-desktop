// Rust Genetics Lab - Standalone & Desktop Integration
import React, { Suspense, lazy, useMemo } from 'react';
import { ThemeProvider, CssBaseline, Box } from '@mui/material';
import { getMuiTheme } from './theme/muiTheme.ts';
import { useApp } from './context/AppContext.tsx';
import { AppHeader } from './components/layout/AppHeader.tsx';
import { WorkspaceLayout } from './components/workspace/WorkspaceLayout.tsx';
import { FarmOutputPlanner } from './components/planner/FarmOutputPlanner.tsx';
import { GuidePage } from './components/guide/GuidePage.tsx';
import { RecipesPage } from './components/recipes/RecipesPage.tsx';
import { BreedingMode } from './components/breeding/BreedingMode.tsx';
import { CompactScannerStatus } from './components/scanner/CompactScannerStatus.tsx';
import { ScannerCalibrationModal } from './components/scanner/ScannerCalibrationModal.tsx';
import { GeneCorrectionModal } from './components/scanner/GeneCorrectionModal.tsx';
import { MobileCameraScannerHost } from './components/scanner/MobileCameraScannerHost.tsx';
import { CameraScannerBanner } from './components/scanner/CameraScannerBanner.tsx';
import { ProjectManagerModal } from './components/projects/ProjectManagerModal.tsx';
import { KeyboardShortcutsModal } from './components/layout/KeyboardShortcutsModal.tsx';
import { OptionsModal } from './components/modals/OptionsModal.tsx';
import { AboutModal } from './components/modals/AboutModal.tsx';
import { ScannerGuideModal } from './components/modals/ScannerGuideModal.tsx';
import { ReflexNoticeModal } from './components/modals/ReflexNoticeModal.tsx';
import { CookieConsentBanner } from './components/modals/CookieConsentBanner.tsx';
import { LivestockBanner } from './components/livestock/LivestockBanner.tsx';

// Loaded on first visit to the tab, so the plant workspace does not pay for it.
const LivestockPage = lazy(() => import('./components/livestock/LivestockPage.tsx'));

export const App: React.FC = () => {
  const {
    activeTab,
    themeMode,
    density,
    isKeyboardShortcutsOpen,
    setIsKeyboardShortcutsOpen,
    isProjectManagerOpen,
    setIsProjectManagerOpen,
    isReflexNoticeOpen,
    setIsReflexNoticeOpen
  } = useApp();

  const muiTheme = useMemo(() => getMuiTheme(themeMode, density), [themeMode, density]);

  return (
    <ThemeProvider theme={muiTheme}>
      <CssBaseline />
      <Box
        sx={{
          height: '100dvh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: muiTheme.palette.background.default,
          color: muiTheme.palette.text.primary
        }}
      >
        {/* Global Navigation Header */}
        <AppHeader />

        {/* Compact phone-camera entry point, mobile layouts only */}
        <CameraScannerBanner />

        {/* New-feature announcement for livestock genetics */}
        <LivestockBanner />

        {/* Main Content Body */}
        <Box component="main" sx={{ flex: 1, minHeight: 0, overflow: 'auto' }}>
          {(activeTab === 'workspace' || (activeTab as any) === 'calculator') && <WorkspaceLayout />}
          {activeTab === 'planner' && <FarmOutputPlanner />}
          {activeTab === 'guide' && <GuidePage />}
          {activeTab === 'recipes' && <RecipesPage />}
          {activeTab === 'livestock' && (
            <Suspense fallback={null}>
              <LivestockPage />
            </Suspense>
          )}
        </Box>

        {/* Step-by-Step Breeding Mode Assistant */}
        <BreedingMode />

        {/* Floating Active Scanner Status Widget */}
        <CompactScannerStatus />

        {/* Scanner Modals */}
        <ScannerCalibrationModal />
        <GeneCorrectionModal />

        {/* Phone Camera Scanner (lazy-loaded, mobile entry only) */}
        <MobileCameraScannerHost />

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
        <ReflexNoticeModal
          open={isReflexNoticeOpen}
          onClose={() => setIsReflexNoticeOpen(false)}
        />
        <CookieConsentBanner />
      </Box>
    </ThemeProvider>
  );
};
