import type { ActiveTab } from '../../context/AppContext.tsx';

export interface TourStep {
  /** Chapter shown above the title. */
  chapter: string;
  title: string;
  /** Paragraphs; short, one idea each. */
  body: string[];
  /** The page the step is about; the tour switches to it. */
  page?: ActiveTab;
  /** `data-tour` anchor to spotlight. Without one, or when it is not on screen, the card is centred. */
  target?: string;
}

/** The whole app, page by page, in the order a new user meets it. */
export const TOUR_STEPS: TourStep[] = [
  {
    chapter: 'Welcome',
    title: 'Welcome to Rust Genetics Lab',
    body: [
      'Plan Rust farming from clone to harvest: breed the genes you want, scan plants and animals straight from the game, and size a whole farm.',
      'This tour walks through every part of the app in about three minutes. Use the arrow keys or the buttons below; Esc closes it at any time.'
    ]
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Pick your plant',
    body: [
      'Everything in the workspace is for the plant chosen here. The grid uses the game’s item icons and shows how many clones you have of each.'
    ],
    page: 'workspace',
    target: 'plant-picker'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Your clones',
    body: [
      'Gene Inputs is the list of clones you own, one six-gene line each (like GGYYHH). Type them, paste a list, or scan them from Rust.',
      'The numbers match positions in a storage box, so routes can tell you exactly which clone to take out.'
    ],
    page: 'workspace',
    target: 'gene-inputs'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Edit and move clones',
    body: [
      'Each line shows its genes as badges as you type.',
      'Click a row’s number, or right-click the row, to move that clone to another plant’s list.'
    ],
    page: 'workspace',
    target: 'gene-list'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'One list per plant',
    body: [
      'Each plant keeps its own list, and only the open one is calculated. Chips switch between them, + starts another, and Groups shows them all with their genes.',
      'Clearing a group can always be undone from the notice.'
    ],
    page: 'workspace',
    target: 'plant-groups'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Saved sets and samples',
    body: [
      'SAVE keeps the current list as a set you can restore from this tab later. SAMPLE loads example clones if you just want to try things out.'
    ],
    page: 'workspace',
    target: 'saved-sets'
  },
  {
    chapter: 'Scanner',
    title: 'Scan genes from Rust',
    body: [
      'Share your Rust screen once and hover clones in game: in the inventory or in a planter. Each one is read and added to the right plant’s list on its own.',
      'The Scanner HUD shows the two regions it reads. Auto-Detect places them on the gene row; the arrows and zoom fine-tune them, and presets keep them per resolution.',
      'The plant type is read from the panel title. A quick misread is corrected in place rather than added twice.'
    ],
    page: 'workspace',
    target: 'scan'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Set the breeding goal',
    body: [
      'Pick the genes you want: a common preset like 3G 3Y or 2G 4Y, or More goals for the rest.'
    ],
    page: 'workspace',
    target: 'goal'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Exact targets',
    body: [
      'Advanced sets a gene for each of the six slots and how strictly to match: exactly, at least these genes, or the best you can get.'
    ],
    page: 'workspace',
    target: 'goal-advanced'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Calculate routes',
    body: [
      'Searches every way your clones can be crossed to reach the goal. The depth sets how many generations to look through: more finds more, and takes longer.',
      'Ctrl+Enter calculates from anywhere.'
    ],
    page: 'workspace',
    target: 'calculate'
  },
  {
    chapter: 'Breeding Workspace',
    title: 'Routes and the inspector',
    body: [
      'Routes appear here, best first. Sort, filter and group them from View; tick a few to compare side by side.',
      'Select one to open the inspector: what it produces, which clones go where, generation by generation, and why it works.',
      'Planter Guide shows the route in Rust’s real 3D planters, and Start Guided Breeding walks you through it in game, step by step, with the current generation in 3D.'
    ],
    page: 'workspace',
    target: 'routes'
  },
  {
    chapter: 'Livestock (beta)',
    title: 'Scan your animals',
    body: [
      'Scan cows, bulls and sheep straight from their in-game panel; no calibration needed. A phone camera or manual entry work too.'
    ],
    page: 'livestock',
    target: 'livestock-scan'
  },
  {
    chapter: 'Livestock (beta)',
    title: 'Your herd',
    body: ['Every animal you scan or add, with its genes at a glance. Filter by kind, or import and export the herd from the menu.'],
    page: 'livestock',
    target: 'livestock-herd'
  },
  {
    chapter: 'Livestock (beta)',
    title: 'Genes, pairs and rules',
    body: [
      'Animal explains each gene and what the animal sells for. Pair suggestions finds the pairs worth breeding and shows the expected calf or lamb gene by gene. Rules covers taming, inbreeding, dung and selling.'
    ],
    page: 'livestock',
    target: 'livestock-tabs'
  },
  {
    chapter: 'Farm Planner',
    title: 'Plan a farm, or check one',
    body: [
      'Plan by goal works back from what you want to produce (cloth, berries, tea, food) to planters, clones, water, power and a build checklist.',
      'Audit a setup takes your farm as it is and names the system holding it back, with the smallest fix.'
    ],
    page: 'planner',
    target: 'planner-mode'
  },
  {
    chapter: 'Tea Recipes',
    title: 'Recipes',
    body: ['Teas, pies, food and resources, by category, with every ingredient listed.'],
    page: 'recipes',
    target: 'recipe-categories'
  },
  {
    chapter: 'Tea Recipes',
    title: 'Scale a batch',
    body: ['Multiply the ingredient totals for a run of 5, 10 or 50.'],
    page: 'recipes',
    target: 'recipe-multiplier'
  },
  {
    chapter: 'Genetics Guide',
    title: 'Learn how breeding works',
    body: [
      'An interactive 3D planter: breeding steps for your plant, which slots cross with which in each planter, and every growth stage. Click any plant for its genes.',
      'The topics alongside explain gene weights, growth and planter layouts.'
    ],
    page: 'guide',
    target: 'guide-3d'
  },
  {
    chapter: 'Everywhere',
    title: 'Projects and your data',
    body: [
      'Save a whole farm as a project and switch between them. Everything stays in this browser; export it to back it up or move it.'
    ],
    target: 'projects'
  },
  {
    chapter: 'Everywhere',
    title: 'More',
    body: [
      'Patch notes, options, keyboard shortcuts and the scanner guide live here, and so does this tutorial: open it again any time.'
    ],
    target: 'menu'
  },
  {
    chapter: 'Done',
    title: 'You’re set',
    body: [
      'Start with your plant and a few clones, set a goal and calculate. If anything is unclear, the tutorial is always in the ⋮ menu.'
    ],
    page: 'workspace'
  }
];
