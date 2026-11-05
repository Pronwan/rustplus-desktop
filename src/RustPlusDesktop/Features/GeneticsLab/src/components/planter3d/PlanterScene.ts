// Imperative three.js scene behind the 3D planting guide. React owns the UI around it
// (see Planter3DView.tsx) and drives it through the setters below.
//
// Rendering is on demand: the loop only draws while something moves (camera, growth,
// gene flow) and stops entirely when the canvas is off screen or the page is hidden, so
// an open guide costs next to nothing while idle.

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { GLTFLoader, type GLTF } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { DRACOLoader } from 'three/examples/jsm/loaders/DRACOLoader.js';
import { CSS2DObject, CSS2DRenderer } from 'three/examples/jsm/renderers/CSS2DRenderer.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import type { PlanterType } from '../../domain/planner/farmPlanning.ts';
import {
  BERRY_TINTS,
  GENE_COLORS,
  PLANTER_MODELS,
  PLANT_MODEL_URLS,
  STAGE_MODEL_URLS,
  STAGE_SCALE,
  type GrowthStage,
  type PlanterModel
} from './planterModels.ts';

export type SlotRole = 'plant' | 'target' | 'donor' | 'result';

export interface SlotContent {
  stage: GrowthStage;
  role: SlotRole;
  /** Six gene letters, e.g. "GGYYHH". Shown above the plant when set. */
  genes?: string;
  /** Small caption under the genes, e.g. "Donor #2". */
  caption?: string;
}

export type SlotHighlight = 'none' | 'selected' | 'neighbour' | 'target' | 'donor' | 'dim';

export interface PlanterSceneOptions {
  accent: string;
  onSlotClick?: (slot: number) => void;
  onSlotHover?: (slot: number | null) => void;
  onLoadingChange?: (loading: boolean) => void;
  onError?: (error: unknown) => void;
}

// ── Shared model loading ───────────────────────────────────────────────────────────────

let sharedLoader: GLTFLoader | null = null;
const gltfCache = new Map<string, Promise<GLTF>>();

function loader(): GLTFLoader {
  if (!sharedLoader) {
    // Decoder files ship in public/draco so the guide works offline in the desktop app.
    const draco = new DRACOLoader();
    draco.setDecoderPath('./draco/');
    sharedLoader = new GLTFLoader();
    sharedLoader.setDRACOLoader(draco);
  }
  return sharedLoader;
}

function loadGltf(url: string): Promise<GLTF> {
  let pending = gltfCache.get(url);
  if (!pending) {
    pending = loader().loadAsync(url);
    pending.catch(() => gltfCache.delete(url));
    gltfCache.set(url, pending);
  }
  return pending;
}

/**
 * The exported models carry no metallic/roughness factors, which glTF reads as fully
 * metallic: everything would render as dark chrome. They are plain diffuse props.
 *
 * Many textures carry cut-outs in their alpha (every plant's leaves, a berry bush's being
 * 83% transparent, and the planters' tarps) but the export marks every material opaque,
 * so the clear parts render black. Every textured material is alpha-tested and
 * double-sided; on textures without alpha the test never discards anything.
 */
function prepareModel(root: THREE.Object3D): void {
  root.traverse((node) => {
    const mesh = node as THREE.Mesh;
    if (!mesh.isMesh) return;
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    const materials = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
    for (const material of materials) {
      const standard = material as THREE.MeshStandardMaterial;
      if (standard.isMeshStandardMaterial) {
        standard.metalness = 0;
        standard.roughness = 0.88;
        standard.envMapIntensity = 0.55;
        if (standard.map) {
          standard.alphaTest = 0.5;
          // Smooths the cut edges using the renderer's MSAA instead of hard stair-steps.
          standard.alphaToCoverage = true;
          standard.transparent = false;
          standard.side = THREE.DoubleSide;
          standard.needsUpdate = true;
        }
      }
    }
  });
}

// Plants are fitted into a slot: no wider than the slot, no taller than this.
const SLOT_FOOTPRINT = 0.64;
const MAX_PLANT_HEIGHT = 0.85;
// Vines (the pumpkin stage models) sprawl past their slot in game too; held to one slot
// width a fruiting pumpkin would shrink to a speck.
const VINE_FOOTPRINT = 1.25;

interface PlantTemplate {
  /** Model per stage; plants without stage models reuse one model for every stage. */
  models: Partial<Record<GrowthStage, THREE.Object3D>>;
  fallback: THREE.Object3D;
  /** Whether `models` holds real stage models (true) or stages are faked by scaling. */
  staged: boolean;
  /** Height of the fully grown plant after fitting, for placing labels. */
  height: number;
}

const templateCache = new Map<string, Promise<PlantTemplate>>();

/** Wraps `model` so it stands on the origin, centred, scaled by `scale`. */
function fitted(model: THREE.Object3D, scale: number): THREE.Object3D {
  const box = new THREE.Box3().setFromObject(model);
  const centre = box.getCenter(new THREE.Vector3());
  const holder = new THREE.Group();
  model.scale.multiplyScalar(scale);
  model.position.set(-centre.x * scale, -box.min.y * scale, -centre.z * scale);
  holder.add(model);
  return holder;
}

function fitScale(model: THREE.Object3D, footprint = SLOT_FOOTPRINT): { scale: number; height: number } {
  const size = new THREE.Box3().setFromObject(model).getSize(new THREE.Vector3());
  const scale = Math.min(footprint / Math.max(size.x, size.z), MAX_PLANT_HEIGHT / size.y);
  return { scale, height: size.y * scale };
}

function loadPlantTemplate(plantType: string): Promise<PlantTemplate> {
  let pending = templateCache.get(plantType);
  if (pending) return pending;

  pending = (async () => {
    const stageUrls = STAGE_MODEL_URLS[plantType];
    if (stageUrls) {
      const entries = await Promise.all(
        (Object.keys(stageUrls) as GrowthStage[]).map(async (stage) => {
          const gltf = await loadGltf(stageUrls[stage]);
          const scene = gltf.scene.clone(true);
          prepareModel(scene);
          return [stage, scene] as const;
        })
      );
      // One scale for every stage, taken from the largest, so the stages keep their real
      // sizes relative to each other.
      const largest = entries.find(([stage]) => stage === 'fruiting')?.[1] ?? entries[entries.length - 1][1];
      const { scale, height } = fitScale(largest, VINE_FOOTPRINT);
      const models: Partial<Record<GrowthStage, THREE.Object3D>> = {};
      for (const [stage, scene] of entries) models[stage] = fitted(scene, scale);
      return { models, fallback: models.fruiting ?? Object.values(models)[0]!, staged: true, height };
    }

    const gltf = await loadGltf(PLANT_MODEL_URLS[plantType] ?? PLANT_MODEL_URLS.hemp);
    const scene = gltf.scene.clone(true);
    prepareModel(scene);
    const tint = BERRY_TINTS[plantType];
    if (tint) tintBerries(scene, tint);
    const { scale, height } = fitScale(scene);
    return { models: {}, fallback: fitted(scene, scale), staged: false, height };
  })();

  pending.catch(() => templateCache.delete(plantType));
  templateCache.set(plantType, pending);
  return pending;
}

const MIXED_BERRIES: [number, number, number][] = [
  [205, 35, 45],
  [60, 95, 210],
  [238, 192, 40]
];

/** RGB (0..255) to hue, saturation and value, each 0..1. */
function hsv(r: number, g: number, b: number): [number, number, number] {
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const d = max - min;
  let h = 0;
  if (d > 0) {
    if (max === r) h = ((g - b) / d + 6) % 6;
    else if (max === g) h = (b - r) / d + 2;
    else h = (r - g) / d + 4;
  }
  return [h / 6, max === 0 ? 0 : d / max, max / 255];
}

/**
 * Recolours the berries of the shared berry-bush texture. The berries are its only
 * blue-grey pixels (leaves are green, stems and dry leaves brown), so those take the
 * variety's colour with their original shading; everything else is left alone.
 */
function tintBerries(root: THREE.Object3D, tint: [number, number, number] | 'mixed'): void {
  const recoloured = new Map<THREE.Texture, THREE.Texture>();
  root.traverse((node) => {
    const mesh = node as THREE.Mesh;
    if (!mesh.isMesh) return;
    const swap = (material: THREE.Material) => {
      const standard = material as THREE.MeshStandardMaterial;
      const source = standard.map;
      const image = source?.image as CanvasImageSource & { width: number; height: number } | undefined;
      if (!source || !image?.width) return material;

      let texture = recoloured.get(source);
      if (!texture) {
        const canvas = document.createElement('canvas');
        canvas.width = image.width;
        canvas.height = image.height;
        const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
        ctx.drawImage(image, 0, 0);
        const pixels = ctx.getImageData(0, 0, canvas.width, canvas.height);
        const data = pixels.data;
        for (let i = 0; i < data.length; i += 4) {
          if (data[i + 3] < 128) continue;
          const [h, sat, v] = hsv(data[i], data[i + 1], data[i + 2]);
          const isBerry = (v > 0.3 && sat < 0.16) || (h >= 0.45 && h <= 0.8 && v > 0.22);
          if (!isBerry) continue;
          const p = i / 4;
          const colour =
            tint === 'mixed'
              ? MIXED_BERRIES[(((Math.floor((p % canvas.width) / 5) * 73856093) ^ (Math.floor(p / canvas.width / 5) * 19349663)) >>> 0) % 3]
              : tint;
          const light = Math.min(1, v * 1.15);
          data[i] = colour[0] * light;
          data[i + 1] = colour[1] * light;
          data[i + 2] = colour[2] * light;
        }
        ctx.putImageData(pixels, 0, 0);
        // Its own Source: Texture.clone() shares the original's, and assigning `image` would
        // overwrite the berry texture every other variety still reads from.
        texture = source.clone();
        texture.source = new THREE.Source(canvas);
        texture.needsUpdate = true;
        recoloured.set(source, texture);
      }

      const copy = standard.clone();
      copy.map = texture;
      return copy;
    };
    mesh.material = Array.isArray(mesh.material) ? mesh.material.map(swap) : swap(mesh.material);
  });
}

/** A browned copy of a model for the dying stage of plants without a dying model. */
function withered(model: THREE.Object3D): THREE.Object3D {
  const copy = model.clone(true);
  copy.traverse((node) => {
    const mesh = node as THREE.Mesh;
    if (!mesh.isMesh) return;
    const tint = (m: THREE.Material) => {
      const c = (m as THREE.MeshStandardMaterial).clone();
      c.color = new THREE.Color('#9b7b4f');
      return c;
    };
    mesh.material = Array.isArray(mesh.material) ? mesh.material.map(tint) : tint(mesh.material);
  });
  return copy;
}

// ── Scene ──────────────────────────────────────────────────────────────────────────────

interface SlotVisual {
  ring: THREE.Mesh<THREE.RingGeometry, THREE.MeshBasicMaterial>;
  hit: THREE.Mesh;
  numberLabel: CSS2DObject;
  plant: THREE.Group;
  plantKey: string;
  geneLabel: CSS2DObject | null;
  /** Displayed scale tweens from `growFrom` to `growTo` as `growT` runs 0 → 1. */
  growFrom: number;
  growTo: number;
  growT: number;
  swayPhase: number;
  highlight: SlotHighlight;
  pulse: number;
}

interface FlowArc {
  tube: THREE.Mesh;
  beads: THREE.Mesh[];
  curve: THREE.QuadraticBezierCurve3;
}

const easeOutBack = (t: number) => {
  const c1 = 1.70158;
  const c3 = c1 + 1;
  return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2);
};

export class PlanterScene {
  private readonly container: HTMLElement;
  private readonly options: PlanterSceneOptions;
  private readonly renderer: THREE.WebGLRenderer;
  private readonly labelRenderer: CSS2DRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera: THREE.PerspectiveCamera;
  private readonly controls: OrbitControls;
  private readonly timer = new THREE.Timer();
  private readonly raycaster = new THREE.Raycaster();
  private readonly pointer = new THREE.Vector2();
  private readonly owned = new Set<{ dispose(): void }>();
  private readonly accent: THREE.Color;

  private planterRoot = new THREE.Group();
  private slotsRoot = new THREE.Group();
  private flowRoot = new THREE.Group();
  private sparkles: THREE.Points | null = null;
  private glow: THREE.PointLight;

  private model: PlanterModel | null = null;
  private planterType: PlanterType | null = null;
  private plantType = 'hemp';
  private template: PlantTemplate | null = null;
  private slots: SlotVisual[] = [];
  private contents: (SlotContent | null)[] = [];
  private highlights: SlotHighlight[] = [];
  private flows: FlowArc[] = [];
  private flowSpec: { from: number[]; to: number | null } = { from: [], to: null };
  private celebrateSlot: number | null = null;
  private hovered: number | null = null;

  private frame = 0;
  private running = false;
  private visible = true;
  private dirty = true;
  private disposed = false;
  private loadToken = 0;
  private pointerDown: { x: number; y: number } | null = null;
  private readonly resizeObserver: ResizeObserver;
  private readonly intersectionObserver: IntersectionObserver;

  constructor(container: HTMLElement, options: PlanterSceneOptions) {
    this.container = container;
    this.options = options;
    this.accent = new THREE.Color(options.accent);

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.renderer.domElement.style.display = 'block';
    this.renderer.domElement.style.touchAction = 'none';
    container.appendChild(this.renderer.domElement);

    this.labelRenderer = new CSS2DRenderer();
    Object.assign(this.labelRenderer.domElement.style, { position: 'absolute', inset: '0', pointerEvents: 'none' });
    container.appendChild(this.labelRenderer.domElement);

    this.camera = new THREE.PerspectiveCamera(36, 1, 0.05, 60);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.08;
    this.controls.minDistance = 1.6;
    this.controls.maxDistance = 9;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    this.controls.autoRotateSpeed = 0.8;
    this.controls.addEventListener('change', () => this.invalidate());

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    const room = new RoomEnvironment();
    const envTexture = pmrem.fromScene(room, 0.04).texture;
    this.scene.environment = envTexture;
    this.owned.add(envTexture);
    room.dispose();
    pmrem.dispose();

    this.buildStage();
    this.glow = new THREE.PointLight(this.accent, 0, 2.2, 1.6);
    this.scene.add(this.glow, this.planterRoot, this.slotsRoot, this.flowRoot);

    const canvas = this.renderer.domElement;
    canvas.addEventListener('pointermove', this.handlePointerMove);
    canvas.addEventListener('pointerdown', this.handlePointerDown);
    canvas.addEventListener('pointerup', this.handlePointerUp);
    canvas.addEventListener('pointerleave', this.handlePointerLeave);
    document.addEventListener('visibilitychange', this.handleVisibility);

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(container);
    this.intersectionObserver = new IntersectionObserver((entries) => {
      this.visible = entries.some((e) => e.isIntersecting);
      this.updateLoop();
    });
    this.intersectionObserver.observe(container);
    this.resize();
    this.updateLoop();
  }

  // ── Public API ─────────────────────────────────────────────────────────────────────

  async setPlanter(type: PlanterType): Promise<void> {
    if (this.planterType === type) return;
    this.planterType = type;
    const model = PLANTER_MODELS[type];
    const token = ++this.loadToken;
    this.options.onLoadingChange?.(true);
    try {
      const gltf = await loadGltf(model.url);
      if (this.disposed || token !== this.loadToken) return;

      this.planterRoot.clear();
      const planter = gltf.scene.clone(true);
      prepareModel(planter);
      this.planterRoot.add(planter);
      this.model = model;
      this.rebuildSlots();
      this.frameCamera();
      await this.refreshPlants();
    } catch (error) {
      this.options.onError?.(error);
    } finally {
      if (token === this.loadToken) this.options.onLoadingChange?.(false);
    }
  }

  async setPlantType(plantType: string): Promise<void> {
    if (this.plantType === plantType && this.template) return;
    this.plantType = plantType;
    await this.refreshPlants();
  }

  setSlots(contents: (SlotContent | null)[]): void {
    this.contents = contents;
    void this.refreshPlants();
  }

  setHighlights(highlights: SlotHighlight[]): void {
    this.highlights = highlights;
    this.applyHighlights();
  }

  /** Animated gene flow from each `from` slot into `to`; pass `to: null` to clear. */
  setFlow(from: number[], to: number | null): void {
    this.flowSpec = { from, to };
    this.rebuildFlows();
  }

  /** Glow and sparkles over a finished clone; null to stop. */
  setCelebrate(slot: number | null): void {
    this.celebrateSlot = slot;
    if (this.sparkles) {
      this.scene.remove(this.sparkles);
      this.sparkles.geometry.dispose();
      (this.sparkles.material as THREE.Material).dispose();
      this.sparkles = null;
    }
    if (slot !== null && this.model) {
      this.sparkles = this.createSparkles();
      const s = this.model.slots[slot];
      this.sparkles.position.set(s.x, this.model.soilY, s.z);
      this.scene.add(this.sparkles);
      this.glow.position.set(s.x, this.model.soilY + 0.6, s.z);
    }
    this.glow.intensity = 0;
    this.invalidate();
  }

  setAutoRotate(enabled: boolean): void {
    this.controls.autoRotate = enabled;
    this.updateLoop();
  }

  resetCamera(): void {
    this.frameCamera();
  }

  dispose(): void {
    this.disposed = true;
    this.running = false;
    cancelAnimationFrame(this.frame);
    this.resizeObserver.disconnect();
    this.intersectionObserver.disconnect();
    document.removeEventListener('visibilitychange', this.handleVisibility);
    const canvas = this.renderer.domElement;
    canvas.removeEventListener('pointermove', this.handlePointerMove);
    canvas.removeEventListener('pointerdown', this.handlePointerDown);
    canvas.removeEventListener('pointerup', this.handlePointerUp);
    canvas.removeEventListener('pointerleave', this.handlePointerLeave);

    this.clearFlows();
    this.clearSlots();
    this.setCelebrate(null);
    this.controls.dispose();
    this.timer.dispose();
    for (const item of this.owned) item.dispose();
    this.owned.clear();
    this.renderer.dispose();
    this.renderer.forceContextLoss();
    canvas.remove();
    this.labelRenderer.domElement.remove();
  }

  // ── Building ─────────────────────────────────────────────────────────────────────────

  private own<T extends { dispose(): void }>(item: T): T {
    this.owned.add(item);
    return item;
  }

  private buildStage(): void {
    this.scene.add(new THREE.HemisphereLight('#e8f0ff', '#3b2f22', 0.9));

    const sun = new THREE.DirectionalLight('#fff4e0', 2.4);
    sun.position.set(3.5, 6, 2.5);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    sun.shadow.camera.left = -3;
    sun.shadow.camera.right = 3;
    sun.shadow.camera.top = 3;
    sun.shadow.camera.bottom = -3;
    sun.shadow.bias = -0.0004;
    sun.shadow.normalBias = 0.02;
    this.scene.add(sun);

    const rim = new THREE.DirectionalLight('#9fc7ff', 0.6);
    rim.position.set(-4, 3, -3);
    this.scene.add(rim);

    // Ground: a soft radial pool of light that fades into the page background, plus a
    // shadow catcher so the planter sits on something.
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 256;
    const ctx = canvas.getContext('2d')!;
    const gradient = ctx.createRadialGradient(128, 128, 0, 128, 128, 128);
    gradient.addColorStop(0, 'rgba(120, 130, 120, 0.42)');
    gradient.addColorStop(0.55, 'rgba(80, 90, 85, 0.18)');
    gradient.addColorStop(1, 'rgba(0, 0, 0, 0)');
    ctx.fillStyle = gradient;
    ctx.fillRect(0, 0, 256, 256);
    const groundTexture = this.own(new THREE.CanvasTexture(canvas));
    groundTexture.colorSpace = THREE.SRGBColorSpace;
    const ground = new THREE.Mesh(
      this.own(new THREE.CircleGeometry(4.5, 64)),
      this.own(new THREE.MeshBasicMaterial({ map: groundTexture, transparent: true, depthWrite: false }))
    );
    ground.rotation.x = -Math.PI / 2;
    ground.position.y = -0.002;
    this.scene.add(ground);

    const shadowCatcher = new THREE.Mesh(
      this.own(new THREE.CircleGeometry(3.5, 48)),
      this.own(new THREE.ShadowMaterial({ opacity: 0.32 }))
    );
    shadowCatcher.rotation.x = -Math.PI / 2;
    shadowCatcher.receiveShadow = true;
    this.scene.add(shadowCatcher);
  }

  private frameCamera(): void {
    if (!this.model) return;
    const [x, y, z] = this.model.camera;
    const aspect = this.camera.aspect || 1;
    // Narrower viewports need more distance to fit the planter's width; very wide strips
    // have spare width, so the camera comes closer to use their height.
    const distanceScale = aspect < 1.6 ? Math.min(1.9, 1.6 / aspect) : aspect > 2.4 ? 0.82 : 1;
    const target = this.slotCentroid();
    this.controls.target.set(target.x, this.model.soilY * 0.7, target.z);
    this.camera.position.set(target.x + x * distanceScale, y * distanceScale, target.z + z * distanceScale);
    this.controls.update();
    this.invalidate();
  }

  private slotCentroid(): THREE.Vector3 {
    const c = new THREE.Vector3();
    if (!this.model) return c;
    for (const s of this.model.slots) c.add(new THREE.Vector3(s.x, 0, s.z));
    return c.multiplyScalar(1 / this.model.slots.length);
  }

  private clearSlots(): void {
    for (const slot of this.slots) {
      slot.ring.geometry.dispose();
      slot.ring.material.dispose();
      (slot.hit as THREE.Mesh<THREE.BufferGeometry, THREE.Material>).geometry.dispose();
      (slot.hit.material as THREE.Material).dispose();
      slot.numberLabel.element.remove();
      slot.geneLabel?.element.remove();
    }
    this.slotsRoot.clear();
    this.slots = [];
  }

  private rebuildSlots(): void {
    this.clearSlots();
    const model = this.model;
    if (!model) return;

    model.slots.forEach((slot, index) => {
      const ring = new THREE.Mesh(
        new THREE.RingGeometry(0.31, 0.37, 48),
        new THREE.MeshBasicMaterial({ color: '#ffffff', transparent: true, opacity: 0.22, depthWrite: false })
      );
      ring.rotation.x = -Math.PI / 2;
      ring.position.set(slot.x, model.soilY + 0.015, slot.z);
      ring.renderOrder = 2;

      const hit = new THREE.Mesh(
        new THREE.CircleGeometry(0.44, 24),
        new THREE.MeshBasicMaterial({ transparent: true, opacity: 0, depthWrite: false, colorWrite: false })
      );
      hit.rotation.x = -Math.PI / 2;
      hit.position.set(slot.x, model.soilY + 0.02, slot.z);
      hit.userData.slot = index;

      const badge = document.createElement('div');
      badge.textContent = String(slot.number);
      Object.assign(badge.style, {
        font: '700 11px ui-monospace, SFMono-Regular, Menlo, monospace',
        color: '#f3f4f6',
        background: 'rgba(12, 14, 18, 0.72)',
        border: '1px solid rgba(255,255,255,0.18)',
        borderRadius: '999px',
        width: '20px',
        height: '20px',
        display: 'grid',
        placeItems: 'center',
        transition: 'all 160ms ease'
      } satisfies Partial<CSSStyleDeclaration>);
      const numberLabel = new CSS2DObject(badge);
      // On the soil at the slot centre: always visible over the plant's base, never off the box.
      numberLabel.position.set(slot.x, model.soilY + 0.03, slot.z);

      const plant = new THREE.Group();
      plant.position.set(slot.x, model.soilY - 0.02, slot.z);

      this.slotsRoot.add(ring, hit, numberLabel, plant);
      this.slots.push({
        ring,
        hit,
        numberLabel,
        plant,
        plantKey: '',
        geneLabel: null,
        growFrom: 0,
        growTo: 0,
        growT: 1,
        swayPhase: index * 1.7,
        highlight: 'none',
        pulse: 0
      });
    });

    this.applyHighlights();
    this.rebuildFlows();
    if (this.celebrateSlot !== null) this.setCelebrate(this.celebrateSlot);
  }

  private async refreshPlants(): Promise<void> {
    const plantType = this.plantType;
    let template: PlantTemplate;
    try {
      template = await loadPlantTemplate(plantType);
    } catch (error) {
      this.options.onError?.(error);
      return;
    }
    if (this.disposed || plantType !== this.plantType) return;
    const templateChanged = this.template !== template;
    this.template = template;

    this.slots.forEach((slot, index) => {
      const content = this.contents[index] ?? null;
      const key = content ? `${plantType}|${content.stage}` : '';

      if (templateChanged || key !== slot.plantKey) {
        const wasEmpty = slot.plantKey === '';
        const current = slot.plant.scale.x;
        slot.plant.clear();
        slot.plantKey = key;
        if (content) {
          slot.plant.add(this.instantiate(template, content.stage));
          // Plants with stage models swap the model and settle with a small pop; the rest
          // grow (or shrink) from their current size to the stage's size.
          const to = template.staged ? 1 : STAGE_SCALE[content.stage];
          const from = wasEmpty ? 0 : template.staged ? 0.82 : current;
          this.tweenGrowth(slot, from, to);
        } else {
          this.tweenGrowth(slot, 0, 0);
        }
      }

      this.updateGeneLabel(slot, content, template);
    });

    this.rebuildFlows();
    this.invalidate();
  }

  private tweenGrowth(slot: SlotVisual, from: number, to: number): void {
    slot.growFrom = from;
    slot.growTo = to;
    slot.growT = from === to ? 1 : 0;
    slot.plant.scale.setScalar(Math.max(0.0001, from));
  }

  private instantiate(template: PlantTemplate, stage: GrowthStage): THREE.Object3D {
    if (template.staged) return (template.models[stage] ?? template.fallback).clone(true);
    if (stage === 'dying') return withered(template.fallback);
    return template.fallback.clone(true);
  }

  private plantTop(content: SlotContent, template: PlantTemplate): number {
    let height: number;
    if (template.staged) {
      // Measured on the detached template, so the slot's current (animating) scale is ignored.
      const model = template.models[content.stage] ?? template.fallback;
      height = Math.max(0.12, new THREE.Box3().setFromObject(model).getSize(new THREE.Vector3()).y);
    } else {
      height = template.height * STAGE_SCALE[content.stage];
    }
    return (this.model?.soilY ?? 0.4) + height + 0.14;
  }

  private updateGeneLabel(slot: SlotVisual, content: SlotContent | null, template: PlantTemplate): void {
    if (slot.geneLabel) {
      slot.geneLabel.element.remove();
      this.slotsRoot.remove(slot.geneLabel);
      slot.geneLabel = null;
    }
    if (!content?.genes && !content?.caption) return;

    // Phones get smaller labels so neighbouring plants' labels overlap less.
    const compact = this.container.clientWidth < 520;
    const dotSize = compact ? '11px' : '15px';
    const roleColor =
      content.role === 'target' ? this.options.accent : content.role === 'result' ? '#F2C94C' : content.role === 'donor' ? '#9CA3AF' : '#6B7280';
    const el = document.createElement('div');
    Object.assign(el.style, {
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: compact ? '1px' : '3px',
      padding: compact ? '2px 3px' : '4px 5px',
      borderRadius: '7px',
      background: 'rgba(10, 12, 16, 0.82)',
      border: `1px solid ${roleColor}`,
      boxShadow: content.role === 'result' ? '0 0 14px rgba(242, 201, 76, 0.55)' : '0 4px 12px rgba(0,0,0,0.35)',
      backdropFilter: 'blur(3px)'
    } satisfies Partial<CSSStyleDeclaration>);

    const row = document.createElement('div');
    Object.assign(row.style, { display: 'flex', gap: '2px' });
    for (const letter of (content.genes ?? '').slice(0, 6)) {
      const green = letter === 'G' || letter === 'Y' || letter === 'H';
      const dot = document.createElement('span');
      dot.textContent = letter;
      Object.assign(dot.style, {
        width: dotSize,
        height: dotSize,
        borderRadius: '50%',
        display: 'grid',
        placeItems: 'center',
        font: `800 ${compact ? 7 : 9}px ui-monospace, SFMono-Regular, Menlo, monospace`,
        color: '#fff',
        background: green ? GENE_COLORS.green : GENE_COLORS.red
      } satisfies Partial<CSSStyleDeclaration>);
      row.appendChild(dot);
    }
    if (content.genes) el.appendChild(row);

    if (content.caption) {
      const caption = document.createElement('div');
      caption.textContent = content.caption;
      Object.assign(caption.style, {
        font: `700 ${compact ? 7 : 9}px ui-monospace, SFMono-Regular, Menlo, monospace`,
        color: roleColor,
        letterSpacing: '0.04em',
        textTransform: 'uppercase',
        whiteSpace: 'nowrap'
      } satisfies Partial<CSSStyleDeclaration>);
      el.appendChild(caption);
    }

    const label = new CSS2DObject(el);
    const s = slot.plant.position;
    label.position.set(s.x, this.plantTop(content, template), s.z);
    slot.geneLabel = label;
    this.slotsRoot.add(label);
  }

  private applyHighlights(): void {
    this.slots.forEach((slot, index) => {
      const hovered = this.hovered === index;
      const highlight = this.highlights[index] ?? 'none';
      slot.highlight = highlight;
      const material = slot.ring.material;
      const badge = slot.numberLabel.element.style;

      let color = new THREE.Color('#ffffff');
      let opacity = 0.22;
      switch (highlight) {
        case 'selected':
        case 'target':
          color = this.accent.clone();
          opacity = 0.95;
          break;
        case 'neighbour':
        case 'donor':
          color = new THREE.Color('#F2C94C');
          opacity = 0.85;
          break;
        case 'dim':
          opacity = 0.08;
          break;
      }
      if (hovered) opacity = Math.min(1, opacity + 0.35);
      material.color.copy(color);
      material.opacity = opacity;

      badge.background = highlight === 'selected' || highlight === 'target'
        ? `#${this.accent.getHexString()}`
        : highlight === 'neighbour' || highlight === 'donor'
          ? '#F2C94C'
          : 'rgba(12, 14, 18, 0.72)';
      badge.color = highlight === 'none' || highlight === 'dim' ? '#f3f4f6' : '#0b0d10';
      badge.opacity = highlight === 'dim' ? '0.45' : '1';
      badge.transform = hovered ? 'scale(1.18)' : 'scale(1)';
    });
    this.invalidate();
  }

  private clearFlows(): void {
    for (const arc of this.flows) {
      arc.tube.geometry.dispose();
      (arc.tube.material as THREE.Material).dispose();
      for (const bead of arc.beads) {
        bead.geometry.dispose();
        (bead.material as THREE.Material).dispose();
      }
    }
    this.flowRoot.clear();
    this.flows = [];
  }

  private rebuildFlows(): void {
    this.clearFlows();
    const { from, to } = this.flowSpec;
    const model = this.model;
    if (!model || to === null || !model.slots[to]) return;

    const end = new THREE.Vector3(model.slots[to].x, model.soilY + 0.45, model.slots[to].z);
    for (const index of from) {
      const s = model.slots[index];
      if (!s) continue;
      const start = new THREE.Vector3(s.x, model.soilY + 0.45, s.z);
      const mid = start.clone().lerp(end, 0.5);
      mid.y += 0.35 + start.distanceTo(end) * 0.25;
      const curve = new THREE.QuadraticBezierCurve3(start, mid, end);

      const tube = new THREE.Mesh(
        new THREE.TubeGeometry(curve, 40, 0.012, 6, false),
        new THREE.MeshBasicMaterial({ color: '#F2C94C', transparent: true, opacity: 0.45, depthWrite: false })
      );

      const genes = this.contents[index]?.genes ?? 'GGGGGG';
      const beads = Array.from({ length: 4 }, (_, k) => {
        const letter = genes[(k * 2) % genes.length];
        const green = letter === 'G' || letter === 'Y' || letter === 'H';
        const bead = new THREE.Mesh(
          new THREE.SphereGeometry(0.034, 12, 8),
          new THREE.MeshBasicMaterial({ color: green ? '#8BD13A' : '#E0573F' })
        );
        bead.userData.offset = k / 4;
        return bead;
      });

      this.flowRoot.add(tube, ...beads);
      this.flows.push({ tube, beads, curve });
    }
    this.updateLoop();
  }

  private createSparkles(): THREE.Points {
    const count = 70;
    const positions = new Float32Array(count * 3);
    const seeds = new Float32Array(count);
    for (let i = 0; i < count; i++) {
      seeds[i] = Math.random();
      const a = Math.random() * Math.PI * 2;
      const r = 0.1 + Math.random() * 0.35;
      positions.set([Math.cos(a) * r, Math.random() * 1.1, Math.sin(a) * r], i * 3);
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('seed', new THREE.BufferAttribute(seeds, 1));
    const material = new THREE.PointsMaterial({
      color: '#FFE08A',
      size: 0.045,
      transparent: true,
      opacity: 0.9,
      depthWrite: false,
      blending: THREE.AdditiveBlending
    });
    return new THREE.Points(geometry, material);
  }

  // ── Interaction ──────────────────────────────────────────────────────────────────────

  private slotAt(event: PointerEvent): number | null {
    const rect = this.renderer.domElement.getBoundingClientRect();
    this.pointer.set(((event.clientX - rect.left) / rect.width) * 2 - 1, -((event.clientY - rect.top) / rect.height) * 2 + 1);
    this.raycaster.setFromCamera(this.pointer, this.camera);
    const targets = this.slots.flatMap((s) => [s.hit, s.plant]);
    const hit = this.raycaster.intersectObjects(targets, true)[0];
    if (!hit) return null;
    let node: THREE.Object3D | null = hit.object;
    while (node) {
      if (typeof node.userData.slot === 'number') return node.userData.slot;
      const owner = this.slots.findIndex((s) => s.plant === node);
      if (owner >= 0) return owner;
      node = node.parent;
    }
    return null;
  }

  private readonly handlePointerMove = (event: PointerEvent) => {
    const slot = this.slotAt(event);
    if (slot !== this.hovered) {
      this.hovered = slot;
      this.renderer.domElement.style.cursor = slot === null ? 'grab' : 'pointer';
      this.options.onSlotHover?.(slot);
      this.applyHighlights();
    }
  };

  private readonly handlePointerDown = (event: PointerEvent) => {
    this.pointerDown = { x: event.clientX, y: event.clientY };
  };

  private readonly handlePointerUp = (event: PointerEvent) => {
    const start = this.pointerDown;
    this.pointerDown = null;
    // A drag that orbits the camera is not a click.
    if (!start || Math.hypot(event.clientX - start.x, event.clientY - start.y) > 6) return;
    const slot = this.slotAt(event);
    if (slot !== null) this.options.onSlotClick?.(slot);
  };

  private readonly handlePointerLeave = () => {
    if (this.hovered !== null) {
      this.hovered = null;
      this.options.onSlotHover?.(null);
      this.applyHighlights();
    }
  };

  private readonly handleVisibility = () => this.updateLoop();

  // ── Loop ─────────────────────────────────────────────────────────────────────────────

  private invalidate(): void {
    this.dirty = true;
    this.updateLoop();
  }

  private resize(): void {
    const width = Math.max(1, this.container.clientWidth);
    const height = Math.max(1, this.container.clientHeight);
    this.renderer.setSize(width, height, false);
    this.renderer.domElement.style.width = '100%';
    this.renderer.domElement.style.height = '100%';
    this.labelRenderer.setSize(width, height);
    const previousAspect = this.camera.aspect;
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    if (Math.abs(previousAspect - this.camera.aspect) > 0.25) this.frameCamera();
    this.invalidate();
  }

  private isAnimating(): boolean {
    return (
      this.controls.autoRotate ||
      this.flows.length > 0 ||
      this.celebrateSlot !== null ||
      this.slots.some((s) => s.growT < 1)
    );
  }

  private updateLoop(): void {
    const shouldRun = !this.disposed && this.visible && document.visibilityState !== 'hidden';
    if (shouldRun && !this.running) {
      this.running = true;
      // Skip the time spent stopped, so animations resume instead of jumping.
      this.timer.update();
      this.frame = requestAnimationFrame(this.tick);
    } else if (!shouldRun && this.running) {
      this.running = false;
      cancelAnimationFrame(this.frame);
    }
  }

  private readonly tick = (timestamp: number) => {
    if (!this.running) return;
    this.timer.update(timestamp);
    const dt = Math.min(this.timer.getDelta(), 0.05);
    const t = this.timer.getElapsed();
    const controlsMoved = this.controls.update();
    const animating = this.isAnimating();

    if (animating || this.dirty || controlsMoved) {
      this.animate(dt, t);
      this.renderer.render(this.scene, this.camera);
      this.labelRenderer.render(this.scene, this.camera);
      this.dirty = false;
    }

    // Idle: stop the loop until the next change (camera input restarts it via 'change').
    if (!animating && !controlsMoved && !this.dirty) {
      this.running = false;
      return;
    }
    this.frame = requestAnimationFrame(this.tick);
  };

  private animate(dt: number, t: number): void {
    for (const slot of this.slots) {
      if (slot.growT < 1) {
        slot.growT = Math.min(1, slot.growT + dt * 1.6);
        const scale = slot.growFrom + (slot.growTo - slot.growFrom) * easeOutBack(slot.growT);
        slot.plant.scale.setScalar(Math.max(0.0001, scale));
      }
      const presence = Math.min(1, slot.plant.scale.x * 2);
      slot.plant.rotation.z = Math.sin(t * 1.3 + slot.swayPhase) * 0.025 * presence;
      slot.plant.rotation.x = Math.cos(t * 1.1 + slot.swayPhase) * 0.018 * presence;

      if (slot.highlight === 'target' || slot.highlight === 'selected') {
        slot.ring.material.opacity = 0.7 + Math.sin(t * 3) * 0.25;
        slot.ring.scale.setScalar(1 + Math.sin(t * 3) * 0.04);
      } else {
        slot.ring.scale.setScalar(1);
      }
    }

    for (const arc of this.flows) {
      for (const bead of arc.beads) {
        const u = (t * 0.45 + (bead.userData.offset as number)) % 1;
        bead.position.copy(arc.curve.getPoint(u));
        bead.scale.setScalar(0.6 + Math.sin(u * Math.PI) * 0.7);
      }
      (arc.tube.material as THREE.MeshBasicMaterial).opacity = 0.32 + Math.sin(t * 2.4) * 0.12;
    }

    if (this.celebrateSlot !== null) {
      this.glow.intensity = 2.4 + Math.sin(t * 2.6) * 0.9;
      if (this.sparkles) {
        const positions = this.sparkles.geometry.getAttribute('position') as THREE.BufferAttribute;
        const seeds = this.sparkles.geometry.getAttribute('seed') as THREE.BufferAttribute;
        for (let i = 0; i < positions.count; i++) {
          let y = positions.getY(i) + dt * (0.25 + seeds.getX(i) * 0.35);
          if (y > 1.25) y = 0;
          positions.setY(i, y);
        }
        positions.needsUpdate = true;
        (this.sparkles.material as THREE.PointsMaterial).opacity = 0.65 + Math.sin(t * 4) * 0.25;
      }
    }
  }
}
