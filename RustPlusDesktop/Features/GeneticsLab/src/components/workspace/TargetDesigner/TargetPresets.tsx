import React from 'react';
import { Box, Chip } from '@mui/material';
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

export const TargetPresets: React.FC = () => {
  const { targetConfig, setTargetPreset } = useWorkspace();
  const currentTarget = targetConfig.targetGenetics.toUpperCase();

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flexWrap: 'wrap' }}>
      {TARGET_PRESETS.map((preset) => {
        const isSelected = currentTarget === preset.target;
        return (
          <Chip
            key={preset.key}
            label={preset.label}
            size="small"
            clickable
            onClick={() => setTargetPreset(preset.target, 'exact')}
            sx={{
              fontWeight: isSelected ? 800 : 600,
              fontSize: '0.72rem',
              backgroundColor: isSelected ? 'rgba(0, 229, 255, 0.15)' : '#181818',
              color: isSelected ? '#00E5FF' : '#AAAAAA',
              border: '1px solid',
              borderColor: isSelected ? '#00E5FF' : '#2A2A2A',
              transition: 'all 0.15s ease',
              '&:hover': {
                backgroundColor: isSelected ? 'rgba(0, 229, 255, 0.2)' : '#222222',
                borderColor: isSelected ? '#00E5FF' : '#444444',
                color: '#FFFFFF'
              }
            }}
          />
        );
      })}
    </Box>
  );
};
