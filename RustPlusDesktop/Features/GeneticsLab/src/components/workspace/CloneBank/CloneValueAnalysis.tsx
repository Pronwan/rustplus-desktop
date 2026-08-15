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
    CORE: { bg: 'rgba(0, 229, 255, 0.15)', text: '#00E5FF', border: 'rgba(0, 229, 255, 0.4)' },
    HIGH: { bg: 'rgba(76, 175, 80, 0.15)', text: '#4CAF50', border: 'rgba(76, 175, 80, 0.4)' },
    MEDIUM: { bg: 'rgba(255, 167, 38, 0.15)', text: '#FFA726', border: 'rgba(255, 167, 38, 0.4)' },
    LOW: { bg: 'rgba(150, 150, 150, 0.12)', text: '#AAAAAA', border: 'rgba(150, 150, 150, 0.3)' },
    REDUNDANT: { bg: 'rgba(229, 57, 53, 0.12)', text: '#E53935', border: 'rgba(229, 57, 53, 0.3)' }
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
            backgroundColor: '#161616',
            border: '1px solid #333333',
            borderRadius: '6px',
            color: '#E0E0E0'
          }
        }
      }}
    >
      <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1 }}>
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 800, color: '#FFFFFF' }}>
            Clone Value Analysis
          </Typography>
          <Typography variant="caption" sx={{ color: '#888888' }}>
            Target: [{targetConfig.targetGenetics}] · {selectedPlant.replace(/-/g, ' ')}
          </Typography>
        </Box>
        <IconButton size="small" onClick={onClose} sx={{ color: '#888' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 1.5, maxHeight: 480 }}>
        {clones.length === 0 ? (
          <Typography variant="body2" sx={{ color: '#888', textAlign: 'center', py: 4 }}>
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
                    <Typography variant="caption" sx={{ color: '#666', fontWeight: 800 }}>
                      #{idx + 1}
                    </Typography>
                    {clone.name && (
                      <Typography variant="caption" sx={{ color: '#FFFFFF', fontWeight: 700 }}>
                        {clone.name}
                      </Typography>
                    )}
                    <Typography variant="caption" sx={{ color: '#00E5FF', fontWeight: 800 }}>
                      ×{clone.quantity}
                    </Typography>
                  </Box>
                  <GeneticsSequence genes={clone.genetics} size="small" showConnectors={true} />
                  <Typography variant="caption" sx={{ color: '#888888', fontSize: '0.72rem' }}>
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
