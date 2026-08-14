import React from 'react';
import {
  Card,
  CardContent,
  Typography,
  List,
  ListItem,
  ListItemText,
  IconButton,
  Tooltip,
  Divider,
  Box
} from '@mui/material';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import DeleteIcon from '@mui/icons-material/Delete';
import { useApp } from '../../context/AppContext.tsx';

export const PreviousGenesTab: React.FC = () => {
  const { savedGeneSets, loadSavedGeneSet, deleteSavedGeneSet } = useApp();

  if (savedGeneSets.length === 0) {
    return (
      <Card variant="outlined">
        <CardContent sx={{ p: 2, textAlign: 'center' }}>
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            No saved plant presets yet. Save your current plant setup in the input panel above.
          </Typography>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card variant="outlined">
      <CardContent sx={{ p: 2 }}>
        <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 1.5 }}>
          Saved Plant Presets ({savedGeneSets.length})
        </Typography>
        <Divider sx={{ mb: 1 }} />

        <List disablePadding>
          {savedGeneSets.map((set, idx) => {
            const count = set.genes.split('\n').filter(Boolean).length;
            return (
              <ListItem
                key={idx}
                disableGutters
                secondaryAction={
                  <Box sx={{ display: 'flex', gap: 0.5 }}>
                    <Tooltip title="Load this plant preset">
                      <IconButton
                        size="small"
                        color="primary"
                        onClick={() => loadSavedGeneSet(set)}
                      >
                        <PlayArrowIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="Delete preset">
                      <IconButton
                        size="small"
                        color="error"
                        onClick={() => deleteSavedGeneSet(set.timestamp)}
                      >
                        <DeleteIcon fontSize="small" />
                      </IconButton>
                    </Tooltip>
                  </Box>
                }
                sx={{ py: 1, borderBottom: '1px solid', borderColor: 'divider' }}
              >
                <ListItemText
                  primary={
                    <Typography variant="body2" sx={{ fontWeight: 600, textTransform: 'capitalize' }}>
                      {(set.selectedPlantType || 'Plant Preset').replace(/-/g, ' ')}
                    </Typography>
                  }
                  secondary={
                    <Typography variant="caption" sx={{ color: 'text.secondary' }}>
                      {count} plants • {new Date(set.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </Typography>
                  }
                />
              </ListItem>
            );
          })}
        </List>
      </CardContent>
    </Card>
  );
};
