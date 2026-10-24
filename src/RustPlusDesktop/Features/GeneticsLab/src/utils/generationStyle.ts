/**
 * Distinct visual identity per breeding generation so users can tell GEN 1 / 2 / 3
 * routes apart at a glance. Each generation gets its own hue, an icon glyph, and a
 * short label; higher generations read as "harder / more steps".
 */
export interface GenerationVisual {
  color: string;
  tint: string;
  border: string;
  label: string;
  /** Compact glyph used as a scannable marker (e.g. on a card corner). */
  icon: string;
}

const GEN_VISUALS: Record<number, GenerationVisual> = {
  1: { color: 'var(--gl-success)', tint: 'rgba(76, 175, 80, 0.15)', border: 'var(--gl-success)', label: 'GEN 1', icon: '●' },
  2: { color: 'var(--gl-warning)', tint: 'rgba(255, 152, 0, 0.15)', border: 'var(--gl-warning)', label: 'GEN 2', icon: '◆' },
  3: { color: '#AB47BC', tint: 'rgba(171, 71, 188, 0.16)', border: '#AB47BC', label: 'GEN 3', icon: '▲' }
};

// GEN 4+ (rare) share a red identity.
const GEN_HIGH: GenerationVisual = {
  color: 'var(--gl-error)',
  tint: 'rgba(229, 57, 53, 0.15)',
  border: 'var(--gl-error)',
  label: 'GEN 4+',
  icon: '★'
};

export const generationVisual = (gen: number): GenerationVisual => {
  return GEN_VISUALS[gen] ?? (gen >= 4 ? { ...GEN_HIGH, label: `GEN ${gen}` } : GEN_VISUALS[1]);
};
