import React, { useEffect, useState } from 'react';
import {
  Paper,
  Typography,
  Stack,
  Button,
  Box,
  Slide,
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Switch,
  FormControlLabel,
  Divider
} from '@mui/material';
import { CookieConsentState } from '../../services/storageService.ts';
import { useApp } from '../../context/AppContext.tsx';

export const CookieConsentBanner: React.FC = () => {
  const { consent, updateConsent, isConsentModalOpen, setIsConsentModalOpen } = useApp();
  const [analyticsEnabled, setAnalyticsEnabled] = useState(consent.analytics);

  useEffect(() => {
    setAnalyticsEnabled(consent.analytics);
  }, [consent.analytics]);

  const handleAcceptAll = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: true,
      analytics: true,
      advertisement: false
    };
    updateConsent(next);
  };

  const handleDeclineAll = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: true,
      analytics: false,
      advertisement: false
    };
    updateConsent(next);
  };

  const handleSaveCustom = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: true,
      analytics: analyticsEnabled,
      advertisement: false
    };
    updateConsent(next);
    setIsConsentModalOpen(false);
  };

  if (consent.isPreferenceDecided) return null;

  return (
    <>
      <Slide direction="up" in={!isConsentModalOpen} mountOnEnter unmountOnExit>
        <Paper
          role="region"
          aria-label="Storage and cookie preferences"
          elevation={6}
          sx={{
            position: 'fixed',
            bottom: { xs: 'max(16px, env(safe-area-inset-bottom))', sm: 24 },
            left: { xs: 16, sm: 24 },
            right: { xs: 16, sm: 24 },
            maxWidth: 800,
            mx: 'auto',
            p: 2.5,
            zIndex: (theme) => theme.zIndex.modal - 1,
            maxHeight: 'calc(100dvh - 32px)',
            overflowY: 'auto',
            borderRadius: 3,
            backgroundColor: 'background.paper',
            border: '1px solid',
            borderColor: 'divider',
            backdropFilter: 'blur(10px)'
          }}
        >
          <Box
            sx={{
              display: 'flex',
              flexDirection: { xs: 'column', md: 'row' },
              gap: 2,
              alignItems: { xs: 'stretch', md: 'center' },
              justifyContent: 'space-between'
            }}
          >
            <Box>
              <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
                Storage &amp; Cookie Preferences
              </Typography>
              <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block' }}>
                Genetics Lab uses essential local storage for your inputs and settings. Optional analytics stays off until you allow it.
              </Typography>
            </Box>

            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              <Button size="small" variant="contained" color="primary" onClick={handleAcceptAll}>
                Accept All
              </Button>
              <Button size="small" variant="outlined" onClick={handleDeclineAll}>
                Decline Optional
              </Button>
              <Button size="small" variant="text" onClick={() => setIsConsentModalOpen(true)}>
                Manage
              </Button>
            </Box>
          </Box>
        </Paper>
      </Slide>

      {/* Detailed Manage Consent Dialog */}
      <Dialog
        open={isConsentModalOpen}
        onClose={() => setIsConsentModalOpen(false)}
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle sx={{ fontWeight: 700 }}>Manage Cookie Preferences</DialogTitle>
        <DialogContent dividers>
          <Stack spacing={2}>
            <Box>
              <FormControlLabel
                control={<Switch checked disabled />}
                label={<Typography variant="subtitle2" sx={{ fontWeight: 700 }}>Essential (Required)</Typography>}
              />
              <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', pl: 4 }}>
                Required for core app functionality and memory management.
              </Typography>
            </Box>

            <Divider />

            <Box>
              <FormControlLabel
                control={
                  <Switch
                    checked
                    disabled
                  />
                }
                label={<Typography variant="subtitle2" sx={{ fontWeight: 700 }}>Functional Storage</Typography>}
              />
              <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', pl: 4 }}>
                Required to save plant setups, favorite clones, and OCR calibration coordinates. Declining optional cookies never deletes this data.
              </Typography>
            </Box>

            <Divider />

            <Box>
              <FormControlLabel
                control={
                  <Switch
                    checked={analyticsEnabled}
                    onChange={(e) => setAnalyticsEnabled(e.target.checked)}
                  />
                }
                label={<Typography variant="subtitle2" sx={{ fontWeight: 700 }}>Analytics &amp; Telemetry</Typography>}
              />
              <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', pl: 4 }}>
                Anonymous telemetry for performance optimization.
              </Typography>
            </Box>
          </Stack>
        </DialogContent>
        <DialogActions sx={{ p: 2 }}>
          <Button variant="contained" onClick={handleSaveCustom}>
            Save Preferences
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
};
