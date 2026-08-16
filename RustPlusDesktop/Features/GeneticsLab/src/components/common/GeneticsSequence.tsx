import React from 'react';
import { Box, Typography, useTheme } from '@mui/material';

export interface GeneticsSequenceProps {
  genes: string; // 6-character string or wildcard string (e.g. "GGGYYY", "G??YG*")
  size?: 'small' | 'medium' | 'large' | 'xlarge';
  showSlotNumbers?: boolean;
  showConnectors?: boolean;
  highlightIndices?: number[];
  donorIndices?: number[];
  interactive?: boolean;
  onGeneClick?: (slotIndex: number) => void;
  ariaLabel?: string;
}

const GREEN_TYPES = new Set(['G', 'Y', 'H']);
const RED_TYPES = new Set(['W', 'X']);

export const GeneticsSequence: React.FC<GeneticsSequenceProps> = ({
  genes,
  size = 'medium',
  showSlotNumbers = false,
  showConnectors = false,
  highlightIndices = [],
  donorIndices = [],
  interactive = false,
  onGeneClick,
  ariaLabel
}) => {
  const theme = useTheme();
  const isDark = theme.palette.mode === 'dark';

  const sizeStyles = {
    small: { box: 20, font: '0.7rem', slotFont: '0.6rem' },
    medium: { box: 24, font: '0.82rem', slotFont: '0.65rem' },
    large: { box: 30, font: '0.95rem', slotFont: '0.72rem' },
    xlarge: { box: 38, font: '1.15rem', slotFont: '0.78rem' }
  };

  const dim = sizeStyles[size];
  const geneArray = genes.padEnd(6, '*').slice(0, 6).split('');

  return (
    <Box
      role="group"
      aria-label={ariaLabel || `Genetics sequence ${genes}`}
      sx={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: showConnectors ? '2px' : '4px',
        userSelect: 'none'
      }}
    >
      {geneArray.map((rawGene, idx) => {
        const gene = rawGene.toUpperCase();
        const isGreen = GREEN_TYPES.has(gene);
        const isRed = RED_TYPES.has(gene);
        const isWildcard = gene === '*' || gene === '?';

        const isHighlighted = highlightIndices.includes(idx);
        const isDonor = donorIndices.includes(idx);

        let bgColor: string;
        let textColor: string;
        let borderColor = 'transparent';

        if (isGreen) {
          bgColor = isDark ? '#4A7C17' : '#3F7013';
          textColor = '#FFFFFF';
        } else if (isRed) {
          bgColor = isDark ? '#8A2E22' : '#9B2C1F';
          textColor = '#FFFFFF';
        } else {
          // Wildcard or neutral
          bgColor = isDark ? 'var(--gl-surface)' : 'var(--gl-border)';
          textColor = isDark ? 'var(--gl-text-muted)' : 'var(--gl-text-secondary)';
          borderColor = isDark ? 'var(--gl-border-strong)' : 'var(--gl-border-strong)';
        }

        if (isHighlighted) {
          borderColor = theme.palette.primary.main;
        } else if (isDonor) {
          borderColor = 'var(--gl-gold)';
        }

        return (
          <React.Fragment key={idx}>
            <Box
              sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                cursor: interactive ? 'pointer' : 'default'
              }}
              onClick={() => interactive && onGeneClick?.(idx)}
            >
              {showSlotNumbers && (
                <Typography
                  variant="caption"
                  sx={{
                    fontSize: dim.slotFont,
                    color: isDark ? 'var(--gl-text-muted)' : 'var(--gl-text-muted)',
                    mb: '2px',
                    fontWeight: 700,
                    fontFamily: '"Roboto Mono", monospace'
                  }}
                >
                  {idx + 1}
                </Typography>
              )}

              <Box
                tabIndex={interactive ? 0 : undefined}
                role={interactive ? 'button' : undefined}
                aria-label={`Slot ${idx + 1}: ${isWildcard ? 'Any gene' : gene}`}
                onKeyDown={(e) => {
                  if (interactive && (e.key === 'Enter' || e.key === ' ')) {
                    e.preventDefault();
                    onGeneClick?.(idx);
                  }
                }}
                sx={{
                  width: dim.box,
                  height: dim.box,
                  borderRadius: '50%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  backgroundColor: bgColor,
                  color: textColor,
                  fontWeight: 800,
                  fontSize: dim.font,
                  fontFamily: '"Roboto Mono", "Consolas", monospace',
                  border: isHighlighted || isDonor || isWildcard ? '2px solid' : '1.5px solid transparent',
                  borderColor: borderColor,
                  boxShadow: isHighlighted ? `0 0 8px ${theme.palette.primary.main}` : isDonor ? '0 0 6px rgba(255, 215, 0, 0.6)' : 'none',
                  transition: 'all 0.15s ease',
                  lineHeight: 1,
                  outline: 'none',
                  '&:focus-visible': {
                    boxShadow: `0 0 0 3px ${theme.palette.primary.main}`
                  },
                  '&:hover': interactive ? {
                    transform: 'scale(1.1)',
                    filter: 'brightness(1.15)'
                  } : {}
                }}
              >
                {isWildcard ? '?' : gene}
              </Box>
            </Box>

            {showConnectors && idx < geneArray.length - 1 && (
              <Typography
                component="span"
                sx={{
                  color: isDark ? 'var(--gl-text-faint)' : 'var(--gl-border-strong)',
                  fontSize: dim.font,
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
