import { lazy } from 'react';

// three.js is ~600 KB; it loads only when a 3D planter is actually shown.
export const LazyPlanter3DView = lazy(() => import('./Planter3DView.tsx'));
