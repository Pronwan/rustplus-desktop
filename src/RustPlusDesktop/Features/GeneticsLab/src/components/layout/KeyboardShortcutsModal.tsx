import React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  IconButton
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';

interface KeyboardShortcutsModalProps {
  open: boolean;
  onClose: () => void;
}

const SHORTCUTS = [
  { keys: ['Ctrl', 'Enter'], description: 'Calculate Breeding Routes' },
  { keys: ['Ctrl', 'Shift', 'S'], description: 'Toggle Live In-Game Scanner' },
  { keys: ['Esc'], description: 'Close any active modal or drawer' }
];

export const KeyboardShortcutsModal: React.FC<KeyboardShortcutsModalProps> = ({ open, onClose }) => {
  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="xs"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: 'var(--gl-panel-bg)',
            border: '1px solid var(--gl-surface-hover)',
            borderRadius: '6px',
            color: 'var(--gl-text-primary)'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Keyboard Shortcuts
        </Typography>
        <IconButton aria-label="Close keyboard shortcuts" size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {SHORTCUTS.map((item, idx) => (
          <Box
            key={idx}
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              p: 1.25,
              backgroundColor: 'var(--gl-card-hover-bg)',
              border: '1px solid var(--gl-border)',
              borderRadius: '4px'
            }}
          >
            <Typography variant="body2" sx={{ color: 'var(--gl-text-primary)' }}>
              {item.description}
            </Typography>

            <Box sx={{ display: 'flex', gap: 0.5 }}>
              {item.keys.map((k, kIdx) => (
                <Box
                  key={kIdx}
                  sx={{
                    px: 1,
                    py: 0.25,
                    backgroundColor: 'var(--gl-border)',
                    border: '1px solid var(--gl-text-faint)',
                    borderRadius: '3px',
                    fontFamily: 'monospace',
                    fontSize: '0.75rem',
                    fontWeight: 800,
                    color: 'var(--gl-primary)'
                  }}
                >
                  {k}
                </Box>
              ))}
            </Box>
          </Box>
        ))}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} variant="contained" size="small">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
};
