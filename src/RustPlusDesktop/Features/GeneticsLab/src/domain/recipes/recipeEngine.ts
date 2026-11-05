import { Recipe, ExpandedRecipe, Ingredient, RUST_RECIPES } from './recipesData.ts';

export class RecipeEngine {
  private recipeByItemMap = new Map<string, Recipe>();

  constructor(recipes: Recipe[] = RUST_RECIPES) {
    for (const r of recipes) {
      this.recipeByItemMap.set(r.output.item.toLowerCase(), r);
    }
  }

  public static getItemImageSlug(name: string): string {
    return `./img/items/${name
      .replaceAll(' ', '-')
      .replaceAll('.', '_')
      .toLowerCase()}.webp`;
  }

  /**
   * Recursively expands an item into its ultimate raw base ingredients.
   */
  public expandItem(itemName: string, quantity = 1): Ingredient[] {
    const recipe = this.recipeByItemMap.get(itemName.toLowerCase());

    if (!recipe) {
      return [{ item: itemName, quantity }];
    }

    const baseList: Ingredient[] = [];
    const outputQty = recipe.output.quantity || 1;

    for (const ingredient of recipe.ingredients) {
      const neededQty = (ingredient.quantity * quantity) / outputQty;
      const nested = this.expandItem(ingredient.item, neededQty);

      for (const n of nested) {
        baseList.push(n);
      }
    }

    return this.consolidateIngredients(baseList);
  }

  /**
   * Consolidates equal items by summing their quantities and rounding.
   */
  public consolidateIngredients(ingredients: Ingredient[]): Ingredient[] {
    const map = new Map<string, number>();

    for (const ing of ingredients) {
      const current = map.get(ing.item) ?? 0;
      map.set(ing.item, current + ing.quantity);
    }

    const result: Ingredient[] = [];
    for (const [item, qty] of map.entries()) {
      result.push({
        item,
        quantity: Math.round(qty * 100) / 100
      });
    }

    return result.sort((a, b) => b.quantity - a.quantity || a.item.localeCompare(b.item));
  }

  /**
   * Returns all recipes expanded with their calculated base ingredients.
   */
  public getExpandedRecipes(recipes: Recipe[] = RUST_RECIPES): ExpandedRecipe[] {
    return recipes.map(r => {
      const baseIngredients = this.expandItem(r.output.item, r.output.quantity);
      return {
        ...r,
        baseIngredients
      };
    });
  }
}
