import React, { useEffect, useRef, useState } from 'react';
import { Box, CircularProgress, Typography } from '@mui/material';
import type { PlanterType } from '../../domain/planner/farmPlanning.ts';
import { PlanterScene, type SlotContent, type SlotHighlight } from './PlanterScene.ts';

export interface Planter3DViewProps {
  planterType: PlanterType;
  plantType: string;
  slots: (SlotContent | null)[];
  highlights: SlotHighlight[];
  flow?: { from: number[]; to: number | null };
  celebrate?: number | null;
  autoRotate?: boolean;
  /** Bumped by the parent to re-frame the camera. */
  resetKey?: number;
  onSlotClick?: (slot: number) => void;
  onSlotHover?: (slot: number | null) => void;
  /** Shown instead of the scene when WebGL or the models fail. */
  fallback: React.ReactNode;
}

function readAccent(): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue('--gl-primary').trim();
  return value || '#60CDFF';
}

/** React host for {@link PlanterScene}: one scene per mount, driven by props. */
const Planter3DView: React.FC<Planter3DViewProps> = ({
  planterType,
  plantType,
  slots,
  highlights,
  flow,
  celebrate = null,
  autoRotate = false,
  resetKey = 0,
  onSlotClick,
  onSlotHover,
  fallback
}) => {
  const hostRef = useRef<HTMLDivElement | null>(null);
  const sceneRef = useRef<PlanterScene | null>(null);
  const handlers = useRef({ onSlotClick, onSlotHover });
  handlers.current = { onSlotClick, onSlotHover };
  const [isLoading, setIsLoading] = useState(true);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    if (!hostRef.current) return;
    let scene: PlanterScene;
    try {
      scene = new PlanterScene(hostRef.current, {
        accent: readAccent(),
        onSlotClick: (slot) => handlers.current.onSlotClick?.(slot),
        onSlotHover: (slot) => handlers.current.onSlotHover?.(slot),
        onLoadingChange: setIsLoading,
        onError: (error) => {
          console.error('[planter3d]', error);
          setFailed(true);
        }
      });
    } catch (error) {
      console.error('[planter3d] WebGL unavailable', error);
      setFailed(true);
      return;
    }
    sceneRef.current = scene;
    return () => {
      scene.dispose();
      sceneRef.current = null;
    };
  }, []);

  useEffect(() => {
    void sceneRef.current?.setPlanter(planterType);
  }, [planterType]);

  useEffect(() => {
    void sceneRef.current?.setPlantType(plantType);
  }, [plantType]);

  useEffect(() => {
    sceneRef.current?.setSlots(slots);
  }, [slots]);

  useEffect(() => {
    sceneRef.current?.setHighlights(highlights);
  }, [highlights]);

  useEffect(() => {
    sceneRef.current?.setFlow(flow?.from ?? [], flow?.to ?? null);
  }, [flow]);

  useEffect(() => {
    sceneRef.current?.setCelebrate(celebrate);
  }, [celebrate]);

  useEffect(() => {
    sceneRef.current?.setAutoRotate(autoRotate);
  }, [autoRotate]);

  useEffect(() => {
    if (resetKey > 0) sceneRef.current?.resetCamera();
  }, [resetKey]);

  if (failed) return <>{fallback}</>;

  return (
    <Box
      sx={{
        position: 'relative',
        width: '100%',
        height: '100%',
        overflow: 'hidden',
        background:
          'radial-gradient(120% 90% at 50% 15%, rgba(var(--gl-primary-rgb), 0.10) 0%, rgba(0,0,0,0) 55%), linear-gradient(180deg, var(--gl-panel-header-bg) 0%, var(--gl-panel-bg) 100%)'
      }}
    >
      <Box ref={hostRef} sx={{ position: 'absolute', inset: 0 }} />
      {isLoading && (
        <Box
          sx={{
            position: 'absolute',
            inset: 0,
            display: 'grid',
            placeItems: 'center',
            pointerEvents: 'none'
          }}
        >
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 1 }}>
            <CircularProgress size={28} />
            <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'var(--font-family-mono)' }}>
              Loading 3D planter…
            </Typography>
          </Box>
        </Box>
      )}
    </Box>
  );
};

export default Planter3DView;
