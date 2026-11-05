import React from 'react';
import {
  Card,
  CardContent,
  Typography,
  Stack,
  Box,
  Divider,
  Button,
  Tooltip
} from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import { useApp } from '../../context/AppContext.tsx';
import { SaplingDetailed } from '../common/SaplingDetailed.tsx';

export const SaplingListPreview: React.FC = () => {
  const { sourceSaplings, setSourceSaplings, setGeneInputText } = useApp();

  const handleRemove = (idx: number) => {
    const next = sourceSaplings.filter((_, i) => i !== idx);
    setSourceSaplings(next);
    setGeneInputText(next.map(s => s.toString()).join('\n'));
  };

  const handleCopyAll = () => {
    const all = sourceSaplings.map(s => s.toString()).join('\n');
    navigator.clipboard.writeText(all);
  };

  if (sourceSaplings.length === 0) return null;

  return (
    <Card variant="outlined">
      <CardContent sx={{ p: 2 }}>
        <Stack spacing={1.5}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="subtitle2" sx={{ fontWeight: 700 }}>
              Parsed Source Plants ({sourceSaplings.length})
            </Typography>

            <Tooltip title="Copy all plants">
              <Button
                size="small"
                variant="text"
                startIcon={<ContentCopyIcon fontSize="small" />}
                onClick={handleCopyAll}
                sx={{ fontSize: '0.75rem' }}
              >
                Copy All
              </Button>
            </Tooltip>
          </Box>

          <Divider />

          <Stack spacing={1} sx={{ maxHeight: 320, overflowY: 'auto', pr: 0.5 }}>
            {sourceSaplings.map((sapling, idx) => (
              <SaplingDetailed
                key={`${sapling.toString()}-${idx}`}
                sapling={sapling}
                onRemove={() => handleRemove(idx)}
              />
            ))}
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
};
