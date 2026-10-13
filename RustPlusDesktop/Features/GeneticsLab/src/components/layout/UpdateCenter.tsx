import React, { useCallback, useEffect, useState } from 'react';
import {
  Box,
  Button,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  Typography
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import SystemUpdateAltIcon from '@mui/icons-material/SystemUpdateAlt';
import NewReleasesIcon from '@mui/icons-material/NewReleases';
import {
  ChangelogEntry,
  UpdateNotice,
  applyUpdate,
  changesBetween,
  compareVersions,
  connectUpdateChannel,
  fetchOwnChangelog
} from '../../services/updates/updateChannel.ts';

const LAST_SEEN_KEY = 'GL_LAST_SEEN_VERSION';

function readLastSeen(): string | null {
  try {
    return localStorage.getItem(LAST_SEEN_KEY);
  } catch {
    return null;
  }
}

function writeLastSeen(version: string): void {
  try {
    localStorage.setItem(LAST_SEEN_KEY, version);
  } catch {
    // Private mode / storage disabled: "What's new" simply shows nothing next time.
  }
}

/**
 * Update notices for Genetics Lab.
 *
 * - A banner under the header while a newer build downloads and once it is ready, with a
 *   preview of what it changes and a button that switches to it.
 * - A "What's new" dialog on the first start of a newer version, listing every release
 *   since the one the user last had open.
 */
export const UpdateCenter: React.FC = () => {
  const [notice, setNotice] = useState<UpdateNotice | null>(null);
  const [isBannerDismissed, setIsBannerDismissed] = useState(false);
  const [dialog, setDialog] = useState<{ mode: 'preview' | 'whats-new'; entries: ChangelogEntry[] } | null>(null);
  const [isApplying, setIsApplying] = useState(false);

  const showWhatsNewSince = useCallback(async (previous: string) => {
    if (compareVersions(__APP_VERSION__, previous) <= 0) return;
    const entries = changesBetween(await fetchOwnChangelog(), previous, __APP_VERSION__);
    if (entries.length > 0) setDialog({ mode: 'whats-new', entries });
  }, []);

  useEffect(() => {
    const lastSeen = readLastSeen();
    writeLastSeen(__APP_VERSION__);
    // A first visit has nothing to compare against, so it stays quiet.
    if (lastSeen) void showWhatsNewSince(lastSeen);

    return connectUpdateChannel({
      onNotice: (next) => {
        setNotice(next);
        // A new state (downloading -> ready) is worth showing again after a dismiss.
        setIsBannerDismissed(false);
      },
      // The desktop host reports the build it replaced; older builds never wrote LAST_SEEN_KEY.
      onPreviousVersion: (previous) => {
        if (!lastSeen) void showWhatsNewSince(previous);
      }
    });
  }, [showWhatsNewSince]);

  const pendingChanges = notice
    ? notice.version
      ? changesBetween(notice.changelog, __APP_VERSION__, notice.version)
      : notice.changelog.slice(0, 1)
    : [];

  const handleApply = () => {
    setIsApplying(true);
    applyUpdate();
  };

  const versionLabel = notice?.version && notice.version !== __APP_VERSION__ ? ` v${notice.version}` : '';

  return (
    <>
      {notice && !isBannerDismissed && (
        <Box
          role="status"
          aria-live="polite"
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
          {notice.state === 'downloading' ? (
            <CircularProgress size={16} thickness={5} sx={{ color: 'var(--gl-primary)', flexShrink: 0 }} />
          ) : (
            <SystemUpdateAltIcon sx={{ fontSize: 18, color: 'var(--gl-primary)', flexShrink: 0 }} />
          )}
          <Typography
            sx={{ flex: 1, minWidth: 0, fontFamily: 'monospace', fontSize: '0.72rem', lineHeight: 1.25, color: 'var(--gl-text-secondary)' }}
          >
            {notice.state === 'downloading'
              ? `Downloading Genetics Lab${versionLabel}…`
              : `Genetics Lab${versionLabel} is ready. Reload to update; a running scan stops.`}
          </Typography>
          {pendingChanges.length > 0 && (
            <Button
              size="small"
              onClick={() => setDialog({ mode: 'preview', entries: pendingChanges })}
              sx={{ flexShrink: 0, minHeight: 32, px: 1.25, fontSize: '0.68rem', fontWeight: 800, fontFamily: 'monospace', whiteSpace: 'nowrap' }}
            >
              WHAT'S NEW
            </Button>
          )}
          {notice.state === 'ready' && (
            <Button
              size="small"
              variant="contained"
              disabled={isApplying}
              onClick={handleApply}
              sx={{ flexShrink: 0, minHeight: 32, px: 1.5, fontSize: '0.68rem', fontWeight: 800, fontFamily: 'monospace', whiteSpace: 'nowrap' }}
            >
              RELOAD NOW
            </Button>
          )}
          <IconButton
            aria-label="Dismiss update notice"
            onClick={() => setIsBannerDismissed(true)}
            size="small"
            sx={{ flexShrink: 0, width: 36, height: 36, color: 'var(--gl-text-muted)' }}
          >
            <CloseIcon sx={{ fontSize: 16 }} />
          </IconButton>
        </Box>
      )}

      <Dialog open={dialog !== null} onClose={() => setDialog(null)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ display: 'flex', alignItems: 'center', gap: 1, pr: 6 }}>
          <NewReleasesIcon sx={{ color: 'var(--gl-primary)' }} />
          {dialog?.mode === 'whats-new' ? `What's new in Genetics Lab v${__APP_VERSION__}` : `Coming in Genetics Lab${versionLabel}`}
          <IconButton aria-label="Close" onClick={() => setDialog(null)} sx={{ position: 'absolute', right: 8, top: 8 }}>
            <CloseIcon />
          </IconButton>
        </DialogTitle>
        <DialogContent dividers>
          {dialog?.entries.map((entry) => (
            <Box key={entry.version} sx={{ '&:not(:last-of-type)': { mb: 2.5 } }}>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 1 }}>
                <Chip size="small" color="primary" label={`v${entry.version}`} sx={{ fontWeight: 800, fontFamily: 'monospace' }} />
                {entry.date && (
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                    {entry.date}
                  </Typography>
                )}
              </Box>
              {entry.sections.map((section) => (
                <Box key={section.title} sx={{ mb: 1 }}>
                  <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
                    {section.title}
                  </Typography>
                  <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
                    {section.items.map((item, i) => (
                      <Typography component="li" variant="body2" key={i} sx={{ color: 'var(--gl-text-secondary)', mb: 0.25 }}>
                        {item}
                      </Typography>
                    ))}
                  </Box>
                </Box>
              ))}
            </Box>
          ))}
        </DialogContent>
        <DialogActions>
          {dialog?.mode === 'preview' && notice?.state === 'ready' && (
            <Button variant="contained" disabled={isApplying} onClick={handleApply}>
              Reload now
            </Button>
          )}
          <Button onClick={() => setDialog(null)}>{dialog?.mode === 'whats-new' ? 'Got it' : 'Later'}</Button>
        </DialogActions>
      </Dialog>
    </>
  );
};
