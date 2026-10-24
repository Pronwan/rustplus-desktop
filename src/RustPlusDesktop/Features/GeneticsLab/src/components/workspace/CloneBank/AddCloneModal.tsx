import React, { useState } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  TextField,
  Box,
  Typography,
  Tabs,
  Tab,
  IconButton
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { Sapling } from '../../../domain/genetics/Sapling.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';

interface AddCloneModalProps {
  open: boolean;
  onClose: () => void;
}

export const AddCloneModal: React.FC<AddCloneModalProps> = ({ open, onClose }) => {
  const { addClone, addBatchClones, selectedPlant } = useWorkspace();

  const [tab, setTab] = useState<'single' | 'batch'>('single');
  const [singleGenes, setSingleGenes] = useState('');
  const [singleName, setSingleName] = useState('');
  const [singleQuantity, setSingleQuantity] = useState(1);
  const [batchText, setBatchText] = useState('');

  const cleanSingle = singleGenes.toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6);
  const isSingleValid = cleanSingle.length === 6 && Sapling.isValidGeneString(cleanSingle);

  const batchCount = (batchText.toUpperCase().match(/[GHYWX]{6}/g) || []).filter(g => Sapling.isValidGeneString(g)).length;

  const handleAddSingle = () => {
    if (!isSingleValid) return;
    addClone(cleanSingle, {
      name: singleName.trim() || undefined,
      quantity: singleQuantity,
      source: 'manual'
    });
    setSingleGenes('');
    setSingleName('');
    setSingleQuantity(1);
    onClose();
  };

  const handleAddBatch = () => {
    const tokens = (batchText.toUpperCase().match(/[GHYWX]{6}/g) || []).filter(g => Sapling.isValidGeneString(g));
    if (tokens.length > 0) {
      addBatchClones(tokens, 'manual');
      setBatchText('');
      onClose();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="xs"
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
          Add Clones to {selectedPlant.replace(/-/g, ' ').toUpperCase()}
        </Typography>
        <IconButton aria-label="Close add clone dialog" size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </DialogTitle>

      <Box sx={{ borderBottom: '1px solid var(--gl-border)', px: 3 }}>
        <Tabs
          value={tab}
          onChange={(_, val) => setTab(val)}
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)', height: 2 } }}
        >
          <Tab value="single" label="Single Clone" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
          <Tab value="batch" label="Batch Paste" sx={{ minHeight: 36, py: 0.5, fontSize: '0.78rem', fontWeight: 700 }} />
        </Tabs>
      </Box>

      <DialogContent sx={{ pt: 2.5 }}>
        {tab === 'single' ? (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            <Box>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, mb: 0.5, display: 'block' }}>
                Genetics (6 letters: G, Y, H, W, X)
              </Typography>
              <TextField
                aria-label="Clone genetics"
                size="small"
                fullWidth
                placeholder="e.g. GGYGYX"
                value={cleanSingle}
                onChange={(e) => setSingleGenes(e.target.value)}
                slotProps={{ htmlInput: { maxLength: 6, style: { fontFamily: 'monospace', fontWeight: 800, letterSpacing: 2 } } }}
                autoFocus
              />
            </Box>

            {cleanSingle.length > 0 && (
              <Box sx={{ display: 'flex', justifyContent: 'center', my: 0.5 }}>
                <GeneticsSequence genes={cleanSingle} size="medium" showSlotNumbers={true} />
              </Box>
            )}

            <Box sx={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: 1.5 }}>
              <Box>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, mb: 0.5, display: 'block' }}>
                  Label / Name (Optional)
                </Typography>
                <TextField
                  aria-label="Clone name"
                  size="small"
                  fullWidth
                  placeholder="e.g. Donor #1"
                  value={singleName}
                  onChange={(e) => setSingleName(e.target.value)}
                />
              </Box>

              <Box>
                <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, mb: 0.5, display: 'block' }}>
                  Quantity
                </Typography>
                <TextField
                  aria-label="Clone quantity"
                  size="small"
                  type="number"
                  fullWidth
                  value={singleQuantity}
                  onChange={(e) => setSingleQuantity(Math.max(1, parseInt(e.target.value, 10) || 1))}
                  slotProps={{ htmlInput: { min: 1 } }}
                />
              </Box>
            </Box>
          </Box>
        ) : (
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.5 }}>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
              Paste multiple 6-gene lines from chat, notes, or previous exports:
            </Typography>
            <TextField
              aria-label="Clone genetics batch, one sequence per line"
              multiline
              rows={6}
              fullWidth
              placeholder={`GGYGYX\nYYGGGX\nGHGYHW`}
              value={batchText}
              onChange={(e) => setBatchText(e.target.value)}
              slotProps={{ htmlInput: { style: { fontFamily: 'monospace', fontSize: '0.85rem' } } }}
              autoFocus
            />
            <Typography variant="caption" sx={{ color: batchCount > 0 ? 'var(--gl-primary)' : 'var(--gl-text-muted)', fontWeight: 700 }}>
              {batchCount} valid clone{batchCount === 1 ? '' : 's'} detected
            </Typography>
          </Box>
        )}
      </DialogContent>

      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onClose} color="inherit" size="small">
          Cancel
        </Button>
        <Button
          onClick={tab === 'single' ? handleAddSingle : handleAddBatch}
          variant="contained"
          size="small"
          disabled={tab === 'single' ? !isSingleValid : batchCount === 0}
        >
          {tab === 'single' ? 'Add Clone' : `Add ${batchCount} Clones`}
        </Button>
      </DialogActions>
    </Dialog>
  );
};
