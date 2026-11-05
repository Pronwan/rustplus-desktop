import React, { useCallback, useState } from 'react';
import { Box, Button, IconButton, Typography } from '@mui/material';
import PetsIcon from '@mui/icons-material/Pets';
import CloseIcon from '@mui/icons-material/Close';
import { useApp } from '../../context/AppContext.tsx';
import { StorageService } from '../../services/storageService.ts';

/**
 * One-line "new" banner for livestock genetics. The tab is the permanent entry point; this is
 * only the announcement, and dismissing it is remembered.
 */
export const LivestockBanner: React.FC = () => {
  const { activeTab, setActiveTab } = useApp();
  const [isDismissed, setIsDismissed] = useState(() => StorageService.getOptions().hideLivestockBanner === true);

  const dismiss = useCallback(() => {
    setIsDismissed(true);
    StorageService.saveOptions({ hideLivestockBanner: true });
  }, []);

  if (isDismissed || activeTab === 'livestock') return null;

  return (
    <Box
      role="region"
      aria-label="Livestock genetics available"
      sx={{
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        px: 1.25,
        py: 0.25,
        borderBottom: '1px solid var(--gl-border)',
        backgroundColor: 'var(--gl-panel-header-bg)'
      }}
    >
      <PetsIcon sx={{ fontSize: 18, color: 'var(--gl-primary)', flexShrink: 0 }} />
      <Typography
        sx={{ flex: 1, minWidth: 0, fontFamily: 'monospace', fontSize: '0.72rem', lineHeight: 1.25, color: 'var(--gl-text-secondary)' }}
      >
        Scan cows and sheep from Rust: herd stats, sale value and pair suggestions
        <Box
          component="span"
          sx={{ ml: 0.75, px: 0.5, fontSize: '0.58rem', borderRadius: '3px', backgroundColor: '#F59E0B', color: '#111827', fontWeight: 900 }}
        >
          BETA
        </Box>
      </Typography>
      <Button
        size="small"
        variant="contained"
        onClick={() => setActiveTab('livestock')}
        sx={{ flexShrink: 0, minHeight: 32, px: 1.5, fontSize: '0.68rem', fontWeight: 800, fontFamily: 'monospace', whiteSpace: 'nowrap' }}
      >
        OPEN
      </Button>
      <IconButton
        aria-label="Dismiss livestock banner"
        onClick={dismiss}
        size="small"
        sx={{ flexShrink: 0, width: 36, height: 36, color: 'var(--gl-text-muted)' }}
      >
        <CloseIcon sx={{ fontSize: 16 }} />
      </IconButton>
    </Box>
  );
};
