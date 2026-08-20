import { useLayoutEffect, useRef } from 'react';

/**
 * FLIP reordering for the results grid.
 *
 * When the sort changes, cards jump to new grid slots. Rather than animating
 * layout (which would force reflow every frame), each card's previous screen
 * position is recorded, the browser is allowed to lay out the new order, and
 * the card is then transformed back to where it was and released. The visible
 * motion is pure `transform`, so it stays on the compositor.
 *
 * Cards are identified by `data-flip-key`; a card whose key is new fades and
 * lifts in instead of sliding, with a small stagger so a burst of streamed
 * results reads as arriving rather than blinking into place.
 */

/** Heavy, decelerating curve - motion with mass, not a linear slide. */
const EASE = 'cubic-bezier(0.32, 0.72, 0, 1)';
const MOVE_MS = 420;
const ENTER_MS = 360;
const STAGGER_MS = 22;
const MAX_STAGGER_STEPS = 8;

const prefersReducedMotion = (): boolean =>
  typeof window !== 'undefined' &&
  !!window.matchMedia &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches;

export interface FlipOptions {
  /**
   * When false, cards still fade in as they arrive but existing cards snap to
   * their new slots instead of sliding. Used while results are streaming, where
   * constant reordering would read as churn rather than polish.
   */
  animateMoves?: boolean;
}

export function useFlipGrid<T extends HTMLElement>(
  dependency: unknown,
  options: FlipOptions = {}
) {
  const { animateMoves = true } = options;
  const containerRef = useRef<T | null>(null);
  const previous = useRef<Map<string, DOMRect>>(new Map());
  const firstRun = useRef(true);

  useLayoutEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    const cards = Array.from(
      container.querySelectorAll<HTMLElement>('[data-flip-key]')
    );
    const next = new Map<string, DOMRect>();
    for (const card of cards) {
      next.set(card.dataset.flipKey as string, card.getBoundingClientRect());
    }

    // Skip animating the very first paint, honour reduced-motion, and skip while
    // the page is hidden - `requestAnimationFrame` does not run in a background
    // tab, so the "play" half of the FLIP would not fire on schedule.
    if (firstRun.current || prefersReducedMotion() || document.visibilityState === 'hidden') {
      firstRun.current = false;
      previous.current = next;
      return;
    }

    let enterIndex = 0;
    const cleanups: (() => void)[] = [];

    for (const card of cards) {
      const key = card.dataset.flipKey as string;
      const before = previous.current.get(key);
      const after = next.get(key)!;

      if (!before) {
        // New card: lift and fade in, staggered by arrival order.
        const delay = Math.min(enterIndex++, MAX_STAGGER_STEPS) * STAGGER_MS;
        card.style.transition = 'none';
        card.style.opacity = '0';
        card.style.transform = 'translate3d(0, 10px, 0) scale(0.985)';
        requestAnimationFrame(() => {
          card.style.willChange = 'transform, opacity';
          card.style.transition =
            `transform ${ENTER_MS}ms ${EASE} ${delay}ms, opacity ${ENTER_MS}ms ${EASE} ${delay}ms`;
          card.style.opacity = '1';
          card.style.transform = 'translate3d(0, 0, 0) scale(1)';
        });
        cleanups.push(() => {
          card.style.willChange = '';
          card.style.transition = '';
          card.style.transform = '';
          card.style.opacity = '';
        });
        continue;
      }

      if (!animateMoves) continue;

      const dx = before.left - after.left;
      const dy = before.top - after.top;
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;

      // Invert to the old position, then play forward to the new one.
      card.style.transition = 'none';
      card.style.transform = `translate3d(${dx}px, ${dy}px, 0)`;
      requestAnimationFrame(() => {
        card.style.willChange = 'transform';
        card.style.transition = `transform ${MOVE_MS}ms ${EASE}`;
        card.style.transform = 'translate3d(0, 0, 0)';
      });
      cleanups.push(() => {
        card.style.willChange = '';
        card.style.transition = '';
        card.style.transform = '';
      });
    }

    previous.current = next;

    // Release the compositor hints once the longest animation can have finished.
    const timer = window.setTimeout(() => {
      for (const cleanup of cleanups) cleanup();
    }, Math.max(MOVE_MS, ENTER_MS + MAX_STAGGER_STEPS * STAGGER_MS) + 60);

    return () => window.clearTimeout(timer);
    // `animateMoves` intentionally excluded: the pass should run on ordering
    // changes only, reading whatever the current mode is.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dependency]);

  return containerRef;
}
