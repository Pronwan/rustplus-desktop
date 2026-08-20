import { describe, expect, it } from 'vitest';
import { nextRoutePageSize } from '../components/workspace/Routes/RouteGrid.tsx';

describe('progressive route expansion', () => {
  it('adds one page without passing the result total', () => {
    expect(nextRoutePageSize(18, 500)).toBe(36);
    expect(nextRoutePageSize(495, 500)).toBe(500);
  });
});
