import React from 'react';
import { FormControl, InputLabel, MenuItem, Select } from '@mui/material';
import { useLivestock } from '../../context/LivestockContext.tsx';
import { LivestockSex, LivestockSpecies } from '../../domain/livestock/animal.ts';

const KINDS: Array<{ value: string; label: string; species: LivestockSpecies; sex: LivestockSex }> = [
  { value: 'cattle:female', label: 'Cow', species: 'cattle', sex: 'female' },
  { value: 'cattle:male', label: 'Bull', species: 'cattle', sex: 'male' },
  { value: 'sheep:female', label: 'Ewe', species: 'sheep', sex: 'female' },
  { value: 'sheep:male', label: 'Ram', species: 'sheep', sex: 'male' },
  { value: 'cattle:unknown', label: 'Cattle (sex unknown)', species: 'cattle', sex: 'unknown' },
  { value: 'sheep:unknown', label: 'Sheep (sex unknown)', species: 'sheep', sex: 'unknown' }
];

/**
 * What scanned animals are filed as. Auto-detect reads the panel's portrait: species always,
 * sex for adult cattle (cow vs bull). The manual kinds force a choice instead.
 */
export const ScanKindSelect: React.FC = () => {
  const { scanAuto, setScanAuto, scanSpecies, scanSex, setScanKind } = useLivestock();
  return (
    <FormControl size="small" sx={{ minWidth: 170 }}>
      <InputLabel id="livestock-scan-kind">Add scans as</InputLabel>
      <Select
        labelId="livestock-scan-kind"
        label="Add scans as"
        value={scanAuto ? 'auto' : `${scanSpecies}:${scanSex}`}
        onChange={(e) => {
          if (e.target.value === 'auto') {
            setScanAuto(true);
            return;
          }
          const kind = KINDS.find((k) => k.value === e.target.value);
          if (kind) setScanKind(kind.species, kind.sex);
        }}
      >
        <MenuItem value="auto">Auto-detect</MenuItem>
        {KINDS.map((kind) => (
          <MenuItem key={kind.value} value={kind.value}>
            {kind.label}
          </MenuItem>
        ))}
      </Select>
    </FormControl>
  );
};
