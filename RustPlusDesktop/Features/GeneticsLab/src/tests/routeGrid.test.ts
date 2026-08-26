import { describe, expect, it } from 'vitest';
import { nextRoutePageSize } from '../components/workspace/Routes/RouteGrid.tsx';

describe('progressive route expansion', () => {
  it('adds one page without passing the result total', () => {
    expect(nextRoutePageSize(8, 500)).toBe(16);
    expect(nextRoutePageSize(495, 500)).toBe(500);
  });
});
