export interface DesignTokens {
  mode: 'dark' | 'light';
  density: 'comfortable' | 'compact';
  colors: {
    bg: {
      app: string;
      panel: string;
      panelHeader: string;
      elevated: string;
      card: string;
      cardHover: string;
      input: string;
      backdrop: string;
    };
    text: {
      primary: string;
      secondary: string;
      muted: string;
      inverse: string;
    };
    border: {
      default: string;
      subtle: string;
      strong: string;
      focus: string;
    };
    brand: {
      primary: string;
      primaryHover: string;
      primaryGlow: string;
      primarySubtle: string;
      accent: string;
    };
    gene: {
      greenBg: string;
      greenText: string;
      greenBorder: string;
      redBg: string;
      redText: string;
      redBorder: string;
      emptyBg: string;
      emptyText: string;
      donorBorder: string;
      highlightBorder: string;
    };
    status: {
      success: string;
      successBg: string;
      warning: string;
      warningBg: string;
      error: string;
      errorBg: string;
      info: string;
      infoBg: string;
    };
  };
  spacing: {
    cardPadding: number;
    gap: number;
    borderRadius: number;
    rowHeight: number;
  };
}

export const darkTokens: DesignTokens = {
  mode: 'dark',
  density: 'comfortable',
  colors: {
    bg: {
      app: '#0E0E0E',
      panel: '#141414',
      panelHeader: '#181818',
      elevated: '#1E1E1E',
      card: '#161616',
      cardHover: '#1C1C1C',
      input: '#1A1A1A',
      backdrop: 'rgba(10, 10, 10, 0.85)'
    },
    text: {
      primary: '#ECECEC',
      secondary: '#9E9E9E',
      muted: '#666666',
      inverse: '#0E0E0E'
    },
    border: {
      default: '#282828',
      subtle: '#202020',
      strong: '#383838',
      focus: '#00E5FF'
    },
    brand: {
      primary: '#00E5FF',
      primaryHover: '#33EBFF',
      primaryGlow: 'rgba(0, 229, 255, 0.25)',
      primarySubtle: 'rgba(0, 229, 255, 0.08)',
      accent: '#FF9800'
    },
    gene: {
      greenBg: '#4A7C17',
      greenText: '#FFFFFF',
      greenBorder: '#598518',
      redBg: '#8A2E22',
      redText: '#FFFFFF',
      redBorder: '#94382A',
      emptyBg: '#2A2A2A',
      emptyText: '#777777',
      donorBorder: '#FFD700',
      highlightBorder: '#00E5FF'
    },
    status: {
      success: '#4CAF50',
      successBg: 'rgba(76, 175, 80, 0.12)',
      warning: '#FFA726',
      warningBg: 'rgba(255, 167, 38, 0.12)',
      error: '#E53935',
      errorBg: 'rgba(229, 57, 53, 0.12)',
      info: '#00E5FF',
      infoBg: 'rgba(0, 229, 255, 0.12)'
    }
  },
  spacing: {
    cardPadding: 16,
    gap: 16,
    borderRadius: 6,
    rowHeight: 32
  }
};

export const lightTokens: DesignTokens = {
  mode: 'light',
  density: 'comfortable',
  colors: {
    bg: {
      app: '#F4F6F8',
      panel: '#FFFFFF',
      panelHeader: '#F8FAFC',
      elevated: '#FFFFFF',
      card: '#FFFFFF',
      cardHover: '#F8FAFC',
      input: '#F1F5F9',
      backdrop: 'rgba(15, 23, 42, 0.6)'
    },
    text: {
      primary: '#1E293B',
      secondary: '#64748B',
      muted: '#94A3B8',
      inverse: '#FFFFFF'
    },
    border: {
      default: '#E2E8F0',
      subtle: '#EDF2F7',
      strong: '#CBD5E1',
      focus: '#0284C7'
    },
    brand: {
      primary: '#0284C7',
      primaryHover: '#0369A1',
      primaryGlow: 'rgba(2, 132, 199, 0.2)',
      primarySubtle: 'rgba(2, 132, 199, 0.08)',
      accent: '#D97706'
    },
    gene: {
      greenBg: '#3F7013',
      greenText: '#FFFFFF',
      greenBorder: '#4E881A',
      redBg: '#9B2C1F',
      redText: '#FFFFFF',
      redBorder: '#B91C1C',
      emptyBg: '#E2E8F0',
      emptyText: '#64748B',
      donorBorder: '#B45309',
      highlightBorder: '#0284C7'
    },
    status: {
      success: '#16A34A',
      successBg: 'rgba(22, 163, 74, 0.12)',
      warning: '#D97706',
      warningBg: 'rgba(217, 119, 6, 0.12)',
      error: '#DC2626',
      errorBg: 'rgba(220, 38, 38, 0.12)',
      info: '#0284C7',
      infoBg: 'rgba(2, 132, 199, 0.12)'
    }
  },
  spacing: {
    cardPadding: 16,
    gap: 16,
    borderRadius: 6,
    rowHeight: 32
  }
};

export const getTokens = (mode: 'dark' | 'light', density: 'comfortable' | 'compact' = 'comfortable'): DesignTokens => {
  const base = mode === 'dark' ? darkTokens : lightTokens;
  if (density === 'compact') {
    return {
      ...base,
      density: 'compact',
      spacing: {
        cardPadding: 10,
        gap: 10,
        borderRadius: 4,
        rowHeight: 26
      }
    };
  }
  return base;
};
