import { describe, it, expect } from 'vitest';
import { LivestockReadStabilizer } from '../services/livestock/livestockReadStabilizer.ts';
import { decodeGeneRow, LivestockGeneRow } from '../domain/livestock/animal.ts';

function read(top: string, bottom: string, confidence = 0.8) {
  return {
    rows: [decodeGeneRow(top)!, decodeGeneRow(bottom)!] as [LivestockGeneRow, LivestockGeneRow],
    confidence
  };
}

describe('livestock read stabiliser', () => {
  it('accepts an animal once two frames agree, and only once', () => {
    const s = new LivestockReadStabilizer();
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 0)).toBeNull();
    const event = s.push(read('rrrrr|0p', 'rrrrr|1b'), 160);
    expect(event?.type).toBe('confirmed');
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 320)).toBeNull();
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 480)).toBeNull();
  });

  it('does not accept two frames that disagree', () => {
    const s = new LivestockReadStabilizer();
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 0)).toBeNull();
    expect(s.push(read('rrgrr|0p', 'rrrrr|1b'), 160)).toBeNull();
    expect(s.push(read('rrgrr|0p', 'rrrrr|1b'), 320)?.type).toBe('confirmed');
  });

  it('accepts a very clean read straight away', () => {
    const s = new LivestockReadStabilizer();
    expect(s.push(read('ggggg|0p', 'nnnnn|1b', 1), 0)?.type).toBe('confirmed');
  });

  it('reports the panel gone, then accepts the same animal again', () => {
    const s = new LivestockReadStabilizer();
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 0);
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 160);
    expect(s.push(null, 500)).toBeNull();
    expect(s.push(null, 1200)?.type).toBe('lost');
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 1300);
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 1460)?.type).toBe('confirmed');
  });

  it('moves straight on to a different animal without waiting for the panel to clear', () => {
    const s = new LivestockReadStabilizer();
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 0);
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 160);
    s.push(read('ggnnr|0p', 'nnrrg|1b'), 320);
    expect(s.push(read('ggnnr|0p', 'nnrrg|1b'), 480)?.type).toBe('confirmed');
  });

  it('can be re-armed to report the animal in view again', () => {
    const s = new LivestockReadStabilizer();
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 0);
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 160);
    s.rearm();
    s.push(read('rrrrr|0p', 'rrrrr|1b'), 320);
    expect(s.push(read('rrrrr|0p', 'rrrrr|1b'), 480)?.type).toBe('confirmed');
  });
});
