import { describe, it, expect } from 'vitest';
import { RecipeEngine } from '../domain/recipes/recipeEngine.ts';
import { RUST_RECIPES } from '../domain/recipes/recipesData.ts';

describe('Recipe Engine', () => {
  const engine = new RecipeEngine(RUST_RECIPES);

  it('recursively expands Pure Healing Tea into exactly 64 Red Berries', () => {
    // Basic = 4 Red Berries
    // Advanced = 4 Basic = 16 Red Berries
    // Pure = 4 Advanced = 64 Red Berries
    const base = engine.expandItem('Pure Healing Tea', 1);
    expect(base.length).toBe(1);
    expect(base[0].item).toBe('Red Berry');
    expect(base[0].quantity).toBe(64);
  });

  it('recursively expands Pure Max Health Tea into 32 Red and 32 Yellow Berries', () => {
    // Basic = 2 Red + 2 Yellow
    // Advanced = 4 Basic = 8 Red + 8 Yellow
    // Pure = 4 Advanced = 32 Red + 32 Yellow
    const base = engine.expandItem('Pure Max Health Tea', 1);
    const red = base.find(b => b.item === 'Red Berry');
    const yellow = base.find(b => b.item === 'Yellow Berry');

    expect(red?.quantity).toBe(32);
    expect(yellow?.quantity).toBe(32);
  });

  it('expands Low Grade Fuel with output batch ratio', () => {
    // 3 Animal Fat + 1 Cloth produces 4 Low Grade Fuel
    // To make 4 LGF, we need 3 Animal Fat and 1 Cloth
    const base = engine.expandItem('Low Grade Fuel', 4);
    const fat = base.find(b => b.item === 'Animal Fat');
    const cloth = base.find(b => b.item === 'Cloth');

    expect(fat?.quantity).toBe(3);
    expect(cloth?.quantity).toBe(1);
  });
});
