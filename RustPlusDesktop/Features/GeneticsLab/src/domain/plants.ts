/** Plants by kind, in the order pickers and group lists show them. */
export const PLANT_GROUPS: { title: string; plants: string[] }[] = [
  { title: 'Crops', plants: ['hemp', 'corn', 'potato', 'pumpkin', 'wheat'] },
  { title: 'Berries', plants: ['red-berry', 'blue-berry', 'yellow-berry', 'green-berry', 'white-berry', 'mixed-berry'] },
  { title: 'Flowers', plants: ['sunflower', 'rose', 'orchid'] }
];

export const PLANT_ORDER = PLANT_GROUPS.flatMap((g) => g.plants);

/** "yellow-berry" -> "Yellow Berry". */
export const plantLabel = (plant: string) => plant.replace(/-/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
