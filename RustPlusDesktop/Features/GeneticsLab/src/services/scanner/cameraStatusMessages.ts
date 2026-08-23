import { CameraQualityIssue, CameraScannerState, CameraSlotReport } from './scannerTypes.ts';

export type CameraStatusTone = 'neutral' | 'warn' | 'active' | 'success' | 'error';

export interface CameraStatusDescriptor {
  tone: CameraStatusTone;
  /** Short state label. Always present so status never depends on border colour alone. */
  headline: string;
  /** The single primary instruction for the user right now. */
  instruction: string;
  /** Whether this status is worth announcing to a screen reader. */
  announce: boolean;
}

export type CameraResultKind = 'added' | 'duplicate' | null;

export interface CameraStatusOptions {
  /** What happened to the most recent accepted read. */
  lastResultKind?: CameraResultKind;
  /** True while a previously tracked row has just gone out of view. */
  recentlyLostTarget?: boolean;
}

/**
 * Order matters: the user gets one instruction at a time, so the issue that most blocks a
 * read is the one that gets shown.
 */
const QUALITY_ISSUE_PRIORITY: CameraQualityIssue[] = [
  'too-close',
  'clipped',
  'too-far',
  'glare',
  'too-dark',
  'blurred',
  'moving',
  'extreme-perspective'
];

const QUALITY_INSTRUCTIONS: Record<CameraQualityIssue, { headline: string; instruction: string }> = {
  'too-far': { headline: 'Too far', instruction: 'Move closer' },
  'too-close': { headline: 'Too close', instruction: 'Move farther so all six genes fit' },
  clipped: { headline: 'Genes cut off', instruction: 'Move farther so all six genes fit' },
  blurred: { headline: 'Blurred', instruction: 'Hold still or tap the screen to focus' },
  glare: { headline: 'Glare', instruction: 'Change the camera angle to reduce glare' },
  'too-dark': { headline: 'Too dark', instruction: 'Increase the monitor brightness' },
  moving: { headline: 'Moving', instruction: 'Hold the phone still' },
  'extreme-perspective': { headline: 'Too angled', instruction: 'Face the monitor more directly' }
};

function firstQualityIssue(issues: CameraQualityIssue[]): CameraQualityIssue | null {
  for (const issue of QUALITY_ISSUE_PRIORITY) {
    if (issues.includes(issue)) return issue;
  }
  return issues[0] ?? null;
}

/**
 * Maps camera scanner state onto the one thing the user should be told. Pure so the status
 * vocabulary can be tested without rendering the camera surface.
 */
export function describeCameraStatus(
  state: CameraScannerState,
  options: CameraStatusOptions = {}
): CameraStatusDescriptor {
  const { lastResultKind = null, recentlyLostTarget = false } = options;

  switch (state.phase) {
    case 'idle':
      return { tone: 'neutral', headline: 'Camera off', instruction: 'Open the camera to start scanning', announce: false };

    case 'requesting-permission':
      return { tone: 'neutral', headline: 'Waiting for permission', instruction: 'Allow camera access to continue', announce: true };

    case 'starting':
      return { tone: 'neutral', headline: 'Starting camera', instruction: 'Point the rear camera at your monitor', announce: false };

    case 'paused':
      return { tone: 'neutral', headline: 'Paused', instruction: 'Resume to keep scanning', announce: true };

    case 'error':
      return {
        tone: 'error',
        headline: 'Camera stopped',
        instruction: state.errorMessage || 'The camera could not be started',
        announce: true
      };

    case 'ambiguous':
      return {
        tone: 'warn',
        headline: 'Multiple gene rows found',
        instruction: 'Tap the intended row',
        announce: true
      };

    case 'quality-blocked': {
      const issue = firstQualityIssue(state.qualityIssues);
      const copy = issue ? QUALITY_INSTRUCTIONS[issue] : { headline: 'Cannot read yet', instruction: 'Reposition the phone' };
      return { tone: 'warn', headline: copy.headline, instruction: copy.instruction, announce: false };
    }

    case 'tracking':
      return { tone: 'active', headline: 'Genetics found', instruction: 'Hold still', announce: false };

    case 'reading': {
      // Many attempts with nothing confirmed is not "reading", it is failing. Saying so
      // gives the user something to act on instead of a spinner that never resolves.
      const stalled = state.diagnostics.readAttempts >= 25 && state.diagnostics.pendingSamples === 0;
      if (stalled) {
        return {
          tone: 'warn',
          headline: 'Cannot read the letters',
          instruction: 'Move a little closer, or tilt to cut screen glare',
          announce: false
        };
      }
      return { tone: 'active', headline: 'Reading genetics…', instruction: 'Keep the row in view', announce: false };
    }

    case 'accepted':
      if (lastResultKind === 'duplicate') {
        return {
          tone: 'success',
          headline: 'Already in clone inventory',
          instruction: state.lastAcceptedGenes ? `${state.lastAcceptedGenes} is already saved` : 'Already saved',
          announce: true
        };
      }
      return {
        tone: 'success',
        headline: state.lastAcceptedGenes ? `Clone added: ${state.lastAcceptedGenes}` : 'Clone added',
        instruction: 'Hover the next plant',
        announce: true
      };

    case 'searching':
    default:
      // Only reachable if the detector failed to install. Say so rather than showing a
      // "searching" message for a scanner that is not searching.
      if (!state.isDetectionAvailable) {
        return {
          tone: 'warn',
          headline: 'Detection unavailable',
          instruction: 'Close and reopen the camera to restart gene detection',
          announce: true
        };
      }
      if (recentlyLostTarget) {
        return { tone: 'neutral', headline: 'Genetics lost', instruction: 'Show all six genes', announce: false };
      }
      return {
        tone: 'neutral',
        headline: 'Searching',
        instruction: 'Hover a plant and point the camera at all six genes',
        announce: false
      };
  }
}

/** Two decimals with no leading zero, so six slots fit on a phone-width line. */
function shortScore(value: number): string {
  const clamped = Math.max(0, Math.min(9.99, value));
  return clamped.toFixed(2).replace(/^0/, '');
}

/**
 * One line per glyph cell, for the debug view.
 *
 * A row that fails to read reports a single null, which is true of a blank cell, an
 * over-inked cell and a genuinely ambiguous glyph alike. Those need completely different
 * fixes, and on a phone the status line is the only place the difference can show up.
 */
export function formatSlotDiagnostics(
  reports: CameraSlotReport[],
  ocrSlots: string[] | null
): string[] {
  const lines = reports.map((report, index) => {
    const letter = report.gene ?? '-';
    const ocr = ocrSlots?.[index] || '-';
    const verdict = report.reject ?? 'ok';
    return `${index + 1} tpl ${letter} ocr ${ocr} ink${shortScore(report.density)} m${shortScore(report.margin)} d${shortScore(report.distance)} ${verdict}`;
  });

  if (reports.length > 0 && !ocrSlots) lines.push('ocr not run yet');
  return lines;
}
