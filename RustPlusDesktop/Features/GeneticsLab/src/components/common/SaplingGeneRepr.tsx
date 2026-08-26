import React from 'react';
import { Box, Typography } from '@mui/material';
import { Sapling } from '../../domain/genetics/Sapling.ts';

interface SaplingGeneReprProps {
  sapling: Sapling;
  highlightIndices?: number[];
  donorIndices?: number[];
  size?: 'small' | 'medium' | 'large';
  showSlotNumbers?: boolean;
  showConnectors?: boolean;
}

export const SaplingGeneRepr: React.FC<SaplingGeneReprProps> = ({
  sapling,
  highlightIndices = [],
  donorIndices = [],
  size = 'medium',
  showSlotNumbers = false,
  showConnectors = false
}) => {
  const sizeMap = {
    small: { box: 18, font: '0.7rem' },
    medium: { box: 22, font: '0.8rem' },
    large: { box: 28, font: '0.95rem' }
  };

  const dim = sizeMap[size];

  return (
    <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: showConnectors ? '1px' : '3px' }}>
      {sapling.genes.map((gene, idx) => {
        const isGreen = gene.isGreen;
        const isHighlighted = highlightIndices.includes(idx);
        const isDonor = donorIndices.includes(idx);

        // Authentic circle colors from Rust Breeder
        const bgColor = isGreen ? '#598518' : '#94382A';
        const borderColor = isHighlighted ? 'var(--gl-primary)' : isDonor ? 'var(--gl-gold)' : 'transparent';

        return (
          <React.Fragment key={idx}>
            <Box sx={{ position: 'relative', textAlign: 'center' }}>
              {showSlotNumbers && (
                <Typography
                  variant="caption"
                  sx={{
                    display: 'block',
                    fontSize: '0.65rem',
                    color: 'var(--gl-text-muted)',
                    mb: '2px',
                    fontWeight: 600,
                    fontFamily: 'monospace'
                  }}
                >
                  {idx + 1}
                </Typography>
              )}

              <Box
                sx={{
                  width: dim.box,
                  height: dim.box,
                  borderRadius: '50%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  backgroundColor: bgColor,
                  color: '#FFFFFF',
                  fontWeight: 800,
                  fontSize: dim.font,
                  fontFamily: '"Roboto Mono", "Consolas", monospace',
                  border: isHighlighted || isDonor ? '2px solid' : 'none',
                  borderColor: borderColor,
                  boxShadow: isHighlighted ? '0 0 8px rgba(0, 229, 255, 0.8)' : 'none',
                  userSelect: 'none',
                  lineHeight: 1
                }}
              >
                {gene.type}
              </Box>
            </Box>

            {/* Connector dash between genes if enabled */}
            {showConnectors && idx < sapling.genes.length - 1 && (
              <Typography
                component="span"
                sx={{
                  color: 'var(--gl-text-faint)',
                  fontSize: '0.75rem',
                  fontFamily: 'monospace',
                  userSelect: 'none',
                  px: '1px'
                }}
              >
                -
              </Typography>
            )}
          </React.Fragment>
        );
      })}
    </Box>
  );
};
