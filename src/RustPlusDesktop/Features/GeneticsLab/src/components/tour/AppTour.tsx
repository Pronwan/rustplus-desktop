import React, { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Box, Button, IconButton, LinearProgress, Paper, Portal, Typography } from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import SchoolIcon from '@mui/icons-material/School';
import { useApp } from '../../context/AppContext.tsx';
import { TOUR_STEPS } from './tourSteps.ts';

const OFFERED_KEY = 'GL_TOUR_OFFERED';
const CARD_WIDTH = 360;
const GAP = 14;
const PAD = 6;

const readOffered = () => {
  try {
    return localStorage.getItem(OFFERED_KEY) === 'true';
  } catch {
    return true; // No storage: never nag on every visit.
  }
};

const markOffered = () => {
  try {
    localStorage.setItem(OFFERED_KEY, 'true');
  } catch {
    // Not remembered; harmless.
  }
};

/** True while any MUI dialog, menu or popover is open, so the offer waits its turn. */
const isAnotherOverlayOpen = () => document.querySelector('.MuiModal-root:not(.MuiModal-hidden)') !== null;

type Rect = { top: number; left: number; width: number; height: number };

function findTarget(target: string | undefined): HTMLElement | null {
  if (!target) return null;
  const el = document.querySelector<HTMLElement>(`[data-tour="${target}"]`);
  if (!el) return null;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0 ? el : null;
}

/** Card position: beside the spotlight where it fits (below, above, right, left), else centred. */
function placeCard(rect: Rect | null, cardHeight: number): { top: number; left: number } {
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const width = Math.min(CARD_WIDTH, vw - 24);
  const centred = { top: Math.max(12, (vh - cardHeight) / 2), left: (vw - width) / 2 };
  if (!rect) return centred;

  const clampLeft = (left: number) => Math.min(Math.max(12, left), vw - width - 12);
  const clampTop = (top: number) => Math.min(Math.max(12, top), vh - cardHeight - 12);
  const below = rect.top + rect.height + GAP;
  if (below + cardHeight <= vh - 12) return { top: below, left: clampLeft(rect.left) };
  const above = rect.top - GAP - cardHeight;
  if (above >= 12) return { top: above, left: clampLeft(rect.left) };
  const right = rect.left + rect.width + GAP;
  if (right + width <= vw - 12) return { top: clampTop(rect.top), left: right };
  const left = rect.left - GAP - width;
  if (left >= 12) return { top: clampTop(rect.top), left };
  // The spotlight fills the screen: sit over its lower part.
  return { top: vh - cardHeight - 16, left: (vw - width) / 2 };
}

/**
 * The app tour: a spotlight on each part of the app with a card explaining it, page by page.
 * Offered once to every user (a small card that waits until no dialog is open), and opened
 * again at any time from the header menu or About.
 */
export const AppTour: React.FC = () => {
  const { isTourOpen, setIsTourOpen, activeTab, setActiveTab } = useApp();
  const [stepIndex, setStepIndex] = useState(0);
  const [rect, setRect] = useState<Rect | null>(null);
  const [isOfferVisible, setIsOfferVisible] = useState(false);
  const cardRef = useRef<HTMLDivElement | null>(null);
  const [cardHeight, setCardHeight] = useState(240);
  const tabBeforeTour = useRef(activeTab);
  const step = TOUR_STEPS[stepIndex];

  // --- The one-time offer ---------------------------------------------------------------
  useEffect(() => {
    if (readOffered()) return;
    let timer: number;
    const tryOffer = () => {
      if (isAnotherOverlayOpen()) {
        timer = window.setTimeout(tryOffer, 2000);
        return;
      }
      setIsOfferVisible(true);
    };
    timer = window.setTimeout(tryOffer, 2500);
    return () => window.clearTimeout(timer);
  }, []);

  const dismissOffer = () => {
    markOffered();
    setIsOfferVisible(false);
  };

  // --- Opening and closing --------------------------------------------------------------
  useEffect(() => {
    if (!isTourOpen) return;
    markOffered();
    setIsOfferVisible(false);
    tabBeforeTour.current = activeTab;
    setStepIndex(0);
    // Only on open; the tour changes the page itself afterwards.
  }, [isTourOpen]);

  const close = useCallback(() => {
    setIsTourOpen(false);
    setRect(null);
    setActiveTab(tabBeforeTour.current);
  }, [setIsTourOpen, setActiveTab]);

  const go = useCallback((delta: number) => {
    setStepIndex((i) => Math.min(TOUR_STEPS.length - 1, Math.max(0, i + delta)));
  }, []);

  // --- Following the step's target -------------------------------------------------------
  useEffect(() => {
    if (!isTourOpen || !step) return;
    if (step.page && step.page !== activeTab) setActiveTab(step.page);

    let cancelled = false;
    let frame = 0;
    let element: HTMLElement | null = null;
    const started = performance.now();

    const measure = () => {
      if (!element) return;
      const r = element.getBoundingClientRect();
      setRect({ top: r.top - PAD, left: r.left - PAD, width: r.width + PAD * 2, height: r.height + PAD * 2 });
    };

    // The page may still be mounting (a lazy page, a tab switch): look for the target for up
    // to a second and a half, then fall back to a centred card.
    const find = () => {
      if (cancelled) return;
      element = findTarget(step.target);
      if (element) {
        element.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        measure();
        return;
      }
      if (!step.target || performance.now() - started > 1500) {
        setRect(null);
        return;
      }
      frame = requestAnimationFrame(find);
    };
    setRect(null);
    frame = requestAnimationFrame(find);

    // Layout keeps moving (fonts, images, panels opening): re-measure on scroll, resize and
    // a slow heartbeat.
    const onChange = () => measure();
    window.addEventListener('resize', onChange);
    window.addEventListener('scroll', onChange, true);
    const heartbeat = window.setInterval(onChange, 400);
    return () => {
      cancelled = true;
      cancelAnimationFrame(frame);
      window.removeEventListener('resize', onChange);
      window.removeEventListener('scroll', onChange, true);
      window.clearInterval(heartbeat);
    };
    // activeTab is left out on purpose: the step switches it, and must not re-run when it does.
  }, [isTourOpen, stepIndex]);

  useLayoutEffect(() => {
    if (cardRef.current) setCardHeight(cardRef.current.offsetHeight);
  });

  // Focus the card on every step, for keyboard and screen-reader users.
  useEffect(() => {
    if (isTourOpen) cardRef.current?.focus();
  }, [isTourOpen, stepIndex]);

  // --- Keyboard --------------------------------------------------------------------------
  useEffect(() => {
    if (!isTourOpen) return;
    const onKey = (event: KeyboardEvent) => {
      const handled: Record<string, () => void> = {
        Escape: close,
        ArrowRight: () => go(1),
        ArrowLeft: () => go(-1)
      };
      const action = handled[event.key];
      if (!action) return;
      // Captured first so the app's own shortcuts (Esc closing dialogs) do not also fire.
      event.preventDefault();
      event.stopPropagation();
      action();
    };
    window.addEventListener('keydown', onKey, true);
    return () => window.removeEventListener('keydown', onKey, true);
  }, [isTourOpen, close, go]);

  // --- Rendering -------------------------------------------------------------------------
  if (!isTourOpen) {
    return isOfferVisible ? (
      <Portal>
        <Paper
          role="dialog"
          aria-labelledby="tour-offer-title"
          elevation={12}
          sx={{
            position: 'fixed',
            left: { xs: 12, sm: 24 },
            right: { xs: 12, sm: 'auto' },
            bottom: 24,
            zIndex: 1450,
            width: { sm: 340 },
            p: 2,
            borderRadius: '12px',
            border: '1px solid var(--gl-primary)',
            backgroundColor: 'var(--gl-panel-bg)'
          }}
        >
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.75 }}>
            <SchoolIcon sx={{ color: 'var(--gl-primary)' }} />
            <Typography id="tour-offer-title" sx={{ fontWeight: 800, fontSize: '0.95rem', flex: 1 }}>
              New here? Take the tour
            </Typography>
            <IconButton size="small" aria-label="Dismiss tour offer" onClick={dismissOffer}>
              <CloseIcon fontSize="small" />
            </IconButton>
          </Box>
          <Typography variant="body2" sx={{ color: 'var(--gl-text-secondary)', mb: 1.5 }}>
            Three minutes through every feature: breeding, the scanner, livestock, the farm planner and more. You can open it
            again any time from the ⋮ menu.
          </Typography>
          <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
            <Button onClick={dismissOffer}>Not now</Button>
            <Button variant="contained" onClick={() => setIsTourOpen(true)}>
              Start tour
            </Button>
          </Box>
        </Paper>
      </Portal>
    ) : null;
  }

  if (!step) return null;
  const isFirst = stepIndex === 0;
  const isLast = stepIndex === TOUR_STEPS.length - 1;
  const position = placeCard(rect, cardHeight);

  return (
    <Portal>
      {/* Blocks the app while the tour runs, so a stray click cannot move the page under it. */}
      <Box sx={{ position: 'fixed', inset: 0, zIndex: 1500 }} aria-hidden>
        {rect ? (
          <Box
            sx={{
              position: 'fixed',
              top: rect.top,
              left: rect.left,
              width: rect.width,
              height: rect.height,
              borderRadius: '10px',
              boxShadow: '0 0 0 9999px rgba(3, 6, 10, 0.68)',
              outline: '2px solid var(--gl-primary)',
              outlineOffset: 0,
              transition: 'top 220ms ease, left 220ms ease, width 220ms ease, height 220ms ease',
              pointerEvents: 'none'
            }}
          />
        ) : (
          <Box sx={{ position: 'fixed', inset: 0, backgroundColor: 'rgba(3, 6, 10, 0.68)' }} />
        )}
      </Box>

      <Paper
        ref={cardRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="tour-step-title"
        aria-describedby="tour-step-body"
        tabIndex={-1}
        elevation={16}
        sx={{
          position: 'fixed',
          top: position.top,
          left: position.left,
          width: `min(${CARD_WIDTH}px, calc(100vw - 24px))`,
          maxHeight: 'calc(100vh - 24px)',
          overflowY: 'auto',
          zIndex: 1501,
          borderRadius: '12px',
          border: '1px solid var(--gl-border)',
          backgroundColor: 'var(--gl-panel-bg)',
          outline: 'none',
          transition: 'top 220ms ease, left 220ms ease'
        }}
      >
        <LinearProgress
          variant="determinate"
          value={((stepIndex + 1) / TOUR_STEPS.length) * 100}
          aria-hidden
          sx={{ height: 3, backgroundColor: 'var(--gl-surface)', '& .MuiLinearProgress-bar': { backgroundColor: 'var(--gl-primary)' } }}
        />
        <Box sx={{ p: 2 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mb: 0.5 }}>
            <Typography sx={{ flex: 1, fontSize: '0.66rem', fontWeight: 800, letterSpacing: '0.08em', textTransform: 'uppercase', color: 'var(--gl-primary)' }}>
              {step.chapter}
            </Typography>
            <Typography sx={{ fontSize: '0.66rem', fontWeight: 700, color: 'var(--gl-text-muted)', fontFamily: 'var(--font-family-mono)' }}>
              {stepIndex + 1} / {TOUR_STEPS.length}
            </Typography>
            <IconButton size="small" aria-label="Close tutorial" onClick={close} sx={{ ml: 0.5, p: 0.25 }}>
              <CloseIcon sx={{ fontSize: 18 }} />
            </IconButton>
          </Box>
          <Typography id="tour-step-title" component="h2" sx={{ fontWeight: 800, fontSize: '1.05rem', mb: 1 }}>
            {step.title}
          </Typography>
          <Box id="tour-step-body">
            {step.body.map((paragraph, i) => (
              <Typography key={i} variant="body2" sx={{ color: 'var(--gl-text-secondary)', '&:not(:last-of-type)': { mb: 0.75 } }}>
                {paragraph}
              </Typography>
            ))}
          </Box>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, mt: 2 }}>
            {!isLast && (
              <Button size="small" onClick={close} sx={{ color: 'var(--gl-text-muted)', mr: 'auto' }}>
                Skip tour
              </Button>
            )}
            {isLast && <Box sx={{ mr: 'auto' }} />}
            {!isFirst && (
              <Button size="small" onClick={() => go(-1)}>
                Back
              </Button>
            )}
            <Button size="small" variant="contained" onClick={isLast ? close : () => go(1)}>
              {isFirst ? 'Start' : isLast ? 'Finish' : 'Next'}
            </Button>
          </Box>
        </Box>
      </Paper>
    </Portal>
  );
};
