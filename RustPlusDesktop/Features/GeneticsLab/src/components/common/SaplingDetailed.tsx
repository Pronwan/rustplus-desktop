import React from 'react';
import { Card, Typography, Chip, IconButton, Tooltip, Box } from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { SaplingGeneRepr } from './SaplingGeneRepr.tsx';
import { useApp } from '../../context/AppContext.tsx';

interface SaplingDetailedProps {
  sapling: Sapling;
  onRemove?: () => void;
  onCopy?: () => void;
  isHighlighted?: boolean;
  highlightIndices?: number[];
  donorIndices?: number[];
}

export const SaplingDetailed: React.FC<SaplingDetailedProps> = ({
  sapling,
  onRemove,
  onCopy,
  isHighlighted = false,
  highlightIndices,
  donorIndices
}) => {
  const { options } = useApp();
  const score = sapling.getScore(options.geneScores);

  const handleCopy = () => {
    navigator.clipboard.writeText(sapling.toString());
    if (onCopy) onCopy();
  };

  return (
    <Card
      variant="outlined"
      sx={{
        p: 1,
        transition: 'all 0.2s ease',
        borderColor: isHighlighted ? 'primary.main' : 'divider',
        backgroundColor: isHighlighted ? 'rgba(96, 205, 255, 0.05)' : 'background.paper',
        '&:hover': {
          borderColor: 'primary.light'
        }
      }}
    >
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1.5, flexWrap: 'wrap' }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          {sapling.generationIndex !== undefined && (
            <Chip
              label={`Gen ${sapling.generationIndex + 1}`}
              size="small"
              variant="outlined"
              color={sapling.generationIndex === 0 ? 'default' : 'primary'}
              sx={{ fontWeight: 700, fontSize: '0.75rem' }}
            />
          )}

          <SaplingGeneRepr
            sapling={sapling}
            highlightIndices={highlightIndices}
            donorIndices={donorIndices}
          />
        </Box>

        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          {/* Gene Summary Chip */}
          <Chip
            label={`${sapling.numberOfGs()}G ${sapling.numberOfYs()}Y ${sapling.numberOfHs()}H`}
            size="small"
            sx={{
              backgroundColor: 'rgba(76, 175, 80, 0.12)',
              color: 'success.main',
              fontWeight: 700,
              fontSize: '0.75rem'
            }}
          />

          {/* Score Badge */}
          <Chip
            label={`Score: ${score.toFixed(1)}`}
            size="small"
            color="primary"
            variant="filled"
            sx={{ fontWeight: 700, fontSize: '0.75rem' }}
          />

          {/* Action Buttons */}
          <Tooltip title="Copy gene string">
            <IconButton aria-label={`Copy genetics ${sapling.toString()}`} size="small" onClick={handleCopy} sx={{ p: 0.5 }}>
              <ContentCopyIcon fontSize="inherit" />
            </IconButton>
          </Tooltip>

          {onRemove && (
            <Tooltip title="Remove plant">
              <IconButton aria-label={`Remove genetics ${sapling.toString()}`} size="small" color="error" onClick={onRemove} sx={{ p: 0.5 }}>
                <DeleteIcon fontSize="inherit" />
              </IconButton>
            </Tooltip>
          )}
        </Box>
      </Box>
    </Card>
  );
};
