# Base Builder and 3D Viewer: Candidate Brief

## Objective

Investigate and fix the issues below in RustPlus Desktop's Base Builder and 3D map viewer. Preserve existing functionality and add focused regression coverage for each fix.

The Base Builder and map viewer use a shared web runtime under `vendor/MapParser`. That directory is a separate Git repository. The desktop host and its tests are in the main repository.

## Issues to investigate

### 1. Co-op guests can stop receiving updates

The host stores a snapshot at version `V` and broadcasts a resynchronization marker at `V+1`. A guest that loads the snapshot after that marker can treat the next host update as a gap. While waiting for a requested snapshot, the guest buffers the host's response marker instead of processing it, leaving the guest waiting and buffering later changes indefinitely.

Start with `vendor/MapParser/modules/coopSync.js`, especially snapshot upload, batch handling, and snapshot loading. A small stand-in-world reproduction confirmed the stuck state.

**Expected outcome:** A guest joining or recovering from a missed batch receives the current base and continues to receive subsequent host edits. Test joining after a snapshot marker and recovering after an actual version gap.

### 2. Concurrent placements can overwrite a host's piece

The host and a guest can independently allocate the same next piece ID from a shared snapshot. If both place a piece before the guest receives the host's update, the guest's proposal can be interpreted as an edit to the host's piece and replace it.

Start with `vendor/MapParser/modules/coopSync.js`, `buildRules.js`, and `buildBase.js`. A stand-in-world reproduction showed a guest wall replacing a host floor.

**Expected outcome:** Concurrent new placements survive and converge across participants. Existing-piece edits and deletes must still target the intended piece. Add a regression test for an ID collision, not only an unused guest ID.

### 3. Recent edits may not reach the recovery draft

The desktop host tries to flush the recovery draft before disposing the builder WebView, but the production JavaScript bundle places the draft function inside a private wrapper. The host's call therefore cannot access it. Edits are saved on a short delay, and application shutdown also disposes the WebView without waiting for a recovery write. A page-hide or visibility save may succeed, so the loss depends on timing.

Start with `src/RustPlusDesktop/Views/MainWindow/Map/MainWindow.BaseBuilder.cs`, `vendor/MapParser/modules/baseWorkshop.js`, and `vendor/MapParser/build-client.mjs`. Review both workspace switching and application shutdown, including save failure behavior.

**Expected outcome:** Recent edits are committed to the recovery draft before the builder is disposed where possible, and failures are surfaced appropriately. Verify the behavior against the production bundle and the desktop integration, not only an unbundled JavaScript test.

### 4. A failed map parse can reuse stale output

Parser attempt directories are keyed by map filename and reused. The manual selection fallback checks whether a prior output file exists even if the latest parse failed. An earlier attempt with the same filename can consequently be promoted and cached as the selected map.

Start with `src/RustPlusDesktop/Services/Map3DLocalBuildService.cs`, including attempt directory creation, parser results, manual fallback, and cache promotion.

**Expected outcome:** A failed parse cannot be reported as a successful selection or promote stale output. Cover a retry and two different map paths with the same filename while preserving the intended manual-selection behavior for a successful parse.

## Verification and handoff

- Run the relevant MapParser checks, including `npm test` from `vendor/MapParser`.
- Run the relevant desktop tests and add focused tests for the affected desktop behavior.
- Exercise the builder and viewer in the desktop app where practical, including rapid exit, recovery, and co-op resynchronization.
- Explain the fixes, tests run, any test limitations, and any remaining risks. Identify changes in each repository separately.

The previous audit ran the MapParser JavaScript suite successfully. It also ran 47 targeted desktop tests using existing binaries; a fresh desktop test build was not completed. The co-op issues were reproduced in a test harness, while the recovery race and parser issue were identified by source review. Backend authorization was outside that audit.
