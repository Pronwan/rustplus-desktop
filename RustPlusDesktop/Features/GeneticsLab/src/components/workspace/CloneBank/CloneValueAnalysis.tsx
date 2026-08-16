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
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { analyzeCloneUtilities } from '../../../domain/genetics/missingGenes.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

interface CloneValueAnalysisProps {
  open: boolean;
  onClose: () => void;
}

export const CloneValueAnalysis: React.FC<CloneValueAnalysisProps> = ({ open, onClose }) => {
  const { clones, targetConfig, selectedPlant } = useWorkspace();
  const { results } = useCalculation();

  const ratingsMap = React.useMemo(() => {
    return analyzeCloneUtilities(clones, results, targetConfig.targetGenetics);
  }, [clones, results, targetConfig.targetGenetics]);

  const ratingColors = {
    CORE: { bg: 'rgba(0, 229, 255, 0.15)', text: 'var(--gl-primary)', border: 'rgba(0, 229, 255, 0.4)' },
    HIGH: { bg: 'rgba(76, 175, 80, 0.15)', text: 'var(--gl-success)', border: 'rgba(76, 175, 80, 0.4)' },
    MEDIUM: { bg: 'rgba(255, 167, 38, 0.15)', text: 'var(--gl-warning)', border: 'rgba(255, 167, 38, 0.4)' },
    LOW: { bg: 'rgba(150, 150, 150, 0.12)', text: 'var(--gl-text-secondary)', border: 'rgba(150, 150, 150, 0.3)' },
    REDUNDANT: { bg: 'rgba(229, 57, 53, 0.12)', text: 'var(--gl-error)', border: 'rgba(229, 57, 53, 0.3)' }
  };

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
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
            Clone Value Analysis
          </Typography>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
            Target: [{targetConfig.targetGenetics}] · {selectedPlant.replace(/-/g, ' ')}
          </Typography>
        </Box>
        <IconButton size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 1.5, maxHeight: 480 }}>
        {clones.length === 0 ? (
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', textAlign: 'center', py: 4 }}>
            No clones in Clone Bank yet.
          </Typography>
        ) : (
          clones.map((clone, idx) => {
            const utility = ratingsMap.get(clone.id);
            const rating = utility?.rating || 'LOW';
            const colors = ratingColors[rating];

            return (
              <Box
                key={clone.id}
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
                    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 800 }}>
                      #{idx + 1}
                    </Typography>
                    {clone.name && (
                      <Typography variant="caption" sx={{ color: 'var(--gl-text-primary)', fontWeight: 700 }}>
                        {clone.name}
                      </Typography>
                    )}
                    <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800 }}>
                      ×{clone.quantity}
                    </Typography>
                  </Box>
                  <GeneticsSequence genes={clone.genetics} size="small" showConnectors={true} />
                  <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.72rem' }}>
                    {utility?.description}
                  </Typography>
                </Box>

                <Chip
                  size="small"
                  label={rating}
                  sx={{
                    fontWeight: 800,
                    fontSize: '0.7rem',
                    backgroundColor: colors.bg,
                    color: colors.text,
                    border: `1px solid ${colors.border}`,
                    px: 1
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
