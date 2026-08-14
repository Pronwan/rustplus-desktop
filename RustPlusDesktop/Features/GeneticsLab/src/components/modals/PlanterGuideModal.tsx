import React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  IconButton,
  Typography,
  Box
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { PlanterVisual } from '../guide/PlanterVisual.tsx';
import { SaplingGeneRepr } from '../common/SaplingGeneRepr.tsx';

interface PlanterGuideModalProps {
  open: boolean;
  onClose: () => void;
  map?: GeneticsMap | null;
}

export const PlanterGuideModal: React.FC<PlanterGuideModalProps> = ({
  open,
  onClose,
  map
}) => {
  if (!open || !map) return null;

  const targetSapling = map.resultSapling;

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
            border: '1px solid #282828',
            borderRadius: '6px',
            color: '#E0E0E0',
            p: 2
          }
        }
      }}
    >
      <DialogTitle
        sx={{
          m: 0,
          p: '8px 12px 16px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between'
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Typography
            variant="h6"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              color: '#FFFFFF',
              fontSize: '1.05rem'
            }}
          >
            Planter Guide
          </Typography>
          <SaplingGeneRepr sapling={targetSapling} size="small" showConnectors={true} />
        </Box>

        <IconButton
          onClick={onClose}
          size="small"
          sx={{ color: '#888888', '&:hover': { color: '#FFFFFF' } }}
        >
          <CloseIcon sx={{ fontSize: 20 }} />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ p: 0 }}>
        <PlanterVisual map={map} />
      </DialogContent>
    </Dialog>
  );
};
