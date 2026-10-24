import React from 'react';
import { Box, Tooltip } from '@mui/material';
import { GeneLevel, LEVEL_LABEL, LIVESTOCK_GENES, LIVESTOCK_GENE_INFO, cycleLevel } from '../../domain/livestock/livestockGenes.ts';
import { LivestockGeneRow, MarkerColor } from '../../domain/livestock/animal.ts';

/**
 * One row of livestock gene badges, drawn the way the game draws them: five lettered genes
 * whose colour is their quality, then the numbered marker badge.
 */

/** Sampled from in-game captures at UI scale 0.7 and 1.0. */
export const LEVEL_COLOR: Record<GeneLevel, string> = {
  low: '#AC472E',
  mid: '#8C8C8C',
  high: '#89AF44'
};

export const MARKER_COLOR: Record<MarkerColor, string> = {
  pink: '#C85A5A',
  blue: '#686DB5',
  purple: '#735BB8',
  teal: '#5FB3A3',
  lime: '#A6BE3E',
  red: '#AC472E',
  green: '#89AF44',
  grey: '#8C8C8C',
  unknown: '#555555'
};

const SIZES = { xs: 16, sm: 20, md: 26, lg: 34 } as const;

const MARKER_CYCLE: MarkerColor[] = ['blue', 'purple', 'teal', 'lime', 'green', 'pink', 'grey', 'unknown'];

export interface LivestockGeneBadgesProps {
  row: LivestockGeneRow;
  size?: keyof typeof SIZES;
  editable?: boolean;
  onChange?: (row: LivestockGeneRow) => void;
  ariaLabel?: string;
  /** Fixed column width, so a small row lines up under a large one. */
  pitch?: number;
}

export const LivestockGeneBadges: React.FC<LivestockGeneBadgesProps> = ({
  row,
  size = 'md',
  editable = false,
  onChange,
  ariaLabel,
  pitch
}) => {
  const px = SIZES[size];
  const gap = Math.max(3, Math.round(px * 0.18));
  const column = (child: React.ReactNode, key: string) =>
    pitch ? (
      <Box key={key} sx={{ width: pitch, display: 'inline-flex', justifyContent: 'center', flex: 'none' }}>
        {child}
      </Box>
    ) : (
      <React.Fragment key={key}>{child}</React.Fragment>
    );

  const badgeSx = (background: string, unread: boolean) => ({
    width: px,
    height: px,
    borderRadius: '50%',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 'none',
    backgroundColor: background,
    color: '#FFFFFF',
    fontFamily: 'monospace',
    fontWeight: 800,
    fontSize: Math.round(px * 0.48),
    lineHeight: 1,
    border: unread ? '1px dashed rgba(255,255,255,0.55)' : '1px solid rgba(255,255,255,0.12)',
    boxShadow: 'inset 0 -2px 0 rgba(0,0,0,0.25)',
    cursor: editable ? 'pointer' : 'default',
    userSelect: 'none' as const,
    p: 0,
    transition: 'transform 80ms ease',
    '&:hover': editable ? { transform: 'scale(1.08)' } : undefined,
    '&:focus-visible': { outline: '2px solid var(--gl-primary)', outlineOffset: 2 }
  });

  return (
    <Box
      role="group"
      aria-label={ariaLabel ?? 'Livestock genes'}
      sx={{ display: 'inline-flex', alignItems: 'center', gap: pitch ? 0 : `${gap}px` }}
    >
      {LIVESTOCK_GENES.map((gene, i) => {
        const level = row.levels[i] ?? null;
        const info = LIVESTOCK_GENE_INFO[gene];
        const label = `${info.name}: ${level ? LEVEL_LABEL[level] : 'not read'}`;
        return column(
          <Tooltip title={editable ? `${label} (click to change)` : label} arrow disableInteractive>
            <Box
              component={editable ? 'button' : 'span'}
              type={editable ? 'button' : undefined}
              aria-label={label}
              onClick={
                editable
                  ? () => {
                      const levels = [...row.levels];
                      levels[i] = cycleLevel(level);
                      onChange?.({ ...row, levels });
                    }
                  : undefined
              }
              sx={badgeSx(level ? LEVEL_COLOR[level] : '#2A2A2A', !level)}
            >
              {gene}
            </Box>
          </Tooltip>,
          gene
        );
      })}
      {column(
      <Tooltip
        title={`Marker ${row.marker.value ?? '?'} (${row.marker.color})${editable ? ' - click to change colour' : ''}. Its meaning is not known yet; it is stored as read.`}
        arrow
        disableInteractive
      >
        <Box
          component={editable ? 'button' : 'span'}
          type={editable ? 'button' : undefined}
          aria-label={`Marker ${row.marker.value ?? 'unknown'}`}
          onClick={
            editable
              ? () => {
                  const next = MARKER_CYCLE[(MARKER_CYCLE.indexOf(row.marker.color) + 1) % MARKER_CYCLE.length];
                  onChange?.({ ...row, marker: { ...row.marker, color: next } });
                }
              : undefined
          }
          sx={{ ...badgeSx(MARKER_COLOR[row.marker.color], row.marker.color === 'unknown'), ml: pitch ? 0 : '2px' }}
        >
          {row.marker.value ?? '?'}
        </Box>
      </Tooltip>,
      'marker'
      )}
    </Box>
  );
};

/**
 * All of an animal's rows, as the game shows them. The live panel has one row; animals
 * recorded from the earlier two-row panel show the second row smaller, columns aligned.
 */
export const LivestockGenePanel: React.FC<{
  rows: LivestockGeneRow[];
  size?: 'sm' | 'md' | 'lg';
  editable?: boolean;
  onChange?: (rows: LivestockGeneRow[]) => void;
}> = ({ rows, size = 'md', editable, onChange }) => {
  const small = size === 'lg' ? 'md' : size === 'md' ? 'sm' : 'xs';
  const pitch = Math.round(SIZES[size] * 1.28);
  return (
    <Box sx={{ display: 'inline-flex', flexDirection: 'column', alignItems: 'flex-start', gap: 0.5 }}>
      {rows.map((row, index) => (
        <LivestockGeneBadges
          key={index}
          row={row}
          size={index === 0 ? size : small}
          pitch={rows.length > 1 ? pitch : undefined}
          editable={editable}
          ariaLabel={index === 0 ? 'Genes' : 'Second row genes'}
          onChange={(next) => onChange?.(rows.map((r, i) => (i === index ? next : r)))}
        />
      ))}
    </Box>
  );
};
