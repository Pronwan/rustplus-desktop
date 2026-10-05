import React, { useEffect, useState } from 'react';
import { Box, Button, Chip, Paper, Slider, Stack, Typography } from '@mui/material';
import EditIcon from '@mui/icons-material/Edit';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import { LivestockAnimal, animalKindLabel, displayName } from '../../domain/livestock/animal.ts';
import { animalStats, formatDuration } from '../../domain/livestock/stats.ts';
import { currentCondition, estimateAnimalPrice } from '../../domain/livestock/pricing.ts';
import { LEVEL_LABEL, LIVESTOCK_GENES, LIVESTOCK_GENE_INFO, VENDOR_PRICES } from '../../domain/livestock/livestockGenes.ts';
import { breedsTrue, hidesBad, inferHerdGenotypes } from '../../domain/livestock/genotype.ts';
import { AnimalPortrait, ProductIcon, kindLabel } from './LivestockImages.tsx';
import { SexIcon, SexQuickPick } from './SexIcon.tsx';
import type { LivestockSex } from '../../domain/livestock/animal.ts';
import { LivestockGenePanel, LEVEL_COLOR } from './LivestockGeneBadges.tsx';

const panelSx = { backgroundColor: 'var(--gl-panel-bg)', borderColor: 'var(--gl-border)', borderRadius: '6px' };

function sliderDefaults(animal: LivestockAnimal) {
  const current = currentCondition(animal);
  return {
    health: current.healthState === null ? 100 : Math.round(current.healthState * 100),
    age: current.ageLived === null ? 25 : Math.round(current.ageLived * 100)
  };
}

export const AnimalDetail: React.FC<{
  animal: LivestockAnimal;
  herd: LivestockAnimal[];
  onEdit: () => void;
  onRemove: () => void;
  onSetSex: (sex: LivestockSex) => void;
}> = ({ animal, herd, onEdit, onRemove, onSetSex }) => {
  const [health, setHealth] = useState(() => sliderDefaults(animal).health);
  const [age, setAge] = useState(() => sliderDefaults(animal).age);
  // A different animal, or a fresh reading of this one, resets the what-if sliders.
  useEffect(() => {
    const defaults = sliderDefaults(animal);
    setHealth(defaults.health);
    setAge(defaults.age);
  }, [animal.id, animal.observed?.at]);
  const current = currentCondition(animal);
  const genotype = inferHerdGenotypes(herd).get(animal.id);
  const estimate = estimateAnimalPrice(animal, { healthState: health / 100, ageLived: age / 100 });
  const stats = animalStats(animal);
  const mother = herd.find((a) => a.id === animal.motherId);
  const father = herd.find((a) => a.id === animal.fatherId);

  return (
    <Stack spacing={2}>
      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 } }}>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 1, flexWrap: 'wrap' }}>
          <Box sx={{ display: 'flex', gap: 1.5, alignItems: 'center' }}>
          <AnimalPortrait animal={animal} size={56} />
          <Box>
            <Typography component="h2" variant="h6" sx={{ fontWeight: 850, color: 'var(--gl-text-primary)' }}>
              {displayName(animal)}
            </Typography>
            <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', mt: 0.5 }}>
              <Chip size="small" label={kindLabel(animal)} />
              {animal.sex === 'unknown' ? <SexQuickPick animal={animal} onPick={onSetSex} /> : <SexIcon animal={animal} size={18} />}
              {animal.inbred && <Chip size="small" color="warning" label="Inbred" />}
              {animal.source === 'scan' && <Chip size="small" variant="outlined" label="Scanned" />}
            </Box>
          </Box>
          </Box>
          <Box sx={{ display: 'flex', gap: 1 }}>
            <Button size="small" variant="outlined" startIcon={<EditIcon />} onClick={onEdit}>
              Edit
            </Button>
            <Button size="small" variant="outlined" color="error" startIcon={<DeleteOutlineIcon />} onClick={onRemove}>
              Remove
            </Button>
          </Box>
        </Box>
        <Box sx={{ mt: 2 }}>
          <LivestockGenePanel rows={animal.rows} size="lg" />
        </Box>
        {animal.observed && (
          <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', mt: 1.5 }}>
            {current.ageSeconds !== null && (
              <Chip size="small" variant="outlined" label={`Age ~${formatDuration(current.ageSeconds)}`} />
            )}
            {current.healthState !== null && (
              <Chip size="small" variant="outlined" label={`Overall ${Math.round(current.healthState * 100)}%`} />
            )}
            {current.isBaby && <Chip size="small" color="info" label="Baby: sellable once 1h old" />}
            {current.readingAgeMs !== null && (
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', alignSelf: 'center' }}>
                read {formatDuration(current.readingAgeMs / 1000)} ago
              </Typography>
            )}
          </Box>
        )}
        {genotype && (
          <Box sx={{ mt: 2 }}>
            <Typography variant="overline" sx={{ color: 'var(--gl-text-muted)', fontWeight: 900 }}>
              Hidden copies
            </Typography>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', mb: 0.75 }}>
              Each badge shows the better of two copies. Chance this animal passes Good every time (pure), or hides a
              Bad copy, estimated from {mother || father ? 'its recorded parents' : 'wild odds'}.
            </Typography>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))', gap: 0.75 }}>
              {LIVESTOCK_GENES.map((gene, i) => {
                const level = animal.rows[0].levels[i];
                return (
                  <Typography key={gene} variant="caption" sx={{ fontFamily: 'monospace', color: 'var(--gl-text-secondary)' }}>
                    <b>{gene}</b> {level ? LEVEL_LABEL[level] : '?'}: {Math.round(breedsTrue(genotype[i]) * 100)}% pure,{' '}
                    {Math.round(hidesBad(genotype[i]) * 100)}% hides Bad
                  </Typography>
                );
              })}
            </Box>
          </Box>
        )}
        {(mother || father) && (
          <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', mt: 1.5 }}>
            {mother && <>Mother: <b>{displayName(mother)}</b>. </>}
            {father && <>Father: <b>{displayName(father)}</b>.</>}
          </Typography>
        )}
        {animal.notes && (
          <Typography variant="body2" sx={{ color: 'var(--gl-text-muted)', mt: 1, whiteSpace: 'pre-wrap' }}>
            {animal.notes}
          </Typography>
        )}
      </Paper>

      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 } }}>
        <Typography variant="overline" sx={{ color: 'var(--gl-text-muted)', fontWeight: 900 }}>
          What these genes do
        </Typography>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block', mb: 1 }}>
          From the genes the panel shows{animal.inbred ? ', weakened by inbreeding (every gene effect x0.7)' : ''}. Values checked against the game on 2 Oct 2026.
        </Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'auto 1fr auto auto', columnGap: 1.5, rowGap: 0.75, alignItems: 'center' }}>
          {stats.map((stat) => (
            <React.Fragment key={stat.label}>
              <Box
                title={`${LIVESTOCK_GENE_INFO[stat.gene].name}: ${LIVESTOCK_GENE_INFO[stat.gene].summary}`}
                sx={{
                  width: 22,
                  height: 22,
                  borderRadius: '50%',
                  display: 'grid',
                  placeItems: 'center',
                  fontSize: 11,
                  fontWeight: 800,
                  fontFamily: 'monospace',
                  color: '#fff',
                  backgroundColor: LEVEL_COLOR[stat.level ?? 'mid'],
                  opacity: stat.applies ? 1 : 0.4
                }}
              >
                {stat.gene}
              </Box>
              <Typography variant="body2" sx={{ color: stat.applies ? 'var(--gl-text-secondary)' : 'var(--gl-text-faint)', display: 'flex', alignItems: 'center', gap: 0.75 }}>
                {stat.icon && <ProductIcon name={stat.icon} />}
                {stat.label}
              </Typography>
              <Typography
                variant="body2"
                sx={{
                  fontFamily: 'monospace',
                  fontWeight: 800,
                  textAlign: 'right',
                  color: !stat.applies
                    ? 'var(--gl-text-faint)'
                    : stat.level === 'high'
                      ? 'var(--gl-success)'
                      : stat.level === 'low'
                        ? 'var(--gl-error)'
                        : 'var(--gl-text-primary)'
                }}
              >
                {stat.applies ? stat.value : '-'}
              </Typography>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontFamily: 'monospace', textAlign: 'right' }}>
                base {stat.baseline}
              </Typography>
              {stat.note && (
                <Typography variant="caption" sx={{ gridColumn: '2 / -1', mt: -0.5, color: 'var(--gl-text-muted)' }}>
                  {stat.note}
                </Typography>
              )}
            </React.Fragment>
          ))}
        </Box>
      </Paper>

      <Paper variant="outlined" sx={{ ...panelSx, p: { xs: 1.5, sm: 2 } }}>
        <Typography variant="overline" sx={{ color: 'var(--gl-text-muted)', fontWeight: 900 }}>
          Stablehand sale estimate
        </Typography>
        <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1 }}>
          <ProductIcon name="scrap" size={30} />
          <Typography sx={{ fontSize: 32, fontWeight: 900, fontFamily: 'monospace', color: 'var(--gl-primary)' }}>
            ~{estimate.price}
          </Typography>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)' }}>
            scrap (range 3-78)
          </Typography>
        </Box>
        {animal.observed && (
          <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)', display: 'block' }}>
            Health and age start from the last reading of the panel; move the sliders to try other values.
          </Typography>
        )}
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', display: 'block' }}>
          50 x type {estimate.factors.type} x genes {estimate.factors.genes.toFixed(2)} x health{' '}
          {estimate.factors.health.toFixed(2)} x age {estimate.factors.age.toFixed(2)}. The game randomises the actual offer.
        </Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr' }, gap: 2, mt: 1.5 }}>
          <Box>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
              Health / needs: {health}%
            </Typography>
            <Slider size="small" value={health} min={0} max={100} onChange={(_, v) => setHealth(v as number)} aria-label="Health" />
          </Box>
          <Box>
            <Typography variant="caption" sx={{ color: 'var(--gl-text-secondary)' }}>
              Lifespan lived: {age}%
            </Typography>
            <Slider size="small" value={age} min={0} max={100} onChange={(_, v) => setAge(v as number)} aria-label="Age" />
          </Box>
        </Box>
        <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)' }}>
          To sell, the animal must be grown, not pregnant, alive, and led by you to the Livestock Vendor. The vendor
          sells bonded calves for {VENDOR_PRICES.calf} scrap and lambs for {VENDOR_PRICES.lamb}.
        </Typography>
      </Paper>
    </Stack>
  );
};
