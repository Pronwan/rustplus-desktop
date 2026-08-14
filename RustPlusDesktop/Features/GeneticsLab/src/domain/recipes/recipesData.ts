export interface Ingredient {
  item: string;
  quantity: number;
}

export interface Recipe {
  id: string;
  name: string;
  category: 'tea' | 'food' | 'resource' | 'pie';
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

export const ITEM_IMAGE_MAP: Record<string, string> = {
  'Bread Loaf': 'bread-loaf',
  'Chicken Pie': 'chicken-pie',
  'Apple Pie': 'apple-pie',
  'Pumpkin Pie': 'pumpkin-pie',
  'Meat Pie': 'pork-pie',
  'Fish Pie': 'fish-pie',
  'Bear Pie': 'bear-pie',
  'Pork Pie': 'pork-pie',
  'Big Cat Pie': 'big-cat-pie',
  'Hunters Pie': 'hunters-pie',
  'Raw Meat Pie': 'pork-pie',

  'Basic Healing Tea': 'basic-healing-tea',
  'Advanced Healing Tea': 'advanced-healing-tea',
  'Pure Healing Tea': 'pure-healing-tea',

  'Basic Max Health Tea': 'basic-max-health-tea',
  'Advanced Max Health Tea': 'advanced-max-health-tea',
  'Pure Max Health Tea': 'pure-max-health-tea',

  'Basic Ore Tea': 'basic-ore-tea',
  'Advanced Ore Tea': 'advanced-ore-tea',
  'Pure Ore Tea': 'pure-ore-tea',

  'Basic Wood Tea': 'basic-wood-tea',
  'Advanced Wood Tea': 'advanced-wood-tea',
  'Pure Wood Tea': 'pure-wood-tea',

  'Basic Scrap Tea': 'basic-scrap-tea',
  'Advanced Scrap Tea': 'advanced-scrap-tea',
  'Pure Scrap Tea': 'pure-scrap-tea',

  'Basic Harvesting Tea': 'basic-harvesting-tea',
  'Advanced Harvesting Tea': 'advanced-harvesting-tea',
  'Pure Harvesting Tea': 'pure-harvesting-tea',

  'Basic Anti-Rad Tea': 'basic-anti-rad-tea',
  'Advanced Anti-Rad Tea': 'advanced-anti-rad-tea',
  'Pure Anti-Rad Tea': 'pure-anti-rad-tea',

  'Basic Cooling Tea': 'basic-cooling-tea',
  'Advanced Cooling Tea': 'advanced-cooling-tea',
  'Pure Cooling Tea': 'pure-cooling-tea',

  'Basic Warming Tea': 'basic-warming-tea',
  'Advanced Warming Tea': 'advanced-warming-tea',
  'Pure Warming Tea': 'pure-warming-tea',

  'Basic Crafting Quality Tea': 'basic-crafting-quality-tea',
  'Advanced Crafting Quality Tea': 'advanced-crafting-quality-tea',
  'Pure Crafting Quality Tea': 'pure-crafting-quality-tea',

  'Red Berry': 'red-berry',
  'Green Berry': 'green-berry',
  'Blue Berry': 'blue-berry',
  'Yellow Berry': 'yellow-berry',
  'White Berry': 'white-berry',
  'Mixed Berry': 'mixed-berry',

  'Apple': 'apple',
  'Egg': 'egg',
  'Corn': 'corn',
  'Potato': 'potato',
  'Pumpkin': 'pumpkin',
  'Wheat': 'wheat',
  'Flour': 'wheat',
  'Water': 'cloth',
  'Honey': 'jar-of-honey',
  'Jar of Honey': 'jar-of-honey',
  'Pie Crust': 'wheat',

  'Cooked Chicken': 'cooked-chicken',
  'Chicken Meat': 'cooked-chicken',
  'Cooked Pork': 'cooked-pork',
  'Pork Meat': 'cooked-pork',
  'Cooked Bear Meat': 'cooked-bear-meat',
  'Bear Meat': 'cooked-bear-meat',
  'Cooked Fish': 'cooked-fish',
  'Fish Meat': 'cooked-fish',
  'Cooked Big Cat Meat': 'cooked-big-cat-meat',
  'Cooked Deer Meat': 'cooked-deer-meat',

  'Low Grade Fuel': 'low-grade-fuel',
  'Animal Fat': 'animal-fat',
  'Cloth': 'cloth',
  'Gun Powder': 'gun-powder',
  'Charcoal': 'charcoal',
  'Sulfur': 'sulfur',
  'Explosives': 'explosives',
  'Metal Fragments': 'metal-fragments',
  'Pipes': 'metal-fragments',

  '5.56 Rifle Ammo': '5_56-rifle-ammo',
  'HV 5.56 Rifle Ammo': 'hv-5_56-rifle-ammo',
  'HV Pistol Ammo': 'hv-pistol-ammo',
  'Pistol Bullet': 'pistol-bullet',
  'Incendiary Pistol Bullet': 'incendiary-pistol-bullet',
  'Incendiary 5.56 Rifle Ammo': 'incendiary-5_56-rifle-ammo',
  'Rocket': 'explosives'
};

export const getItemImageSlug = (itemName: string): string => {
  if (ITEM_IMAGE_MAP[itemName]) {
    return ITEM_IMAGE_MAP[itemName];
  }
  const slug = itemName.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
  return slug;
};

export const RUST_RECIPES: Recipe[] = [
  // --- FOOD & BREAD ---
  {
    id: 'bread-loaf',
    name: 'Bread Loaf',
    category: 'food',
    imageSlug: 'bread-loaf',
    ingredients: [
      { item: 'Wheat', quantity: 3 }
    ],
    output: { item: 'Bread Loaf', quantity: 1 },
    description: 'Fresh baked bread providing steady nourishment.'
  },

  // --- PIES ---
  {
    id: 'chicken-pie',
    name: 'Chicken Pie',
    category: 'pie',
    imageSlug: 'chicken-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Corn', quantity: 1 },
      { item: 'Cooked Chicken', quantity: 1 },
      { item: 'Potato', quantity: 1 }
    ],
    output: { item: 'Chicken Pie', quantity: 1 },
    description: 'Delicious poultry pie providing high energy.'
  },
  {
    id: 'apple-pie',
    name: 'Apple Pie',
    category: 'pie',
    imageSlug: 'apple-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Apple', quantity: 3 },
      { item: 'Jar of Honey', quantity: 1 }
    ],
    output: { item: 'Apple Pie', quantity: 1 },
    description: 'Sweet apple pastry restoring health and hydration.'
  },
  {
    id: 'pumpkin-pie',
    name: 'Pumpkin Pie',
    category: 'pie',
    imageSlug: 'pumpkin-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Pumpkin', quantity: 1 }
    ],
    output: { item: 'Pumpkin Pie', quantity: 1 },
    description: 'A hearty baked pie that restores hunger and health.'
  },
  {
    id: 'meat-pie',
    name: 'Meat Pie',
    category: 'pie',
    imageSlug: 'pork-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Bear Meat', quantity: 1 },
      { item: 'Cooked Pork', quantity: 1 },
      { item: 'Potato', quantity: 1 }
    ],
    output: { item: 'Meat Pie', quantity: 1 },
    description: 'Filling combination meat pie with prolonged healing.'
  },
  {
    id: 'fish-pie',
    name: 'Fish Pie',
    category: 'pie',
    imageSlug: 'fish-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Fish', quantity: 2 },
      { item: 'Potato', quantity: 1 }
    ],
    output: { item: 'Fish Pie', quantity: 1 },
    description: 'Nutritious seafood pie with rapid hunger restoration.'
  },
  {
    id: 'bear-pie',
    name: 'Bear Pie',
    category: 'pie',
    imageSlug: 'bear-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Bear Meat', quantity: 2 }
    ],
    output: { item: 'Bear Pie', quantity: 1 },
    description: 'Hearty big game pie offering massive calorie gains.'
  },
  {
    id: 'pork-pie',
    name: 'Pork Pie',
    category: 'pie',
    imageSlug: 'pork-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Pork', quantity: 2 }
    ],
    output: { item: 'Pork Pie', quantity: 1 },
    description: 'Savory meat pie that replenishes calories.'
  },
  {
    id: 'big-cat-pie',
    name: 'Big Cat Pie',
    category: 'pie',
    imageSlug: 'big-cat-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Big Cat Meat', quantity: 2 }
    ],
    output: { item: 'Big Cat Pie', quantity: 1 },
    description: 'Exotic predator meat pie providing stamina.'
  },
  {
    id: 'hunters-pie',
    name: 'Hunters Pie',
    category: 'pie',
    imageSlug: 'hunters-pie',
    ingredients: [
      { item: 'Wheat', quantity: 1 },
      { item: 'Egg', quantity: 1 },
      { item: 'Cooked Deer Meat', quantity: 2 }
    ],
    output: { item: 'Hunters Pie', quantity: 1 },
    description: 'Wild game pie offering high nutritional value.'
  },

  // --- TEAS: MAX HEALTH ---
  {
    id: 'basic-max-health-tea',
    name: 'Basic Max Health Tea',
    category: 'tea',
    imageSlug: 'basic-max-health-tea',
    ingredients: [
      { item: 'Red Berry', quantity: 2 },
      { item: 'Yellow Berry', quantity: 2 }
    ],
    output: { item: 'Basic Max Health Tea', quantity: 1 },
    description: 'Increases max health by +5% for 5 minutes.'
  },
  {
    id: 'advanced-max-health-tea',
    name: 'Advanced Max Health Tea',
    category: 'tea',
    imageSlug: 'advanced-max-health-tea',
    ingredients: [{ item: 'Basic Max Health Tea', quantity: 4 }],
    output: { item: 'Advanced Max Health Tea', quantity: 1 },
    description: 'Increases max health by +12.5% for 15 minutes.'
  },
  {
    id: 'pure-max-health-tea',
    name: 'Pure Max Health Tea',
    category: 'tea',
    imageSlug: 'pure-max-health-tea',
    ingredients: [{ item: 'Advanced Max Health Tea', quantity: 4 }],
    output: { item: 'Pure Max Health Tea', quantity: 1 },
    description: 'Increases max health by +20% for 30 minutes.'
  },

  // --- TEAS: HEALING ---
  {
    id: 'basic-healing-tea',
    name: 'Basic Healing Tea',
    category: 'tea',
    imageSlug: 'basic-healing-tea',
    ingredients: [{ item: 'Red Berry', quantity: 4 }],
    output: { item: 'Basic Healing Tea', quantity: 1 },
    description: 'Instantly restores 30 HP.'
  },
  {
    id: 'advanced-healing-tea',
    name: 'Advanced Healing Tea',
    category: 'tea',
    imageSlug: 'advanced-healing-tea',
    ingredients: [{ item: 'Basic Healing Tea', quantity: 4 }],
    output: { item: 'Advanced Healing Tea', quantity: 1 },
    description: 'Instantly restores 50 HP.'
  },
  {
    id: 'pure-healing-tea',
    name: 'Pure Healing Tea',
    category: 'tea',
    imageSlug: 'pure-healing-tea',
    ingredients: [{ item: 'Advanced Healing Tea', quantity: 4 }],
    output: { item: 'Pure Healing Tea', quantity: 1 },
    description: 'Instantly restores 75 HP.'
  },

  // --- TEAS: ORE ---
  {
    id: 'basic-ore-tea',
    name: 'Basic Ore Tea',
    category: 'tea',
    imageSlug: 'basic-ore-tea',
    ingredients: [{ item: 'Yellow Berry', quantity: 4 }],
    output: { item: 'Basic Ore Tea', quantity: 1 },
    description: 'Increases ore node yield by +20% for 5 minutes.'
  },
  {
    id: 'advanced-ore-tea',
    name: 'Advanced Ore Tea',
    category: 'tea',
    imageSlug: 'advanced-ore-tea',
    ingredients: [{ item: 'Basic Ore Tea', quantity: 4 }],
    output: { item: 'Advanced Ore Tea', quantity: 1 },
    description: 'Increases ore node yield by +35% for 15 minutes.'
  },
  {
    id: 'pure-ore-tea',
    name: 'Pure Ore Tea',
    category: 'tea',
    imageSlug: 'pure-ore-tea',
    ingredients: [{ item: 'Advanced Ore Tea', quantity: 4 }],
    output: { item: 'Pure Ore Tea', quantity: 1 },
    description: 'Increases ore node yield by +50% for 30 minutes.'
  },

  // --- TEAS: WOOD ---
  {
    id: 'basic-wood-tea',
    name: 'Basic Wood Tea',
    category: 'tea',
    imageSlug: 'basic-wood-tea',
    ingredients: [{ item: 'Blue Berry', quantity: 4 }],
    output: { item: 'Basic Wood Tea', quantity: 1 },
    description: 'Increases tree wood yield by +50% for 5 minutes.'
  },
  {
    id: 'advanced-wood-tea',
    name: 'Advanced Wood Tea',
    category: 'tea',
    imageSlug: 'advanced-wood-tea',
    ingredients: [{ item: 'Basic Wood Tea', quantity: 4 }],
    output: { item: 'Advanced Wood Tea', quantity: 1 },
    description: 'Increases tree wood yield by +100% for 15 minutes.'
  },
  {
    id: 'pure-wood-tea',
    name: 'Pure Wood Tea',
    category: 'tea',
    imageSlug: 'pure-wood-tea',
    ingredients: [{ item: 'Advanced Wood Tea', quantity: 4 }],
    output: { item: 'Pure Wood Tea', quantity: 1 },
    description: 'Increases tree wood yield by +200% for 30 minutes.'
  },

  // --- TEAS: SCRAP ---
  {
    id: 'basic-scrap-tea',
    name: 'Basic Scrap Tea',
    category: 'tea',
    imageSlug: 'basic-scrap-tea',
    ingredients: [
      { item: 'White Berry', quantity: 2 },
      { item: 'Yellow Berry', quantity: 2 }
    ],
    output: { item: 'Basic Scrap Tea', quantity: 1 },
    description: 'Increases barrel scrap yield by +1 for 5 minutes.'
  },
  {
    id: 'advanced-scrap-tea',
    name: 'Advanced Scrap Tea',
    category: 'tea',
    imageSlug: 'advanced-scrap-tea',
    ingredients: [{ item: 'Basic Scrap Tea', quantity: 4 }],
    output: { item: 'Advanced Scrap Tea', quantity: 1 },
    description: 'Increases barrel scrap yield by +2 for 15 minutes.'
  },
  {
    id: 'pure-scrap-tea',
    name: 'Pure Scrap Tea',
    category: 'tea',
    imageSlug: 'pure-scrap-tea',
    ingredients: [{ item: 'Advanced Scrap Tea', quantity: 4 }],
    output: { item: 'Pure Scrap Tea', quantity: 1 },
    description: 'Increases barrel scrap yield by +3 for 30 minutes.'
  },

  // --- TEAS: HARVESTING / CLOTH ---
  {
    id: 'basic-harvesting-tea',
    name: 'Basic Harvesting Tea',
    category: 'tea',
    imageSlug: 'basic-harvesting-tea',
    ingredients: [
      { item: 'Green Berry', quantity: 2 },
      { item: 'Yellow Berry', quantity: 2 }
    ],
    output: { item: 'Basic Harvesting Tea', quantity: 1 },
    description: 'Increases plant gather yield by 150% for 5 minutes.'
  },
  {
    id: 'advanced-harvesting-tea',
    name: 'Advanced Harvesting Tea',
    category: 'tea',
    imageSlug: 'advanced-harvesting-tea',
    ingredients: [{ item: 'Basic Harvesting Tea', quantity: 4 }],
    output: { item: 'Advanced Harvesting Tea', quantity: 1 },
    description: 'Increases plant gather yield by 200% for 15 minutes.'
  },
  {
    id: 'pure-harvesting-tea',
    name: 'Pure Harvesting Tea',
    category: 'tea',
    imageSlug: 'pure-harvesting-tea',
    ingredients: [{ item: 'Advanced Harvesting Tea', quantity: 4 }],
    output: { item: 'Pure Harvesting Tea', quantity: 1 },
    description: 'Increases plant gather yield by 250% for 30 minutes.'
  },

  // --- TEAS: ANTI-RAD ---
  {
    id: 'basic-anti-rad-tea',
    name: 'Basic Anti-Rad Tea',
    category: 'tea',
    imageSlug: 'basic-anti-rad-tea',
    ingredients: [
      { item: 'Red Berry', quantity: 2 },
      { item: 'Green Berry', quantity: 2 }
    ],
    output: { item: 'Basic Anti-Rad Tea', quantity: 1 },
    description: 'Decreases radiation poisoning by 10 points.'
  },
  {
    id: 'advanced-anti-rad-tea',
    name: 'Advanced Anti-Rad Tea',
    category: 'tea',
    imageSlug: 'advanced-anti-rad-tea',
    ingredients: [{ item: 'Basic Anti-Rad Tea', quantity: 4 }],
    output: { item: 'Advanced Anti-Rad Tea', quantity: 1 },
    description: 'Decreases radiation poisoning by 25 points.'
  },
  {
    id: 'pure-anti-rad-tea',
    name: 'Pure Anti-Rad Tea',
    category: 'tea',
    imageSlug: 'pure-anti-rad-tea',
    ingredients: [{ item: 'Advanced Anti-Rad Tea', quantity: 4 }],
    output: { item: 'Pure Anti-Rad Tea', quantity: 1 },
    description: 'Decreases radiation poisoning by 50 points.'
  },

  // --- RESOURCE & REFINING ---
  {
    id: 'low-grade-fuel',
    name: 'Low Grade Fuel',
    category: 'resource',
    imageSlug: 'low-grade-fuel',
    ingredients: [
      { item: 'Animal Fat', quantity: 3 },
      { item: 'Cloth', quantity: 1 }
    ],
    output: { item: 'Low Grade Fuel', quantity: 4 },
    description: 'Refined fuel for lamps, torches, engines, and explosives.'
  },
  {
    id: 'gun-powder',
    name: 'Gun Powder',
    category: 'resource',
    imageSlug: 'gun-powder',
    ingredients: [
      { item: 'Charcoal', quantity: 30 },
      { item: 'Sulfur', quantity: 20 }
    ],
    output: { item: 'Gun Powder', quantity: 10 },
    description: 'Standard propellant powder for ballistics and raiding tools.'
  },
  {
    id: 'explosives',
    name: 'Explosives',
    category: 'resource',
    imageSlug: 'explosives',
    ingredients: [
      { item: 'Gun Powder', quantity: 50 },
      { item: 'Low Grade Fuel', quantity: 3 },
      { item: 'Sulfur', quantity: 10 },
      { item: 'Metal Fragments', quantity: 10 }
    ],
    output: { item: 'Explosives', quantity: 1 },
    description: 'High explosive compound used in C4 and Rockets.'
  },

  // --- AMMUNITION ---
  {
    id: '5_56-rifle-ammo',
    name: '5.56 Rifle Ammo',
    category: 'resource',
    imageSlug: '5_56-rifle-ammo',
    ingredients: [
      { item: 'Metal Fragments', quantity: 5 },
      { item: 'Gun Powder', quantity: 10 }
    ],
    output: { item: '5.56 Rifle Ammo', quantity: 3 },
    description: 'Standard intermediate cartridge for AK, LR-300, and Bolt Action.'
  },
  {
    id: 'hv-5_56-rifle-ammo',
    name: 'HV 5.56 Rifle Ammo',
    category: 'resource',
    imageSlug: 'hv-5_56-rifle-ammo',
    ingredients: [
      { item: 'Metal Fragments', quantity: 5 },
      { item: 'Gun Powder', quantity: 20 }
    ],
    output: { item: 'HV 5.56 Rifle Ammo', quantity: 3 },
    description: 'High-velocity rifle ammunition with flatter trajectory.'
  },
  {
    id: 'hv-pistol-ammo',
    name: 'HV Pistol Ammo',
    category: 'resource',
    imageSlug: 'hv-pistol-ammo',
    ingredients: [
      { item: 'Metal Fragments', quantity: 5 },
      { item: 'Gun Powder', quantity: 10 }
    ],
    output: { item: 'HV Pistol Ammo', quantity: 4 },
    description: 'High-velocity 9mm handgun and SMG ammunition.'
  },
  {
    id: 'incendiary-pistol-bullet',
    name: 'Incendiary Pistol Bullet',
    category: 'resource',
    imageSlug: 'incendiary-pistol-bullet',
    ingredients: [
      { item: 'Metal Fragments', quantity: 5 },
      { item: 'Gun Powder', quantity: 10 },
      { item: 'Sulfur', quantity: 5 }
    ],
    output: { item: 'Incendiary Pistol Bullet', quantity: 4 },
    description: 'Pistol ammunition that ignites targets on impact.'
  },
  {
    id: 'incendiary-5_56-rifle-ammo',
    name: 'Incendiary 5.56 Rifle Ammo',
    category: 'resource',
    imageSlug: 'incendiary-5_56-rifle-ammo',
    ingredients: [
      { item: 'Metal Fragments', quantity: 5 },
      { item: 'Gun Powder', quantity: 10 },
      { item: 'Sulfur', quantity: 5 }
    ],
    output: { item: 'Incendiary 5.56 Rifle Ammo', quantity: 3 },
    description: 'Rifle ammunition that produces fire on contact.'
  }
];
