import React from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Typography,
  Stack,
  Button,
  Box,
  IconButton,
  Paper
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import GitHubIcon from '@mui/icons-material/GitHub';
import { useApp } from '../../context/AppContext.tsx';

export const AboutModal: React.FC = () => {
  const { isAboutModalOpen, setIsAboutModalOpen } = useApp();

  return (
    <Dialog
      open={isAboutModalOpen}
      onClose={() => setIsAboutModalOpen(false)}
      maxWidth="xs"
      fullWidth
    >
      <DialogTitle sx={{ m: 0, p: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h6" sx={{ fontWeight: 700 }}>
          About Genetics Lab
        </Typography>
        <IconButton size="small" onClick={() => setIsAboutModalOpen(false)}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>

      <DialogContent dividers sx={{ p: 2.5 }}>
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', textAlign: 'center', gap: 2 }}>
          <Box
            component="img"
            src="./img/items/green-berry.webp"
            alt="Genetics Lab"
            sx={{ width: 64, height: 64, objectFit: 'contain' }}
          />

          <Box>
            <Typography variant="h5" sx={{ fontWeight: 800 }}>
              Genetics Lab
            </Typography>
            <Typography variant="caption" sx={{ color: 'primary.main', fontWeight: 700 }}>
              Rust Plant Genetics &amp; Crossbreeding
            </Typography>
          </Box>

          <Typography variant="body2" sx={{ color: 'text.secondary', lineHeight: 1.6, textAlign: 'left' }}>
            Genetics Lab is a fast, client-side plant genetics calculator and farming companion for Rust. It performs exhaustive multi-generation crossbreeding simulations in Web Workers, scores resulting plant clones, and features offline OCR screen-scanning directly from your game window.
          </Typography>

          <Paper
            variant="outlined"
            sx={{
              p: 1.5,
              width: '100%',
              backgroundColor: 'rgba(96, 205, 255, 0.04)',
              borderRadius: 1.5,
              textAlign: 'left'
            }}
          >
            <Typography variant="caption" sx={{ fontWeight: 700, color: 'text.primary', display: 'block', mb: 0.5 }}>
              Architecture
            </Typography>
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              Built with TypeScript, React, Material UI (MUI v6), Web Workers, and local Tesseract.js.
            </Typography>
          </Paper>
        </Box>
      </DialogContent>

      <DialogActions sx={{ p: 2, justifyContent: 'space-between' }}>
        <Button
          variant="outlined"
          component="a"
          href="https://github.com/JawadYzbk/rust-genetics-lab"
          target="_blank"
          rel="noopener noreferrer"
          startIcon={<GitHubIcon />}
          sx={{ borderColor: 'var(--gl-text-faint)', color: 'var(--gl-text-secondary)', fontSize: '0.8rem' }}
        >
          Contribute
        </Button>
        <Button variant="contained" onClick={() => setIsAboutModalOpen(false)}>
          Close
        </Button>
      </DialogActions>
    </Dialog>
  );
};
