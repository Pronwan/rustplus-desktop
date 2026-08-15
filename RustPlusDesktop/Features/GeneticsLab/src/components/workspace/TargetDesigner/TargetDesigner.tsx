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
  { value: 'G', label: 'G (Growth)', color: '#4CAF50' },
  { value: 'Y', label: 'Y (Yield)', color: '#4CAF50' },
  { value: 'H', label: 'H (Hardiness)', color: '#4CAF50' },
  { value: 'W', label: 'W (Water)', color: '#E53935' },
  { value: 'X', label: 'X (Empty)', color: '#E53935' },
  { value: '*', label: '? (Any Gene)', color: '#888888' }
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
        backgroundColor: '#141414',
        borderColor: '#262626',
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
              color: '#FFFFFF',
              letterSpacing: '0.5px'
            }}
          >
            TARGET GENETICS
          </Typography>

          <Tooltip title="Click any gene circle below to cycle through genes or choose custom targets." arrow>
            <HelpIcon sx={{ fontSize: 15, color: '#666', cursor: 'help' }} />
          </Tooltip>
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography variant="caption" sx={{ color: '#888', fontWeight: 700 }}>
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
              backgroundColor: '#1C1C1C',
              color: '#00E5FF',
              '& .MuiSelect-select': { py: 0.5, px: 1.2 },
              '& fieldset': { borderColor: '#333' }
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
              color: '#FF9800',
              borderColor: 'rgba(255, 152, 0, 0.4)',
              backgroundColor: 'rgba(255, 152, 0, 0.05)',
              '&:hover': {
                backgroundColor: 'rgba(255, 152, 0, 0.15)',
                borderColor: '#FF9800'
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

        <Typography variant="caption" sx={{ color: '#666666', fontSize: '0.7rem' }}>
          Click any gene to cycle [G → Y → H → W → X → ?]
        </Typography>
      </Box>

      {/* Target Presets Row */}
      <Box sx={{ pt: 1, borderTop: '1px solid #222' }}>
        <Typography variant="caption" sx={{ color: '#666', fontWeight: 800, display: 'block', mb: 0.75 }}>
          QUICK PRESETS:
        </Typography>
        <TargetPresets />
      </Box>

      {/* Missing Clone Advisor Modal */}
      <MissingCloneAdvisor open={isAdvisorOpen} onClose={() => setIsAdvisorOpen(false)} />
    </Paper>
  );
};
