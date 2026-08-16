import { useTheme } from '@mui/material';

/**
 * Semantic color hook for the Genetics Lab UI.
 *
 * Many components were originally authored with hard-coded dark-theme hex values,
 * which left them broken in light mode. This hook exposes theme-aware, role-based
 * colors sourced from the active MUI palette (see designTokens.ts) so components
 * render correctly in both light and dark themes.
 */
export const useLabColors = () => {
  const theme = useTheme();
  const p = theme.palette;
  const isDark = p.mode === 'dark';

  return {
    isDark,

    // Structural backgrounds (map 1:1 to design tokens)
    appBg: p.customBg.app,
    panelBg: p.customBg.panel,
    panelHeaderBg: p.customBg.panelHeader,
    elevatedBg: p.customBg.elevated,
    cardBg: p.customBg.card,
    cardHoverBg: p.customBg.cardHover,
    inputBg: p.customBg.input,
    backdrop: p.customBg.backdrop,

    // Mid-tone interactive surfaces (secondary buttons, chips, tracks)
    surface: isDark ? '#262626' : '#EEF2F6',
    surfaceHover: isDark ? '#333333' : '#E2E8F0',
    surfaceActive: isDark ? '#3A3A3A' : '#D6DEE7',

    // Text
    textPrimary: p.text.primary,
    textSecondary: p.text.secondary,
    textMuted: p.text.disabled,
    textFaint: isDark ? '#555555' : '#A0AAB8',

    // Borders
    border: p.divider,
    borderStrong: isDark ? '#383838' : '#CBD5E1',
    borderSubtle: isDark ? '#202020' : '#EDF2F7',

    // Brand
    primary: p.primary.main,
    primaryHover: p.primary.light,
    onPrimary: p.primary.contrastText,
    primarySubtle: isDark ? 'rgba(0, 229, 255, 0.08)' : 'rgba(2, 132, 199, 0.08)',
    primaryBorder: isDark ? 'rgba(0, 229, 255, 0.3)' : 'rgba(2, 132, 199, 0.35)',

    // Status
    error: p.error.main,
    errorHover: isDark ? '#D32F2F' : '#B91C1C',
    warning: p.warning.main,
    warningHover: isDark ? '#FB8C00' : '#B45309',
    success: p.success.main,
    accent: p.secondary.main,

    // Overlays / shadows
    overlay: isDark ? 'rgba(0,0,0,0.5)' : 'rgba(15,23,42,0.25)',
    overlaySoft: isDark ? 'rgba(0,0,0,0.4)' : 'rgba(15,23,42,0.18)',
  };
};

export type LabColors = ReturnType<typeof useLabColors>;
