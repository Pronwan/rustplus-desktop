import React from 'react';
import { Card, CardContent, Typography, Stack, Breadcrumbs, Link, Button, Box } from '@mui/material';
import NavigateNextIcon from '@mui/icons-material/NavigateNext';
import { GeneticsMapGroup } from '../../domain/genetics/GeneticsMapGroup.ts';
import { SimulationMapCard } from './SimulationMapCard.tsx';

interface MapGroupBrowserProps {
  rootGroup: GeneticsMapGroup;
  historyStack: GeneticsMapGroup[];
  onNavigateToGroup: (group: GeneticsMapGroup) => void;
  onPopToStep: (index: number) => void;
}

export const MapGroupBrowser: React.FC<MapGroupBrowserProps> = ({
  rootGroup,
  historyStack,
  onNavigateToGroup,
  onPopToStep
}) => {
  const currentGroup = historyStack[historyStack.length - 1] || rootGroup;

  return (
    <Card variant="outlined">
      <CardContent sx={{ p: 2 }}>
        <Stack spacing={2}>
          {/* Breadcrumbs Navigation */}
          {historyStack.length > 1 && (
            <Breadcrumbs separator={<NavigateNextIcon fontSize="small" />} aria-label="breadcrumb">
              {historyStack.map((grp, idx) => {
                const isLast = idx === historyStack.length - 1;
                return isLast ? (
                  <Typography key={idx} color="text.primary" sx={{ fontWeight: 700, fontSize: '0.85rem' }}>
                    {grp.resultSaplingGeneString} (Active)
                  </Typography>
                ) : (
                  <Link
                    key={idx}
                    underline="hover"
                    color="inherit"
                    href="#"
                    onClick={(e) => {
                      e.preventDefault();
                      onPopToStep(idx);
                    }}
                    sx={{ fontSize: '0.85rem', fontWeight: 600 }}
                  >
                    {grp.resultSaplingGeneString}
                  </Link>
                );
              })}
            </Breadcrumbs>
          )}

          {/* Active Simulation Group View */}
          <SimulationMapCard group={currentGroup} />
        </Stack>
      </CardContent>
    </Card>
  );
};
