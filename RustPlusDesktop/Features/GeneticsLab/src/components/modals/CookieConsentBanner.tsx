import React, { useState, useEffect } from 'react';
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
import { StorageService, CookieConsentState } from '../../services/storageService.ts';

export const CookieConsentBanner: React.FC = () => {
  const [consent, setConsent] = useState<CookieConsentState | null>(null);
  const [isConsentModalOpen, setIsConsentModalOpen] = useState(false);
  const [tempPreferences, setTempPreferences] = useState({
    functional: true,
    analytics: false
  });

  useEffect(() => {
    const saved = StorageService.getConsent();
    if (saved && saved.isPreferenceDecided) {
      setConsent(saved);
      setTempPreferences({
        functional: saved.functional,
        analytics: saved.analytics
      });
    }
  }, []);

  const handleAcceptAll = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: true,
      analytics: true,
      advertisement: false
    };
    StorageService.saveConsent(next);
    setConsent(next);
  };

  const handleDeclineAll = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: false,
      analytics: false,
      advertisement: false
    };
    StorageService.saveConsent(next);
    setConsent(next);
  };

  const handleSaveCustom = () => {
    const next: CookieConsentState = {
      isPreferenceDecided: true,
      functional: tempPreferences.functional,
      analytics: tempPreferences.analytics,
      advertisement: false
    };
    StorageService.saveConsent(next);
    setConsent(next);
    setIsConsentModalOpen(false);
  };

  if (consent?.isPreferenceDecided) return null;

  return (
    <>
      <Slide direction="up" in={!consent?.isPreferenceDecided} mountOnEnter unmountOnExit>
        <Paper
          elevation={6}
          sx={{
            position: 'fixed',
            bottom: 24,
            left: { xs: 16, sm: 24 },
            right: { xs: 16, sm: 24 },
            maxWidth: 800,
            mx: 'auto',
            p: 2.5,
            zIndex: 1400,
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
                Genetics Lab uses functional local storage to remember your plant inputs, calculation preferences, and scanner calibration settings.
              </Typography>
            </Box>

            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
              <Button size="small" variant="contained" color="primary" onClick={handleAcceptAll}>
                Accept All
              </Button>
              <Button size="small" variant="outlined" onClick={handleDeclineAll}>
                Decline
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
                    checked={tempPreferences.functional}
                    onChange={(e) => setTempPreferences({ ...tempPreferences, functional: e.target.checked })}
                  />
                }
                label={<Typography variant="subtitle2" sx={{ fontWeight: 700 }}>Functional Storage</Typography>}
              />
              <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', pl: 4 }}>
                Saves plant setups, favorite clones, and OCR calibration coordinates.
              </Typography>
            </Box>

            <Divider />

            <Box>
              <FormControlLabel
                control={
                  <Switch
                    checked={tempPreferences.analytics}
                    onChange={(e) => setTempPreferences({ ...tempPreferences, analytics: e.target.checked })}
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
