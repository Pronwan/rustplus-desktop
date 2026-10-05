import React from 'react';
import { Box, Paper, Stack, Typography } from '@mui/material';
import {
  BIOFUEL_SECONDS_PER_DUNG,
  GENE_LEVELS,
  GeneLevel,
  HARDINESS_FULL_CONDITION,
  INBRED_EFFECT,
  LEVEL_LABEL,
  LIVESTOCK_BASE as B,
  LivestockGene,
  geneMultiplier
} from '../../domain/livestock/livestockGenes.ts';
import { formatDuration } from '../../domain/livestock/stats.ts';
import { LEVEL_COLOR } from './LivestockGeneBadges.tsx';

const panelSx = { backgroundColor: 'var(--gl-panel-bg)', borderColor: 'var(--gl-border)', borderRadius: '6px' };

const round1 = (value: number) => String(Math.round(value * 10) / 10);
const m = (gene: LivestockGene, level: GeneLevel) => geneMultiplier(gene, level);
const fleecesPerHour = (level: GeneLevel) => 3600 / (B.gatherCooldownSeconds / m('Y', level));
const milk = (level: GeneLevel) => Math.max(1, Math.round(B.milkPerGather * m('Y', level)));
const dungPerHour = (level: GeneLevel) => 3600 / (B.dungIntervalSeconds / m('D', level));

/** Gene effects at Bad / Ok / Good, from the same values the animal stats use. */
const GENE_ROWS: Array<{ gene: LivestockGene; label: string; value: (level: GeneLevel) => string }> = [
  { gene: 'L', label: 'Adult lifespan', value: (l) => formatDuration(B.lifespanHours * 3600 * m('L', l)) },
  { gene: 'Y', label: 'Milk per milking', value: (l) => String(milk(l)) },
  { gene: 'Y', label: 'Milk per hour', value: (l) => round1(milk(l) * fleecesPerHour(l)) },
  { gene: 'Y', label: 'Wool per fleece', value: (l) => String(Math.floor(B.woolPerFleece * m('Y', l) + 1e-9)) },
  { gene: 'Y', label: 'Wool per hour', value: (l) => round1(Math.floor(B.woolPerFleece * m('Y', l) + 1e-9) * fleecesPerHour(l)) },
  { gene: 'Y', label: 'Milking / fleece regrow', value: (l) => formatDuration(B.gatherCooldownSeconds / m('Y', l)) },
  { gene: 'F', label: 'Male breeding cooldown', value: (l) => formatDuration(B.maleBreedingCooldownSeconds / m('F', l)) },
  { gene: 'F', label: 'Female cooldown after birth', value: (l) => formatDuration(B.femaleCooldownAfterBirthSeconds / m('F', l)) },
  { gene: 'F', label: 'Twins chance', value: (l) => (l === 'high' ? `${B.goodFertilityTwinChance * 100}%` : '0%') },
  { gene: 'H', label: 'Lowest need for full condition', value: (l) => `${Math.round(HARDINESS_FULL_CONDITION[l] * 100)}%` },
  { gene: 'D', label: 'Dung interval (adults)', value: (l) => formatDuration(B.dungIntervalSeconds / m('D', l)) }
];

const TRUST_LADDER: Array<{ seconds: number; name: string; effect: string }> = [
  { seconds: 5, name: 'Known', effect: 'Curious animals (about 1 in 5) start following you.' },
  { seconds: 30, name: 'Tolerated', effect: 'A bull stops charging you.' },
  { seconds: 300, name: 'Bonded', effect: 'Lead it, set a Tool Cupboard as its home. It is now tame for everyone.' },
  { seconds: 900, name: 'Herd', effect: 'A bull defends you.' }
];

/** The livestock rules in one place: taming, trust, gene values, inbreeding and dung. */
export const LivestockRulesPanel: React.FC = () => {
  const goodDungAdults = Math.ceil(3600 / BIOFUEL_SECONDS_PER_DUNG / dungPerHour('high') - 1e-9);
  return (
    <Stack spacing={1.5}>
      <Section title="Taming">
        An animal counts as <b>tame</b> once at least one player has spent <b>5 minutes</b> within 20 m of it. Until then it
        does not grow up, breed, drop dung or give milk; a wild animal nobody bonds with stays frozen. Any calf or lamb grows
        up in about <b>1 hour</b>, whatever its Longevity.
      </Section>

      <Section title="Trust (time spent within 20 m, per player)">
        <Box sx={{ display: 'grid', gridTemplateColumns: 'auto auto 1fr', columnGap: 1.5, rowGap: 0.5, mt: 0.5 }}>
          {TRUST_LADDER.map((step) => (
            <React.Fragment key={step.name}>
              <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 800, textAlign: 'right' }}>
                {formatDuration(step.seconds)}
              </Typography>
              <Typography variant="body2" sx={{ fontWeight: 800, color: 'var(--gl-text-primary)' }}>
                {step.name}
              </Typography>
              <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
                {step.effect}
              </Typography>
            </React.Fragment>
          ))}
        </Box>
        <Typography variant="caption" sx={{ display: 'block', mt: 1, color: 'var(--gl-text-muted)' }}>
          Trust tops out at 16 minutes per player. Hitting an animal costs 1 minute of trust (30 s with the rest of its herd),
          and for 45 s after you hurt it, its trust in you counts as zero.
        </Typography>
      </Section>

      <Section title="Gene effects">
        <Box sx={{ display: 'grid', gridTemplateColumns: 'auto minmax(0, 1fr) repeat(3, auto)', columnGap: 1.5, rowGap: 0.5, alignItems: 'center' }}>
          <Box />
          <Box />
          {GENE_LEVELS.map((level) => (
            <Typography key={level} variant="caption" sx={{ fontWeight: 800, textAlign: 'right', color: LEVEL_COLOR[level] }}>
              {LEVEL_LABEL[level]}
            </Typography>
          ))}
          {GENE_ROWS.map((row) => (
            <React.Fragment key={row.label}>
              <Typography variant="caption" sx={{ fontFamily: 'monospace', fontWeight: 800, color: 'var(--gl-text-muted)' }}>
                {row.gene}
              </Typography>
              <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
                {row.label}
              </Typography>
              {GENE_LEVELS.map((level) => (
                <Typography key={level} variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 700, textAlign: 'right', whiteSpace: 'nowrap' }}>
                  {row.value(level)}
                </Typography>
              ))}
            </React.Fragment>
          ))}
        </Box>
      </Section>

      <Section title="Inbreeding">
        An inbred animal&apos;s genes all work at <b>x{INBRED_EFFECT}</b>: an inbred Good sheep gives 11 wool instead of 16, an
        inbred Good cow&apos;s milk rounds back to 1, and inbred lifespans are{' '}
        {GENE_LEVELS.map((l) => formatDuration(B.lifespanHours * 3600 * geneMultiplier('L', l, true))).join(' / ')}. It still
        passes its Good copies on.
      </Section>

      <Section title="Dung and biofuel">
        Adult cows, bulls and sheep drop dung; calves and lambs do not. Dung fuels the Biofuel Generator, which burns one every{' '}
        {BIOFUEL_SECONDS_PER_DUNG} s ({Math.round(3600 / BIOFUEL_SECONDS_PER_DUNG)} an hour). Dung stacks to 100, so a full
        3-slot load lasts 3 hours. About <b>{goodDungAdults}</b> Good-dung adults keep one generator running.
      </Section>

      <Section title="Selling">
        The animal must be grown, not pregnant, alive, and led by you to the Livestock Vendor.
      </Section>
    </Stack>
  );
};

const Section: React.FC<{ title: string; children: React.ReactNode }> = ({ title, children }) => (
  <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 } }}>
    <Typography variant="overline" sx={{ color: 'var(--gl-text-muted)', fontWeight: 900, display: 'block', lineHeight: 1.6 }}>
      {title}
    </Typography>
    <Typography component="div" variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
      {children}
    </Typography>
  </Paper>
);
