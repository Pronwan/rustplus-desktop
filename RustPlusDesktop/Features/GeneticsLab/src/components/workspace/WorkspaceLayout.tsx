import React, { useState } from 'react';
import { Box, useMediaQuery, useTheme, Drawer, IconButton, Tooltip } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ViewSidebarIcon from '@mui/icons-material/ViewSidebar';
import { CloneBank } from './CloneBank/CloneBank.tsx';
import { TargetDesigner } from './TargetDesigner/TargetDesigner.tsx';
import { RouteGrid } from './Routes/RouteGrid.tsx';
import { RouteInspector } from './Inspector/RouteInspector.tsx';
import { useCalculation } from '../../context/CalculationContext.tsx';

/**
 * Responsive workspace with three tiers:
 *   - Desktop (≥ lg / 1200px): 3 fluid columns, inspector docked on the right.
 *   - Tablet  (md–lg): 2 columns (inputs + center), inspector slides in as a drawer.
 *   - Compact (< md / 900px): single stacked column that scrolls; inspector drawer.
 * Column tracks use minmax so they shrink instead of overflowing when the window
 * is resized, and the center track can collapse to 0 to avoid horizontal blowout.
 */
export const WorkspaceLayout: React.FC = () => {
  const theme = useTheme();
  const isXL = useMediaQuery(theme.breakpoints.up('xl'));       // very wide
  const isDesktop = useMediaQuery(theme.breakpoints.up('lg'));  // ≥1200 → dock inspector
  const isCompact = useMediaQuery(theme.breakpoints.down('md')); // <900 → stack

  const { selectedGroup, isInspectorOpen, setIsInspectorOpen } = useCalculation();
  const [isInspectorManuallyClosed, setIsInspectorManuallyClosed] = useState(false);

  const showDesktopInspector = isDesktop && !isInspectorManuallyClosed && Boolean(selectedGroup);

  // The small-screen drawer opens ONLY when the user explicitly inspects a route
  // (isInspectorOpen), never from auto-selection — otherwise it would take over
  // the screen unprompted and reopen every time it's closed.
  const showDrawer = !isDesktop && Boolean(selectedGroup) && isInspectorOpen;
  const closeDrawer = () => setIsInspectorOpen(false);

  const centerArea = (
    <Box
      sx={{
        height: '100%',
        minWidth: 0,
        overflowY: 'auto',
        pr: 0.5,
        display: 'flex',
        flexDirection: 'column',
        '&::-webkit-scrollbar': { width: 5 },
        '&::-webkit-scrollbar-thumb': { backgroundColor: 'var(--gl-surface)', borderRadius: 3 }
      }}
    >
      <TargetDesigner />
      <RouteGrid />
    </Box>
  );

  const inspectorDrawer = (
    <Drawer
      anchor="right"
      open={showDrawer}
      onClose={closeDrawer}
      slotProps={{
        paper: {
          sx: {
            width: { xs: '100%', sm: 400 },
            maxWidth: '100vw',
            backgroundColor: 'var(--gl-panel-bg)',
            p: 1.5
          }
        }
      }}
    >
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
        <IconButton size="small" onClick={closeDrawer} sx={{ color: 'var(--gl-text-muted)' }}>
          <CloseIcon sx={{ fontSize: 18 }} />
        </IconButton>
      </Box>
      <RouteInspector onClose={closeDrawer} />
    </Drawer>
  );

  // --- COMPACT (< md): single stacked column; the whole area scrolls vertically ---
  if (isCompact) {
    return (
      <Box
        sx={{
          height: 'calc(100vh - 80px)',
          overflowY: 'auto',
          p: 1.5,
          boxSizing: 'border-box',
          display: 'flex',
          flexDirection: 'column',
          gap: 2
        }}
      >
        {/* Gene inputs get a bounded, internally-scrolling block */}
        <Box sx={{ height: 'min(52vh, 460px)', flexShrink: 0 }}>
          <CloneBank />
        </Box>

        {/* Target + routes flow beneath and grow with content */}
        <Box sx={{ flexShrink: 0, minWidth: 0 }}>
          <TargetDesigner />
          <RouteGrid />
        </Box>

        {inspectorDrawer}
      </Box>
    );
  }

  // --- TABLET / DESKTOP: CSS grid (2 or 3 columns) ---
  const gridTemplateColumns = showDesktopInspector
    ? isXL
      ? 'minmax(260px, 320px) minmax(440px, 1fr) minmax(320px, 380px)'
      : 'minmax(230px, 270px) minmax(340px, 1fr) minmax(290px, 330px)'
    : 'minmax(240px, 300px) minmax(0, 1fr)';

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns,
        gap: 2,
        height: 'calc(100vh - 80px)',
        p: 1.5,
        boxSizing: 'border-box',
        overflow: 'hidden',
        transition: 'grid-template-columns 0.2s ease'
      }}
    >
      {/* Column 1: Gene Inputs */}
      <Box sx={{ height: '100%', minWidth: 0, overflow: 'hidden' }}>
        <CloneBank />
      </Box>

      {/* Column 2: Center Solver Area */}
      {centerArea}

      {/* Column 3: Inspector — docked on desktop, drawer otherwise */}
      {showDesktopInspector ? (
        <Box sx={{ height: '100%', minWidth: 0, overflow: 'hidden' }}>
          <RouteInspector onClose={() => setIsInspectorManuallyClosed(true)} />
        </Box>
      ) : isDesktop && selectedGroup && isInspectorManuallyClosed ? (
        /* Floating reopen pill when the user closed the docked inspector */
        <Box sx={{ position: 'fixed', right: 16, bottom: 24, zIndex: 100 }}>
          <Tooltip title="Open Route Inspector" arrow>
            <IconButton
              onClick={() => setIsInspectorManuallyClosed(false)}
              sx={{
                backgroundColor: 'var(--gl-elevated-bg)',
                color: 'var(--gl-primary)',
                border: '1px solid var(--gl-primary)',
                boxShadow: '0 4px 12px rgba(0,0,0,0.5)',
                '&:hover': { backgroundColor: 'var(--gl-border)' }
              }}
            >
              <ViewSidebarIcon sx={{ fontSize: 20 }} />
            </IconButton>
          </Tooltip>
        </Box>
      ) : (
        inspectorDrawer
      )}
    </Box>
  );
};
