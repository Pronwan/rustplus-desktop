import { Sapling } from '../genetics/Sapling.ts';

export interface GuideSection {
  id: string;
  title: string;
  category: string;
  summary: string;
  content: string[];
  keyPoints?: string[];
}

export const GUIDE_SECTIONS: GuideSection[] = [
  {
    id: 'stages',
    title: 'Plant Growth Stages',
    category: 'Fundamentals',
    summary: 'Understand the plant lifecycle in Rust from seed to clone harvesting.',
    content: [
      'Every plant in Rust progresses through eight distinct stages:',
      '1. **Seed / Cutting**: Planting phase. A seed carries random genetics, while a cutting (clone) copies its parent exactly.',
      '2. **Seedling**: A small sprout forms as health builds. Just keep it watered and lit.',
      '3. **Sapling**: Genetics become visible on inspect and cloning unlocks. Take cuttings here to lock in a gene string before it can change.',
      '4. **Crossbreed**: A short cross-pollination window where surrounding mature plants can rewrite the center plant\'s next-generation genetics.',
      '5. **Mature**: Plant mass and shape are nearly final, and yield begins to build.',
      '6. **Fruiting**: Fruit, cloth, or berries actively develop on the plant.',
      '7. **Ripe**: Maximum harvest yield. Ideal time to harvest and to take cuttings that duplicate a desired 6-green gene string forever.',
      '8. **Dying**: Left unharvested, the plant withers and its yield is lost, leaving only compost scraps.'
    ],
    keyPoints: [
      'Clone (take cuttings) as early as the Sapling stage to preserve a plant\'s exact genetics before crossbreeding can alter it.',
      'Harvest at the Ripe stage for maximum yield — letting a plant slip into Dying wastes the crop.',
      'Crossbreeding only happens during the brief Crossbreed window and is driven by the genetics of the surrounding mature plants.'
    ]
  },
  {
    id: 'genes',
    title: 'Gene Meanings & Weights',
    category: 'Genetics',
    summary: 'Green genes enhance growth and yields, while Red genes impose penalties.',
    content: [
      'Each plant has exactly 6 gene slots with 5 possible gene types:',
      '• **G (Growth Speed)**: Increases the rate at which the plant transitions between stages.',
      '• **Y (Yield)**: Increases the quantity of crops/cloth/berries harvested per plant.',
      '• **H (Hardiness)**: Increases temperature and water tolerance in non-ideal biomes.',
      '• **W (Water Loss)**: Undesirable red gene that drastically increases water consumption.',
      '• **X (Empty / Null)**: Undesirable red gene with no beneficial properties.'
    ],
    keyPoints: [
      'Green genes (G, Y, H) have a crossbreeding weight of **0.6**.',
      'Red genes (W, X) have a dominant crossbreeding weight of **1.0**.',
      'Because red genes weigh more, it takes two green genes (0.6 + 0.6 = 1.2) to overpower a single red gene (1.0).'
    ]
  },
  {
    id: 'planter-layout',
    title: '3x3 Planter Crossbreeding Layout',
    category: 'Breeding',
    summary: 'Master the proper planting order and arrangement of center and surrounding parent plants.',
    content: [
      'In a Large Planter Box (3×3 grid), follow this exact planting order:',
      '• **Step 1**: Plant the **Center Target Plant** (or any base plant of the same type) in the center slot (Position 5) first, leaving the surrounding slots empty.',
      '• **Step 2**: Wait until the center plant grows to the **Sapling stage** (the stage right before crossbreeding triggers).',
      '• **Step 3**: Plant the **Surrounding Parent Plants** in the adjacent cross slots. When the center plant transitions to Crossbreeding, the surrounding parents donate their genes to rewrite the center plant.',
      '• **Step 4**: Once crossbreeding completes, the center plant transforms into your target god-clone! Take cuttings at the Ripe stage to duplicate it.'
    ],
    keyPoints: [
      'Always plant the center plant first and let it reach Sapling before planting surrounding parents.',
      'A minimum of 2 surrounding donor plants with matching green genes are required to overpower red genes.'
    ]
  },
  {
    id: 'multi-gen',
    title: 'Multi-Generation Breeding',
    category: 'Breeding',
    summary: 'How to breed perfect 6-green (e.g. 3G3Y or 4G2Y) clones in 2 or 3 generations.',
    content: [
      'Wild seed genetics are usually poor with multiple red genes (W, X).',
      'The Genetics Lab calculator searches multi-generation pathways:',
      '• **Gen 1**: Breeds wild seeds into intermediate hybrid plants with fewer red genes.',
      '• **Gen 2**: Breeds intermediate Gen 1 hybrids together to eliminate remaining red genes.',
      '• **Gen 3**: Final refinement step to produce perfect god-clones like **GGYYYY** or **GGGYYY**.'
    ],
    keyPoints: [
      'Never throw away seeds with G and Y in rare slot positions; they are invaluable intermediate parents.',
      'The calculator automatically chains dependency steps and shows intermediate recipe requirements.'
    ]
  },
  {
    id: 'optimal-conditions',
    title: 'Farming Environment & 100% Health',
    category: 'Optimization',
    summary: 'Lighting, watering, temperature, and fertilizer conditions.',
    content: [
      'To reach 100% plant condition and maximum yield speed:',
      '• **Water**: Keep planter water saturation between 4,000 and 6,000 mL using fluid switches and timers.',
      '• **Light**: Ceiling lights above planters provide 100% light 24/7, even through the night.',
      '• **Temperature**: Use heaters in snow biomes to keep temperature at 100%.',
      '• **Fertilizer**: Place Horse Dung / Compost into planters to maintain 100% soil nutrition.'
    ],
    keyPoints: [
      '100% overall condition doubles growth speed compared to wild ground growth.',
      'Pure Ore and Wood teas created from berry harvests provide game-changing resource boosts.'
    ]
  }
];
