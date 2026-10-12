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
        contrastText: tokens.colors.text.inverse
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
      fontFamily: '"Segoe UI Variable Text", "Segoe UI", sans-serif',
      h4: {
        fontWeight: 600,
        letterSpacing: 'normal'
      },
      h5: {
        fontWeight: 600,
        letterSpacing: 'normal'
      },
      h6: {
        fontWeight: 600,
        fontSize: '1rem',
        letterSpacing: 'normal'
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
        fontWeight: 600,
        letterSpacing: 'normal'
      }
    },
    shape: {
      borderRadius: tokens.spacing.borderRadius
    },
    components: {
      MuiButtonBase: {
        styleOverrides: {
          root: {
            '&.Mui-focusVisible': {
              outline: `3px solid ${tokens.colors.border.focus}`,
              outlineOffset: 2
            }
          }
        }
      },
      MuiIconButton: {
        styleOverrides: {
          root: {
            minWidth: 32,
            minHeight: 32
          }
        }
      },
      MuiCssBaseline: {
        styleOverrides: {
          body: {
            backgroundColor: tokens.colors.bg.app,
            color: tokens.colors.text.primary,
            scrollbarColor: `${tokens.colors.border.strong} ${tokens.colors.bg.app}`
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
            borderRadius: 8,
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
            fontWeight: 600,
            textTransform: 'none'
          },
          contained: ({ ownerState }) => ({
            boxShadow: 'none',
            ...(ownerState.color === 'primary' ? {
              backgroundColor: tokens.colors.brand.primary,
              color: tokens.colors.text.inverse,
              '&:hover': {
                backgroundColor: tokens.colors.brand.primaryHover
              }
            } : {})
          })
        }
      },
      MuiChip: {
        styleOverrides: {
          root: {
            fontWeight: 600,
            borderRadius: 4,
            fontSize: '0.72rem'
          }
        }
      },
      MuiTab: {
        styleOverrides: {
          root: {
            textTransform: 'none',
            fontWeight: 600,
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
            backgroundColor: isDark ? tokens.colors.bg.elevated : '#1E293B',
            color: '#FFFFFF',
            border: `1px solid ${isDark ? tokens.colors.border.default : '#475569'}`,
            fontSize: '0.75rem',
            boxShadow: '0 4px 12px rgba(0,0,0,0.3)'
          }
        }
      }
    }
  };

  return createTheme(themeOptions);
};
