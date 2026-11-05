export interface Ingredient {
  item: string;
  quantity: number;
}

/** Where the recipe is made in game. */
export type CraftStation = 'mixing-table' | 'cooking-workbench' | 'workbench' | 'fridge';

export const STATION_LABELS: Record<CraftStation, string> = {
  'mixing-table': 'Mixing Table or Cooking Workbench',
  'cooking-workbench': 'Cooking Workbench',
  workbench: 'Workbench Level 1',
  fridge: 'Powered fridge, 4 hours'
};

export interface Recipe {
  id: string;
  name: string;
  category: 'tea' | 'food' | 'resource' | 'pie';
  station: CraftStation;
  imageSlug: string;
  ingredients: Ingredient[];
  output: {
    item: string;
    quantity: number;
  };
  description?: string;
}

export interface ExpandedRecipe extends Recipe {
  baseIngredients: Ingredient[];
}

/** Icon file names that the generic slug rule below cannot derive. */
export const ITEM_IMAGE_MAP: Record<string, string> = {
  '5.56 Rifle Ammo': '5_56-rifle-ammo',
  'HV 5.56 Rifle Ammo': 'hv-5_56-rifle-ammo',
  'Incendiary 5.56 Rifle Ammo': 'incendiary-5_56-rifle-ammo'
};

export const getItemImageSlug = (itemName: string): string => {
  if (ITEM_IMAGE_MAP[itemName]) {
    return ITEM_IMAGE_MAP[itemName];
  }
  const slug = itemName.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
  return slug;
};

const slugOf = (name: string) => getItemImageSlug(name);

function recipe(
  name: string,
  category: Recipe['category'],
  station: CraftStation,
  ingredients: Array<[string, number]>,
  description: string,
  outputQuantity = 1
): Recipe {
  const id = slugOf(name);
  return {
    id,
    name,
    category,
    station,
    imageSlug: id,
    ingredients: ingredients.map(([item, quantity]) => ({ item, quantity })),
    output: { item: name, quantity: outputQuantity },
    description
  };
}

/* ------------------------------------------------------------------ *
 * Teas
 *
 * Every tea line is Basic (4 berries) -> Advanced (4 Basic) -> Pure (4 Advanced), made at a
 * Mixing Table or Cooking Workbench without a blueprint. Every tea also gives +30 hydration.
 * Recipes and effects verified against the game data on 2026-10-12; the Advanced and Pure
 * Max Health, Ore, Wood, Scrap and Harvesting values are the reduced ones from the upcoming
 * tea rebalance.
 * ------------------------------------------------------------------ */

interface TeaLine {
  type: string;
  berries: Array<[string, number]>;
  /** Effect of the Basic, Advanced and Pure tea. */
  effects: [string, string, string];
}

const TEA_LINES: TeaLine[] = [
  {
    type: 'Healing',
    berries: [['Red Berry', 4]],
    effects: ['Heals 30 HP over time', '+5 HP, then heals 50 HP over time', '+10 HP, then heals 75 HP over time']
  },
  {
    type: 'Max Health',
    berries: [['Red Berry', 3], ['Yellow Berry', 1]],
    effects: ['+5% max health for 20 minutes', '+10% max health for 20 minutes', '+15% max health for 20 minutes']
  },
  {
    type: 'Ore',
    berries: [['Yellow Berry', 2], ['Blue Berry', 2]],
    effects: ['+20% ore yield for 30 minutes', '+30% ore yield for 30 minutes', '+40% ore yield for 30 minutes']
  },
  {
    type: 'Wood',
    berries: [['Red Berry', 2], ['Blue Berry', 2]],
    effects: ['+50% wood yield for 30 minutes', '+100% wood yield for 30 minutes', '+150% wood yield for 30 minutes']
  },
  {
    type: 'Scrap',
    berries: [['Yellow Berry', 2], ['White Berry', 2]],
    effects: ['+100% scrap yield for 30 minutes', '+200% scrap yield for 45 minutes', '+300% scrap yield for 1 hour']
  },
  {
    type: 'Harvesting',
    berries: [['Green Berry', 3], ['Yellow Berry', 1]],
    effects: ['+35% harvesting yield for 20 minutes', '+45% harvesting yield for 20 minutes', '+55% harvesting yield for 20 minutes']
  },
  {
    type: 'Anti-Rad',
    berries: [['Red Berry', 2], ['Green Berry', 2]],
    effects: ['+15% radiation resistance for 30 minutes', '+30% radiation resistance for 30 minutes', '+45% radiation resistance for 30 minutes']
  },
  {
    type: 'Cooling',
    berries: [['White Berry', 2], ['Blue Berry', 2]],
    effects: [
      '+10° cooling, max core temperature +40°, for 20 minutes',
      '+15° cooling, max core temperature +40°, for 45 minutes',
      '+20° cooling, max core temperature +40°, for 1 hour'
    ]
  },
  {
    type: 'Warming',
    berries: [['White Berry', 2], ['Yellow Berry', 2]],
    effects: [
      '+5° warming, min core temperature -15°, for 20 minutes',
      '+10° warming, min core temperature -8°, for 45 minutes',
      '+15° warming, min core temperature 0°, for 1 hour'
    ]
  },
  {
    type: 'Crafting Quality',
    berries: [['Green Berry', 3], ['White Berry', 1]],
    effects: ['+50% crafting quality for 3m 20s', '+75% crafting quality for 5 minutes', '+85% crafting quality for 6m 40s']
  }
];

function teaRecipes(): Recipe[] {
  return TEA_LINES.flatMap((line) => {
    const basic = `Basic ${line.type} Tea`;
    const advanced = `Advanced ${line.type} Tea`;
    const pure = `Pure ${line.type} Tea`;
    return [
      recipe(basic, 'tea', 'mixing-table', line.berries, `${line.effects[0]}. +30 hydration.`),
      recipe(advanced, 'tea', 'mixing-table', [[basic, 4]], `${line.effects[1]}. +30 hydration.`),
      recipe(pure, 'tea', 'mixing-table', [[advanced, 4]], `${line.effects[2]}. +30 hydration.`)
    ];
  });
}

/* ------------------------------------------------------------------ *
 * Milky and Creamy teas (October 2026 livestock update)
 *
 * Every Pure tea except Crafting Quality has a Milky and a Creamy version: one Pure tea plus
 * three Milk or three Cream, crafted at a Level 1 Workbench. Milk makes the effect last 50%
 * longer; Cream makes it stronger and shorter.
 * ------------------------------------------------------------------ */

interface EnrichedTea {
  /** Tea type as it appears in the item name, e.g. "Ore". */
  type: string;
  milky: string;
  creamy: string;
}

const ENRICHED_TEAS: EnrichedTea[] = [
  { type: 'Healing', milky: '+10 HP, then heals 112 HP over time', creamy: '+18 HP, then heals 97 HP over time' },
  { type: 'Max Health', milky: '+15% max health for 30 minutes', creamy: '+20% max health for 16 minutes' },
  { type: 'Ore', milky: '+40% ore yield for 45 minutes', creamy: '+50% ore yield for 24 minutes' },
  { type: 'Wood', milky: '+150% wood yield for 45 minutes', creamy: '+200% wood yield for 24 minutes' },
  { type: 'Scrap', milky: '+300% scrap yield for 1h 30m', creamy: '+400% scrap yield for 48 minutes' },
  { type: 'Harvesting', milky: '+55% harvesting yield for 30 minutes', creamy: '+65% harvesting yield for 16 minutes' },
  { type: 'Anti-Rad', milky: '+45% radiation resistance for 45 minutes', creamy: '+60% radiation resistance for 24 minutes' },
  { type: 'Cooling', milky: '+20° cooling, max core temperature +40°, for 1h 30m', creamy: '+26° cooling, max core temperature +40°, for 48 minutes' },
  { type: 'Warming', milky: '+15° warming, min core temperature 0°, for 1h 30m', creamy: '+19.5° warming, min core temperature 0°, for 48 minutes' }
];

function enrichedTeaRecipes(): Recipe[] {
  return ENRICHED_TEAS.flatMap((tea) => {
    const pure = `Pure ${tea.type} Tea`;
    // The Creamy Healing tea's +18 HP already includes the cream's +5 HP bonus.
    const creamyBonus = tea.type === 'Healing' ? '+30 hydration, +45 calories' : '+30 hydration, +45 calories, +5 HP';
    return [
      recipe(
        `Milky ${pure}`,
        'tea',
        'workbench',
        [[pure, 1], ['Milk', 3]],
        `${tea.milky}. +45 hydration, +90 calories. Milk makes the effect last longer.`
      ),
      recipe(
        `Creamy ${pure}`,
        'tea',
        'workbench',
        [[pure, 1], ['Cream', 3]],
        `${tea.creamy}. ${creamyBonus}. Cream makes the effect stronger and shorter.`
      )
    ];
  });
}

/* ------------------------------------------------------------------ *
 * Bread and pies (Cooking Workbench)
 *
 * Every pie gives +80 calories, +15 hydration and +2 HP; a Rich pie adds Cream and gives
 * +100 calories, +19 hydration, +3 HP and a stronger or longer effect.
 * ------------------------------------------------------------------ */

const PIE_BASE = '+80 calories, +15 hydration, +2 HP';
const RICH_PIE_BASE = '+100 calories, +19 hydration, +3 HP';

interface PieDef {
  name: string;
  ingredients: Array<[string, number]>;
  effect: string;
  rich: { ingredients: Array<[string, number]>; effect: string };
}

const PIES: PieDef[] = [
  {
    name: 'Apple Pie',
    ingredients: [['Wheat', 3], ['Apple', 1], ['Jar of Honey', 2]],
    effect: '+100% vision care for 15 minutes',
    rich: { ingredients: [['Wheat', 3], ['Apple', 1], ['Jar of Honey', 2], ['Cream', 4]], effect: '+100% vision care for 20 minutes' }
  },
  {
    name: 'Pumpkin Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Pumpkin', 1]],
    effect: '+10% max health for 10 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Pumpkin', 1], ['Cream', 4]], effect: '+15% max health for 10 minutes' }
  },
  {
    name: 'Chicken Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Corn', 3], ['Cooked Chicken', 3]],
    effect: '+52% better gene chance for 10 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Corn', 3], ['Cooked Chicken', 3], ['Cream', 4]], effect: '+65% better gene chance for 10 minutes' }
  },
  {
    name: 'Fish Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Mushroom', 1], ['Potato', 2], ['Cooked Fish', 1]],
    effect: '+100% comfort for 20 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Potato', 2], ['Cooked Fish', 1], ['Cream', 6]], effect: '+100% comfort for 25 minutes' }
  },
  {
    name: 'Bear Pie',
    ingredients: [['Wheat', 3], ['Egg', 2], ['Mushroom', 2], ['Cooked Bear Meat', 3]],
    effect: '+150% digestion boost for 15 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 2], ['Mushroom', 2], ['Cooked Bear Meat', 3], ['Cream', 4]], effect: '+175% digestion boost for 15 minutes' }
  },
  {
    name: 'Pork Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Pork', 2]],
    effect: '+200% metabolism boost for 30 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Pork', 2], ['Cream', 4]], effect: '+250% metabolism boost for 30 minutes' }
  },
  {
    name: 'Big Cat Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Big Cat Meat', 2]],
    effect: '+100% hunter vision for 30 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Big Cat Meat', 2], ['Cream', 4]], effect: '+100% hunter vision for 40 minutes' }
  },
  {
    name: 'Hunters Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Mushroom', 1], ['Potato', 2], ['Cooked Deer Meat', 2]],
    effect: '+90% clotting for 30 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Potato', 2], ['Cooked Deer Meat', 2], ['Cream', 6]], effect: '+100% clotting for 30 minutes' }
  },
  {
    name: 'Beef Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Beef', 3]],
    effect: '+75% hunger capacity for 2 hours',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Beef', 3], ['Cream', 4]], effect: '+100% hunger capacity for 2 hours' }
  },
  {
    name: 'Shepherds Pie',
    ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Mutton', 2], ['Potato', 1]],
    effect: '+100% livestock handling for 15 minutes',
    rich: { ingredients: [['Wheat', 3], ['Egg', 3], ['Cooked Mutton', 2], ['Cream', 4]], effect: '+150% livestock handling for 15 minutes' }
  }
];

function pieRecipes(): Recipe[] {
  const pies = PIES.map((pie) => recipe(pie.name, 'pie', 'cooking-workbench', pie.ingredients, `${pie.effect}. ${PIE_BASE}.`));
  const rich = PIES.map((pie) =>
    recipe(`Rich ${pie.name}`, 'pie', 'cooking-workbench', pie.rich.ingredients, `${pie.rich.effect}. ${RICH_PIE_BASE}.`)
  );
  return [...pies, ...rich];
}

const FOOD_RECIPES: Recipe[] = [
  recipe('Bread Loaf', 'food', 'cooking-workbench', [['Wheat', 5]], '+150 calories, +10 hydration, heals 8 HP over time. +250% horse dung for 2 minutes.'),
  recipe(
    'Brioche Bread',
    'food',
    'cooking-workbench',
    [['Wheat', 5], ['Egg', 2], ['Cream', 5]],
    '+190 calories, +13 hydration, heals 10 HP over time. +312.5% horse dung for 2 minutes.'
  )
];

/* ------------------------------------------------------------------ *
 * Dairy (October 2026 livestock update)
 * ------------------------------------------------------------------ */

const DAIRY_RECIPES: Recipe[] = [
  recipe(
    'Cream',
    'resource',
    'fridge',
    [['Milk', 1]],
    'Skim milk: 4 hours in a powered fridge or vending machine turns each Milk into 1 Cream and 1 Skimmed Milk. Milk comes from tame cows, 1 every 5 minutes.'
  )
];

/* ------------------------------------------------------------------ *
 * Resources and ammunition (blueprint required; the Mixing Table uses less charcoal for
 * Gun Powder than hand crafting)
 * ------------------------------------------------------------------ */

const RESOURCE_RECIPES: Recipe[] = [
  recipe('Low Grade Fuel', 'resource', 'mixing-table', [['Animal Fat', 3], ['Cloth', 1]], 'Fuel for lamps, furnaces, engines and explosives.', 4),
  recipe('Gun Powder', 'resource', 'mixing-table', [['Charcoal', 20], ['Sulfur', 20]], 'Propellant for ammunition and explosives. Hand crafting costs 30 Charcoal instead of 20.', 10),
  recipe(
    'Explosives',
    'resource',
    'mixing-table',
    [['Gun Powder', 50], ['Low Grade Fuel', 3], ['Sulfur', 10], ['Metal Fragments', 10]],
    'High explosive used in C4 and rockets.'
  ),
  recipe('Pistol Bullet', 'resource', 'mixing-table', [['Metal Fragments', 10], ['Gun Powder', 5]], 'Standard pistol and SMG ammunition.', 4),
  recipe('HV Pistol Ammo', 'resource', 'mixing-table', [['Metal Fragments', 10], ['Gun Powder', 10]], 'High-velocity pistol and SMG ammunition.', 4),
  recipe(
    'Incendiary Pistol Bullet',
    'resource',
    'mixing-table',
    [['Metal Fragments', 10], ['Gun Powder', 10], ['Sulfur', 5]],
    'Pistol ammunition that sets targets on fire.',
    3
  ),
  recipe('5.56 Rifle Ammo', 'resource', 'mixing-table', [['Metal Fragments', 10], ['Gun Powder', 5]], 'Standard rifle ammunition.', 3),
  recipe('HV 5.56 Rifle Ammo', 'resource', 'mixing-table', [['Metal Fragments', 10], ['Gun Powder', 10]], 'High-velocity rifle ammunition with a flatter trajectory.', 3),
  recipe(
    'Incendiary 5.56 Rifle Ammo',
    'resource',
    'mixing-table',
    [['Metal Fragments', 10], ['Gun Powder', 10], ['Sulfur', 5]],
    'Rifle ammunition that sets targets on fire.',
    2
  )
];

export const RUST_RECIPES: Recipe[] = [
  ...FOOD_RECIPES,
  ...pieRecipes(),
  ...teaRecipes(),
  ...enrichedTeaRecipes(),
  ...DAIRY_RECIPES,
  ...RESOURCE_RECIPES
];
