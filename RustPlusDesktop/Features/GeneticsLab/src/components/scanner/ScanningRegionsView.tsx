import React, { useState, useEffect, useRef } from 'react';
import {
  Box,
  Typography,
  IconButton,
  Button,
  Tooltip,
  Select,
  MenuItem,
  CircularProgress
} from '@mui/material';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import ZoomInIcon from '@mui/icons-material/ZoomIn';
import ZoomOutIcon from '@mui/icons-material/ZoomOut';
import AutoFixHighIcon from '@mui/icons-material/AutoFixHigh';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined';
import TuneIcon from '@mui/icons-material/Tune';
import AspectRatioIcon from '@mui/icons-material/AspectRatio';
import { useScanner } from '../../context/ScannerContext.tsx';
import { SCANNER_CONFIG } from '../../services/scanner/scannerConfig.ts';

interface ScanningRegionsViewProps {
  compact?: boolean;
  initialRegion?: number;
  onClose?: () => void;
}

export const ScanningRegionsView: React.FC<ScanningRegionsViewProps> = ({
  compact = true,
  initialRegion = 0
}) => {
  const {
    isScannerActive,
    isScannerInitializing,
    scannerPreviews,
    setScannerPreviewEnabled,
    startScanner,
    moveScannerRegion,
    scaleScannerRegion,
    resetScannerRegions,
    autoCalibrateScanner,
    isAutoCalibrating,
    activeRegion,
    activeRegionIndex,
    profiles,
    activeProfileId,
    setActiveProfileId
  } = useScanner();

  // Active region selected for manual calibration (0 = Inventory, 1 = Planter)
  const [activeRegionIdx, setActiveRegionIdx] = useState<number>(initialRegion);

  const holdTimerRef = useRef<any>(null);
  const repeatTimerRef = useRef<any>(null);

  // Enable live preview rendering while this component is visible
  useEffect(() => {
    setScannerPreviewEnabled(true);
    return () => {
      setScannerPreviewEnabled(false);
      stopHold();
    };
  }, [setScannerPreviewEnabled]);

  const startHold = (action: () => void) => {
    action();
    holdTimerRef.current = setTimeout(() => {
      repeatTimerRef.current = setInterval(() => {
        action();
      }, SCANNER_CONFIG.calibration.holdRepeatMs || 50);
    }, SCANNER_CONFIG.calibration.holdDelayMs || 220);
  };

  const stopHold = () => {
    if (holdTimerRef.current) clearTimeout(holdTimerRef.current);
    if (repeatTimerRef.current) clearInterval(repeatTimerRef.current);
    holdTimerRef.current = null;
    repeatTimerRef.current = null;
  };

  const renderRegionCard = (rIdx: number) => {
    const isSelected = activeRegionIdx === rIdx;
    const isInventory = rIdx === 0;
    const tag = isInventory ? 'R1' : 'R2';
    const regionName = isInventory ? 'Inventory Tooltip' : 'Planter Tooltip';
    const regionDescription = isInventory
      ? 'Scans plant tooltips inside your inventory or storage boxes.'
      : 'Scans plant tooltips when looking directly at planter boxes.';

    const isCurrentlyDetecting = activeRegionIndex === rIdx;

    return (
      <Box
        key={rIdx}
        onClick={() => setActiveRegionIdx(rIdx)}
        sx={{
          display: 'flex',
          flexDirection: 'column',
          gap: 0.75,
          backgroundColor: isCurrentlyDetecting
            ? 'rgba(0, 229, 255, 0.08)'
            : isSelected
              ? 'rgba(0, 229, 255, 0.04)'
              : 'rgba(20, 24, 30, 0.65)',
          border: '1px solid',
          borderColor: isCurrentlyDetecting
            ? '#00E5FF'
            : isSelected
              ? 'rgba(0, 229, 255, 0.55)'
              : 'rgba(255, 255, 255, 0.08)',
          borderLeft: isCurrentlyDetecting
            ? '4px solid #00E5FF'
            : isSelected
              ? '3.5px solid var(--gl-primary, #00E5FF)'
              : '3.5px solid transparent',
          borderRadius: '6px',
          p: 1.25,
          cursor: 'pointer',
          transition: 'all 0.18s ease',
          boxShadow: isCurrentlyDetecting
            ? '0 0 16px rgba(0, 229, 255, 0.45)'
            : isSelected
              ? '0 4px 16px rgba(0, 229, 255, 0.12)'
              : 'none',
          '&:hover': {
            borderColor: isSelected ? 'var(--gl-primary)' : 'rgba(255, 255, 255, 0.2)',
            backgroundColor: isSelected ? 'rgba(0, 229, 255, 0.07)' : 'rgba(255, 255, 255, 0.02)'
          }
        }}
      >
        {/* Header: Tag + Title + Info + Auto-Detect */}
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.85, minWidth: 0 }}>
            {/* Tag Badge */}
            <Box
              sx={{
                px: 0.65,
                py: 0.1,
                borderRadius: '3px',
                backgroundColor: isCurrentlyDetecting
                  ? '#00E5FF'
                  : isSelected
                    ? 'var(--gl-primary)'
                    : 'rgba(255, 255, 255, 0.08)',
                color: isCurrentlyDetecting || isSelected ? '#000' : 'var(--gl-text-secondary)',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.66rem',
                fontWeight: 800,
                letterSpacing: '0.04em',
                flexShrink: 0
              }}
            >
              {tag}
            </Box>

            {isCurrentlyDetecting && (
              <Box
                sx={{
                  px: 0.5,
                  py: 0.05,
                  borderRadius: '2px',
                  backgroundColor: 'rgba(0, 229, 255, 0.22)',
                  border: '1px solid rgba(0, 229, 255, 0.6)',
                  color: '#00E5FF',
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.6rem',
                  fontWeight: 800,
                  letterSpacing: '0.05em'
                }}
              >
                ACTIVE
              </Box>
            )}

            {/* Region Title (Strictly single line) */}
            <Typography
              sx={{
                fontSize: '0.78rem',
                fontWeight: 700,
                color: isSelected ? 'var(--gl-primary)' : 'var(--gl-text-primary)',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                textOverflow: 'ellipsis'
              }}
            >
              {regionName}
            </Typography>

            <Tooltip title={regionDescription} arrow>
              <InfoOutlinedIcon sx={{ fontSize: 14, color: 'var(--gl-text-muted)', flexShrink: 0 }} />
            </Tooltip>
          </Box>

          {/* 1-Click Auto Calibrate Button (Single-line guaranteed) */}
          <Button
            size="small"
            variant={isSelected ? 'contained' : 'outlined'}
            onClick={(e) => {
              e.stopPropagation();
              setActiveRegionIdx(rIdx);
              autoCalibrateScanner(rIdx);
            }}
            disabled={isAutoCalibrating}
            startIcon={
              isAutoCalibrating ? (
                <CircularProgress size={11} color="inherit" />
              ) : (
                <AutoFixHighIcon sx={{ fontSize: 13 }} />
              )
            }
            sx={{
              whiteSpace: 'nowrap',
              minWidth: 98,
              py: 0.3,
              px: 1,
              fontSize: '0.7rem',
              fontWeight: 800,
              backgroundColor: isSelected ? 'var(--gl-primary)' : 'transparent',
              color: isSelected ? '#000' : 'var(--gl-primary)',
              borderColor: isSelected ? 'var(--gl-primary)' : 'rgba(0, 229, 255, 0.4)',
              boxShadow: isSelected ? '0 0 10px rgba(0, 229, 255, 0.25)' : 'none',
              flexShrink: 0,
              '&:hover': {
                backgroundColor: isSelected ? 'var(--gl-primary-hover, #00C8E0)' : 'rgba(0, 229, 255, 0.1)',
                borderColor: 'var(--gl-primary)'
              }
            }}
          >
            {isAutoCalibrating ? 'Scanning...' : 'Auto-Detect'}
          </Button>
        </Box>

        {/* Live Video Preview Box with Guide Stripes */}
        <Box
          sx={{
            width: '100%',
            height: compact ? 52 : 62,
            backgroundColor: '#000000',
            borderRadius: '4px',
            border: '1px solid',
            borderColor: isSelected ? 'rgba(0, 229, 255, 0.35)' : 'rgba(255, 255, 255, 0.1)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            overflow: 'hidden',
            position: 'relative',
            boxShadow: 'inset 0 0 10px rgba(0, 0, 0, 0.85)'
          }}
        >
          <Box
            component="img"
            id={`scanner-preview-img-${rIdx}`}
            src={scannerPreviews[rIdx] || ''}
            alt={`${regionName} Preview`}
            sx={{
              width: '100%',
              height: '100%',
              objectFit: 'fill',
              imageRendering: 'crisp-edges',
              display: isScannerActive || scannerPreviews[rIdx] ? 'block' : 'none'
            }}
          />
          {!isScannerActive && !scannerPreviews[rIdx] && (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontSize: '0.7rem' }}>
                Capture inactive
              </Typography>
              <Button
                size="small"
                variant="outlined"
                onClick={(e) => {
                  e.stopPropagation();
                  startScanner();
                }}
                startIcon={<PlayArrowIcon sx={{ fontSize: 12 }} />}
                sx={{
                  fontSize: '0.66rem',
                  py: 0.1,
                  px: 0.75,
                  borderColor: 'var(--gl-primary)',
                  color: 'var(--gl-primary)'
                }}
              >
                Start
              </Button>
            </Box>
          )}
          {isScannerActive && !scannerPreviews[rIdx] && (
            <Typography
              variant="caption"
              sx={{
                color: 'var(--gl-text-faint, #666)',
                fontSize: '0.68rem',
                fontFamily: 'monospace'
              }}
            >
              {isScannerInitializing ? 'Initializing OCR…' : 'Waiting for video stream…'}
            </Typography>
          )}
        </Box>
      </Box>
    );
  };

  const activeRegionLabel = activeRegionIdx === 0 ? 'Region 1 · Inventory' : 'Region 2 · Planter';

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
      {/* Preset Toolbar */}
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          backgroundColor: 'rgba(255, 255, 255, 0.025)',
          border: '1px solid rgba(255, 255, 255, 0.07)',
          borderRadius: '6px',
          px: 1.25,
          py: 0.5
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.75 }}>
          <AspectRatioIcon sx={{ fontSize: 14, color: 'var(--gl-text-muted)' }} />
          <Typography
            variant="caption"
            sx={{
              color: 'var(--gl-text-muted)',
              fontSize: '0.68rem',
              fontWeight: 700,
              letterSpacing: '0.04em'
            }}
          >
            PRESET
          </Typography>
          <Select
            size="small"
            value={activeProfileId}
            onChange={(e) => setActiveProfileId(e.target.value)}
            sx={{
              fontSize: '0.72rem',
              height: 24,
              color: 'var(--gl-primary)',
              backgroundColor: 'rgba(0, 0, 0, 0.3)',
              '& .MuiSelect-select': { py: 0.2, px: 0.8 }
            }}
          >
            {profiles.map((p) => (
              <MenuItem key={p.id} value={p.id} sx={{ fontSize: '0.75rem' }}>
                {p.name}
              </MenuItem>
            ))}
          </Select>
        </Box>

        <Tooltip title="Reset selected region to preset defaults">
          <IconButton
            size="small"
            onClick={resetScannerRegions}
            sx={{
              color: 'var(--gl-text-muted)',
              p: 0.35,
              '&:hover': { color: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.08)' }
            }}
          >
            <RestartAltIcon sx={{ fontSize: 15 }} />
          </IconButton>
        </Tooltip>
      </Box>

      {/* Both Regions Stacked Under Each Other */}
      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        {renderRegionCard(0)}
        {renderRegionCard(1)}
      </Box>

      {/* Tactile Manual Calibration Deck */}
      <Box
        sx={{
          backgroundColor: 'rgba(14, 18, 24, 0.85)',
          border: '1px solid rgba(255, 255, 255, 0.08)',
          borderRadius: '6px',
          p: 1.25,
          display: 'flex',
          flexDirection: 'column',
          gap: 1
        }}
      >
        {/* Section Header: Title & Segmented Region Switcher */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.6 }}>
            <TuneIcon sx={{ fontSize: 14, color: 'var(--gl-primary)' }} />
            <Typography
              variant="caption"
              sx={{
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.7rem',
                color: 'var(--gl-text-primary)',
                fontWeight: 700,
                letterSpacing: '0.02em'
              }}
            >
              NUDGE & SCALE
            </Typography>
          </Box>

          {/* Segmented Switcher for Active Region */}
          <Box
            sx={{
              display: 'inline-flex',
              backgroundColor: 'rgba(0, 0, 0, 0.45)',
              borderRadius: '4px',
              p: '2px',
              border: '1px solid rgba(255, 255, 255, 0.08)'
            }}
          >
            <Button
              size="small"
              onClick={() => setActiveRegionIdx(0)}
              sx={{
                py: 0.15,
                px: 0.85,
                minWidth: 'auto',
                fontSize: '0.66rem',
                fontWeight: 800,
                backgroundColor: activeRegionIdx === 0 ? 'var(--gl-primary)' : 'transparent',
                color: activeRegionIdx === 0 ? '#000' : 'var(--gl-text-muted)',
                borderRadius: '3px',
                '&:hover': {
                  backgroundColor: activeRegionIdx === 0 ? 'var(--gl-primary)' : 'rgba(255, 255, 255, 0.05)'
                }
              }}
            >
              R1 · Inv
            </Button>
            <Button
              size="small"
              onClick={() => setActiveRegionIdx(1)}
              sx={{
                py: 0.15,
                px: 0.85,
                minWidth: 'auto',
                fontSize: '0.66rem',
                fontWeight: 800,
                backgroundColor: activeRegionIdx === 1 ? 'var(--gl-primary)' : 'transparent',
                color: activeRegionIdx === 1 ? '#000' : 'var(--gl-text-muted)',
                borderRadius: '3px',
                '&:hover': {
                  backgroundColor: activeRegionIdx === 1 ? 'var(--gl-primary)' : 'rgba(255, 255, 255, 0.05)'
                }
              }}
            >
              R2 · Planter
            </Button>
          </Box>
        </Box>

        {/* Tactile Control Grid */}
        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-around', pt: 0.25 }}>
          {/* Left: Directional Nudge D-Pad with Real Button Tiles */}
          <Box
            sx={{
              display: 'grid',
              gridTemplateColumns: 'repeat(3, 28px)',
              gridTemplateRows: 'repeat(3, 28px)',
              gap: '3px',
              alignItems: 'center',
              justifyItems: 'center'
            }}
          >
            <Box />
            <Tooltip title={`Nudge Up: ${activeRegionLabel}`} arrow>
              <IconButton
                aria-label="Nudge Up"
                size="small"
                onMouseDown={() => startHold(() => moveScannerRegion(activeRegionIdx, 0, -0.001))}
                onMouseUp={stopHold}
                onMouseLeave={stopHold}
                sx={{
                  width: 28,
                  height: 28,
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                  border: '1px solid rgba(255, 255, 255, 0.1)',
                  borderRadius: '4px',
                  color: 'var(--gl-text-secondary)',
                  '&:hover': {
                    color: 'var(--gl-primary)',
                    borderColor: 'var(--gl-primary)',
                    backgroundColor: 'rgba(0, 229, 255, 0.1)'
                  }
                }}
              >
                <ArrowUpwardIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
            <Box />

            <Tooltip title={`Nudge Left: ${activeRegionLabel}`} arrow>
              <IconButton
                aria-label="Nudge Left"
                size="small"
                onMouseDown={() => startHold(() => moveScannerRegion(activeRegionIdx, -0.001, 0))}
                onMouseUp={stopHold}
                onMouseLeave={stopHold}
                sx={{
                  width: 28,
                  height: 28,
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                  border: '1px solid rgba(255, 255, 255, 0.1)',
                  borderRadius: '4px',
                  color: 'var(--gl-text-secondary)',
                  '&:hover': {
                    color: 'var(--gl-primary)',
                    borderColor: 'var(--gl-primary)',
                    backgroundColor: 'rgba(0, 229, 255, 0.1)'
                  }
                }}
              >
                <ArrowBackIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>

            {/* Center: Active Region & 1px indicator */}
            <Box
              sx={{
                width: 28,
                height: 28,
                borderRadius: '4px',
                border: '1px solid rgba(0, 229, 255, 0.4)',
                backgroundColor: 'rgba(0, 229, 255, 0.08)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
              }}
            >
              <Typography
                variant="caption"
                sx={{
                  fontSize: '0.62rem',
                  color: 'var(--gl-primary)',
                  fontWeight: 800,
                  fontFamily: 'monospace'
                }}
              >
                {activeRegionIdx === 0 ? 'R1' : 'R2'}
              </Typography>
            </Box>

            <Tooltip title={`Nudge Right: ${activeRegionLabel}`} arrow>
              <IconButton
                aria-label="Nudge Right"
                size="small"
                onMouseDown={() => startHold(() => moveScannerRegion(activeRegionIdx, 0.001, 0))}
                onMouseUp={stopHold}
                onMouseLeave={stopHold}
                sx={{
                  width: 28,
                  height: 28,
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                  border: '1px solid rgba(255, 255, 255, 0.1)',
                  borderRadius: '4px',
                  color: 'var(--gl-text-secondary)',
                  '&:hover': {
                    color: 'var(--gl-primary)',
                    borderColor: 'var(--gl-primary)',
                    backgroundColor: 'rgba(0, 229, 255, 0.1)'
                  }
                }}
              >
                <ArrowForwardIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>

            <Box />
            <Tooltip title={`Nudge Down: ${activeRegionLabel}`} arrow>
              <IconButton
                aria-label="Nudge Down"
                size="small"
                onMouseDown={() => startHold(() => moveScannerRegion(activeRegionIdx, 0, 0.001))}
                onMouseUp={stopHold}
                onMouseLeave={stopHold}
                sx={{
                  width: 28,
                  height: 28,
                  backgroundColor: 'rgba(255, 255, 255, 0.05)',
                  border: '1px solid rgba(255, 255, 255, 0.1)',
                  borderRadius: '4px',
                  color: 'var(--gl-text-secondary)',
                  '&:hover': {
                    color: 'var(--gl-primary)',
                    borderColor: 'var(--gl-primary)',
                    backgroundColor: 'rgba(0, 229, 255, 0.1)'
                  }
                }}
              >
                <ArrowDownwardIcon sx={{ fontSize: 16 }} />
              </IconButton>
            </Tooltip>
            <Box />
          </Box>

          {/* Right: Width Scaling Controls */}
          <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 0.75 }}>
            <Typography
              variant="caption"
              sx={{
                fontSize: '0.64rem',
                color: 'var(--gl-text-muted)',
                fontWeight: 700,
                letterSpacing: '0.04em'
              }}
            >
              WIDTH SCALE
            </Typography>

            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                backgroundColor: 'rgba(255, 255, 255, 0.03)',
                border: '1px solid rgba(255, 255, 255, 0.08)',
                borderRadius: '5px',
                p: '2px'
              }}
            >
              <Tooltip title={`Shrink Width: ${activeRegionLabel}`} arrow>
                <IconButton
                  aria-label="Shrink Region"
                  size="small"
                  onMouseDown={() => startHold(() => scaleScannerRegion(activeRegionIdx, -0.001))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                  sx={{
                    color: 'var(--gl-text-secondary)',
                    p: 0.6,
                    '&:hover': { color: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.1)' }
                  }}
                >
                  <ZoomOutIcon sx={{ fontSize: 17 }} />
                </IconButton>
              </Tooltip>

              <Box sx={{ width: 1, height: 16, backgroundColor: 'rgba(255, 255, 255, 0.1)' }} />

              <Tooltip title={`Enlarge Width: ${activeRegionLabel}`} arrow>
                <IconButton
                  aria-label="Enlarge Region"
                  size="small"
                  onMouseDown={() => startHold(() => scaleScannerRegion(activeRegionIdx, 0.001))}
                  onMouseUp={stopHold}
                  onMouseLeave={stopHold}
                  sx={{
                    color: 'var(--gl-text-secondary)',
                    p: 0.6,
                    '&:hover': { color: 'var(--gl-primary)', backgroundColor: 'rgba(0, 229, 255, 0.1)' }
                  }}
                >
                  <ZoomInIcon sx={{ fontSize: 17 }} />
                </IconButton>
              </Tooltip>
            </Box>

            <Typography
              variant="caption"
              sx={{
                fontSize: '0.6rem',
                color: 'var(--gl-text-faint, #666)',
                fontFamily: 'monospace'
              }}
            >
              Hold for continuous
            </Typography>
          </Box>
        </Box>
      </Box>
    </Box>
  );
};
