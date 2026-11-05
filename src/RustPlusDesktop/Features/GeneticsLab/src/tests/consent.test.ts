import './setupLocalStorage.ts';
import { beforeEach, describe, expect, it } from 'vitest';
import { StorageService } from '../services/storageService.ts';

describe('analytics preference', () => {
  beforeEach(() => localStorage.clear());

  it('defaults anonymous analytics on and preserves a saved opt-out', () => {
    expect(StorageService.getConsent().analytics).toBe(true);

    StorageService.saveConsent({
      isPreferenceDecided: true,
      functional: true,
      analytics: false,
      advertisement: false
    });

    expect(StorageService.getConsent().analytics).toBe(false);
  });
});
