import { describe, expect, it } from 'vitest';
import { darkTokens, getTokens } from '../theme/designTokens.ts';
import { getMuiTheme } from '../theme/muiTheme.ts';

describe('Fluent theme', () => {
  it('applies the shared surface, text and accent tokens to MUI', () => {
    const theme = getMuiTheme('dark');
    expect(theme.palette.background.default).toBe(darkTokens.colors.bg.app);
    expect(theme.palette.background.paper).toBe(darkTokens.colors.bg.panel);
    expect(theme.palette.text.primary).toBe(darkTokens.colors.text.primary);
    expect(theme.palette.text.secondary).toBe(darkTokens.colors.text.secondary);
    expect(theme.palette.primary.main).toBe(darkTokens.colors.brand.primary);
    expect(theme.palette.primary.contrastText).toBe(darkTokens.colors.text.inverse);
    expect(theme.shape.borderRadius).toBe(4);
  });

  it('retains semantic button colors and both theme/density choices', () => {
    const theme = getMuiTheme('dark');
    const contained = theme.components?.MuiButton?.styleOverrides?.contained;
    expect(typeof contained).toBe('function');
    if (typeof contained !== 'function') throw new Error('Missing button variant styles');
    const primary = contained({ ownerState: { color: 'primary' }, theme } as Parameters<typeof contained>[0]);
    const danger = contained({ ownerState: { color: 'error' }, theme } as Parameters<typeof contained>[0]);
    expect(primary).toHaveProperty('backgroundColor', theme.palette.primary.main);
    expect(danger).not.toHaveProperty('backgroundColor');
    expect(getMuiTheme('light').palette.mode).toBe('light');
    expect(getTokens('dark', 'compact').spacing.rowHeight).toBeLessThan(getTokens('dark').spacing.rowHeight);
  });
});
