import React, { useState, useEffect } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Box,
  Typography,
  TextField
} from '@mui/material';
import { useScanner } from '../../context/ScannerContext.tsx';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

export const GeneCorrectionModal: React.FC = () => {
  const { correctionCandidate, setCorrectionCandidate } = useScanner();
  const { addClone } = useWorkspace();

  const [editGenes, setEditGenes] = useState('');

  useEffect(() => {
    if (correctionCandidate) {
      setEditGenes(correctionCandidate.genes);
    }
  }, [correctionCandidate]);

  if (!correctionCandidate) return null;

  const clean = editGenes.toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);
  const isValid = clean.length === 6 && Sapling.isValidGeneString(clean);

  const handleConfirm = () => {
    if (!isValid) return;
    addClone(clean, { source: 'scanner' });
    setCorrectionCandidate(null);
  };

  const handleDismiss = () => {
    setCorrectionCandidate(null);
  };

  return (
    <Dialog
      open={true}
      onClose={handleDismiss}
      maxWidth="xs"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: '#161616',
            border: '1.5px solid #FFA726',
            borderRadius: '6px',
            color: '#E0E0E0'
          }
        }
      }}
    >
      <DialogTitle sx={{ pb: 1, fontWeight: 800, color: '#FFA726', fontSize: '1rem' }}>
        Scanner Verification Needed
      </DialogTitle>

      <DialogContent sx={{ pt: 1.5, display: 'flex', flexDirection: 'column', gap: 2 }}>
        <Typography variant="caption" sx={{ color: '#AAAAAA' }}>
          The scanner detected an uncertain character ({correctionCandidate.confidence}% confidence). Please verify or correct the 6 genes:
        </Typography>

        <TextField
          size="small"
          fullWidth
          value={clean}
          onChange={(e) => setEditGenes(e.target.value)}
          slotProps={{ htmlInput: { maxLength: 6, style: { fontFamily: 'monospace', fontWeight: 800, letterSpacing: 2, fontSize: '1.1rem', textAlign: 'center' } } }}
          autoFocus
        />

        {clean.length > 0 && (
          <Box sx={{ display: 'flex', justifyContent: 'center' }}>
            <GeneticsSequence genes={clean} size="medium" showSlotNumbers={true} />
          </Box>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={handleDismiss} color="inherit" size="small">
          Ignore
        </Button>
        <Button onClick={handleConfirm} variant="contained" color="primary" size="small" disabled={!isValid}>
          Add to Bank
        </Button>
      </DialogActions>
    </Dialog>
  );
};
