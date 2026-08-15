import React, { useMemo } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  Chip,
  IconButton,
  Tooltip
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useNotification } from '../../../context/NotificationContext.tsx';
import { analyzeMissingDonors } from '../../../domain/genetics/missingGenes.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

interface MissingCloneAdvisorProps {
  open: boolean;
  onClose: () => void;
}

export const MissingCloneAdvisor: React.FC<MissingCloneAdvisorProps> = ({ open, onClose }) => {
  const { clones, targetConfig, selectedPlant } = useWorkspace();
  const { notifySuccess } = useNotification();

  const { slotAnalysis, recommendedPatterns } = useMemo(() => {
    return analyzeMissingDonors(clones, targetConfig.targetGenetics);
  }, [clones, targetConfig.targetGenetics]);

  const handleCopyPattern = (pattern: string) => {
    navigator.clipboard.writeText(pattern);
    notifySuccess(`Copied pattern [${pattern}] to clipboard`);
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
            What Clone Am I Missing?
          </Typography>
          <Typography variant="caption" sx={{ color: '#888888' }}>
            Inventory Gap Analysis for Target [{targetConfig.targetGenetics}] · {selectedPlant.replace(/-/g, ' ')}
          </Typography>
        </Box>
        <IconButton size="small" onClick={onClose} sx={{ color: '#888' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ pt: 2, display: 'flex', flexDirection: 'column', gap: 2.5 }}>
        {/* Section 1: Per-Slot Donor Strength */}
        <Box>
          <Typography variant="caption" sx={{ color: '#00E5FF', fontWeight: 800, textTransform: 'uppercase', mb: 1, display: 'block' }}>
            Slot-by-Slot Donor Strength
          </Typography>

          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(6, 1fr)', gap: 1 }}>
            {slotAnalysis.map((slot) => {
              const isCritical = slot.weaknessLevel === 'critical';
              const isModerate = slot.weaknessLevel === 'moderate';

              const border = isCritical ? '#E53935' : isModerate ? '#FFA726' : '#4CAF50';
              const bg = isCritical ? 'rgba(229, 57, 53, 0.12)' : isModerate ? 'rgba(255, 167, 38, 0.12)' : 'rgba(76, 175, 80, 0.12)';

              return (
                <Box
                  key={slot.slotIndex}
                  sx={{
                    p: 1,
                    borderRadius: '4px',
                    backgroundColor: bg,
                    border: `1px solid ${border}`,
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    gap: 0.5
                  }}
                >
                  <Typography variant="caption" sx={{ fontWeight: 800, fontSize: '0.65rem', color: '#888' }}>
                    Slot {slot.slotNumber}
                  </Typography>

                  <Typography
                    variant="body2"
                    sx={{
                      fontWeight: 800,
                      fontFamily: 'monospace',
                      fontSize: '0.95rem',
                      color: slot.targetGene === 'G' || slot.targetGene === 'Y' || slot.targetGene === 'H' ? '#4CAF50' : '#E0E0E0'
                    }}
                  >
                    {slot.targetGene}
                  </Typography>

                  <Typography
                    variant="caption"
                    sx={{
                      fontWeight: 700,
                      fontSize: '0.68rem',
                      color: isCritical ? '#E53935' : isModerate ? '#FFA726' : '#4CAF50'
                    }}
                  >
                    {slot.currentDonorCount} donor{slot.currentDonorCount === 1 ? '' : 's'}
                  </Typography>
                </Box>
              );
            })}
          </Box>
        </Box>

        {/* Section 2: Ranked Patterns to Look For */}
        <Box>
          <Typography variant="caption" sx={{ color: '#00E5FF', fontWeight: 800, textTransform: 'uppercase', mb: 1, display: 'block' }}>
            Recommended Donor Patterns To Look For
          </Typography>

          {recommendedPatterns.length === 0 ? (
            <Typography variant="body2" sx={{ color: '#4CAF50', fontWeight: 700, p: 2, backgroundColor: 'rgba(76, 175, 80, 0.08)', borderRadius: '4px', textAlign: 'center' }}>
              ✓ Your inventory already has strong donors for all positions in this target!
            </Typography>
          ) : (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
              {recommendedPatterns.map((rec, idx) => (
                <Box
                  key={idx}
                  sx={{
                    p: 1.5,
                    borderRadius: '4px',
                    backgroundColor: '#1C1C1C',
                    border: '1px solid #282828',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: 1.5
                  }}
                >
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                    <Chip
                      size="small"
                      label={rec.priority.toUpperCase()}
                      sx={{
                        height: 18,
                        fontSize: '0.65rem',
                        fontWeight: 800,
                        backgroundColor: rec.priority === 'high' ? 'rgba(229, 57, 53, 0.15)' : 'rgba(255, 167, 38, 0.15)',
                        color: rec.priority === 'high' ? '#E53935' : '#FFA726',
                        border: `1px solid ${rec.priority === 'high' ? 'rgba(229, 57, 53, 0.4)' : 'rgba(255, 167, 38, 0.4)'}`
                      }}
                    />
                    <GeneticsSequence genes={rec.pattern} size="small" showConnectors={true} />
                    <Typography variant="caption" sx={{ color: '#AAAAAA' }}>
                      {rec.reason}
                    </Typography>
                  </Box>

                  <Tooltip title="Copy pattern" arrow>
                    <IconButton size="small" onClick={() => handleCopyPattern(rec.pattern)} sx={{ color: '#888', '&:hover': { color: '#00E5FF' } }}>
                      <ContentCopyIcon sx={{ fontSize: 14 }} />
                    </IconButton>
                  </Tooltip>
                </Box>
              ))}
            </Box>
          )}
        </Box>
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} variant="contained" size="small">
          Done
        </Button>
      </DialogActions>
    </Dialog>
  );
};
