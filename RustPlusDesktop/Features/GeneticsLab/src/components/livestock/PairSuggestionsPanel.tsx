import React, { useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Chip,
  FormControl,
  InputLabel,
  MenuItem,
  Paper,
  Select,
  Stack,
  ToggleButton,
  ToggleButtonGroup,
  Typography
} from '@mui/material';
import { LivestockAnimal, LivestockSpecies, displayName } from '../../domain/livestock/animal.ts';
import { PairSortKey, RELATION_LABEL, suggestPairs } from '../../domain/livestock/pairing.ts';
import { LIVESTOCK_GENES } from '../../domain/livestock/livestockGenes.ts';
import { LivestockGenePanel, LEVEL_COLOR } from './LivestockGeneBadges.tsx';
import { GeneOutlook } from '../../domain/livestock/pairing.ts';
import { LIVESTOCK_GENE_INFO } from '../../domain/livestock/livestockGenes.ts';

const panelSx = { backgroundColor: 'var(--gl-panel-bg)', borderColor: 'var(--gl-border)', borderRadius: '6px' };

function pct(value: number): string {
  if (value === 0) return '0%';
  if (value < 0.001) return '<0.1%';
  return `${Math.round(value * 1000) / 10}%`;
}

const SORT_LABEL: Record<PairSortKey, string> = {
  expected: 'Best average genes',
  allHigh: 'Best chance of all green',
  godClone: 'Best chance of a god clone',
  noLow: 'Fewest red genes'
};

export const PairSuggestionsPanel: React.FC<{
  herd: LivestockAnimal[];
  onSelect: (id: string) => void;
}> = ({ herd, onSelect }) => {
  const [species, setSpecies] = useState<LivestockSpecies>(() =>
    herd.some((a) => a.species === 'cattle') || !herd.some((a) => a.species === 'sheep') ? 'cattle' : 'sheep'
  );
  const [sortBy, setSortBy] = useState<PairSortKey>('expected');

  const pairs = useMemo(
    () => suggestPairs(herd.filter((a) => a.species === species), sortBy).slice(0, 25),
    [herd, species, sortBy]
  );

  return (
    <Stack spacing={1.5}>
      <Alert severity="info" variant="outlined">
        Each animal carries <b>two copies</b> of every gene and shows the <b>better</b> one; a newborn gets one
        random copy from each parent. The panel hides the second copy, so it is estimated from the wild odds
        (Good 20%, Ok 50%, Bad 30%) and from parents you record. Record parents to sharpen the odds and to get
        inbreeding warnings. A <b>god clone</b> carries all ten copies Good and always breeds true.
      </Alert>

      <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', alignItems: 'center' }}>
        <ToggleButtonGroup
          exclusive
          size="small"
          value={species}
          onChange={(_, value: LivestockSpecies | null) => value && setSpecies(value)}
          aria-label="Species to pair"
        >
          <ToggleButton value="cattle">Cattle</ToggleButton>
          <ToggleButton value="sheep">Sheep</ToggleButton>
        </ToggleButtonGroup>
        <FormControl size="small" sx={{ minWidth: 210 }}>
          <InputLabel id="pair-sort">Rank by</InputLabel>
          <Select labelId="pair-sort" label="Rank by" value={sortBy} onChange={(e) => setSortBy(e.target.value as PairSortKey)}>
            {(Object.keys(SORT_LABEL) as PairSortKey[]).map((key) => (
              <MenuItem key={key} value={key}>
                {SORT_LABEL[key]}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Box>

      {pairs.length === 0 && (
        <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)' }}>
          Add at least one {species === 'cattle' ? 'bull and one cow' : 'ram and one ewe'} to see pairings.
        </Typography>
      )}

      {pairs.map((pair, index) => (
        <Paper
          key={`${pair.male.id}-${pair.female.id}`}
          variant="outlined"
          sx={{ ...panelSx, p: 1.5, borderLeft: `4px solid ${pair.relation ? 'var(--gl-warning)' : index === 0 ? 'var(--gl-success)' : 'var(--gl-border)'}` }}
        >
          <Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 1, flexWrap: 'wrap', alignItems: 'baseline' }}>
            <Typography sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
              #{index + 1}{' '}
              <Box component="button" type="button" onClick={() => onSelect(pair.male.id)} sx={linkSx}>
                {displayName(pair.male)}
              </Box>{' '}
              x{' '}
              <Box component="button" type="button" onClick={() => onSelect(pair.female.id)} sx={linkSx}>
                {displayName(pair.female)}
              </Box>
            </Typography>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap' }}>
              {pair.relation && <Chip size="small" color="warning" label={`Inbreeding risk: ${RELATION_LABEL[pair.relation]}`} />}
              {pair.sexAssumed && <Chip size="small" variant="outlined" label="Sex not recorded" />}
            </Box>
          </Box>

          <Box sx={{ display: 'flex', gap: 2, flexWrap: 'wrap', mt: 1, alignItems: 'center' }}>
            <LivestockGenePanel rows={pair.male.rows} size="sm" />
            <LivestockGenePanel rows={pair.female.rows} size="sm" />
          </Box>

          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(120px, 1fr))', gap: 1, mt: 1.25 }}>
            <Metric label="Avg gene value" value={`x${pair.expectedGeneFactor.toFixed(2)}`} />
            <Metric label="Est. offspring value" value={`~${Math.round(pair.expectedValue)} scrap`} />
            <Metric label="All green" value={pct(pair.allHighChance)} />
            <Metric label="No red genes" value={pct(pair.noLowChance)} />
            <Metric label="God clone" value={pct(pair.godCloneChance)} />
          </Box>

          <OffspringOutlook perGene={pair.perGene} />
        </Paper>
      ))}
    </Stack>
  );
};

const linkSx = {
  background: 'none',
  border: 'none',
  p: 0,
  font: 'inherit',
  color: 'var(--gl-primary)',
  cursor: 'pointer',
  textDecoration: 'underline',
  textUnderlineOffset: 3
};

const Metric: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <Box>
    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', fontWeight: 700 }}>
      {label}
    </Typography>
    <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 800, color: 'var(--gl-text-primary)' }}>
      {value}
    </Typography>
  </Box>
);

/**
 * What the newborn is likely to show, gene by gene: a badge in its most likely colour, a bar
 * split by the odds of Good / Ok / Bad, the chance of Good, and the chance it carries two
 * Good copies (pure, so it always passes Good on).
 */
const OffspringOutlook: React.FC<{ perGene: GeneOutlook[] }> = ({ perGene }) => (
  <Box sx={{ mt: 1.5 }}>
    <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontWeight: 700, display: 'block', mb: 0.75 }}>
      Expected newborn
    </Typography>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(5, minmax(0, 1fr))', gap: 1 }}>
      {LIVESTOCK_GENES.map((gene, i) => {
        const g = perGene[i];
        const mid = Math.max(0, 1 - g.high - g.low);
        const likely = g.high >= mid && g.high >= g.low ? 'high' : mid >= g.low ? 'mid' : 'low';
        const share = (value: number) => `${Math.round(value * 1000) / 10}%`;
        return (
          <Box
            key={gene}
            title={`${LIVESTOCK_GENE_INFO[gene].name}: Good ${share(g.high)}, Ok ${share(mid)}, Bad ${share(g.low)}. Pure Good ${share(g.breedsTrue)}.`}
            sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0.5, minWidth: 0 }}
          >
            <Box
              sx={{
                width: 26,
                height: 26,
                borderRadius: '50%',
                display: 'grid',
                placeItems: 'center',
                fontFamily: 'monospace',
                fontWeight: 800,
                fontSize: 13,
                color: '#fff',
                backgroundColor: LEVEL_COLOR[likely],
                boxShadow: g.breedsTrue >= 0.5 ? '0 0 0 2px #F5C542' : 'inset 0 -2px 0 rgba(0,0,0,0.25)'
              }}
            >
              {gene}
            </Box>
            <Box sx={{ display: 'flex', width: '100%', height: 8, borderRadius: 4, overflow: 'hidden', backgroundColor: 'var(--gl-border)' }}>
              <Box sx={{ width: share(g.high), backgroundColor: LEVEL_COLOR.high }} />
              <Box sx={{ width: share(mid), backgroundColor: LEVEL_COLOR.mid }} />
              <Box sx={{ width: share(g.low), backgroundColor: LEVEL_COLOR.low }} />
            </Box>
            <Typography
              variant="body2"
              sx={{ fontFamily: 'monospace', fontWeight: 800, lineHeight: 1, color: g.high > 0 ? 'var(--gl-success)' : 'var(--gl-text-faint)' }}
            >
              {Math.round(g.high * 100)}%
            </Typography>
            <Typography variant="caption" sx={{ lineHeight: 1, color: g.breedsTrue > 0 ? '#C9A227' : 'var(--gl-text-faint)', whiteSpace: 'nowrap' }}>
              {g.breedsTrue > 0 ? `\u2605 ${Math.round(g.breedsTrue * 100)}% pure` : 'no pure'}
            </Typography>
          </Box>
        );
      })}
    </Box>
  </Box>
);
