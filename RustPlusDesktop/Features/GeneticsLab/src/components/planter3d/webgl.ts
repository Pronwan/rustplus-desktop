// Kept apart from PlanterScene.ts so checking for WebGL does not pull three.js into the
// main bundle.

/** True when the browser can create a WebGL context; the guide falls back to 2D otherwise. */
export function isWebGLAvailable(): boolean {
  try {
    const canvas = document.createElement('canvas');
    const ok = Boolean(canvas.getContext('webgl2') || canvas.getContext('webgl'));
    canvas.width = canvas.height = 0;
    return ok;
  } catch {
    return false;
  }
}
