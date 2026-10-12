import { describe, it, expect } from 'vitest';
import { RecipeEngine } from '../domain/recipes/recipeEngine.ts';
import { RUST_RECIPES, getItemImageSlug } from '../domain/recipes/recipesData.ts';

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

  it('recursively expands Pure Max Health Tea into 48 Red and 16 Yellow Berries', () => {
    // Basic = 3 Red + 1 Yellow
    // Advanced = 4 Basic = 12 Red + 4 Yellow
    // Pure = 4 Advanced = 48 Red + 16 Yellow
    const base = engine.expandItem('Pure Max Health Tea', 1);
    const red = base.find(b => b.item === 'Red Berry');
    const yellow = base.find(b => b.item === 'Yellow Berry');

    expect(red?.quantity).toBe(48);
    expect(yellow?.quantity).toBe(16);
  });

  it('has all three tiers of all ten tea lines', () => {
    const types = ['Healing', 'Max Health', 'Ore', 'Wood', 'Scrap', 'Harvesting', 'Anti-Rad', 'Cooling', 'Warming', 'Crafting Quality'];
    for (const type of types) {
      for (const tier of ['Basic', 'Advanced', 'Pure']) {
        expect(RUST_RECIPES.some((r) => r.name === `${tier} ${type} Tea`), `${tier} ${type}`).toBe(true);
      }
    }
  });

  it('makes every basic tea from exactly 4 berries', () => {
    for (const r of RUST_RECIPES.filter((r) => r.name.startsWith('Basic '))) {
      const berries = r.ingredients.reduce((sum, i) => sum + i.quantity, 0);
      expect(berries, r.name).toBe(4);
      expect(r.ingredients.every((i) => i.item.endsWith(' Berry')), r.name).toBe(true);
    }
  });

  it('expands Rich Chicken Pie cream back to milk', () => {
    const base = engine.expandItem('Rich Chicken Pie', 1);
    expect(base).toContainEqual({ item: 'Milk', quantity: 4 });
    expect(base).toContainEqual({ item: 'Wheat', quantity: 3 });
    expect(base).toContainEqual({ item: 'Cooked Chicken', quantity: 3 });
  });

  it('expands Explosives through Mixing Table Gun Powder', () => {
    // 50 Gun Powder = 5 batches of 20 Charcoal + 20 Sulfur, plus 10 Sulfur in the recipe.
    const base = engine.expandItem('Explosives', 1);
    expect(base).toContainEqual({ item: 'Charcoal', quantity: 100 });
    expect(base).toContainEqual({ item: 'Sulfur', quantity: 110 });
  });

  it('has an icon for every recipe and ingredient', async () => {
    // Non-literal specifier: the project carries no Node type definitions.
    const fsName = 'node:fs';
    const { existsSync } = await import(fsName);
    const names = new Set(RUST_RECIPES.flatMap((r) => [r.name, ...r.ingredients.map((i) => i.item)]));
    for (const name of names) {
      expect(existsSync(`public/img/items/${getItemImageSlug(name)}.webp`), name).toBe(true);
    }
  });

  it('has unique recipe ids', () => {
    expect(new Set(RUST_RECIPES.map((r) => r.id)).size).toBe(RUST_RECIPES.length);
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

describe('Milky and Creamy teas (livestock update)', () => {
  const engine = new RecipeEngine(RUST_RECIPES);
  const types = ['Healing', 'Max Health', 'Ore', 'Wood', 'Scrap', 'Harvesting', 'Anti-Rad', 'Cooling', 'Warming'];

  it('adds a Milky and a Creamy version of nine Pure teas', () => {
    for (const type of types) {
      expect(RUST_RECIPES.some((r) => r.name === `Milky Pure ${type} Tea`), `Milky ${type}`).toBe(true);
      expect(RUST_RECIPES.some((r) => r.name === `Creamy Pure ${type} Tea`), `Creamy ${type}`).toBe(true);
    }
    expect(RUST_RECIPES.some((r) => r.name.includes('Crafting Quality') && r.name.startsWith('Milky'))).toBe(false);
  });

  it('expands Milky Pure Healing Tea to the Pure tea\'s berries plus 3 Milk', () => {
    const base = engine.expandItem('Milky Pure Healing Tea', 1);
    expect(base).toContainEqual({ item: 'Red Berry', quantity: 64 });
    expect(base).toContainEqual({ item: 'Milk', quantity: 3 });
  });

  it('traces Creamy tea cream back to milk', () => {
    const base = engine.expandItem('Creamy Pure Ore Tea', 2);
    expect(base).toContainEqual({ item: 'Milk', quantity: 6 });
    expect(base.some((i) => i.item === 'Cream')).toBe(false);
  });

  it('has an icon for every new tea', () => {
    for (const r of RUST_RECIPES.filter((r) => /^(Milky|Creamy) /.test(r.name))) {
      expect(r.imageSlug).toBe(r.id);
    }
  });
});
