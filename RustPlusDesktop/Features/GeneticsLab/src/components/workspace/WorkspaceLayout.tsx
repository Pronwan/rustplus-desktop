import React, { useState } from 'react';
import { Box, useMediaQuery, useTheme, Drawer, IconButton, Tooltip } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ViewSidebarIcon from '@mui/icons-material/ViewSidebar';
import { CloneBank } from './CloneBank/CloneBank.tsx';
import { TargetDesigner } from './TargetDesigner/TargetDesigner.tsx';
import { RouteGrid } from './Routes/RouteGrid.tsx';
import { RouteInspector } from './Inspector/RouteInspector.tsx';
import { useCalculation } from '../../context/CalculationContext.tsx';

export const WorkspaceLayout: React.FC = () => {
  const theme = useTheme();
  const isWideDesktop = useMediaQuery(theme.breakpoints.up('xl')); // >= 1440px
  const isDesktop = useMediaQuery(theme.breakpoints.up('lg')); // >= 1200px

  const { selectedGroup, setSelectedGroup } = useCalculation();
  const [isInspectorManuallyClosed, setIsInspectorManuallyClosed] = useState(false);

  const showDesktopInspector = isDesktop && !isInspectorManuallyClosed && Boolean(selectedGroup);

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: showDesktopInspector
          ? isWideDesktop
            ? '300px minmax(420px, 1fr) 350px'
            : '290px minmax(360px, 1fr) 320px'
          : '295px minmax(420px, 1fr)',
        gap: 2,
        height: 'calc(100vh - 80px)',
        p: 1.5,
        boxSizing: 'border-box',
        overflow: 'hidden',
        transition: 'grid-template-columns 0.2s ease'
      }}
    >
      {/* Column 1: Gene Inputs (Left panel) */}
      <Box sx={{ height: '100%', overflow: 'hidden' }}>
        <CloneBank />
      </Box>

      {/* Column 2: Center Solver Area (Target Designer + Route Grid) */}
      <Box
        sx={{
          height: '100%',
          overflowY: 'auto',
          pr: 0.5,
          display: 'flex',
          flexDirection: 'column',
          '&::-webkit-scrollbar': { width: 5 },
          '&::-webkit-scrollbar-thumb': { backgroundColor: '#2E2E2E', borderRadius: 3 }
        }}
      >
        <TargetDesigner />
        <RouteGrid />
      </Box>

      {/* Column 3: Route Inspector (Desktop docking) */}
      {showDesktopInspector ? (
        <Box sx={{ height: '100%', overflow: 'hidden' }}>
          <RouteInspector onClose={() => setIsInspectorManuallyClosed(true)} />
        </Box>
      ) : isDesktop && selectedGroup && isInspectorManuallyClosed ? (
        /* Floating reopen pill when closed on desktop */
        <Box sx={{ position: 'fixed', right: 16, bottom: 24, zIndex: 100 }}>
          <Tooltip title="Open Route Inspector" arrow>
            <IconButton
              onClick={() => setIsInspectorManuallyClosed(false)}
              sx={{
                backgroundColor: '#1E1E1E',
                color: '#00E5FF',
                border: '1px solid #00E5FF',
                boxShadow: '0 4px 12px rgba(0,0,0,0.5)',
                '&:hover': { backgroundColor: '#282828' }
              }}
            >
              <ViewSidebarIcon sx={{ fontSize: 20 }} />
            </IconButton>
          </Tooltip>
        </Box>
      ) : (
        /* Tablet / Mobile Drawer when selected */
        <Drawer
          anchor="right"
          open={Boolean(selectedGroup && !isDesktop)}
          onClose={() => setSelectedGroup(null)}
          slotProps={{
            paper: {
              sx: {
                width: { xs: '100%', sm: 360 },
                backgroundColor: '#121212',
                p: 1.5
              }
            }
          }}
        >
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1 }}>
            <IconButton size="small" onClick={() => setSelectedGroup(null)} sx={{ color: '#888' }}>
              <CloseIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Box>
          <RouteInspector onClose={() => setSelectedGroup(null)} />
        </Drawer>
      )}
    </Box>
  );
};
