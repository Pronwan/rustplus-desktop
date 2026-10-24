import './setupLocalStorage.ts';
import { describe, it, expect, beforeEach } from 'vitest';
import { CloneUtils, SavedClone } from '../domain/genetics/Clone.ts';
import { StorageService } from '../services/storageService.ts';

describe('Clone Bank and Domain Utilities', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('should create a valid SavedClone with default attributes', () => {
    const clone = CloneUtils.create('GGYGYX', 'hemp', {
      name: 'Test Clone',
      quantity: 5,
      favorite: true,
      tags: ['top', 'fast']
    });

    expect(clone.id).toBeDefined();
    expect(clone.genetics).toBe('GGYGYX');
    expect(clone.cropType).toBe('hemp');
    expect(clone.name).toBe('Test Clone');
    expect(clone.quantity).toBe(5);
    expect(clone.favorite).toBe(true);
    expect(clone.tags).toEqual(['top', 'fast']);
    expect(clone.source).toBe('manual');
    expect(clone.createdAt).toBeDefined();
    expect(clone.updatedAt).toBeDefined();
  });

  it('should count green and red genes accurately', () => {
    expect(CloneUtils.countGreenGenes('GGYYHH')).toBe(6);
    expect(CloneUtils.countRedGenes('GGYYHH')).toBe(0);

    expect(CloneUtils.countGreenGenes('WWXXWW')).toBe(0);
    expect(CloneUtils.countRedGenes('WWXXWW')).toBe(6);

    expect(CloneUtils.countGreenGenes('GGYWWX')).toBe(3);
    expect(CloneUtils.countRedGenes('GGYWWX')).toBe(3);
  });

  it('should convert SavedClone to Sapling correctly', () => {
    const clone = CloneUtils.create('GGYGYX', 'hemp');
    const sapling = CloneUtils.toSapling(clone, 4);

    expect(sapling.toString()).toBe('GGYGYX');
    expect(sapling.generationIndex).toBe(0);
    expect(sapling.index).toBe(4);
  });

  it('should persist and retrieve clones in StorageService', () => {
    const clone1 = CloneUtils.create('GGYYHH', 'hemp', { quantity: 2 });
    const clone2 = CloneUtils.create('YYGGHH', 'red-berry', { quantity: 1 });

    StorageService.addClone(clone1);
    StorageService.addClone(clone2);

    const hempClones = StorageService.getClones('hemp');
    expect(hempClones.length).toBe(1);
    expect(hempClones[0].genetics).toBe('GGYYHH');

    const berryClones = StorageService.getClones('red-berry');
    expect(berryClones.length).toBe(1);
    expect(berryClones[0].genetics).toBe('YYGGHH');

    const allClones = StorageService.getClones();
    expect(allClones.length).toBe(2);
  });

  it('should increment quantity when adding an identical clone for the same crop', () => {
    const clone1 = CloneUtils.create('GGYYHH', 'hemp', { quantity: 2 });
    const clone2 = CloneUtils.create('GGYYHH', 'hemp', { quantity: 3 });

    StorageService.addClone(clone1);
    const updated = StorageService.addClone(clone2);

    expect(updated.length).toBe(1);
    expect(updated[0].quantity).toBe(5);
  });

  it('should update and remove clones correctly', () => {
    const clone = CloneUtils.create('GGYYHH', 'hemp', { name: 'Old Name' });
    StorageService.addClone(clone);

    StorageService.updateClone(clone.id, { name: 'New Name', favorite: true });
    let clones = StorageService.getClones('hemp');
    expect(clones[0].name).toBe('New Name');
    expect(clones[0].favorite).toBe(true);

    StorageService.removeClone(clone.id);
    clones = StorageService.getClones('hemp');
    expect(clones.length).toBe(0);
  });
});
