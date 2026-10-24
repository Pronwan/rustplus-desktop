import React from 'react';
import {
  Drawer,
  Box,
  Typography,
  IconButton,
  Divider,
  FormGroup,
  FormControlLabel,
  Checkbox,
  RadioGroup,
  Radio,
  Button
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';

export interface CloneFilterState {
  onlyFavorites: boolean;
  onlyScanner: boolean;
  containsG: boolean;
  containsY: boolean;
  containsH: boolean;
  containsW: boolean;
  containsX: boolean;
  sortBy: 'created-desc' | 'created-asc' | 'quantity-desc' | 'greens-desc' | 'name-asc';
}

interface CloneFilterDrawerProps {
  open: boolean;
  onClose: () => void;
  filters: CloneFilterState;
  onFiltersChange: (filters: CloneFilterState) => void;
  onReset: () => void;
}

export const CloneFilterDrawer: React.FC<CloneFilterDrawerProps> = ({
  open,
  onClose,
  filters,
  onFiltersChange,
  onReset
}) => {
  return (
    <Drawer
      anchor="left"
      open={open}
      onClose={onClose}
      slotProps={{
        paper: {
          sx: {
            width: 280,
            backgroundColor: 'var(--gl-card-bg)',
            color: 'var(--gl-text-primary)',
            p: 2.5,
            borderRight: '1px solid var(--gl-border)'
          }
        }
      }}
    >
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
          Filter Clones
        </Typography>
        <IconButton aria-label="Close clone filters" size="small" onClick={onClose} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </Box>

      <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Sort Order
      </Typography>
      <RadioGroup
        value={filters.sortBy}
        onChange={(e) => onFiltersChange({ ...filters, sortBy: e.target.value as any })}
        sx={{ my: 1 }}
      >
        <FormControlLabel value="created-desc" control={<Radio size="small" />} label={<Typography variant="body2">Recently Added</Typography>} />
        <FormControlLabel value="quantity-desc" control={<Radio size="small" />} label={<Typography variant="body2">Highest Quantity</Typography>} />
        <FormControlLabel value="greens-desc" control={<Radio size="small" />} label={<Typography variant="body2">Most Green Genes (G/Y/H)</Typography>} />
        <FormControlLabel value="name-asc" control={<Radio size="small" />} label={<Typography variant="body2">Name (A-Z)</Typography>} />
      </RadioGroup>

      <Divider sx={{ my: 2, borderColor: 'var(--gl-border)' }} />

      <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Status & Source
      </Typography>
      <FormGroup sx={{ my: 1 }}>
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.onlyFavorites}
              onChange={(e) => onFiltersChange({ ...filters, onlyFavorites: e.target.checked })}
            />
          }
          label={<Typography variant="body2">★ Favorites Only</Typography>}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.onlyScanner}
              onChange={(e) => onFiltersChange({ ...filters, onlyScanner: e.target.checked })}
            />
          }
          label={<Typography variant="body2">Scanner Imports</Typography>}
        />
      </FormGroup>

      <Divider sx={{ my: 2, borderColor: 'var(--gl-border)' }} />

      <Typography variant="caption" sx={{ color: 'var(--gl-primary)', fontWeight: 800, textTransform: 'uppercase', letterSpacing: 0.5 }}>
        Gene Content
      </Typography>
      <FormGroup sx={{ my: 1 }}>
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.containsG}
              onChange={(e) => onFiltersChange({ ...filters, containsG: e.target.checked })}
            />
          }
          label={<Typography variant="body2" sx={{ color: 'var(--gl-success)', fontWeight: 700 }}>Contains [G] Growth</Typography>}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.containsY}
              onChange={(e) => onFiltersChange({ ...filters, containsY: e.target.checked })}
            />
          }
          label={<Typography variant="body2" sx={{ color: 'var(--gl-success)', fontWeight: 700 }}>Contains [Y] Yield</Typography>}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.containsH}
              onChange={(e) => onFiltersChange({ ...filters, containsH: e.target.checked })}
            />
          }
          label={<Typography variant="body2" sx={{ color: 'var(--gl-success)', fontWeight: 700 }}>Contains [H] Hardiness</Typography>}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.containsW}
              onChange={(e) => onFiltersChange({ ...filters, containsW: e.target.checked })}
            />
          }
          label={<Typography variant="body2" sx={{ color: 'var(--gl-error)', fontWeight: 700 }}>Contains [W] Water</Typography>}
        />
        <FormControlLabel
          control={
            <Checkbox
              size="small"
              checked={filters.containsX}
              onChange={(e) => onFiltersChange({ ...filters, containsX: e.target.checked })}
            />
          }
          label={<Typography variant="body2" sx={{ color: 'var(--gl-error)', fontWeight: 700 }}>Contains [X] Empty</Typography>}
        />
      </FormGroup>

      <Box sx={{ mt: 'auto', pt: 2, display: 'flex', gap: 1 }}>
        <Button onClick={onReset} variant="outlined" size="small" fullWidth color="inherit">
          Reset
        </Button>
        <Button onClick={onClose} variant="contained" size="small" fullWidth>
          Done
        </Button>
      </Box>
    </Drawer>
  );
};
