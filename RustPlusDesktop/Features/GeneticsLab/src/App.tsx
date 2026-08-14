import React, { useMemo } from 'react';
import { ThemeProvider, CssBaseline, Box } from '@mui/material';
import { getMuiTheme } from './theme/muiTheme.ts';
import { useApp } from './context/AppContext.tsx';
import { AppHeader } from './components/layout/AppHeader.tsx';
import { CalculatorPage } from './components/calculator/CalculatorPage.tsx';
import { GuidePage } from './components/guide/GuidePage.tsx';
import { RecipesPage } from './components/recipes/RecipesPage.tsx';
import { OptionsModal } from './components/modals/OptionsModal.tsx';
import { AboutModal } from './components/modals/AboutModal.tsx';
import { ScannerGuideModal } from './components/modals/ScannerGuideModal.tsx';
import { CookieConsentBanner } from './components/modals/CookieConsentBanner.tsx';
import { ScannerWidget } from './components/scanner/ScannerWidget.tsx';

export const App: React.FC = () => {
  const { activeTab, themeMode } = useApp();

  const muiTheme = useMemo(() => getMuiTheme(themeMode), [themeMode]);

  return (
    <ThemeProvider theme={muiTheme}>
      <CssBaseline />
      <Box
        sx={{
          minHeight: '100vh',
          display: 'flex',
          flexDirection: 'column',
          backgroundColor: '#0E0E0E',
          color: '#E0E0E0'
        }}
      >
        <AppHeader />

        <Box component="main" sx={{ flex: 1, p: { xs: 2, sm: 3, md: 4 } }}>
          {activeTab === 'calculator' && <CalculatorPage />}
          {activeTab === 'guide' && <GuidePage />}
          {activeTab === 'recipes' && <RecipesPage />}
        </Box>

        {/* Floating Bottom-Right Scanner Preview (No Modal) */}
        <ScannerWidget />

        {/* Global Modals */}
        <OptionsModal />
        <AboutModal />
        <ScannerGuideModal />
        <CookieConsentBanner />
      </Box>
    </ThemeProvider>
  );
};
