import React, { useState } from 'react';
import {
  Paper,
  Box,
  Typography,
  Select,
  MenuItem,
  TextField,
  FormControlLabel,
  Switch,
  Divider,
  Chip,
  Button
} from '@mui/material';
import SpaIcon from '@mui/icons-material/Spa';
import AccessTimeIcon from '@mui/icons-material/AccessTime';
import WaterDropIcon from '@mui/icons-material/WaterDrop';
import Inventory2Icon from '@mui/icons-material/Inventory2';
import { CROP_GROWTH_DATABASE, estimateFarmOutput } from '../../domain/planner/cropGrowthData.ts';
import { useWorkspace } from '../../context/WorkspaceContext.tsx';
import { GeneticsSequence } from '../common/GeneticsSequence.tsx';

export const FarmOutputPlanner: React.FC = () => {
  const { selectedPlant, setSelectedPlant, targetConfig } = useWorkspace();

  const [planterCount, setPlanterCount] = useState<number>(4);
  const [plantsPerPlanter, setPlantsPerPlanter] = useState<number>(9);
  const [optimalConditions, setOptimalConditions] = useState<boolean>(true);
  const [customGenetics, setCustomGenetics] = useState<string>(targetConfig.targetGenetics || 'GGYYYY');

  const estimation = estimateFarmOutput({
    cropType: selectedPlant,
    genetics: customGenetics,
    planterCount: Math.max(1, planterCount),
    plantsPerPlanter: Math.max(1, plantsPerPlanter),
    optimalConditions
  });

  return (
    <Box sx={{ maxWidth: 960, mx: 'auto', p: 3, display: 'flex', flexDirection: 'column', gap: 3 }}>
      {/* Header */}
      <Box>
        <Typography variant="h5" sx={{ fontWeight: 800, color: '#FFFFFF', mb: 0.5 }}>
          Farm Output & Harvest Planner
        </Typography>
        <Typography variant="body2" sx={{ color: '#888888' }}>
          Calculate estimated yields, cycle duration, and resource requirements for your farming setup.
        </Typography>
      </Box>

      {/* Configuration Controls */}
      <Paper variant="outlined" sx={{ p: 2.5, backgroundColor: '#141414', borderColor: '#282828', borderRadius: '6px' }}>
        <Typography variant="caption" sx={{ color: '#00E5FF', fontWeight: 800, textTransform: 'uppercase', mb: 2, display: 'block' }}>
          FARM SETUP CONFIGURATION
        </Typography>

        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(3, 1fr)' }, gap: 2 }}>
          {/* Crop Selector */}
          <Box>
            <Typography variant="caption" sx={{ color: '#888', fontWeight: 700, mb: 0.5, display: 'block' }}>
              Crop Type
            </Typography>
            <Select
              size="small"
              fullWidth
              value={selectedPlant}
              onChange={(e) => setSelectedPlant(e.target.value)}
              sx={{ backgroundColor: '#1C1C1C', color: '#FFF' }}
            >
              {Object.values(CROP_GROWTH_DATABASE).map((crop) => (
                <MenuItem key={crop.id} value={crop.id}>
                  {crop.name}
                </MenuItem>
              ))}
            </Select>
          </Box>

          {/* Planter Count */}
          <Box>
            <Typography variant="caption" sx={{ color: '#888', fontWeight: 700, mb: 0.5, display: 'block' }}>
              Large Planter Boxes
            </Typography>
            <TextField
              size="small"
              type="number"
              fullWidth
              value={planterCount}
              onChange={(e) => setPlanterCount(Math.max(1, parseInt(e.target.value, 10) || 1))}
              slotProps={{ htmlInput: { min: 1 } }}
              sx={{ backgroundColor: '#1C1C1C' }}
            />
          </Box>

          {/* Genetics Input */}
          <Box>
            <Typography variant="caption" sx={{ color: '#888', fontWeight: 700, mb: 0.5, display: 'block' }}>
              Target Genetics
            </Typography>
            <TextField
              size="small"
              fullWidth
              value={customGenetics}
              onChange={(e) => setCustomGenetics(e.target.value.toUpperCase().replace(/[^GHYWX]/g, '').slice(0, 6))}
              slotProps={{ htmlInput: { maxLength: 6, style: { fontFamily: 'monospace', fontWeight: 800 } } }}
              sx={{ backgroundColor: '#1C1C1C' }}
            />
          </Box>
        </Box>

        <Box sx={{ mt: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: 1 }}>
          <FormControlLabel
            control={
              <Switch
                checked={optimalConditions}
                onChange={(e) => setOptimalConditions(e.target.checked)}
                color="primary"
              />
            }
            label={
              <Typography variant="body2" sx={{ color: '#E0E0E0' }}>
                Optimal Environment (100% Light, 100% Water, Fertilizer)
              </Typography>
            }
          />

          <Chip
            size="small"
            label="Calculations are Estimated based on Rust mechanics"
            sx={{ backgroundColor: 'rgba(255, 152, 0, 0.12)', color: '#FF9800', border: '1px solid rgba(255, 152, 0, 0.3)', fontSize: '0.68rem' }}
          />
        </Box>
      </Paper>

      {/* Estimation Results Summary Cards */}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(4, 1fr)' }, gap: 2 }}>
        {/* Total Plants */}
        <Paper variant="outlined" sx={{ p: 2, backgroundColor: '#161616', borderColor: '#282828', borderRadius: '6px', textAlign: 'center' }}>
          <SpaIcon sx={{ fontSize: 24, color: '#4CAF50', mb: 0.5 }} />
          <Typography variant="caption" sx={{ color: '#888', display: 'block', fontWeight: 700 }}>
            PLANT CAPACITY
          </Typography>
          <Typography variant="h6" sx={{ fontWeight: 800, color: '#FFFFFF', fontFamily: 'monospace' }}>
            {estimation.totalPlants} Plants
          </Typography>
          <Typography variant="caption" sx={{ color: '#666' }}>
            ({estimation.totalPlanters} planters × 9 slots)
          </Typography>
        </Paper>

        {/* Cycle Duration */}
        <Paper variant="outlined" sx={{ p: 2, backgroundColor: '#161616', borderColor: '#282828', borderRadius: '6px', textAlign: 'center' }}>
          <AccessTimeIcon sx={{ fontSize: 24, color: '#00E5FF', mb: 0.5 }} />
          <Typography variant="caption" sx={{ color: '#888', display: 'block', fontWeight: 700 }}>
            CYCLE DURATION
          </Typography>
          <Typography variant="h6" sx={{ fontWeight: 800, color: '#00E5FF', fontFamily: 'monospace' }}>
            ~{estimation.estimatedCycleDurationMinutes} min
          </Typography>
          <Typography variant="caption" sx={{ color: '#4CAF50' }}>
            {estimation.gCount} Growth Gene{estimation.gCount === 1 ? '' : 's'}
          </Typography>
        </Paper>

        {/* Yield Per Harvest */}
        <Paper variant="outlined" sx={{ p: 2, backgroundColor: '#161616', borderColor: '#282828', borderRadius: '6px', textAlign: 'center' }}>
          <Inventory2Icon sx={{ fontSize: 24, color: '#FFA726', mb: 0.5 }} />
          <Typography variant="caption" sx={{ color: '#888', display: 'block', fontWeight: 700 }}>
            HARVEST YIELD
          </Typography>
          <Typography variant="h6" sx={{ fontWeight: 800, color: '#FFA726', fontFamily: 'monospace' }}>
            ~{estimation.estimatedHarvestPerCycle.toLocaleString()} items
          </Typography>
          <Typography variant="caption" sx={{ color: '#4CAF50' }}>
            ~{estimation.estimatedYieldPerPlant} per plant ({estimation.yCount} Yield Genes)
          </Typography>
        </Paper>

        {/* Water Needed */}
        <Paper variant="outlined" sx={{ p: 2, backgroundColor: '#161616', borderColor: '#282828', borderRadius: '6px', textAlign: 'center' }}>
          <WaterDropIcon sx={{ fontSize: 24, color: '#0284C7', mb: 0.5 }} />
          <Typography variant="caption" sx={{ color: '#888', display: 'block', fontWeight: 700 }}>
            WATER NEEDED
          </Typography>
          <Typography variant="h6" sx={{ fontWeight: 800, color: '#0284C7', fontFamily: 'monospace' }}>
            ~{estimation.estimatedWaterNeededPerCycleLiters} L
          </Typography>
          <Typography variant="caption" sx={{ color: '#888' }}>
            per growth cycle
          </Typography>
        </Paper>
      </Box>
    </Box>
  );
};
