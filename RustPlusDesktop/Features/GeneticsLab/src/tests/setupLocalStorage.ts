const storage: Record<string, string> = {};

export const mockLocalStorage = {
  getItem: (key: string) => storage[key] || null,
  setItem: (key: string, value: string) => {
    storage[key] = String(value);
  },
  removeItem: (key: string) => {
    delete storage[key];
  },
  clear: () => {
    for (const key of Object.keys(storage)) {
      delete storage[key];
    }
  }
};

if (typeof globalThis.localStorage === 'undefined') {
  (globalThis as any).localStorage = mockLocalStorage;
}
