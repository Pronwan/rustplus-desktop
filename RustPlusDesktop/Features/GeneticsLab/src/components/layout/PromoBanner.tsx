import React, { useState } from 'react';
import { Box, Typography, IconButton, Button } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import FavoriteIcon from '@mui/icons-material/Favorite';

const DISMISS_KEY = 'GL_PROMO_DISMISSED_V1';

/**
 * Slim, dismissible promotional slot shown on the free web build only.
 *
 * - Hidden for Premium users (the desktop host sets `window.__RGL_PREMIUM__`).
 * - Dismissal persists in localStorage.
 * - Content is intentionally a single config object so self-promo can later be
 *   swapped for (or supplemented by) a real ad unit on the hosted web build.
 *   Fill PROMO.href with your Patreon / Discord link when ready.
 */
const PROMO = {
  message: 'Enjoying Genetics Lab? It is free & open-source.',
  cta: 'Support development',
  href: 'https://github.com/JawadYzbk/rust-genetics-lab'
};

const isPremium = (): boolean =>
  typeof window !== 'undefined' && window.__RGL_PREMIUM__ === true;

export const PromoBanner: React.FC = () => {
  const [dismissed, setDismissed] = useState<boolean>(() => {
    try {
      return localStorage.getItem(DISMISS_KEY) === 'true';
    } catch {
      return false;
    }
  });

  if (isPremium() || dismissed) return null;

  const handleDismiss = () => {
    try {
      localStorage.setItem(DISMISS_KEY, 'true');
    } catch {
      // storage unavailable — dismiss for this session only
    }
    setDismissed(true);
  };

  return (
    <Box
      sx={{
        position: 'relative',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 1.5,
        px: 5,
        py: 0.6,
        backgroundColor: 'var(--gl-panel-header-bg)',
        borderBottom: '1px solid var(--gl-border)',
        flexWrap: 'wrap'
      }}
    >
      <FavoriteIcon sx={{ fontSize: 14, color: 'var(--gl-warning)' }} />
      <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontWeight: 600 }}>
        {PROMO.message}
      </Typography>
      <Button
        component="a"
        href={PROMO.href}
        target="_blank"
        rel="noopener noreferrer"
        size="small"
        sx={{
          minWidth: 0,
          py: 0.1,
          px: 1,
          fontSize: '0.7rem',
          fontWeight: 800,
          color: 'var(--gl-primary)',
          '&:hover': { backgroundColor: 'rgba(0, 229, 255, 0.08)' }
        }}
      >
        {PROMO.cta}
      </Button>
      <IconButton
        size="small"
        onClick={handleDismiss}
        aria-label="Dismiss"
        sx={{ position: 'absolute', right: 8, color: 'var(--gl-text-muted)', p: 0.25 }}
      >
        <CloseIcon sx={{ fontSize: 15 }} />
      </IconButton>
    </Box>
  );
};
