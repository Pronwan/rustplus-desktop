import React, { useState } from 'react';
import {
  Paper,
  Box,
  Typography,
  Select,
  MenuItem,
  Button,
  Menu,
  Tooltip
} from '@mui/material';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import HelpIcon from '@mui/icons-material/Help';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { TargetPresets } from './TargetPresets.tsx';
import { MissingCloneAdvisor } from './MissingCloneAdvisor.tsx';

const GENE_OPTIONS = [
  { value: 'G', label: 'G (Growth)', color: 'var(--gl-success)' },
  { value: 'Y', label: 'Y (Yield)', color: 'var(--gl-success)' },
  { value: 'H', label: 'H (Hardiness)', color: 'var(--gl-success)' },
  { value: 'W', label: 'W (Water)', color: 'var(--gl-error)' },
  { value: 'X', label: 'X (Empty)', color: 'var(--gl-error)' },
  { value: '*', label: '? (Any Gene)', color: 'var(--gl-text-muted)' }
];

export const TargetDesigner: React.FC = () => {
  const { targetConfig, setTargetConfig, setTargetSlot } = useWorkspace();

  const [activeSlotIdx, setActiveSlotIdx] = useState<number | null>(null);
  const [slotMenuAnchor, setSlotMenuAnchor] = useState<null | HTMLElement>(null);
  const [isAdvisorOpen, setIsAdvisorOpen] = useState(false);

  const handleSlotClick = (idx: number, e?: React.MouseEvent<HTMLElement>) => {
    setActiveSlotIdx(idx);
    if (e) {
      setSlotMenuAnchor(e.currentTarget);
    }
  };

  const handleSelectGene = (gene: string) => {
    if (activeSlotIdx !== null) {
      setTargetSlot(activeSlotIdx, gene);
    }
    setSlotMenuAnchor(null);
    setActiveSlotIdx(null);
  };

  // Quick cycle on direct slot click
  const handleCycleSlot = (idx: number) => {
    const cycleOrder = ['G', 'Y', 'H', 'W', 'X', '*'];
    const current = targetConfig.targetGenetics[idx] || '*';
    const nextIdx = (cycleOrder.indexOf(current) + 1) % cycleOrder.length;
    setTargetSlot(idx, cycleOrder[nextIdx]);
  };

  return (
    <Paper
      variant="outlined"
      sx={{
        backgroundColor: 'var(--gl-panel-bg)',
        borderColor: 'var(--gl-surface)',
        borderRadius: '6px',
        p: 2,
        mb: 2.5
      }}
    >
      {/* Header Row: Title & Matching Mode */}
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1.5, flexWrap: 'wrap', gap: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography
            variant="subtitle1"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.9rem',
              color: 'var(--gl-text-primary)',
              letterSpacing: '0.5px'
            }}
          >
            TARGET GENETICS
          </Typography>

          <Tooltip title="Click any gene circle below to cycle through genes or choose custom targets." arrow>
            <HelpIcon sx={{ fontSize: 15, color: 'var(--gl-text-muted)', cursor: 'help' }} />
          </Tooltip>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700 }}>
            Match Mode:
          </Typography>
          <Select
            size="small"
            value={targetConfig.matchMode}
            onChange={(e) => setTargetConfig(prev => ({ ...prev, matchMode: e.target.value as any }))}
            sx={{
              height: 28,
              fontSize: '0.75rem',
              fontWeight: 700,
              backgroundColor: 'var(--gl-card-hover-bg)',
              color: 'var(--gl-primary)',
              '& .MuiSelect-select': { py: 0.5, px: 1.2 },
              '& fieldset': { borderColor: 'var(--gl-surface-hover)' }
            }}
          >
            <MenuItem value="exact" sx={{ fontSize: '0.75rem' }}>Exact Target</MenuItem>
            <MenuItem value="at-least" sx={{ fontSize: '0.75rem' }}>At Least Target</MenuItem>
            <MenuItem value="best-possible" sx={{ fontSize: '0.75rem' }}>Best Possible</MenuItem>
          </Select>

          <Button
            size="small"
            variant="outlined"
            onClick={() => setIsAdvisorOpen(true)}
            startIcon={<AutoAwesomeIcon sx={{ fontSize: 14 }} />}
            sx={{
              height: 28,
              fontSize: '0.72rem',
              fontWeight: 800,
              color: 'var(--gl-warning)',
              borderColor: 'rgba(255, 152, 0, 0.4)',
              backgroundColor: 'rgba(255, 152, 0, 0.05)',
              '&:hover': {
                backgroundColor: 'rgba(255, 152, 0, 0.15)',
                borderColor: 'var(--gl-warning)'
              }
            }}
          >
            Missing Clones?
          </Button>
        </Box>
      </Box>

      {/* Center Interactive Target Slots */}
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', my: 1.5, gap: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <GeneticsSequence
            genes={targetConfig.targetGenetics}
            size="xlarge"
            showSlotNumbers={true}
            showConnectors={true}
            interactive={true}
            onGeneClick={(idx) => handleCycleSlot(idx)}
          />
        </Box>

        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.7rem' }}>
          Click any gene to cycle [G → Y → H → W → X → ?]
        </Typography>
      </Box>

      {/* Target Presets Row */}
      <Box sx={{ pt: 1, borderTop: '1px solid var(--gl-surface)' }}>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 800, display: 'block', mb: 0.75 }}>
          QUICK PRESETS:
        </Typography>
        <TargetPresets />
      </Box>

      {/* Missing Clone Advisor Modal */}
      <MissingCloneAdvisor open={isAdvisorOpen} onClose={() => setIsAdvisorOpen(false)} />
    </Paper>
  );
};
