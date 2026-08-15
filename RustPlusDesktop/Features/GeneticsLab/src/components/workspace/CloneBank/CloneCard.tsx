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
    CORE: { bg: 'rgba(0, 229, 255, 0.15)', text: '#00E5FF', border: 'rgba(0, 229, 255, 0.4)' },
    HIGH: { bg: 'rgba(76, 175, 80, 0.15)', text: '#4CAF50', border: 'rgba(76, 175, 80, 0.4)' },
    MEDIUM: { bg: 'rgba(255, 167, 38, 0.15)', text: '#FFA726', border: 'rgba(255, 167, 38, 0.4)' },
    LOW: { bg: 'rgba(150, 150, 150, 0.12)', text: '#AAAAAA', border: 'rgba(150, 150, 150, 0.3)' },
    REDUNDANT: { bg: 'rgba(229, 57, 53, 0.12)', text: '#E53935', border: 'rgba(229, 57, 53, 0.3)' }
  };

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 1.25,
        mb: 1,
        borderRadius: '5px',
        backgroundColor: '#161616',
        borderColor: '#262626',
        transition: 'all 0.15s ease',
        '&:hover': {
          borderColor: '#383838',
          backgroundColor: '#1A1A1A'
        }
      }}
    >
      {/* Top Row: Index/Name, Favorite, Quantity, Menu */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 0.75 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75, flex: 1, overflow: 'hidden' }}>
          <Typography
            variant="caption"
            sx={{
              color: '#666666',
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.72rem'
            }}
          >
            #{index + 1}
          </Typography>

          {isEditingName ? (
            <TextField
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
                  color: '#FFFFFF',
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
                color: clone.name ? '#E0E0E0' : '#888888',
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
              backgroundColor: '#202020',
              border: '1px solid #333333',
              borderRadius: '3px',
              px: 0.5,
              py: 0.1
            }}
          >
            <IconButton
              size="small"
              onClick={(e) => handleQuantityChange(-1, e)}
              disabled={clone.quantity <= 1}
              sx={{ p: '1px', color: '#888', '&:hover': { color: '#FFF' } }}
            >
              <RemoveIcon sx={{ fontSize: 11 }} />
            </IconButton>

            <Typography
              variant="caption"
              sx={{
                fontWeight: 800,
                fontFamily: 'monospace',
                fontSize: '0.72rem',
                color: '#00E5FF',
                px: 0.5,
                minWidth: 16,
                textAlign: 'center'
              }}
            >
              ×{clone.quantity}
            </Typography>

            <IconButton
              size="small"
              onClick={(e) => handleQuantityChange(1, e)}
              sx={{ p: '1px', color: '#888', '&:hover': { color: '#FFF' } }}
            >
              <AddIcon sx={{ fontSize: 11 }} />
            </IconButton>
          </Box>

          <IconButton
            size="small"
            onClick={() => toggleFavorite(clone.id)}
            sx={{ p: '2px', color: clone.favorite ? '#FFD700' : '#444444', '&:hover': { color: '#FFD700' } }}
            title={clone.favorite ? 'Unfavorite' : 'Favorite'}
          >
            {clone.favorite ? <StarIcon sx={{ fontSize: 15 }} /> : <StarBorderIcon sx={{ fontSize: 15 }} />}
          </IconButton>

          <IconButton
            size="small"
            onClick={(e) => setMenuAnchorEl(e.currentTarget)}
            sx={{ p: '2px', color: '#666666', '&:hover': { color: '#FFFFFF' } }}
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
            size="small"
            onClick={handleCopyGenetics}
            sx={{
              p: '3px',
              color: '#666666',
              borderRadius: '3px',
              '&:hover': { color: '#00E5FF', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
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
              color: '#00E5FF',
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
              color: '#888888',
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
              backgroundColor: '#1A1A1A',
              border: '1px solid #333333',
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
          sx={{ fontSize: '0.75rem', color: '#E53935', gap: 1 }}
        >
          <DeleteIcon sx={{ fontSize: 14 }} /> Remove
        </MenuItem>
      </Menu>
    </Paper>
  );
};
