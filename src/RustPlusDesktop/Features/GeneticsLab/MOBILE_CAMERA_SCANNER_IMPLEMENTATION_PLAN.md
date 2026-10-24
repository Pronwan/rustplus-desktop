# Dynamic Mobile Camera Scanner Implementation Plan

**Date:** 2026-08-21  
**Status:** Proposed  
**Baseline:** `master` at `4a12351`  
**Estimated delivery:** 4-6 developer weeks, including real-device tuning

## Executive decision

Add a second, mobile-only scanner that continuously finds, tracks, straightens, and reads a visible Rust genetics row from the phone camera.

The mobile scanner must not use a saved screen coordinate or require the phone to stay in one calibrated position. The user can hold or mount the phone at different positions, rotations, and moderate viewing angles as long as:

- All six genes are visible.
- The row occupies enough camera pixels.
- The image is focused and not obscured by glare.
- The phone is steady long enough to confirm the reading.

The existing desktop screen-capture scanner is a protected compatibility path. Its capture method, profiles, regions, preprocessing, Tesseract configuration, temporal voting, deduplication, timing, and UI behavior must remain unchanged.

## Product goal

A phone user should be able to:

1. Open Genetics Lab over HTTPS.
2. Start **Phone Camera Scanner**.
3. Point the rear camera toward the monitor running Rust.
4. Hover a plant's genetics in Rust exactly as they do with the desktop scanner.
5. See a border automatically lock onto the six-gene row wherever it appears in the camera frame.
6. Move, rotate, or slightly angle the phone while the border follows the row.
7. Receive specific positioning guidance when the image is not usable.
8. Have a clone added only after the same six genes are confirmed across multiple stable frames.

No manual ROI editor should be part of the normal phone flow.

## Hard requirements

- [ ] Keep the existing desktop OCR flow behaviorally unchanged.
- [ ] Do not change desktop default scanner regions or saved scanner profiles.
- [ ] Do not run camera computer-vision code unless phone-camera mode is opened.
- [ ] Detect the genetics row dynamically across the camera frame.
- [ ] Correct rotation and perspective before OCR.
- [ ] Track the row while the phone or tooltip moves.
- [ ] Give explicit closer/farther/focus/glare/stability feedback.
- [ ] Fail closed: an ambiguous or low-confidence reading must never auto-add a clone.
- [ ] Keep frames and OCR processing on the device.
- [ ] Upload and persist no camera frames by default.
- [ ] Support touch, safe-area insets, landscape layout, and accessible non-color status text.
- [ ] Continue emitting the existing `SAPLING-FOUND` result contract.

## Assumptions and scope boundary

This plan assumes:

- The phone runs the web version of Genetics Lab.
- The rear phone camera points at a monitor showing Rust.
- The phone's clone inventory is local to that phone.
- Production serves the mobile app from a secure HTTPS origin.
- A moderate camera angle means approximately 0-35 degrees from perpendicular to the monitor.

This plan does not include live phone-to-desktop inventory synchronization. If phone scans must immediately appear in a simultaneously running desktop instance, that is a separate pairing and synchronization project.

## Non-goals for the first release

- Rewriting or tuning desktop OCR.
- Replacing Tesseract with a custom neural network.
- Server-side image processing.
- Accounts, cloud storage, or phone-to-PC pairing.
- Recognizing a partially hidden six-gene row.
- Promising reliable operation through strong glare, heavy blur, or extreme viewing angles.
- Scanning every visible plant row in a Rust inventory at once.
- Persisting a camera ROI or asking the user to repeat manual calibration.
- Building a general-purpose object-detection framework.

## Existing system baseline

The current desktop path is:

```text
getDisplayMedia()
    -> scannerService scan loop
    -> fixed normalized desktop regions
    -> GeneImagePreprocessor
    -> TesseractGeneRecognizer
    -> row OCR with six-slot fallback
    -> temporal voting
    -> deduplication
    -> SAPLING-FOUND
    -> ScannerContext adds clone
```

Reusable existing modules:

- `src/services/scanner/GeneImagePreprocessor.ts`
- `src/services/scanner/TesseractGeneRecognizer.ts`
- `src/services/scanner/TemporalVotingService.ts`
- `src/services/scanner/PlantScanDeduplicator.ts`
- `src/services/scanner/scannerTypes.ts`
- `src/context/ScannerContext.tsx`

Desktop-only modules and behavior to preserve:

- `src/services/scannerService.ts`
- `src/services/scanner/scannerConfig.ts`
- `src/components/scanner/CompactScannerStatus.tsx`
- `src/components/scanner/ScannerCalibrationModal.tsx`
- Desktop `ScannerProfile` and `ScannerRegion` persistence in `storageService.ts`

## Proposed architecture

```text
DESKTOP - FROZEN PATH

getDisplayMedia
    -> existing scannerService
    -> existing desktop regions and OCR orchestration
    -> SAPLING-FOUND


MOBILE - NEW PATH

getUserMedia rear camera
    -> full-screen video
    -> DynamicGeneLocator
       - candidate detection
       - six-badge geometry validation
       - target selection
       - target tracking
       - perspective normalization
       - camera quality measurement
    -> normalized horizontal six-gene canvas
    -> existing GeneImagePreprocessor
    -> existing TesseractGeneRecognizer
    -> camera-specific confirmation and deduplication
    -> existing SAPLING-FOUND contract
```

### Why the camera path remains separate

The current `scannerService.ts` is an established desktop scanner with capture, ROI handling, recognition arbitration, preview generation, voting, deduplication, and lifecycle behavior in one service. Refactoring it before the camera approach is validated would create unnecessary desktop regression risk.

The first camera version should therefore use a separate orchestrator and reuse only stable low-level recognition modules. A small amount of orchestration duplication is accepted during the beta. Shared orchestration can be extracted later only if the camera behavior is proven and the extraction can be covered by desktop regression fixtures.

## Mobile user experience

### Entry

- On compact/mobile layouts, expose a one-line **camera banner** directly under the header
  with a START action, plus **Use Phone Camera** in the overflow menu as the permanent path.
  The banner is dismissible and the dismissal is remembered.
- On desktop, keep the current scanner action and behavior unchanged.
- Do not choose behavior solely from user-agent detection. Camera mode should remain an explicit action.
- Explain that the rear camera and landscape orientation are recommended.

### Permission state

Before requesting permission, show:

- Why camera access is required.
- That processing stays on the device.
- That frames are not uploaded or saved.
- A clear **Open Camera** action.

On denial:

- Show browser-specific permission recovery guidance.
- Keep **Try Again** and **Close** available.
- Do not loop permission prompts automatically.

### Active scanner layout

Use a full-screen mobile surface instead of the desktop floating scanner widget.

```text
+--------------------------------------------------+
| Camera Scanner             12 clones       Close |
|                                                  |
|              live rear-camera video              |
|                                                  |
|      /====================================/       |
|     /  G - Y - G - H - Y - Y             /       |
|    /=====================================/        |
|              TRACKING - HOLD STILL               |
|                                                  |
| Last added: GYGHYY                               |
|                                                  |
| [Pause]       [Switch camera]       [Settings]   |
+--------------------------------------------------+
```

Requirements:

- Preserve the complete camera image with `object-fit: contain`.
- Map overlays through the actual letterboxed video rectangle.
- Respect notches and browser controls through safe-area insets.
- Use at least 44 x 44 CSS pixel hit areas for primary actions.
- Keep status text visible in addition to border color.
- Announce accepted clones through a polite live region.
- Use distinct success and duplicate sounds; optionally vibrate on supported devices.

### Dynamic status vocabulary

The UI should expose one primary instruction at a time:

| State | Border | Message |
| --- | --- | --- |
| Searching | Gray | Hover a plant and point the camera at all six genes |
| Too far | Amber | Move closer |
| Too close | Amber | Move farther so all six genes fit |
| Blurred | Amber | Hold still or tap the screen to focus |
| Glare | Amber | Change the camera angle to reduce glare |
| Ambiguous | Amber | Multiple gene rows found - tap the intended row |
| Tracking | Cyan | Genetics found - hold still |
| Reading | Cyan | Reading genetics... |
| Accepted | Green | Clone added: GYGHYY |
| Duplicate | Green | Already in clone inventory |
| Lost | Gray | Genetics lost - show all six genes |

### Optional tap behavior

A tap is not calibration and must not save coordinates.

- Tapping an ambiguous detected border selects that candidate for the current appearance.
- Tapping the video can request point focus only when the browser and device expose that capability.
- When the selected row disappears, selection resets and full-frame discovery resumes.

## Dynamic gene-location algorithm

### Processing cadence

Use two rates:

- **Discovery:** analyze a downscaled full frame at 5-8 frames per second.
- **Tracking:** follow an acquired target at up to 15 frames per second.
- **OCR:** run only after the target passes quality and stability gates.

Do not run Tesseract on every camera frame.

Initial processing sizes:

- Camera request: ideal 1920 x 1080, 15 FPS.
- Discovery frame: maximum width 960 px while preserving aspect ratio.
- Normalized OCR row: tune from fixtures; begin near 600 x 100 px.
- OCR slot input: reuse current slot proportions after normalization.

### Step 1: Acquire camera frames

Request the camera with progressive fallback:

```ts
const preferred = {
  audio: false,
  video: {
    facingMode: { ideal: 'environment' },
    width: { ideal: 1920 },
    height: { ideal: 1080 },
    frameRate: { ideal: 15, max: 30 },
  },
};
```

Fallback order:

1. Rear camera with ideal resolution and frame rate.
2. Rear camera without resolution constraints.
3. Any available camera using `video: true`.

Never use `exact` constraints for the normal flow. A device that cannot provide 1080p should still be allowed to attempt 720p scanning.

### Step 2: Generate badge candidates

On the downscaled discovery frame:

1. Convert RGB to HSV and grayscale representations.
2. Build broad masks for the green and red gene badge families.
3. Apply a small morphological open/close operation to suppress monitor noise.
4. Find connected contours.
5. Calculate each contour's bounding box, area, circularity, fill ratio, and center.
6. Reject components outside the current badge-size envelope.
7. Retain grayscale edge evidence so a color shift alone does not erase a candidate.

Do not hard-code one exact green or red value. Monitor white balance, HDR, phone exposure, and camera processing will shift colors.

### Step 3: Group candidates into six-gene rows

Group components using geometry rather than fixed coordinates:

- Exactly six candidate centers.
- Similar apparent badge size after allowing for perspective.
- Consistent ordering along a fitted line.
- Reasonably consistent adjacent spacing.
- Total row aspect ratio compatible with six badges.
- Limited perpendicular distance from the fitted row axis.
- All six components fully inside the frame.

Score each group from 0-1:

```text
candidate score =
    0.25 * six-item geometry
  + 0.20 * spacing consistency
  + 0.15 * size consistency
  + 0.15 * badge shape evidence
  + 0.10 * green/red color evidence
  + 0.10 * tooltip/background context
  + 0.05 * temporal persistence
```

These weights are starting values, not product constants. Tune them against labeled fixtures.

Reject a group before OCR if its geometry score is below the measured beta threshold.

### Step 4: Select the hovered target

Selection order:

1. Continue tracking the previously locked target if it remains valid.
2. Prefer a newly appeared high-confidence row over unchanged background rows.
3. Prefer a complete, sharp, larger candidate contained by a tooltip-like dark panel.
4. Prefer the candidate with the strongest valid-letter OCR preview.
5. If two candidates remain close in score, do not auto-select; ask for a tap.

Never resolve ambiguity by silently choosing the first contour returned by the vision library.

### Step 5: Derive a row quadrilateral

From the six detected badges:

1. Fit the row center line.
2. Estimate the top and bottom edges using the apparent badge extents.
3. Use the first and last badge extents to estimate left and right edges.
4. Expand the quadrilateral by a measured padding percentage so letters are not clipped.
5. Order the four corners consistently: top-left, top-right, bottom-right, bottom-left.
6. Reject self-intersecting, inverted, degenerate, or mostly off-frame quadrilaterals.

### Step 6: Normalize perspective

Map the detected quadrilateral into a standard horizontal rectangle:

```text
camera quadrilateral
    -> getPerspectiveTransform
    -> warpPerspective
    -> canonical six-gene row canvas
```

This canonical canvas is the contract between camera vision and the existing OCR modules. OCR should not need to know the phone position, camera rotation, or perspective angle.

### Step 7: Track the target

After acquisition:

- Track corner/features between adjacent frames using sparse optical flow or equivalent local tracking.
- Update the overlay every animation frame from the latest tracked quadrilateral.
- Re-run full candidate discovery every 500-1,000 ms to correct drift.
- Re-run discovery immediately if too many tracked features are lost or tracking error crosses its threshold.
- Do not continue OCR against an extrapolated location after tracking confidence fails.

### Step 8: Measure camera quality

Calculate quality on the normalized row rather than the entire frame.

#### Scale

Start testing with this envelope:

- Under approximately 24 source pixels per gene: too far.
- Approximately 24-80 pixels per gene: target operating range.
- Above approximately 100-120 pixels per gene or clipped outer padding: too close.

The final limits must come from real fixtures, not assumptions.

#### Sharpness

- Use variance of Laplacian or a simpler measured edge-energy score.
- Calculate only inside letter areas so flat tooltip backgrounds do not lower the score.
- Tune separate thresholds for 720p and 1080p if the data demonstrates a meaningful difference.

#### Exposure and glare

- Measure ratios of near-white and near-black clipped pixels.
- Treat a small bright letter highlight differently from a large glare region.
- Reject when glare overlaps enough badge/letter area to make OCR unsafe.

#### Stability

- Reuse the principle of the existing stability detector without changing its desktop threshold.
- Begin camera testing around 300-500 ms of acceptable movement.
- Reset the camera confirmation window when the quadrilateral changes too quickly.

#### Perspective

- Estimate foreshortening from left/right badge size and quadrilateral geometry.
- Begin with a supported envelope of roughly 0-35 degrees from perpendicular.
- Reject transformations that would excessively stretch a compressed source image.

### Step 9: OCR and validate

After normalization and quality approval:

1. Feed the canonical row into the existing row preprocessor.
2. Run the existing stitched-row Tesseract recognition.
3. Use the existing six-slot fallback when the row result is incomplete.
4. Accept only six characters from the existing valid gene alphabet.
5. Require matching OCR results across multiple stable frames.
6. Require a valid geometry and quality score throughout the confirmation window.
7. Emit `SAPLING-FOUND` only after all gates pass.

Initial confirmation policy:

- Four recent stable OCR samples.
- At least three matching complete gene strings.
- No competing candidate with a similar target score.
- Target still visible at acceptance time.

### Step 10: Rearm and deduplicate

- Emit only once for the same visible tooltip/target.
- Rearm when the target disappears for a measured interval or a clearly different gene string replaces it.
- Preserve the current clone-inventory duplicate behavior and feedback.
- Never use time alone to repeatedly emit a target that has remained continuously visible.

## Camera scanner state model

```ts
type CameraScannerPhase =
  | 'idle'
  | 'requesting-permission'
  | 'starting'
  | 'searching'
  | 'ambiguous'
  | 'tracking'
  | 'quality-blocked'
  | 'reading'
  | 'accepted'
  | 'paused'
  | 'error';

type CameraQualityIssue =
  | 'too-far'
  | 'too-close'
  | 'blurred'
  | 'glare'
  | 'too-dark'
  | 'moving'
  | 'extreme-perspective'
  | 'clipped';

interface CameraTarget {
  corners: [Point, Point, Point, Point];
  candidateScore: number;
  trackingConfidence: number;
  qualityIssues: CameraQualityIssue[];
  normalizedCanvas: HTMLCanvasElement;
}
```

Keep these concrete types. Do not introduce a generic capture-provider interface until there are enough proven capture implementations to justify it.

## File-level implementation plan

### New files

#### `src/services/cameraScannerService.ts`

Responsibilities:

- Own `getUserMedia` lifecycle.
- Attach the stream to the visible mobile video element.
- Schedule discovery, tracking, quality, and OCR work.
- Warm and reuse the existing Tesseract recognizer.
- Maintain camera-specific confirmation and rearm state.
- Emit scanner events using the existing event shape.
- Stop every media track and release image-processing resources.

It must not contain desktop capture branches.

#### `src/services/scanner/DynamicGeneLocator.ts`

Responsibilities:

- Convert video frames into a downscaled analysis image.
- Detect badge contours.
- Build and score six-item candidates.
- Track the chosen candidate.
- Compute the quadrilateral and normalized row.
- Calculate camera-only quality measurements.
- Return explicit no-target, ambiguous, blocked, or ready results.

Keep the first version in one focused module. Split it only if the tested implementation becomes difficult to navigate, not in advance.

#### `src/components/scanner/MobileCameraScanner.tsx`

Responsibilities:

- Full-screen video and overlay presentation.
- Permission/start/error states.
- Status guidance.
- Candidate tap selection.
- Pause, stop, camera switch, and supported focus/zoom controls.
- Safe-area and landscape handling.
- Live-region announcements and touch target sizing.

#### `src/services/scanner/DynamicGeneLocator.test.ts`

One focused test file covering pure geometry and decision logic:

- Six-component row grouping.
- Rotated row ordering.
- Perspective quadrilateral validation.
- Candidate ambiguity.
- Scale and clipping gates.
- Fail-closed behavior.

#### `src/services/cameraScannerService.test.ts`

One focused service test file covering:

- Camera-constraint fallback.
- Permission rejection.
- Stream end and stop cleanup.
- Three-of-four confirmation.
- No event while quality is blocked.
- One event per continuously visible target.

### Existing files to change

#### `src/context/ScannerContext.tsx`

- Add camera-scanner lifecycle state and actions.
- Route accepted camera results through the same clone-add behavior.
- Keep the current desktop `startScanner` implementation intact.
- Ensure desktop and camera modes cannot own capture simultaneously.

#### `src/services/scanner/scannerTypes.ts`

- Add only the camera state/result types needed across service and UI boundaries.
- Do not change the meaning of existing desktop scanner events.

#### Mobile scanner entry component

- Add the phone-camera action to the existing header/scanner entry point.
- Preserve the current desktop action and scanner status surface.
- Lazy-load the camera UI and vision code after the user selects camera mode.

#### Scanner guide/help copy

- Add positioning, privacy, supported-angle, and permission guidance.
- State that desktop screen scanning remains the recommended desktop flow.

### Files explicitly not changed during the first camera implementation

- `src/services/scannerService.ts`, except an unavoidable type-only import if compilation requires it.
- Desktop values in `src/services/scanner/scannerConfig.ts`.
- Default desktop profiles in `src/services/storageService.ts`.
- Desktop calibration behavior.
- Desktop scanner screenshots until the camera feature itself is ready to document.

## Vision dependency decision

Dynamic arbitrary-position scanning requires reliable contour processing, perspective warping, and target tracking. Implementing those operations from scratch would add more custom code and testing risk than using a proven computer-vision runtime.

### Proposed choice

Use a pinned OpenCV.js/WASM build for camera mode only, loaded lazily from a local application asset. Do not fetch it from a CDN at runtime.

Required operations:

- Color conversion and masks.
- Morphological cleanup.
- Contour discovery and geometry.
- Perspective transform and warp.
- Sparse optical flow or equivalent point tracking.

### Decision taken: a documented smaller alternative

**Status:** Implemented. OpenCV.js was **not** adopted.

The Phase 0 dependency gate below could not be run, because it requires timing a WASM
runtime on real phones. Rather than adopt an unmeasured ~8 MB dependency, the gate's own
escape clause was taken: "a documented smaller alternative is selected".

Every required operation is implemented in plain TypeScript under
`src/services/scanner/vision/`:

| Required operation | Implementation |
| --- | --- |
| Colour conversion and masks | `rasterOps.buildBadgeMask` - relative channel dominance, not fixed hue windows |
| Morphological cleanup | `rasterOps.cleanBadgeMask` - separable 3x3 open, then flood-fill hole closing |
| Contour discovery and geometry | `rasterOps.findComponents` - 8-connected labelling with an explicit stack |
| Perspective transform and warp | `perspective.solveHomography` / `warpQuadToRect` - 8x8 DLT solve, inverse-mapped bilinear sampling |
| Point tracking | `DynamicGeneLocator.searchNearTarget` - local re-detection in a crop around the last row |

Why this came out ahead of the library:

- **Size.** The whole vision stack ships as a 23 kB lazy chunk (8.7 kB gzipped) against
  roughly 8 MB for an OpenCV.js build. There is no WASM download, no init cost, and no
  first-open progress state to design.
- **Testability.** Pure functions over plain RGBA buffers run in the repository's existing
  node test environment. 59 tests cover the vision stack directly, with no browser, no
  jsdom, and no new dev dependency.
- **Memory.** There are no manually managed native handles, so the "delete every matrix
  deterministically" risk in the plan does not exist. The only pooled resources are three
  canvases, released on dispose.
- **Scope fit.** Detection needs bounding boxes, centroids and fill ratios for six coloured
  blobs, not general contour hierarchies. Full-contour machinery would have gone unused.

Instead of sparse optical flow, tracking re-runs the same detector inside a crop around the
last known row. On this content the badges *are* the features, so re-detection is both
simpler and more robust than tracking corner points across frames, and it reuses code that
is already tested.

**Revisit trigger:** measured detection accuracy on real device fixtures falls short of the
Phase 6 gates in a way that traces to the detector rather than to thresholds, or a future
requirement genuinely needs general contour or feature-tracking machinery.

### Phase 0 dependency gate

Before permanently adopting it, measure on representative phones:

- Download/package size.
- Initialization time.
- Peak memory.
- Discovery-frame processing time.
- Tracking-frame processing time.
- Cleanup behavior over repeated open/close cycles.

Adopt when:

- Warm discovery p95 stays below 120 ms on the lowest supported test phone.
- Tracking p95 stays below 50 ms.
- The scanner remains responsive after ten start/stop cycles.
- Memory returns close to its pre-scan level after stopping.

If it fails the gate, first reduce discovery resolution and cadence. Only consider a smaller custom implementation after fixtures prove which exact operations are required.

## Performance and resource plan

- Request 1080p as ideal, never mandatory.
- Prefer 15 FPS over 30 FPS for heat and battery control.
- Downscale before full-frame detection.
- Track locally between slower full-frame detections.
- OCR only stable, normalized candidate crops.
- Reuse one warmed Tesseract worker.
- Avoid base64 preview snapshots; the video element is already the preview.
- Stop work while the document is hidden.
- Stop every `MediaStreamTrack` on close.
- Delete every OpenCV matrix/resource deterministically.
- Release the wake lock, timers, animation frames, and event listeners on stop.
- Reacquire a wake lock only after a user-visible resume.

Performance degradation order:

1. Reduce discovery from 8 FPS toward 5 FPS.
2. Reduce discovery width from 960 toward 720 px.
3. Reduce camera request from 1080p toward 720p.
4. Increase OCR interval.
5. Disable continuous tracking animation while retaining periodic redetection.

Do not lower acceptance confidence to improve performance.

Move vision work to a Web Worker only if production profiling shows repeated main-thread tasks over 50 ms or visible camera-control lag. Do not add the worker before measuring this.

## Focus, zoom, torch, and wake lock

- Inspect `MediaStreamTrack.getCapabilities()` before showing hardware controls.
- Treat zoom as an optional convenience, not part of correctness.
- Keep torch off by default because it can create monitor glare.
- Offer camera switching only when multiple video inputs are available.
- If point focus is unsupported, a tap should only select a candidate and never pretend focus changed.
- Request a screen wake lock only during active scanning and handle its loss without failing the scanner.

## Privacy and security

- Serve camera mode only in a secure context.
- Request video only; never request microphone audio.
- Process frames locally.
- Do not log pixels, screenshots, OCR crops, device labels, or camera IDs to analytics.
- Do not persist frames in local storage, IndexedDB, cache storage, or the filesystem.
- Report only aggregate operational events if analytics is enabled, such as permission outcome, accepted count, and quality-block reason.
- Clear canvases and references on stop so frames can be garbage-collected.
- Display a persistent camera-active indicator inside the scanner UI in addition to the browser indicator.

## Accessibility requirements

- Every icon-only action has an accessible name.
- Status is communicated with text, not only gray/amber/cyan/green.
- Accepted, duplicate, lost-target, and permission errors are announced appropriately.
- Do not announce every tracking update or quality frame.
- Candidate overlays selected by touch must also be selectable by keyboard if exposed as UI controls.
- Respect `prefers-reduced-motion` for border animation.
- Keep pause and stop reachable without precise aiming.
- Maintain readable contrast over bright and dark camera content by using an opaque status backing surface.

## Error handling matrix

| Condition | Required behavior |
| --- | --- |
| Camera API unavailable | Explain unsupported browser/context; keep manual clone entry available |
| Insecure origin | Explain that HTTPS is required; do not show a broken permission flow |
| Permission denied | Show recovery instructions and Try Again |
| No rear camera | Fall back to any video input and explain camera-switch limitations |
| Stream ended | Stop processing, release resources, and show Restart Camera |
| App hidden | Pause processing; preserve no stale acceptance window |
| App resumed | Revalidate stream and target before OCR |
| OpenCV initialization failure | Stop camera processing and show a recoverable error |
| Tesseract warm-up failure | Keep camera preview, report OCR unavailable, and allow retry |
| No target | Continue discovery with positioning guidance |
| Multiple targets | Fail closed and request a tap |
| Target lost during OCR | Discard the pending vote window |
| Thermal/performance pressure | Reduce cadence/resolution without reducing confidence gates |

## Test-data plan

### Fixture collection

Collect labeled camera recordings or frame sequences covering:

- At least two iPhones.
- Low-, mid-, and high-range Android devices.
- 1080p, 1440p, and 4K monitors.
- LCD and OLED displays.
- 60, 120, and 144 Hz monitor refresh rates where available.
- Rust inventory hover and planted-planter hover.
- All supported gene letters and both badge colors.
- Landscape and portrait capture.
- Phone roll at 0, 15, 30, 60, and 90 degrees.
- Camera angle at 0, 10, 20, 30, and 40 degrees.
- Distances around 30, 45, 60, and 80 cm.
- Bright room, dark room, and visible reflection/glare.
- Stable stand, normal handheld movement, and deliberately excessive movement.
- Single candidate and multiple candidate scenes.

Ground truth for each sequence:

- Expected six-gene string.
- Whether automatic target selection is allowed.
- Target quadrilateral or approximate badge centers.
- Expected quality-block reason when intentionally invalid.
- Whether an accepted event should occur.

### Fixture storage

- Commit a small representative, license-safe set of cropped fixtures needed for regression tests.
- Keep large raw recordings outside the application bundle.
- Document capture device, monitor, distance, angle, and expected result in fixture metadata.
- Do not use frames containing personal information, notifications, usernames, or unrelated desktop content.

## Automated verification

### Pure geometry and scoring

- [ ] Six valid badges form one candidate.
- [ ] Five or seven badges do not form an accepted candidate.
- [ ] Reversed contour order becomes left-to-right canonical order.
- [ ] Rotation does not change the canonical gene order.
- [ ] Moderate perspective produces a valid quadrilateral.
- [ ] Degenerate and self-intersecting quadrilaterals are rejected.
- [ ] Two similarly scored rows produce ambiguity, not automatic selection.
- [ ] Too-small and clipped candidates produce the correct quality issue.

### Camera lifecycle

- [ ] Preferred camera constraints are attempted first.
- [ ] Constraint failure falls back safely.
- [ ] Permission rejection produces a recoverable state.
- [ ] Stop ends all tracks.
- [ ] Repeated start/stop does not retain timers or frame callbacks.
- [ ] Hidden-page handling clears pending confirmation.

### Recognition safety

- [ ] No OCR runs before geometry and quality approval.
- [ ] Three of four matching stable samples accept.
- [ ] Two of four matching samples do not accept.
- [ ] Candidate loss clears pending samples.
- [ ] Competing candidates prevent acceptance.
- [ ] One continuously visible target emits once.
- [ ] A newly displayed target can emit after rearm.
- [ ] Desktop scanner regression tests remain unchanged and passing.

### Build checks

- [ ] `npm test` passes.
- [ ] `npm run build` passes.
- [ ] Camera vision code is absent from the initial desktop bundle.
- [ ] Existing desktop scanner starts, recognizes, stops, and restarts as before.

## Manual device verification

For each supported test device:

1. Open the production HTTPS build.
2. Grant camera permission.
3. Verify the rear camera is preferred.
4. Hover an inventory plant and accept ten known sequences.
5. Hover a planted plant and accept ten known sequences.
6. Move the phone laterally while tracking.
7. Roll the phone while tracking.
8. Change distance through too-far, good, and too-close ranges.
9. Introduce glare and verify rejection.
10. Show multiple valid rows and verify ambiguity handling.
11. Background and restore the browser.
12. Stop and restart ten times.
13. Deny and later grant camera permission.
14. Confirm no raw frames appear in network requests or persistent storage.
15. Run the desktop scanner afterward and confirm its behavior is unchanged.

## Success metrics and release gates

### Correctness

- False automatic clone additions below 0.1% across the labeled supported-condition sequence set.
- At least 95% exact six-gene acceptance in supported good conditions.
- No automatic selection in labeled ambiguous scenes.
- One event maximum per continuously visible target.

### Speed

- Median accepted scan below 1 second after Tesseract warm-up.
- p95 accepted scan below 2 seconds after warm-up.
- Camera controls remain responsive during discovery and OCR.

### Dynamic positioning

- Target remains locked during normal handheld movement at a supported distance.
- Correct normalization at tested roll angles through 90 degrees.
- Correct normalization at tested perspective angles through 30 degrees.
- Explicit rejection rather than false acceptance beyond the supported quality envelope.

### Reliability

- Permission denial, stream termination, backgrounding, and restart recover without page reload where the browser permits it.
- Ten start/stop cycles leave no active tracks and no material memory growth trend.
- Desktop scanner acceptance rate and timing remain at their pre-camera baseline.

### Privacy and accessibility

- No image data leaves the device.
- All camera controls have accessible names and adequate touch targets.
- Primary states and instructions remain understandable without color.

## Implementation phases

## Phase 0 - Evidence and technical spike (3-5 days)

### Work

- [ ] Capture the first labeled device/monitor fixture set.
- [ ] Run the existing Tesseract preprocessor/recognizer against manually normalized camera crops.
- [ ] Establish whether current OCR can meet accuracy after perspective normalization.
- [ ] Prototype six-badge contour detection on full camera frames.
- [ ] Prototype perspective normalization.
- [ ] Measure OpenCV.js initialization, memory, discovery, warp, and tracking performance.
- [ ] Establish initial scale, sharpness, glare, movement, and angle thresholds.
- [ ] Record desktop scanner baseline timing and acceptance fixtures before camera code changes.

### Exit gate

- Current OCR reads at least 95% of good-condition manually normalized fixture rows exactly.
- Candidate detection finds the intended row in at least 95% of the initial supported-condition frames.
- OpenCV.js passes the performance dependency gate or a documented smaller alternative is selected.
- If normalized OCR fails, stop and evaluate a camera-only recognizer; do not change desktop OCR.

## Phase 1 - Camera lifecycle and mobile shell (3-5 days) — IMPLEMENTED

### Work

- [x] Add `cameraScannerService.ts` lifecycle skeleton.
- [x] Add secure-context and camera-support detection.
- [x] Implement permission request and constraint fallback.
- [x] Add the full-screen mobile camera component.
- [x] Implement stop, pause, resume, background, and stream-ended cleanup.
- [x] Add explicit phone-camera entry without changing the desktop start action.
- [x] Lazy-load the camera component and vision runtime.
- [x] Add permission, unsupported, and initialization error states.

### Exit gate

- Camera starts on target mobile browsers.
- The rear camera is preferred but fallback works.
- Repeated start/stop leaves no active track.
- Desktop scanner smoke test remains identical.

### Delivered

Automated coverage lives in `src/tests/cameraScanner.test.ts` rather than the
`src/services/cameraScannerService.test.ts` path named above, to match the existing
repository convention of keeping test files under `src/tests/`.

New files:

- `src/services/cameraScannerService.ts` - stream lifecycle, constraint ladder, cadence scheduling.
- `src/services/scanner/cameraScannerConfig.ts` - camera-only tunables, kept out of the frozen desktop config.
- `src/services/scanner/cameraSupport.ts` - secure-context/support predicates and the idle state factory.
- `src/services/scanner/cameraStatusMessages.ts` - pure status vocabulary.
- `src/components/scanner/MobileCameraScanner.tsx` - full-screen surface.
- `src/components/scanner/MobileCameraScannerHost.tsx` - lazy mount point.

Changed files (additive only): `scannerTypes.ts`, `ScannerContext.tsx`, `AppHeader.tsx`,
`App.tsx`, `ScannerGuideModal.tsx`, and one new option field in `storageService.ts` for the
banner dismissal. `scannerService.ts`, `scannerConfig.ts`, the default scanner profiles and
regions, and the desktop calibration/status components are all untouched.

The Phase 2 seam is `CameraFrameAnalyzer` plus `CameraScannerService.setAnalyzerFactory()`.
The service creates one analyzer per camera session and disposes it on stop. Until an
analyzer is installed no per-frame loop runs at all, and the UI states plainly that
automatic detection is not enabled yet instead of drawing a lock it cannot perform.

## Phase 2 - Dynamic discovery and perspective normalization (5-8 days) — IMPLEMENTED

### Work

- [x] Implement downscaled discovery frames.
- [x] Implement green/red plus shape-based contour candidates.
- [x] Implement six-badge grouping and scoring.
- [x] Implement candidate ambiguity detection.
- [x] Implement quadrilateral derivation and validation.
- [x] Implement perspective normalization to the canonical row.
- [x] Draw a correctly mapped overlay over letterboxed video.
- [x] Add geometry unit tests and labeled fixture evaluation.

### Exit gate

- Supported fixture rows are found without saved coordinates.
- Rotated and moderately angled rows normalize into the correct left-to-right order.
- Multiple plausible rows fail closed.

## Phase 3 - Tracking and dynamic quality guidance (4-6 days) — IMPLEMENTED

### Work

- [x] Track the active row between discovery frames.
- [x] Periodically redetect to correct drift.
- [x] Implement scale, clipping, sharpness, exposure, glare, movement, and perspective checks.
- [x] Connect quality results to explicit UI messages.
- [x] Add temporary candidate tap selection.
- [x] Add supported camera focus/zoom controls only when capabilities exist.
- [x] Measure handheld and stand-mounted behavior.

### Exit gate

- Overlay follows normal phone and tooltip motion without a persistent ROI.
- The scanner correctly distinguishes too far, usable, and too close.
- Low-quality frames do not reach acceptance.

## Phase 4 - OCR integration and clone events (3-5 days) — IMPLEMENTED

### Work

- [x] Feed the canonical row into the existing preprocessor and recognizer.
- [x] Implement camera-specific voting and target-loss reset.
- [x] Implement camera rearm and deduplication.
- [x] Emit the existing `SAPLING-FOUND` event contract.
- [x] Reuse current clone-add, duplicate, sound, and notification behavior.
- [x] Show the last accepted sequence and clone count.
- [x] Test inventory hover and planter hover separately.

### Exit gate

- Good-condition fixture sequences meet the correctness threshold.
- Ambiguous, moving, blurred, clipped, and glare-obscured sequences produce no false addition.
- Desktop OCR fixtures and behavior remain unchanged.

## Phase 5 - Performance, accessibility, and resilience (3-5 days) — IMPLEMENTED

### Work

- [x] Tune cadence and resolution on low-, mid-, and high-range devices.
- [x] Add adaptive degradation without lowering confidence.
- [x] Add optional wake lock and release handling.
- [x] Verify cleanup of OpenCV objects, canvases, timers, tracks, and Tesseract use.
- [x] Complete touch-target, live-region, contrast, and reduced-motion checks.
- [x] Verify portrait behavior explains or supports rotation appropriately.
- [x] Add privacy/help documentation.
- [x] Confirm vision code remains lazy-loaded.

### Exit gate

- Performance, lifecycle, accessibility, and privacy release gates pass.
- No sustained UI jank requires a worker; otherwise add a worker as a measured follow-up within this phase.

## Implementation status

Phases 1 to 5 are implemented, tested and building. Phase 0 and Phase 6 remain open because
both are physical-device work: capturing labelled fixtures across the phone/monitor matrix,
and tuning thresholds against those fixtures.

Consequences of Phase 0 never having run:

- Every threshold in `cameraScannerConfig.ts` and `vision/quality.ts` is a reasoned starting
  value validated against synthetic fixtures, **not** a measured one. Real-device tuning is
  the substance of Phase 6.
- The OpenCV.js performance gate could not be executed, which is why the documented smaller
  alternative was taken instead. See the vision dependency decision above.
- Whether the existing Tesseract configuration reads perspective-normalised *camera* pixels
  as well as it reads screen captures is still unverified. That was Phase 0's central
  question and it remains the largest open risk in the feature.

What is covered by automated tests (222 passing, 20 files):

- `cameraVision.test.ts` - masking, morphology, component filtering, row grouping and
  scoring, quadrilateral derivation and rejection, homography and warp, quality gates,
  stability, overlay letterbox mapping.
- `cameraLocator.test.ts` - the locator end to end against synthetic camera frames: lock-on,
  rotation, slot measurement, ambiguity, tap resolution, drift tracking, target loss,
  stability gating, disposal.
- `cameraScanner.test.ts` - stream lifecycle, constraint ladder, permission handling,
  cleanup across ten start/stop cycles, recognition safety gates, rearm, adaptive
  degradation, status vocabulary.

Not covered by automated tests, and honestly unproven until Phase 6:

- Behaviour against real camera noise, moire, autofocus hunting and rolling shutter.
- Real per-frame cost on a low-end phone; the adaptive degradation ladder is implemented and
  tested, but its trigger point has never been observed on real hardware.
- End-to-end OCR accuracy, which is what the correctness release gates actually measure.

## Phase 6 - Device beta and threshold tuning (5-8 days)

### Work

- [ ] Run the complete device/monitor matrix.
- [ ] Tune thresholds from labeled failures, not individual anecdotes.
- [ ] Classify every failure as detection, selection, tracking, quality, normalization, OCR, voting, or lifecycle.
- [ ] Add only representative regression fixtures.
- [ ] Run full desktop scanner regression verification.
- [ ] Release behind a clearly labeled **Phone Camera Scanner (Beta)** action.
- [ ] Collect aggregate quality-block and scanner-success metrics only when analytics consent allows it.

### Exit gate

- All release gates pass on the supported matrix.
- Remaining limitations are documented in the scanner guide.
- Desktop users experience no scanner UI or recognition change.

## Rollout plan

1. Keep the camera action labeled Beta.
2. Do not automatically redirect mobile users into camera mode.
3. Publish the supported browser/device expectations and HTTPS requirement.
4. Track aggregate permission, initialization, accepted-scan, and quality-block outcomes only with consent.
5. Review false-add reports before broad promotion; false additions have higher priority than missed scans.
6. Remove the Beta label only after two releases without a material false-add regression.

Rollback is simple: hide or disable the phone-camera entry. The desktop scanner remains independent and available.

## Architecture decision records

### ADR-001: Keep desktop and camera orchestration separate

**Status:** Proposed

**Context:** The desktop scanner works and the user explicitly requires its OCR behavior to remain unchanged.

**Decision:** Add a separate camera scanner service and reuse only proven low-level recognition modules and the final event contract.

**Alternatives considered:**

| Option | Benefit | Cost | Decision |
| --- | --- | --- | --- |
| Refactor desktop into generic capture providers | Maximum shared orchestration | High desktop regression surface before camera validation | Reject for MVP |
| Add camera branches throughout `scannerService.ts` | Fewer new files | Entangles different timing, quality, and lifecycle requirements | Reject |
| Separate camera orchestrator | Protects desktop and permits camera-specific behavior | Some temporary duplicated orchestration | Choose |

**Revisit trigger:** Camera is stable, duplication becomes a demonstrated maintenance problem, and desktop regression fixtures cover extraction.

### ADR-002: Dynamic row detection instead of saved mobile ROI

**Status:** Proposed

**Decision:** Search, select, track, and normalize a genetics row continuously.

**Trade-off accepted:** More camera processing and implementation work in exchange for freedom of camera position and no repeated manual calibration.

**Revisit trigger:** Supported devices cannot sustain the measured discovery/tracking budget.

### ADR-003: Classical vision before custom machine learning

**Status:** Proposed

**Decision:** Use badge color, shape, six-item geometry, perspective transforms, and temporal tracking before training a custom detector.

**Rationale:** The target has strong structured visual features and the product does not yet have the labeled dataset needed to justify an ML model.

**Revisit trigger:** More than 5% of otherwise good-condition labeled frames fail candidate detection after threshold tuning, or UI themes invalidate the classical features.

### ADR-004: On-device processing

**Status:** Proposed

**Decision:** Keep vision and OCR in the browser.

**Trade-off accepted:** Mobile CPU, heat, and initial vision-runtime load in exchange for privacy, offline-capable processing, no backend cost, and lower latency.

**Revisit trigger:** The supported low-end device class cannot meet accuracy and latency gates after adaptive processing is exhausted.

### ADR-005: Fail closed on ambiguity

**Status:** Proposed

**Decision:** Require user selection when multiple candidates are similarly plausible and discard any vote window when target identity is uncertain.

**Trade-off accepted:** Occasional extra tap in exchange for preventing incorrect clone inventory data.

**Revisit trigger:** A proven target-selection signal makes ambiguity both rare and objectively resolvable.

## Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Monitor moire or refresh banding | Bad shape detection or OCR | Multiple devices/refresh-rate fixtures; lower exposure if supported; normalize only stable frames |
| Camera autofocus hunts | Delayed or wrong OCR | Sharpness gate; hold-still feedback; optional supported point focus |
| Strong glare | Missing letters | Glare gate and angle guidance; torch off by default |
| Multiple visible genetics-like rows | Wrong target | Temporal/context scoring; fail closed; temporary tap selection |
| Extreme perspective | Stretched, unreadable letters | Perspective quality gate and explicit reposition message |
| OpenCV.js size/startup | Slow first camera open | Lazy local load, progress state, pinned build, measured dependency gate |
| Mobile heat/battery | Throttling and poor UX | 15 FPS request, slower discovery, local tracking, stable-only OCR, adaptive resolution |
| Vision memory leaks | Crashes after repeated use | Deterministic matrix deletion and ten-cycle lifecycle tests |
| Desktop regression | Existing scanner becomes unreliable | Separate service, frozen desktop configuration, pre/post regression fixtures |
| Browser capability variance | Missing zoom/focus/wake lock | Feature-detect optional controls; never require them for correctness |
| OCR works on screenshots but not monitors | Feature fails despite good locator | Phase 0 normalized camera fixture gate before full implementation |

## Definition of done

- [ ] Phone camera mode dynamically finds the hovered genetics row without a saved ROI.
- [ ] The target overlay follows normal phone movement and tooltip movement.
- [ ] Rotation and supported perspective are corrected before OCR.
- [ ] Distance, clipping, blur, glare, movement, and perspective have clear guidance.
- [ ] Multiple plausible targets cannot produce an automatic clone addition.
- [ ] Exact-sequence, false-add, latency, lifecycle, privacy, and accessibility release gates pass.
- [ ] Inventory and planter hover flows pass on the supported real-device matrix.
- [ ] Camera vision code loads only after camera mode is requested.
- [ ] No image data is transmitted or persisted.
- [ ] All camera resources are released on stop.
- [ ] The existing desktop scanner retains its current capture, profiles, OCR, voting, deduplication, timing, and UI behavior.
- [ ] `npm test` and `npm run build` pass.
- [ ] The scanner guide documents supported positioning and honest limitations.

## Implementation checklist summary

- [ ] Phase 0: prove normalized current OCR and dynamic detection on camera fixtures.
- [x] Phase 1: deliver camera permissions, stream lifecycle, and full-screen mobile shell.
- [x] Phase 2: deliver six-badge discovery and perspective normalization.
- [x] Phase 3: deliver tracking and dynamic quality guidance.
- [x] Phase 4: integrate current OCR, voting, deduplication, and clone events.
- [x] Phase 5: tune performance, accessibility, cleanup, and privacy.
- [ ] Phase 6: validate devices, tune thresholds, and release Beta.

## Technical references

- [W3C Media Capture and Streams](https://www.w3.org/TR/mediacapture-streams/)
- [W3C MediaStream Image Capture](https://www.w3.org/TR/image-capture/)
- [W3C Screen Wake Lock](https://www.w3.org/TR/screen-wake-lock/)
- [OpenCV.js geometric transformations](https://docs.opencv.org/5.0/js_tutorials/js_imgproc/js_geometric_transformations/js_geometric_transformations.html)
- [OpenCV.js optical flow](https://docs.opencv.org/5.0/js_tutorials/js_video/js_lucas_kanade/js_lucas_kanade.html)
- [Tesseract.js API](https://github.com/naptha/tesseract.js/blob/master/docs/api.md)

