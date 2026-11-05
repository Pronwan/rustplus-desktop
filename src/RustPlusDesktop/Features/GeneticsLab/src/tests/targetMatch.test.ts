import { describe, it, expect } from 'vitest';
import {
  targetHasConstraint,
  isExactMatch,
  meetsAtLeast,
  targetCloseness,
  positionMatches
} from '../utils/targetMatch.ts';

describe('targetMatch', () => {
  describe('targetHasConstraint', () => {
    it('is false for all-wildcard / empty targets', () => {
      expect(targetHasConstraint('******')).toBe(false);
      expect(targetHasConstraint('??????')).toBe(false);
      expect(targetHasConstraint('')).toBe(false);
    });
    it('is true when any concrete gene is present', () => {
      expect(targetHasConstraint('G*****')).toBe(true);
      expect(targetHasConstraint('GGGYYY')).toBe(true);
    });
  });

  describe('exact', () => {
    it('matches identical strings', () => {
      expect(isExactMatch('GGGYYY', 'GGGYYY')).toBe(true);
    });
    it('rejects different order (position matters)', () => {
      expect(isExactMatch('GYGYGY', 'GGGYYY')).toBe(false);
    });
    it('honors wildcards per slot', () => {
      expect(isExactMatch('GGGABC'.replace('ABC', 'YYY'), 'GGG***')).toBe(true);
      expect(isExactMatch('GGGYYW', 'GGG**W')).toBe(true);
      expect(isExactMatch('GGGYYY', 'GGG**W')).toBe(false);
    });
    it('all-wildcard target matches anything', () => {
      expect(isExactMatch('WXWXWX', '******')).toBe(true);
    });
    it('is case-insensitive', () => {
      expect(isExactMatch('gggyyy', 'GGGYYY')).toBe(true);
    });
  });

  describe('at-least', () => {
    it('requires at least the target gene counts, order-independent', () => {
      expect(meetsAtLeast('GYGYGY', 'GGGYYY')).toBe(true); // 3G 3Y
      expect(meetsAtLeast('GGGGYY', 'GGGYYY')).toBe(false); // only 2Y < 3
      expect(meetsAtLeast('GGGGGG', 'GGGYYY')).toBe(false); // 0Y
    });
    it('wildcards impose no minimum', () => {
      expect(meetsAtLeast('GGGWWW', 'GGG***')).toBe(true); // needs >=3G only
      expect(meetsAtLeast('GGYYYY', 'GGG***')).toBe(false); // only 2G
    });
    it('counts red genes too when required', () => {
      expect(meetsAtLeast('GGGGGW', 'W*****')).toBe(true);
      expect(meetsAtLeast('GGGGGG', 'W*****')).toBe(false);
    });
    it('all-green 6G target only satisfied by GGGGGG', () => {
      expect(meetsAtLeast('GGGGGG', 'GGGGGG')).toBe(true);
      expect(meetsAtLeast('GGGGGY', 'GGGGGG')).toBe(false);
    });
  });

  describe('best-possible ranking helpers', () => {
    it('closeness = multiset overlap with target', () => {
      expect(targetCloseness('GGGYYY', 'GGGYYY')).toBe(6);
      expect(targetCloseness('GYGYGY', 'GGGYYY')).toBe(6); // same multiset
      expect(targetCloseness('GGGGYY', 'GGGYYY')).toBe(5); // 3G + 2Y
      expect(targetCloseness('GGGGGG', 'GGGYYY')).toBe(3); // 3G + 0Y
      expect(targetCloseness('WWWWWW', 'GGGYYY')).toBe(0);
    });
    it('closeness ignores wildcard slots', () => {
      expect(targetCloseness('GGGWWW', 'GGG***')).toBe(3);
      expect(targetCloseness('WWWWWW', 'GGG***')).toBe(0);
    });
    it('positionMatches counts exact-slot hits on concrete genes only', () => {
      expect(positionMatches('GGGYYY', 'GGGYYY')).toBe(6);
      // target G G G Y Y Y  vs  G Y G Y G Y → slots 0,2,3,5 match
      expect(positionMatches('GYGYGY', 'GGGYYY')).toBe(4);
      expect(positionMatches('WWWYYY', 'GGG***')).toBe(0);
    });
    it('exact and same-multiset tie on closeness but exact wins on positions', () => {
      const target = 'GGGYYY';
      expect(targetCloseness('GGGYYY', target)).toBe(targetCloseness('GYGYGY', target));
      expect(positionMatches('GGGYYY', target)).toBeGreaterThan(positionMatches('GYGYGY', target));
    });
  });
});
