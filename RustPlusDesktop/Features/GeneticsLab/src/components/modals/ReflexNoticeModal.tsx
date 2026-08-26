import React, { useState, useEffect } from 'react';
import {
  Dialog,
  DialogTitle,
  DialogContent,
  DialogActions,
  Typography,
  Box,
  Button,
  IconButton,
  Checkbox,
  FormControlLabel,
  Tooltip,
  Paper
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import CheckIcon from '@mui/icons-material/Check';
import SpeedIcon from '@mui/icons-material/Speed';
import FlashOnIcon from '@mui/icons-material/FlashOn';
import TuneIcon from '@mui/icons-material/Tune';

const STORAGE_KEY = 'genetics_reflex_notice_dismissed';
const REFLEX_COMMAND = 'graphics.reflexmode 2';

export const ReflexNoticeModal: React.FC<{
  open?: boolean;
  onClose?: () => void;
}> = ({ open: externalOpen, onClose: externalClose }) => {
  const [internalOpen, setInternalOpen] = useState(false);
  const [copied, setCopied] = useState(false);
  const [dontShowAgain, setDontShowAgain] = useState(true);

  useEffect(() => {
    // If not controlled externally, check localStorage for one-time display
    if (externalOpen === undefined) {
      try {
        const dismissed = localStorage.getItem(STORAGE_KEY);
        if (!dismissed) {
          // Show automatically on first visit after a slight delay
          const timer = setTimeout(() => setInternalOpen(true), 600);
          return () => clearTimeout(timer);
        }
      } catch {
        // Tolerant to storage restrictions
      }
    }
  }, [externalOpen]);

  const isOpen = externalOpen !== undefined ? externalOpen : internalOpen;

  const handleClose = () => {
    if (dontShowAgain) {
      try {
        localStorage.setItem(STORAGE_KEY, 'true');
      } catch { }
    }
    if (externalClose) {
      externalClose();
    } else {
      setInternalOpen(false);
    }
  };

  const handleCopyCommand = async () => {
    try {
      await navigator.clipboard.writeText(REFLEX_COMMAND);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Fallback
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  return (
    <Dialog
      open={isOpen}
      onClose={handleClose}
      maxWidth="sm"
      fullWidth
      slotProps={{
        paper: {
          sx: {
            backgroundColor: '#111827',
            border: '1px solid rgba(245, 158, 11, 0.4)',
            borderRadius: '8px',
            color: '#F3F4F6',
            boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.7), 0 10px 10px -5px rgba(0, 0, 0, 0.5)'
          }
        }
      }}
    >
      <DialogTitle
        sx={{
          m: 0,
          p: '18px 20px 12px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          borderBottom: '1px solid rgba(255, 255, 255, 0.08)'
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.2 }}>
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: 32,
              height: 32,
              borderRadius: '6px',
              backgroundColor: 'rgba(245, 158, 11, 0.15)',
              color: '#F59E0B'
            }}
          >
            <FlashOnIcon fontSize="small" />
          </Box>
          <Box>
            <Typography variant="subtitle1" sx={{ fontWeight: 800, fontFamily: '"Roboto Mono", monospace', lineHeight: 1.2 }}>
              Important: Scanner &amp; Game Performance
            </Typography>
            <Typography variant="caption" sx={{ color: '#9CA3AF', fontFamily: '"Roboto Mono", monospace', fontSize: '0.72rem' }}>
              Prevent background starvation &amp; ensure real-time plant recognition
            </Typography>
          </Box>
        </Box>
        <IconButton size="small" onClick={handleClose} sx={{ color: '#9CA3AF', '&:hover': { color: '#FFF' } }}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>

      <DialogContent sx={{ p: '18px 20px', display: 'flex', flexDirection: 'column', gap: 2 }}>
        {/* Explanation */}
        <Typography variant="body2" sx={{ color: '#D1D5DB', fontSize: '0.85rem', lineHeight: 1.55 }}>
          When running Rust, high GPU workloads can starve background screen capture and plant OCR threads. To guarantee instant gene recognition without frame drops or delays, please configure NVIDIA Reflex:
        </Typography>

        {/* Setting Card: Graphic Menu */}
        <Paper
          elevation={0}
          sx={{
            p: 1.8,
            backgroundColor: '#1F2937',
            border: '1px solid rgba(255, 255, 255, 0.1)',
            borderRadius: '6px',
            display: 'flex',
            flexDirection: 'column',
            gap: 1
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <TuneIcon sx={{ fontSize: 18, color: '#60A5FA' }} />
            <Typography variant="body2" sx={{ fontWeight: 700, color: '#F9FAFB', fontFamily: '"Roboto Mono", monospace' }}>
              Method 1: In-Game Graphics Settings
            </Typography>
          </Box>
          <Typography variant="caption" sx={{ color: '#9CA3AF', lineHeight: 1.4 }}>
            In Rust, go to <strong>Options &rarr; Graphics</strong> and set:
          </Typography>
          <Box
            component="img"
            src="./img/Nvidia-Reflex.png"
            alt="NVIDIA Reflex Mode : ON + BOOST"
            sx={{
              width: '100%',
              height: 'auto',
              borderRadius: '4px',
              border: '1px solid rgba(255, 255, 255, 0.15)',
              mt: 0.5,
              objectFit: 'contain'
            }}
            onError={(e) => { (e.target as HTMLElement).style.display = 'none'; }}
          />
        </Paper>

        {/* Setting Card: Console Command */}
        <Paper
          elevation={0}
          sx={{
            p: 1.8,
            backgroundColor: '#1F2937',
            border: '1px solid rgba(255, 255, 255, 0.1)',
            borderRadius: '6px',
            display: 'flex',
            flexDirection: 'column',
            gap: 1
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
            <SpeedIcon sx={{ fontSize: 18, color: '#34D399' }} />
            <Typography variant="body2" sx={{ fontWeight: 700, color: '#F9FAFB', fontFamily: '"Roboto Mono", monospace' }}>
              Method 2: Rust Console Command (F1)
            </Typography>
          </Box>
          <Typography variant="caption" sx={{ color: '#9CA3AF', lineHeight: 1.4 }}>
            Press <strong>F1</strong> in Rust and paste the following command:
          </Typography>
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              backgroundColor: '#0B0F19',
              border: '1px solid #374151',
              borderRadius: '4px',
              p: '6px 10px',
              mt: 0.5
            }}
          >
            <Typography
              component="code"
              sx={{
                fontFamily: 'monospace',
                fontWeight: 700,
                color: '#38BDF8',
                fontSize: '0.85rem'
              }}
            >
              {REFLEX_COMMAND}
            </Typography>
            <Tooltip title={copied ? 'Copied to Clipboard!' : 'Click to copy'}>
              <Button
                size="small"
                variant="outlined"
                onClick={handleCopyCommand}
                startIcon={copied ? <CheckIcon fontSize="small" /> : <ContentCopyIcon fontSize="small" />}
                sx={{
                  py: 0.2,
                  px: 1,
                  fontSize: '0.72rem',
                  fontWeight: 700,
                  color: copied ? '#34D399' : '#9CA3AF',
                  borderColor: copied ? '#34D399' : '#4B5563',
                  '&:hover': {
                    borderColor: '#38BDF8',
                    color: '#38BDF8',
                    backgroundColor: 'rgba(56, 189, 248, 0.05)'
                  }
                }}
              >
                {copied ? 'COPIED' : 'COPY'}
              </Button>
            </Tooltip>
          </Box>
        </Paper>

        {/* Extra Pro Tip */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, px: 0.5 }}>
          <Typography variant="caption" sx={{ color: '#9CA3AF', fontSize: '0.75rem', lineHeight: 1.4 }}>
            💡 <em>Pro Tip:</em> Running Rust in <strong>Borderless/Windowed</strong> mode with <strong>fps.limit 60</strong> while scanning guarantees smooth, instant recognition.
          </Typography>
        </Box>
      </DialogContent>

      <DialogActions
        sx={{
          p: '12px 20px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          borderTop: '1px solid rgba(255, 255, 255, 0.08)'
        }}
      >
        <FormControlLabel
          control={
            <Checkbox
              checked={dontShowAgain}
              onChange={(e) => setDontShowAgain(e.target.checked)}
              size="small"
              sx={{ color: '#6B7280', '&.Mui-checked': { color: '#F59E0B' } }}
            />
          }
          label={
            <Typography variant="caption" sx={{ color: '#9CA3AF', fontSize: '0.75rem', userSelect: 'none' }}>
              Don't show this notice again
            </Typography>
          }
        />

        <Button
          variant="contained"
          onClick={handleClose}
          sx={{
            backgroundColor: '#F59E0B',
            color: '#000000',
            fontWeight: 800,
            fontFamily: '"Roboto Mono", monospace',
            fontSize: '0.8rem',
            px: 2.5,
            py: 0.6,
            '&:hover': { backgroundColor: '#D97706' }
          }}
        >
          GOT IT
        </Button>
      </DialogActions>
    </Dialog>
  );
};
