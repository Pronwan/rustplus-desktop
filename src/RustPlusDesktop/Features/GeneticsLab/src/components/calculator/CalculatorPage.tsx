import React from 'react';
import { Box } from '@mui/material';
import { GeneInputs } from './GeneInputs.tsx';
import { ResultsPanel } from './ResultsPanel.tsx';

export const CalculatorPage: React.FC = () => {
  return (
    <Box
      sx={{
        maxWidth: 1400,
        mx: 'auto',
        display: 'grid',
        gridTemplateColumns: { xs: '1fr', lg: '380px 1fr' },
        gap: { xs: 3, lg: 6 },
        alignItems: 'start'
      }}
    >
      {/* Left Action & Inputs Column */}
      <Box sx={{ width: '100%' }}>
        <GeneInputs />
      </Box>

      {/* Right Filters & Results Column */}
      <Box sx={{ width: '100%' }}>
        <ResultsPanel />
      </Box>
    </Box>
  );
};
