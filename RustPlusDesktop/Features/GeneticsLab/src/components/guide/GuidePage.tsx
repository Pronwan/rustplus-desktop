import React, { useState } from 'react';
import {
  Box,
  Typography,
  List,
  ListItemButton,
  ListItemText,
  Paper,
  Divider
} from '@mui/material';
import { GUIDE_SECTIONS } from '../../domain/guide/guideData.ts';
import { PlanterVisual } from './PlanterVisual.tsx';

export const GuidePage: React.FC = () => {
  const [selectedSectionId, setSelectedSectionId] = useState(GUIDE_SECTIONS[0].id);

  const activeSection = GUIDE_SECTIONS.find((s) => s.id === selectedSectionId) || GUIDE_SECTIONS[0];

  return (
    <Box sx={{ maxWidth: 1200, mx: 'auto' }}>
      <Typography
        variant="h4"
        sx={{
          fontWeight: 800,
          color: 'var(--gl-text-primary)',
          fontFamily: '"Roboto Mono", monospace',
          mb: 3,
          letterSpacing: '0.5px'
        }}
      >
        Guide
      </Typography>

      <Box
        sx={{
          display: 'grid',
          gridTemplateColumns: { xs: '1fr', md: '260px 1fr' },
          gap: 4,
          alignItems: 'start'
        }}
      >
        {/* Left Navigation */}
        <Paper
          variant="outlined"
          sx={{
            backgroundColor: 'var(--gl-panel-bg)',
            borderColor: 'var(--gl-border)',
            borderRadius: '6px',
            p: 1.5,
            position: { md: 'sticky' },
            top: 20
          }}
        >
          <List disablePadding>
            {GUIDE_SECTIONS.map((sec) => {
              const isSelected = sec.id === selectedSectionId;
              return (
                <ListItemButton
                  key={sec.id}
                  selected={isSelected}
                  onClick={() => setSelectedSectionId(sec.id)}
                  sx={{
                    borderRadius: '4px',
                    mb: 0.5,
                    py: 1,
                    px: 1.5,
                    backgroundColor: isSelected ? 'rgba(0, 229, 255, 0.12)' : 'transparent',
                    borderLeft: isSelected ? '3px solid var(--gl-primary)' : '3px solid transparent',
                    '&:hover': {
                      backgroundColor: 'rgba(255, 255, 255, 0.04)'
                    }
                  }}
                >
                  <ListItemText
                    primary={
                      <Typography
                        variant="body2"
                        sx={{
                          fontWeight: isSelected ? 800 : 500,
                          color: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-secondary)',
                          fontFamily: '"Roboto Mono", monospace',
                          fontSize: '0.85rem'
                        }}
                      >
                        {sec.title}
                      </Typography>
                    }
                  />
                </ListItemButton>
              );
            })}
          </List>
        </Paper>

        {/* Right Article Content */}
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {/* Interactive 3x3 Planter Visualizer */}
          <PlanterVisual />

          {/* Article Section */}
          <Paper
            variant="outlined"
            sx={{
              backgroundColor: 'var(--gl-panel-bg)',
              borderColor: 'var(--gl-border)',
              borderRadius: '6px',
              p: 3.5
            }}
          >
            <Typography
              variant="h5"
              sx={{
                fontWeight: 800,
                color: 'var(--gl-text-primary)',
                fontFamily: '"Roboto Mono", monospace',
                mb: 1
              }}
            >
              {activeSection.title}
            </Typography>

            <Typography
              variant="body2"
              sx={{
                color: 'var(--gl-text-muted)',
                fontFamily: '"Roboto Mono", monospace',
                mb: 3
              }}
            >
              {activeSection.summary}
            </Typography>

            <Divider sx={{ mb: 3, borderColor: 'var(--gl-border)' }} />

            {/* Paragraphs */}
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {activeSection.content.map((p, idx) => (
                <Typography
                  key={idx}
                  variant="body2"
                  sx={{
                    lineHeight: 1.8,
                    color: 'var(--gl-text-secondary)',
                    fontFamily: '"Roboto Mono", monospace',
                    fontSize: '0.88rem',
                    '& strong': { color: 'var(--gl-primary)', fontWeight: 700 }
                  }}
                  dangerouslySetInnerHTML={{
                    __html: p.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
                  }}
                />
              ))}
            </Box>

            {/* Takeaways */}
            {activeSection.keyPoints && activeSection.keyPoints.length > 0 && (
              <Box
                sx={{
                  mt: 3,
                  p: 2,
                  backgroundColor: 'rgba(76, 175, 80, 0.08)',
                  border: '1px solid rgba(76, 175, 80, 0.3)',
                  borderRadius: '4px'
                }}
              >
                <Typography
                  variant="subtitle2"
                  sx={{
                    fontWeight: 700,
                    color: 'var(--gl-success)',
                    fontFamily: 'monospace',
                    mb: 1
                  }}
                >
                  Key Takeaways:
                </Typography>
                {activeSection.keyPoints.map((kp, idx) => (
                  <Typography
                    key={idx}
                    variant="caption"
                    sx={{
                      display: 'block',
                      color: 'var(--gl-text-primary)',
                      fontFamily: 'monospace',
                      lineHeight: 1.6
                    }}
                  >
                    • {kp}
                  </Typography>
                ))}
              </Box>
            )}
          </Paper>
        </Box>
      </Box>
    </Box>
  );
};
