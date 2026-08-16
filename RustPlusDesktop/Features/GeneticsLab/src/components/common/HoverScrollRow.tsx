import React, { useRef, useState, useEffect, useCallback } from 'react';
import { Box, IconButton } from '@mui/material';
import ChevronLeftIcon from '@mui/icons-material/ChevronLeft';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';

interface HoverScrollRowProps {
  children: React.ReactNode;
  /** Gap between children, in MUI spacing units. */
  gap?: number;
  /** Pixels scrolled per animation frame while an arrow is hovered. */
  speed?: number;
}

/**
 * Horizontal row that scrolls when the content overflows. Left/right direction
 * arrows fade in only on the side(s) that can still scroll, and the row
 * auto-scrolls while the mouse hovers over an arrow (clicking nudges it too).
 */
export const HoverScrollRow: React.FC<HoverScrollRowProps> = ({ children, gap = 0.5, speed = 6 }) => {
  const scrollRef = useRef<HTMLDivElement>(null);
  const rafRef = useRef<number | null>(null);
  const [canScroll, setCanScroll] = useState({ left: false, right: false });

  const updateArrows = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    const maxScroll = el.scrollWidth - el.clientWidth;
    setCanScroll({
      left: el.scrollLeft > 1,
      right: el.scrollLeft < maxScroll - 1
    });
  }, []);

  const stopScroll = useCallback(() => {
    if (rafRef.current !== null) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }
  }, []);

  const startScroll = useCallback((dir: -1 | 1) => {
    stopScroll();
    const step = () => {
      const el = scrollRef.current;
      if (!el) return;
      el.scrollLeft += dir * speed;
      updateArrows();
      rafRef.current = requestAnimationFrame(step);
    };
    rafRef.current = requestAnimationFrame(step);
  }, [speed, stopScroll, updateArrows]);

  const nudge = (dir: -1 | 1) => {
    const el = scrollRef.current;
    if (el) el.scrollBy({ left: dir * 90, behavior: 'smooth' });
  };

  useEffect(() => {
    updateArrows();
    const el = scrollRef.current;
    if (!el) return;
    const ro = new ResizeObserver(updateArrows);
    ro.observe(el);
    return () => {
      ro.disconnect();
      stopScroll();
    };
  }, [updateArrows, stopScroll, children]);

  const arrowSx = (side: 'left' | 'right') => ({
    position: 'absolute' as const,
    top: 0,
    bottom: 0,
    [side]: 0,
    zIndex: 2,
    height: '100%',
    width: 26,
    borderRadius: 0,
    color: 'var(--gl-primary)',
    backgroundColor: 'transparent',
    background:
      side === 'left'
        ? 'linear-gradient(to right, var(--gl-panel-bg) 40%, transparent)'
        : 'linear-gradient(to left, var(--gl-panel-bg) 40%, transparent)',
    '&:hover': { color: 'var(--gl-primary-hover)' }
  });

  return (
    <Box sx={{ position: 'relative' }}>
      {canScroll.left && (
        <IconButton
          size="small"
          disableRipple
          onMouseEnter={() => startScroll(-1)}
          onMouseLeave={stopScroll}
          onClick={() => nudge(-1)}
          sx={arrowSx('left')}
        >
          <ChevronLeftIcon sx={{ fontSize: 20 }} />
        </IconButton>
      )}

      <Box
        ref={scrollRef}
        onScroll={updateArrows}
        sx={{
          display: 'flex',
          alignItems: 'center',
          gap,
          overflowX: 'auto',
          scrollbarWidth: 'none',
          msOverflowStyle: 'none',
          '&::-webkit-scrollbar': { display: 'none' }
        }}
      >
        {children}
      </Box>

      {canScroll.right && (
        <IconButton
          size="small"
          disableRipple
          onMouseEnter={() => startScroll(1)}
          onMouseLeave={stopScroll}
          onClick={() => nudge(1)}
          sx={arrowSx('right')}
        >
          <ChevronRightIcon sx={{ fontSize: 20 }} />
        </IconButton>
      )}
    </Box>
  );
};
