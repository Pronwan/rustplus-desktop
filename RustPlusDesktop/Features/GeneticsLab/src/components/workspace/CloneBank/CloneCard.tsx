import React, { useState } from 'react';
import {
  Paper,
  Box,
  Typography,
  IconButton,
  Tooltip,
  Chip,
  Menu,
  MenuItem,
  TextField,
  InputAdornment
} from '@mui/material';
import StarIcon from '@mui/icons-material/Star';
import StarBorderIcon from '@mui/icons-material/StarBorder';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import AddIcon from '@mui/icons-material/Add';
import RemoveIcon from '@mui/icons-material/Remove';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import EditIcon from '@mui/icons-material/Edit';
import { SavedClone } from '../../../domain/genetics/Clone.ts';
import { GeneticsSequence } from '../../common/GeneticsSequence.tsx';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useNotification } from '../../../context/NotificationContext.tsx';

interface CloneCardProps {
  clone: SavedClone;
  index: number;
  utilityRating?: 'CORE' | 'HIGH' | 'MEDIUM' | 'LOW' | 'REDUNDANT';
  usedInRoutesCount?: number;
}

export const CloneCard: React.FC<CloneCardProps> = ({
  clone,
  index,
  utilityRating,
  usedInRoutesCount
}) => {
  const { updateClone, removeClone, duplicateClone, toggleFavorite } = useWorkspace();
  const { notifySuccess } = useNotification();

  const [menuAnchorEl, setMenuAnchorEl] = useState<null | HTMLElement>(null);
  const [isEditingName, setIsEditingName] = useState(false);
  const [editNameValue, setEditNameValue] = useState(clone.name || '');

  const handleCopyGenetics = (e: React.MouseEvent) => {
    e.stopPropagation();
    navigator.clipboard.writeText(clone.genetics);
    notifySuccess(`Copied [${clone.genetics}] to clipboard`);
  };

  const handleSaveName = () => {
    updateClone(clone.id, { name: editNameValue.trim() });
    setIsEditingName(false);
  };

  const handleQuantityChange = (delta: number, e: React.MouseEvent) => {
    e.stopPropagation();
    const next = Math.max(1, clone.quantity + delta);
    updateClone(clone.id, { quantity: next });
  };

  const ratingColors = {
    CORE: { bg: 'rgba(0, 229, 255, 0.15)', text: 'var(--gl-primary)', border: 'rgba(0, 229, 255, 0.4)' },
    HIGH: { bg: 'rgba(76, 175, 80, 0.15)', text: 'var(--gl-success)', border: 'rgba(76, 175, 80, 0.4)' },
    MEDIUM: { bg: 'rgba(255, 167, 38, 0.15)', text: 'var(--gl-warning)', border: 'rgba(255, 167, 38, 0.4)' },
    LOW: { bg: 'rgba(150, 150, 150, 0.12)', text: 'var(--gl-text-secondary)', border: 'rgba(150, 150, 150, 0.3)' },
    REDUNDANT: { bg: 'rgba(229, 57, 53, 0.12)', text: 'var(--gl-error)', border: 'rgba(229, 57, 53, 0.3)' }
  };

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 1.25,
        mb: 1,
        borderRadius: '5px',
        backgroundColor: 'var(--gl-card-bg)',
        borderColor: 'var(--gl-surface)',
        transition: 'all 0.15s ease',
        '&:hover': {
          borderColor: 'var(--gl-border-strong)',
          backgroundColor: 'var(--gl-input-bg)'
        }
      }}
    >
      {/* Top Row: Index/Name, Favorite, Quantity, Menu */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 0.75 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flex: 1, overflow: 'hidden' }}>
          <Typography
            variant="caption"
            sx={{
              color: 'var(--gl-text-muted)',
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.72rem'
            }}
          >
            #{index + 1}
          </Typography>

          {isEditingName ? (
            <TextField
              aria-label={`Name for clone ${clone.genetics}`}
              size="small"
              value={editNameValue}
              onChange={(e) => setEditNameValue(e.target.value)}
              onBlur={handleSaveName}
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleSaveName();
                if (e.key === 'Escape') setIsEditingName(false);
              }}
              autoFocus
              variant="standard"
              sx={{
                '& input': {
                  fontSize: '0.75rem',
                  fontWeight: 700,
                  color: 'var(--gl-text-primary)',
                  py: 0
                }
              }}
            />
          ) : (
            <Typography
              variant="caption"
              onDoubleClick={() => setIsEditingName(true)}
              sx={{
                fontWeight: 700,
                color: clone.name ? 'var(--gl-text-primary)' : 'var(--gl-text-muted)',
                fontSize: '0.75rem',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                cursor: 'pointer'
              }}
              title={clone.name ? `${clone.name} (Double-click to edit)` : 'Double-click to name'}
            >
              {clone.name || 'Unnamed Clone'}
            </Typography>
          )}
        </Box>

        {/* Quantity Controls & Actions */}
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
          {/* Quantity stepper */}
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              backgroundColor: 'var(--gl-border-subtle)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '3px',
              px: 0.5,
              py: 0.1
            }}
          >
            <IconButton
              aria-label={`Decrease quantity for ${clone.genetics}`}
              size="small"
              onClick={(e) => handleQuantityChange(-1, e)}
              disabled={clone.quantity <= 1}
              sx={{ p: '1px', color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-text-primary)' } }}
            >
              <RemoveIcon sx={{ fontSize: 11 }} />
            </IconButton>

            <Typography
              variant="caption"
              sx={{
                fontWeight: 800,
                fontFamily: 'monospace',
                fontSize: '0.72rem',
                color: 'var(--gl-primary)',
                px: 0.5,
                minWidth: 16,
                textAlign: 'center'
              }}
            >
              ×{clone.quantity}
            </Typography>

            <IconButton
              aria-label={`Increase quantity for ${clone.genetics}`}
              size="small"
              onClick={(e) => handleQuantityChange(1, e)}
              sx={{ p: '1px', color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-text-primary)' } }}
            >
              <AddIcon sx={{ fontSize: 11 }} />
            </IconButton>
          </Box>

          <IconButton
            aria-label={`${clone.favorite ? 'Remove' : 'Add'} ${clone.genetics} ${clone.favorite ? 'from' : 'to'} favorites`}
            size="small"
            onClick={() => toggleFavorite(clone.id)}
            sx={{ p: '2px', color: clone.favorite ? 'var(--gl-gold)' : 'var(--gl-text-faint)', '&:hover': { color: 'var(--gl-gold)' } }}
            title={clone.favorite ? 'Unfavorite' : 'Favorite'}
          >
            {clone.favorite ? <StarIcon sx={{ fontSize: 15 }} /> : <StarBorderIcon sx={{ fontSize: 15 }} />}
          </IconButton>

          <IconButton
            aria-label={`More actions for ${clone.genetics}`}
            size="small"
            onClick={(e) => setMenuAnchorEl(e.currentTarget)}
            sx={{ p: '2px', color: 'var(--gl-text-muted)', '&:hover': { color: 'var(--gl-text-primary)' } }}
          >
            <MoreVertIcon sx={{ fontSize: 15 }} />
          </IconButton>
        </Box>
      </Box>

      {/* Middle Row: Genetics Sequence & Quick Copy */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <GeneticsSequence genes={clone.genetics} size="small" showConnectors={true} />

        <Tooltip title="Copy genes" arrow>
          <IconButton
            aria-label={`Copy genetics ${clone.genetics}`}
            size="small"
            onClick={handleCopyGenetics}
            sx={{
              p: '3px',
              color: 'var(--gl-text-muted)',
              borderRadius: '3px',
              '&:hover': { color: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
            }}
          >
            <ContentCopyIcon sx={{ fontSize: 13 }} />
          </IconButton>
        </Tooltip>
      </Box>

      {/* Bottom Row: Tags, Source, & Utility Rating */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mt: 0.75, flexWrap: 'wrap' }}>
        {clone.source === 'scanner' && (
          <Chip
            size="small"
            label="Scanner"
            sx={{
              height: 16,
              fontSize: '0.62rem',
              backgroundColor: 'rgba(0, 229, 255, 0.08)',
              color: 'var(--gl-primary)',
              border: '1px solid rgba(0, 229, 255, 0.25)',
              '& .MuiChip-label': { px: 0.5 }
            }}
          />
        )}

        {utilityRating && (
          <Chip
            size="small"
            label={utilityRating}
            sx={{
              height: 16,
              fontSize: '0.62rem',
              fontWeight: 800,
              backgroundColor: ratingColors[utilityRating].bg,
              color: ratingColors[utilityRating].text,
              border: `1px solid ${ratingColors[utilityRating].border}`,
              '& .MuiChip-label': { px: 0.5 }
            }}
          />
        )}

        {usedInRoutesCount !== undefined && usedInRoutesCount > 0 && (
          <Typography
            variant="caption"
            sx={{
              fontSize: '0.65rem',
              color: 'var(--gl-text-muted)',
              fontFamily: 'monospace',
              ml: 'auto'
            }}
          >
            In {usedInRoutesCount} routes
          </Typography>
        )}
      </Box>

      {/* Context Menu */}
      <Menu
        anchorEl={menuAnchorEl}
        open={Boolean(menuAnchorEl)}
        onClose={() => setMenuAnchorEl(null)}
        slotProps={{
          paper: {
            sx: {
              backgroundColor: 'var(--gl-input-bg)',
              border: '1px solid var(--gl-surface-hover)',
              borderRadius: '4px',
              minWidth: 130
            }
          }
        }}
      >
        <MenuItem
          onClick={() => {
            setMenuAnchorEl(null);
            setIsEditingName(true);
          }}
          sx={{ fontSize: '0.75rem', gap: 1 }}
        >
          <EditIcon sx={{ fontSize: 14 }} /> Rename
        </MenuItem>
        <MenuItem
          onClick={() => {
            setMenuAnchorEl(null);
            duplicateClone(clone.id);
          }}
          sx={{ fontSize: '0.75rem', gap: 1 }}
        >
          <ContentCopyIcon sx={{ fontSize: 14 }} /> Duplicate
        </MenuItem>
        <MenuItem
          onClick={() => {
            setMenuAnchorEl(null);
            removeClone(clone.id);
          }}
          sx={{ fontSize: '0.75rem', color: 'var(--gl-error)', gap: 1 }}
        >
          <DeleteIcon sx={{ fontSize: 14 }} /> Remove
        </MenuItem>
      </Menu>
    </Paper>
  );
};
