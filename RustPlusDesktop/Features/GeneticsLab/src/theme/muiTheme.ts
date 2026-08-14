import { createTheme, ThemeOptions } from '@mui/material/styles';

declare module '@mui/material/styles' {
  interface Palette {
    geneGreen: Palette['primary'];
    geneRed: Palette['primary'];
  }
  interface PaletteOptions {
    geneGreen?: PaletteOptions['primary'];
    geneRed?: PaletteOptions['primary'];
  }
}

export const getMuiTheme = (mode: 'dark' | 'light') => {
  const isDark = mode === 'dark';

  const themeOptions: ThemeOptions = {
    palette: {
      mode,
      primary: {
        main: '#00E5FF', // Authentic Cyan / Teal
        contrastText: '#000000'
      },
      secondary: {
        main: '#4CAF50', // Rust Green
        contrastText: '#ffffff'
      },
      error: {
        main: '#E53935'
      },
      warning: {
        main: '#FFA726'
      },
      info: {
        main: '#00E5FF'
      },
      success: {
        main: '#4CAF50'
      },
      geneGreen: {
        main: '#4CAF50',
        light: '#66BB6A',
        dark: '#2E7D32',
        contrastText: '#ffffff'
      },
      geneRed: {
        main: '#C62828',
        light: '#EF5350',
        dark: '#B71C1C',
        contrastText: '#ffffff'
      },
      background: {
        default: isDark ? '#0E0E0E' : '#F5F7FA',
        paper: isDark ? '#181818' : '#FFFFFF'
      },
      text: {
        primary: isDark ? '#E0E0E0' : '#1A2027',
        secondary: isDark ? '#8E8E8E' : '#5A6475',
        disabled: isDark ? '#555555' : '#9E9E9E'
      },
      divider: isDark ? 'rgba(255, 255, 255, 0.1)' : 'rgba(0, 0, 0, 0.1)'
    },
    typography: {
      fontFamily: '"Consolas", "Roboto Mono", "Courier New", monospace, -apple-system, sans-serif',
      button: {
        textTransform: 'none',
        fontWeight: 700,
        fontFamily: '"Inter", -apple-system, sans-serif'
      }
    },
    shape: {
      borderRadius: 4
    },
    components: {
      MuiCard: {
        styleOverrides: {
          root: {
            backgroundImage: 'none',
            backgroundColor: isDark ? '#181818' : '#FFFFFF',
            border: isDark ? '1px solid #282828' : '1px solid #E0E0E0',
            boxShadow: 'none',
            borderRadius: 6
          }
        }
      },
      MuiPaper: {
        styleOverrides: {
          root: {
            backgroundImage: 'none',
            backgroundColor: isDark ? '#181818' : '#FFFFFF'
          }
        }
      },
      MuiButton: {
        styleOverrides: {
          root: {
            borderRadius: 4,
            padding: '6px 16px',
            textTransform: 'uppercase',
            fontWeight: 700,
            letterSpacing: '0.5px'
          }
        }
      },
      MuiChip: {
        styleOverrides: {
          root: {
            fontWeight: 700,
            borderRadius: 4,
            fontFamily: '"Consolas", "Roboto Mono", monospace'
          }
        }
      },
      MuiTab: {
        styleOverrides: {
          root: {
            textTransform: 'uppercase',
            fontWeight: 700,
            letterSpacing: '1px',
            fontSize: '0.85rem',
            color: isDark ? '#8E8E8E' : '#666666',
            '&.Mui-selected': {
              color: '#00E5FF'
            }
          }
        }
      },
      MuiTabs: {
        styleOverrides: {
          indicator: {
            backgroundColor: '#00E5FF',
            height: 3
          }
        }
      }
    }
  };

  return createTheme(themeOptions);
};
