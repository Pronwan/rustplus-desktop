import { createTheme, ThemeOptions } from '@mui/material/styles';
import { getTokens } from './designTokens.ts';

declare module '@mui/material/styles' {
  interface Palette {
    geneGreen: Palette['primary'];
    geneRed: Palette['primary'];
    customBg: {
      app: string;
      panel: string;
      panelHeader: string;
      elevated: string;
      card: string;
      cardHover: string;
      input: string;
      backdrop: string;
    };
  }
  interface PaletteOptions {
    geneGreen?: PaletteOptions['primary'];
    geneRed?: PaletteOptions['primary'];
    customBg?: {
      app: string;
      panel: string;
      panelHeader: string;
      elevated: string;
      card: string;
      cardHover: string;
      input: string;
      backdrop: string;
    };
  }
}

export const getMuiTheme = (mode: 'dark' | 'light', density: 'comfortable' | 'compact' = 'comfortable') => {
  const isDark = mode === 'dark';
  const tokens = getTokens(mode, density);

  const themeOptions: ThemeOptions = {
    palette: {
      mode,
      primary: {
        main: tokens.colors.brand.primary,
        light: tokens.colors.brand.primaryHover,
        contrastText: isDark ? '#000000' : '#FFFFFF'
      },
      secondary: {
        main: tokens.colors.brand.accent,
        contrastText: '#FFFFFF'
      },
      error: {
        main: tokens.colors.status.error
      },
      warning: {
        main: tokens.colors.status.warning
      },
      info: {
        main: tokens.colors.status.info
      },
      success: {
        main: tokens.colors.status.success
      },
      geneGreen: {
        main: tokens.colors.gene.greenBg,
        contrastText: tokens.colors.gene.greenText
      },
      geneRed: {
        main: tokens.colors.gene.redBg,
        contrastText: tokens.colors.gene.redText
      },
      customBg: tokens.colors.bg,
      background: {
        default: tokens.colors.bg.app,
        paper: tokens.colors.bg.panel
      },
      text: {
        primary: tokens.colors.text.primary,
        secondary: tokens.colors.text.secondary,
        disabled: tokens.colors.text.muted
      },
      divider: tokens.colors.border.default
    },
    typography: {
      fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
      h4: {
        fontWeight: 800,
        letterSpacing: '-0.02em'
      },
      h5: {
        fontWeight: 700,
        letterSpacing: '-0.01em'
      },
      h6: {
        fontWeight: 700,
        fontSize: '1rem',
        letterSpacing: '-0.01em'
      },
      subtitle1: {
        fontWeight: 600
      },
      subtitle2: {
        fontWeight: 600,
        fontSize: '0.82rem'
      },
      body1: {
        fontSize: '0.875rem'
      },
      body2: {
        fontSize: '0.8125rem'
      },
      caption: {
        fontSize: '0.72rem'
      },
      button: {
        textTransform: 'none',
        fontWeight: 700,
        letterSpacing: '0.02em'
      }
    },
    shape: {
      borderRadius: tokens.spacing.borderRadius
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          body: {
            backgroundColor: tokens.colors.bg.app,
            color: tokens.colors.text.primary,
            scrollbarColor: isDark ? '#333333 #121212' : '#CBD5E1 #F1F5F9'
          }
        }
      },
      MuiCard: {
        styleOverrides: {
          root: {
            backgroundImage: 'none',
            backgroundColor: tokens.colors.bg.card,
            border: `1px solid ${tokens.colors.border.default}`,
            boxShadow: 'none',
            borderRadius: tokens.spacing.borderRadius,
            transition: 'border-color 0.15s ease, background-color 0.15s ease, transform 0.15s ease'
          }
        }
      },
      MuiPaper: {
        styleOverrides: {
          root: {
            backgroundImage: 'none',
            backgroundColor: tokens.colors.bg.panel
          }
        }
      },
      MuiButton: {
        styleOverrides: {
          root: {
            borderRadius: tokens.spacing.borderRadius,
            padding: density === 'compact' ? '4px 10px' : '6px 14px',
            fontSize: density === 'compact' ? '0.75rem' : '0.8rem',
            fontWeight: 700,
            textTransform: 'none'
          },
          contained: {
            backgroundColor: tokens.colors.brand.primary,
            color: isDark ? '#000000' : '#FFFFFF',
            '&:hover': {
              backgroundColor: tokens.colors.brand.primaryHover
            }
          }
        }
      },
      MuiChip: {
        styleOverrides: {
          root: {
            fontWeight: 700,
            borderRadius: 4,
            fontSize: '0.72rem'
          }
        }
      },
      MuiTab: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            fontWeight: 700,
            fontSize: '0.82rem',
            color: tokens.colors.text.secondary,
            minHeight: density === 'compact' ? 36 : 44,
            '&.Mui-selected': {
              color: tokens.colors.brand.primary
            }
          }
        }
      },
      MuiTabs: {
        styleOverrides: {
          root: {
            minHeight: density === 'compact' ? 36 : 44
          },
          indicator: {
            backgroundColor: tokens.colors.brand.primary,
            height: 2.5
          }
        }
      },
      MuiTooltip: {
        styleOverrides: {
          tooltip: {
            backgroundColor: isDark ? '#1C1C1C' : '#1E293B',
            color: '#FFFFFF',
            border: `1px solid ${isDark ? '#333333' : '#475569'}`,
            fontSize: '0.75rem',
            boxShadow: '0 4px 12px rgba(0,0,0,0.3)'
          }
        }
      }
    }
  };

  return createTheme(themeOptions);
};
