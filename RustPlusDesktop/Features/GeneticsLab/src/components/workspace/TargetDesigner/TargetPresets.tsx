import React, { useState } from 'react';
import { Box, Button, Chip, Menu, MenuItem, Tooltip } from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';

export interface TargetPreset {
  key: string;
  label: string;
  target: string;
  description: string;
}

export const TARGET_PRESETS: TargetPreset[] = [
  { key: 'balanced_3g3y', label: '3G 3Y Balanced', target: 'GGGYYY', description: 'Standard balanced production' },
  { key: 'max_yield_2g4y', label: '2G 4Y Max Yield', target: 'GGYYYY', description: 'Maximum output per harvest' },
  { key: 'max_growth_4g2y', label: '4G 2Y Fast Growth', target: 'GGGGYY', description: 'Fastest harvest turnover' },
  { key: 'speedy_5g1y', label: '5G 1Y Ultra Fast', target: 'GGGGGY', description: 'Extreme speed for rapid clones' },
  { key: 'hardy_3g2y1h', label: '3G 2Y 1H Hardy', target: 'GGGYHY', description: 'Cold & dry environment resistance' },
  { key: 'super_yield_1g5y', label: '1G 5Y Giant Yield', target: 'GYYYYY', description: 'Massive single harvest' }
];

const geneKey = (genes: string) => genes.toUpperCase().split('').sort().join('');

export const TargetPresets: React.FC = () => {
  const { targetConfig, setTargetPreset } = useWorkspace();
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);
  const currentKey = geneKey(targetConfig.targetGenetics);
  const commonPresets = TARGET_PRESETS.slice(0, 3);
  const morePresets = TARGET_PRESETS.slice(3);
  const choose = (preset: TargetPreset) => {
    setTargetPreset(preset.target, 'best-possible');
    setMenuAnchor(null);
  };

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
      {commonPresets.map((preset) => {
        const isSelected = currentKey === geneKey(preset.target);
        return (
          <Tooltip key={preset.key} title={preset.description} arrow>
            <Chip
              label={preset.label.split(' ').slice(0, 2).join(' ')}
              size="small"
              clickable
              onClick={() => choose(preset)}
              sx={{
                height: 28,
                fontWeight: isSelected ? 800 : 700,
                fontSize: '0.75rem',
                backgroundColor: isSelected ? 'rgba(0, 229, 255, 0.15)' : 'var(--gl-panel-header-bg)',
                color: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-secondary)',
                border: '1px solid',
                borderColor: isSelected ? 'var(--gl-primary)' : 'var(--gl-surface)'
              }}
            />
          </Tooltip>
        );
      })}

      <Button
        size="small"
        endIcon={<ExpandMoreIcon sx={{ fontSize: 16 }} />}
        aria-haspopup="menu"
        aria-expanded={Boolean(menuAnchor)}
        onClick={(event) => setMenuAnchor(event.currentTarget)}
        sx={{ minHeight: 28, px: 1, color: 'var(--gl-text-muted)', fontSize: '0.75rem', fontWeight: 800 }}
      >
        More goals
      </Button>
      <Menu anchorEl={menuAnchor} open={Boolean(menuAnchor)} onClose={() => setMenuAnchor(null)}>
        {morePresets.map((preset) => (
          <MenuItem key={preset.key} selected={currentKey === geneKey(preset.target)} onClick={() => choose(preset)}>
            <Box>
              <Box sx={{ fontSize: '0.78rem', fontWeight: 800 }}>{preset.label}</Box>
              <Box sx={{ color: 'text.secondary', fontSize: '0.75rem' }}>{preset.description}</Box>
            </Box>
          </MenuItem>
        ))}
      </Menu>
    </Box>
  );
};
