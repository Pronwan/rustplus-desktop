import { describe, expect, it } from 'vitest';
import { buildPlanterSvg } from '../utils/planterExport.ts';

describe('planter image export', () => {
  it('includes the target, center, and surrounding clone genetics', () => {
    const svg = buildPlanterSvg({ target: 'GGGYYY', center: 'GHGHGH', surrounding: ['YYYYYY'] });
    expect(svg).toContain('<svg');
    expect(svg).toContain('TARGET GGGYYY');
    expect(svg).toContain('CENTER · PLANT 1ST');
    expect(svg).toContain('>H</text>');
  });

  it('labels an unspecified center as a receiver plant', () => {
    const svg = buildPlanterSvg({ target: 'GGGYYY', surrounding: ['YYYYYY'] });
    expect(svg).toContain('ANY RECEIVER');
  });
});
