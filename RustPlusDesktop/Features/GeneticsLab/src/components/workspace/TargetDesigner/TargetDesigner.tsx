import React, { useEffect, useState } from 'react';
import {
  Paper,
  Box,
  Typography,
  Select,
  MenuItem,
  Button,
  Tooltip,
  TextField,
  Collapse
} from '@mui/material';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import HelpIcon from '@mui/icons-material/Help';
import TuneIcon from '@mui/icons-material/Tune';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { TargetPresets } from './TargetPresets.tsx';
import { MissingCloneAdvisor } from './MissingCloneAdvisor.tsx';

export const TargetDesigner: React.FC = () => {
  const { targetConfig, setTargetConfig, setTargetSlot } = useWorkspace();
  const [isAdvancedOpen, setIsAdvancedOpen] = useState(false);
  const [isAdvisorOpen, setIsAdvisorOpen] = useState(false);
  const [targetInput, setTargetInput] = useState(targetConfig.targetGenetics);

  useEffect(() => {
    const stripped = targetConfig.targetGenetics.replace(/\*+$/, '');
    setTargetInput(prev => (prev.replace(/\*+$/, '') === stripped ? prev : stripped));
  }, [targetConfig.targetGenetics]);

  const handleTargetInputChange = (raw: string) => {
    const sanitized = raw
      .toUpperCase()
      .replace(/\?/g, '*')
      .replace(/[^GYHWX*]/g, '')
      .slice(0, 6);
    setTargetInput(sanitized);
    setTargetConfig(prev => ({ ...prev, targetGenetics: sanitized.padEnd(6, '*') }));
  };

  const handleCycleSlot = (idx: number) => {
    const cycleOrder = ['G', 'Y', 'H', 'W', 'X', '*'];
    const current = targetConfig.targetGenetics[idx] || '*';
    setTargetSlot(idx, cycleOrder[(cycleOrder.indexOf(current) + 1) % cycleOrder.length]);
  };

  return (
    <Paper
      variant="outlined"
      sx={{
        backgroundColor: 'var(--gl-panel-bg)',
        borderColor: 'var(--gl-surface)',
        borderRadius: '6px',
        p: 1.5,
        mb: 2
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, flexWrap: 'wrap' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, minWidth: { xs: '100%', md: 170 } }}>
          <Typography sx={{ fontWeight: 900, fontFamily: '"Roboto Mono", monospace', fontSize: '0.8rem', color: 'var(--gl-text-primary)' }}>
            BREEDING GOAL
          </Typography>
          <Tooltip title="Choose a common goal, or open Advanced for an exact six-slot target and match mode." arrow>
            <HelpIcon sx={{ fontSize: 16, color: 'var(--gl-text-muted)', cursor: 'help' }} />
          </Tooltip>
        </Box>

        <GeneticsSequence genes={targetConfig.targetGenetics} size="medium" showConnectors={true} />
        <Box sx={{ flex: 1, minWidth: 220 }}>
          <TargetPresets />
        </Box>
        <Button
          size="small"
          variant="outlined"
          startIcon={<TuneIcon sx={{ fontSize: 16 }} />}
          aria-expanded={isAdvancedOpen}
          aria-controls="advanced-target-controls"
          onClick={() => setIsAdvancedOpen(open => !open)}
          sx={{ minHeight: 32, color: 'var(--gl-text-secondary)', borderColor: 'var(--gl-surface-hover)', fontSize: '0.75rem', fontWeight: 800 }}
        >
          Advanced
        </Button>
      </Box>

      <Collapse in={isAdvancedOpen} unmountOnExit>
        <Box id="advanced-target-controls" sx={{ mt: 1.5, pt: 1.5, borderTop: '1px solid var(--gl-surface)', display: 'flex', flexDirection: 'column', gap: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1.5, flexWrap: 'wrap' }}>
            <Box>
              <Typography sx={{ color: 'var(--gl-text-primary)', fontSize: '0.78rem', fontWeight: 800 }}>
                Exact six-slot target
              </Typography>
              <Typography sx={{ color: 'var(--gl-text-muted)', fontSize: '0.75rem' }}>
                Select a gene to cycle G → Y → H → W → X → Any.
              </Typography>
            </Box>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Select
                inputProps={{ 'aria-label': 'Target match mode' }}
                size="small"
                value={targetConfig.matchMode}
                onChange={(event) => setTargetConfig(prev => ({ ...prev, matchMode: event.target.value as typeof prev.matchMode }))}
                sx={{ height: 32, minWidth: 130, fontSize: '0.75rem', fontWeight: 700, backgroundColor: 'var(--gl-card-hover-bg)', color: 'var(--gl-primary)', '& fieldset': { borderColor: 'var(--gl-surface-hover)' } }}
              >
                <MenuItem value="exact" sx={{ fontSize: '0.75rem' }}>Exact target</MenuItem>
                <MenuItem value="at-least" sx={{ fontSize: '0.75rem' }}>At least target</MenuItem>
                <MenuItem value="best-possible" sx={{ fontSize: '0.75rem' }}>Best possible</MenuItem>
              </Select>
              <Button
                size="small"
                variant="outlined"
                onClick={() => setIsAdvisorOpen(true)}
                startIcon={<AutoAwesomeIcon sx={{ fontSize: 16 }} />}
                sx={{ minHeight: 32, fontSize: '0.75rem', fontWeight: 800, color: 'var(--gl-warning)', borderColor: 'rgba(255, 152, 0, 0.45)' }}
              >
                Clone advisor
              </Button>
            </Box>
          </Box>

          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 1.5, flexWrap: 'wrap' }}>
            <GeneticsSequence
              genes={targetConfig.targetGenetics}
              size="large"
              showSlotNumbers={true}
              showConnectors={true}
              interactive={true}
              onGeneClick={handleCycleSlot}
            />
            <TextField
              value={targetInput}
              onChange={(event) => handleTargetInputChange(event.target.value)}
              placeholder="GGGYYY"
              spellCheck={false}
              slotProps={{ htmlInput: { 'aria-label': 'Target genetics', maxLength: 6, style: { textTransform: 'uppercase', letterSpacing: '0.3em', textAlign: 'center', fontWeight: 800 } } }}
              sx={{
                width: 150,
                '& .MuiInputBase-root': { height: 36, backgroundColor: 'var(--gl-input-bg)', color: 'var(--gl-text-primary)', fontFamily: '"Roboto Mono", monospace', fontSize: '0.9rem' },
                '& fieldset': { borderColor: 'var(--gl-surface-hover)' }
              }}
            />
          </Box>
        </Box>
      </Collapse>

      <MissingCloneAdvisor open={isAdvisorOpen} onClose={() => setIsAdvisorOpen(false)} />
    </Paper>
  );
};
