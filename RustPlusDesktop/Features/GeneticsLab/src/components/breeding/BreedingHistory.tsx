import React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  Chip,
  IconButton
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

interface BreedingHistoryProps {
  open: boolean;
  onClose: () => void;
}

export const BreedingHistory: React.FC<BreedingHistoryProps> = ({ open, onClose }) => {
  const { breedingHistory } = useWorkspace();

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="sm"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: 'var(--gl-card-bg)',
            border: '1px solid var(--gl-surface-hover)',
            borderRadius: '6px',
            color: 'var(--gl-text-primary)'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Breeding Sessions History
        </Typography>
        <IconButton size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 1.5, maxHeight: 480 }}>
        {breedingHistory.length === 0 ? (
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', textAlign: 'center', py: 4 }}>
            No past breeding sessions recorded.
          </Typography>
        ) : (
          breedingHistory.map((sess) => {
            const isDone = sess.status === 'completed';
            const isAbandoned = sess.status === 'abandoned';

            return (
              <Box
                key={sess.id}
                sx={{
                  p: 1.5,
                  borderRadius: '4px',
                  backgroundColor: 'var(--gl-card-hover-bg)',
                  border: '1px solid var(--gl-border)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 2
                }}
              >
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', fontWeight: 700, textTransform: 'capitalize' }}>
                      {sess.cropType.replace(/-/g, ' ')}
                    </Typography>
                    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
                      · {new Date(sess.startedAt).toLocaleDateString()}
                    </Typography>
                  </Box>
                  <GeneticsSequence genes={sess.targetGenetics} size="small" showConnectors={true} />
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.7rem' }}>
                    {sess.steps.length} Generation{sess.steps.length > 1 ? 's' : ''}
                  </Typography>
                </Box>

                <Chip
                  size="small"
                  label={isDone ? 'COMPLETED' : isAbandoned ? 'ABANDONED' : 'ACTIVE'}
                  sx={{
                    fontWeight: 800,
                    fontSize: '0.68rem',
                    backgroundColor: isDone ? 'rgba(76, 175, 80, 0.15)' : isAbandoned ? 'rgba(229, 57, 53, 0.15)' : 'rgba(0, 229, 255, 0.15)',
                    color: isDone ? 'var(--gl-success)' : isAbandoned ? 'var(--gl-error)' : 'var(--gl-primary)',
                    border: '1px solid',
                    borderColor: isDone ? 'var(--gl-success)' : isAbandoned ? 'var(--gl-error)' : 'var(--gl-primary)'
                  }}
                />
              </Box>
            );
          })
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} variant="contained" size="small">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
};
