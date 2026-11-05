import { describe, it, expect } from 'vitest';
import { CloneUtils } from '../domain/genetics/Clone.ts';
import { StorageService } from '../services/storageService.ts';

describe('Import and Export Workspace Data', () => {
  it('should validate and create valid clones from exported JSON format', () => {
    const rawExport = {
      version: '3.1.0',
      exportedAt: '2026-08-15T00:00:00.000Z',
      cropType: 'hemp',
      targetConfig: {
        targetGenetics: 'GGGYYY',
        matchMode: 'exact'
      },
      clones: [
        {
          id: 'clone_1',
          genetics: 'GGYYHH',
          cropType: 'hemp',
          name: 'Super Hemp',
          quantity: 4,
          favorite: true,
          tags: ['top']
        },
        'YYGGHH' // simple string support
      ]
    };

    const clones = rawExport.clones.map(c => {
      const genes = typeof c === 'string' ? c : c.genetics;
      return CloneUtils.create(genes, rawExport.cropType, typeof c === 'object' ? c : {});
    });

    expect(clones.length).toBe(2);
    expect(clones[0].name).toBe('Super Hemp');
    expect(clones[0].quantity).toBe(4);
    expect(clones[1].genetics).toBe('YYGGHH');
    expect(clones[1].quantity).toBe(1);
  });
});
