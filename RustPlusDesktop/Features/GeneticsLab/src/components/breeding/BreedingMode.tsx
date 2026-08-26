import React, { useState } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  Stepper,
  Step,
  StepLabel,
  IconButton
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import HistoryIcon from '@mui/icons-material/History';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { BreedingStepCard } from './BreedingStepCard.tsx';
import { BreedingHistory } from './BreedingHistory.tsx';
import { ConfirmDialog } from '../common/ConfirmDialog.tsx';

export const BreedingMode: React.FC = () => {
  const {
    activeSession,
    updateSessionStepPlanted,
    completeBreedingStep,
    abandonBreedingSession,
    selectedPlant
  } = useWorkspace();

  const [isAbandonConfirmOpen, setIsAbandonConfirmOpen] = useState(false);
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);

  if (!activeSession) return null;

  const currentStepIdx = activeSession.currentStepIndex || 0;
  const currentStep = activeSession.steps[currentStepIdx];

  return (
    <>
      <Dialog
        open={true}
        onClose={() => setIsAbandonConfirmOpen(true)}
        maxWidth="md"
        fullWidth
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-panel-bg)',
              border: '1.5px solid var(--gl-surface-hover)',
              borderRadius: '8px',
              color: 'var(--gl-text-primary)',
              minHeight: 520
            }
          }
        }}
      >
        <DialogTitle sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', pb: 1, borderBottom: '1px solid var(--gl-surface)' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-primary)', fontFamily: '"Roboto Mono", monospace' }}>
              STEP-BY-STEP BREEDING ASSISTANT
            </Typography>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', textTransform: 'capitalize' }}>
              {selectedPlant.replace(/-/g, ' ')}
            </Typography>
          </Box>

          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <Button
              size="small"
              variant="outlined"
              color="inherit"
              onClick={() => setIsHistoryOpen(true)}
              startIcon={<HistoryIcon sx={{ fontSize: 16 }} />}
              sx={{ fontSize: '0.72rem', borderColor: 'var(--gl-surface-hover)' }}
            >
              History
            </Button>
            <IconButton aria-label="Abandon breeding session" size="small" onClick={() => setIsAbandonConfirmOpen(true)} sx={{ color: 'var(--gl-text-muted)' }}>
              <CloseIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Box>
        </DialogTitle>

        <DialogContent sx={{ pt: 3, display: 'flex', flexDirection: 'column', gap: 3 }}>
          {/* Stepper Header */}
          <Stepper activeStep={currentStepIdx} alternativeLabel sx={{ '& .MuiStepIcon-root.Mui-active': { color: 'var(--gl-primary)' }, '& .MuiStepIcon-root.Mui-completed': { color: 'var(--gl-success)' } }}>
            {activeSession.steps.map((s, idx) => (
              <Step key={idx} completed={s.isCompleted}>
                <StepLabel>
                  <Typography variant="caption" sx={{ color: idx === currentStepIdx ? 'var(--gl-primary)' : 'var(--gl-text-muted)', fontWeight: 800 }}>
                    GEN.{s.generationIndex}
                  </Typography>
                </StepLabel>
              </Step>
            ))}
          </Stepper>

          {/* Active Generation Step Card */}
          {currentStep && (
            <BreedingStepCard
              step={currentStep}
              stepIndex={currentStepIdx}
              totalSteps={activeSession.steps.length}
              isCurrent={true}
              onToggleCenterPlanted={(planted) => updateSessionStepPlanted(currentStepIdx, 'center', planted)}
              onToggleSurroundingPlanted={(planted) => updateSessionStepPlanted(currentStepIdx, 'surrounding', planted)}
              onCompleteStep={() => completeBreedingStep(currentStepIdx)}
            />
          )}
        </DialogContent>

        <DialogActions sx={{ px: 3, pb: 2, justifyContent: 'space-between', borderTop: '1px solid var(--gl-surface)' }}>
          <Button onClick={() => setIsAbandonConfirmOpen(true)} color="error" size="small">
            Exit Session
          </Button>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.72rem' }}>
            Progress is automatically saved in your browser.
          </Typography>
        </DialogActions>
      </Dialog>

      {/* Exit Confirmation */}
      <ConfirmDialog
        open={isAbandonConfirmOpen}
        title="Exit Breeding Session?"
        message="Your active breeding progress will be archived to your history. You can start another session at any time."
        confirmLabel="Exit & Archive"
        isDestructive={false}
        onConfirm={() => {
          setIsAbandonConfirmOpen(false);
          abandonBreedingSession();
        }}
        onCancel={() => setIsAbandonConfirmOpen(false)}
      />

      {/* History Modal */}
      <BreedingHistory open={isHistoryOpen} onClose={() => setIsHistoryOpen(false)} />
    </>
  );
};
