import React from 'react';
import {
  Paper,
  Box,
  Typography,
  Checkbox,
  FormControlLabel,
  Button,
  Chip
} from '@mui/material';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { BreedingSessionStep } from '../../services/storageService.ts';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

interface BreedingStepCardProps {
  step: BreedingSessionStep;
  stepIndex: number;
  totalSteps: number;
  isCurrent: boolean;
  onToggleCenterPlanted: (planted: boolean) => void;
  onToggleSurroundingPlanted: (planted: boolean) => void;
  onCompleteStep: () => void;
}

export const BreedingStepCard: React.FC<BreedingStepCardProps> = ({
  step,
  stepIndex,
  totalSteps,
  isCurrent,
  onToggleCenterPlanted,
  onToggleSurroundingPlanted,
  onCompleteStep
}) => {
  const isReadyToComplete = (step.centerSaplingString ? step.isCenterPlanted : true) && step.isSurroundingPlanted;

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2.5,
        backgroundColor: step.isCompleted ? '#141E16' : isCurrent ? '#181818' : '#121212',
        border: '1.5px solid',
        borderColor: step.isCompleted ? '#4CAF50' : isCurrent ? '#00E5FF' : '#282828',
        borderRadius: '6px',
        display: 'flex',
        flexDirection: 'column',
        gap: 2,
        opacity: isCurrent || step.isCompleted ? 1 : 0.6
      }}
    >
      {/* Header */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Chip
            size="small"
            label={step.isCompleted ? '✓ COMPLETED' : isCurrent ? `CURRENT · STEP ${stepIndex + 1}/${totalSteps}` : `STEP ${stepIndex + 1}/${totalSteps}`}
            sx={{
              fontWeight: 800,
              fontSize: '0.72rem',
              backgroundColor: step.isCompleted ? 'rgba(76, 175, 80, 0.2)' : isCurrent ? 'rgba(0, 229, 255, 0.2)' : '#202020',
              color: step.isCompleted ? '#4CAF50' : isCurrent ? '#00E5FF' : '#888',
              border: '1px solid',
              borderColor: step.isCompleted ? '#4CAF50' : isCurrent ? '#00E5FF' : '#333'
            }}
          />

          <Typography variant="caption" sx={{ color: '#AAAAAA', fontWeight: 700 }}>
            GEN.{step.generationIndex} Crossbreed
          </Typography>
        </Box>

        <Typography variant="caption" sx={{ color: '#4CAF50', fontWeight: 800, fontFamily: 'monospace' }}>
          {(step.chance * 100).toFixed(0)}% Probability
        </Typography>
      </Box>

      {/* Target Outcome of this Step */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', p: 1.5, backgroundColor: '#141414', border: '1px solid #222', borderRadius: '4px' }}>
        <Typography variant="caption" sx={{ color: '#888', fontWeight: 700 }}>
          STEP RESULT:
        </Typography>
        <GeneticsSequence genes={step.targetGeneString} size="medium" showConnectors={true} />
      </Box>

      {/* Planting Instructions */}
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
        {/* Step 1: Center plant */}
        {step.centerSaplingString && (
          <Box sx={{ p: 1.25, backgroundColor: '#1A1A1A', border: '1px solid #2A2A2A', borderRadius: '4px' }}>
            <FormControlLabel
              control={
                <Checkbox
                  checked={step.isCenterPlanted}
                  onChange={(e) => onToggleCenterPlanted(e.target.checked)}
                  disabled={!isCurrent}
                  color="primary"
                />
              }
              label={
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                  <Typography variant="body2" sx={{ fontWeight: 700, color: '#E0E0E0' }}>
                    1. Plant <strong>CENTER</strong> plant:
                  </Typography>
                  <GeneticsSequence genes={step.centerSaplingString} size="small" />
                </Box>
              }
            />
          </Box>
        )}

        {/* Step 2: Surrounding plants */}
        <Box sx={{ p: 1.25, backgroundColor: '#1A1A1A', border: '1px solid #2A2A2A', borderRadius: '4px' }}>
          <FormControlLabel
            control={
              <Checkbox
                checked={step.isSurroundingPlanted}
                onChange={(e) => onToggleSurroundingPlanted(e.target.checked)}
                disabled={!isCurrent}
                color="primary"
              />
            }
            label={
              <Typography variant="body2" sx={{ fontWeight: 700, color: '#E0E0E0' }}>
                2. Plant <strong>SURROUNDING</strong> clones ({step.surroundingSaplingsStrings.length} cuttings):
              </Typography>
            }
          />

          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5, pl: 4, pt: 1 }}>
            {step.surroundingSaplingsStrings.map((genes, pIdx) => {
              const isFirst = step.priorityWinningIndices?.includes(pIdx);
              const isSecond = step.priorityLosingIndices?.includes(pIdx);

              return (
                <Box key={pIdx} sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  <Typography variant="caption" sx={{ color: '#888', minWidth: 16 }}>
                    #{pIdx + 1}
                  </Typography>
                  <GeneticsSequence genes={genes} size="small" />
                  {isFirst && (
                    <Typography variant="caption" sx={{ color: '#4CAF50', fontWeight: 800, fontFamily: 'monospace' }}>
                      (1st placement)
                    </Typography>
                  )}
                  {isSecond && (
                    <Typography variant="caption" sx={{ color: '#E53935', fontWeight: 800, fontFamily: 'monospace' }}>
                      (2nd placement)
                    </Typography>
                  )}
                </Box>
              );
            })}
          </Box>
        </Box>

        <Typography variant="caption" sx={{ color: '#888', fontStyle: 'italic', pl: 1 }}>
          3. Wait until all plants reach <strong>Crossbreeding</strong> stage in Rust, then take cuttings from the center plant!
        </Typography>
      </Box>

      {/* Action Button */}
      {isCurrent && !step.isCompleted && (
        <Button
          variant="contained"
          color="success"
          size="medium"
          onClick={onCompleteStep}
          disabled={!isReadyToComplete}
          startIcon={<CheckCircleIcon sx={{ fontSize: 18 }} />}
          sx={{ fontWeight: 800, py: 1 }}
        >
          {stepIndex + 1 === totalSteps ? 'COMPLETE BREEDING ROUTE' : 'ADVANCE TO NEXT GENERATION'}
        </Button>
      )}
    </Paper>
  );
};
