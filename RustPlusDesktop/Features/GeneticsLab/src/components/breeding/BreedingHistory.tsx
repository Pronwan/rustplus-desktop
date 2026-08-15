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
            backgroundColor: '#161616',
            border: '1px solid #333333',
            borderRadius: '6px',
            color: '#E0E0E0'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: '#FFFFFF' }}>
          Breeding Sessions History
        </Typography>
        <IconButton size="small" onClick={onClose} sx={{ color: '#888' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 1.5, maxHeight: 480 }}>
        {breedingHistory.length === 0 ? (
          <Typography variant="body2" sx={{ color: '#888', textAlign: 'center', py: 4 }}>
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
                  backgroundColor: '#1C1C1C',
                  border: '1px solid #282828',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  gap: 2
                }}
              >
                <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Typography variant="caption" sx={{ color: '#AAAAAA', fontWeight: 700, textTransform: 'capitalize' }}>
                      {sess.cropType.replace(/-/g, ' ')}
                    </Typography>
                    <Typography variant="caption" sx={{ color: '#666' }}>
                      · {new Date(sess.startedAt).toLocaleDateString()}
                    </Typography>
                  </Box>
                  <GeneticsSequence genes={sess.targetGenetics} size="small" showConnectors={true} />
                  <Typography variant="caption" sx={{ color: '#888', fontSize: '0.7rem' }}>
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
                    color: isDone ? '#4CAF50' : isAbandoned ? '#E53935' : '#00E5FF',
                    border: '1px solid',
                    borderColor: isDone ? '#4CAF50' : isAbandoned ? '#E53935' : '#00E5FF'
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
