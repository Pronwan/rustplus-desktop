import React, { useState, useEffect, useRef, useMemo } from 'react';
import {
  Box,
  Typography,
  Button,
  Tabs,
  Tab,
  IconButton,
  Paper,
  Tooltip,
  LinearProgress,
  Popover
} from '@mui/material';
import SettingsIcon from '@mui/icons-material/Settings';
import DesktopWindowsIcon from '@mui/icons-material/DesktopWindows';
import SaveIcon from '@mui/icons-material/Save';
import CloseIcon from '@mui/icons-material/Close';
import BlockIcon from '@mui/icons-material/Block';
import SkipNextIcon from '@mui/icons-material/SkipNext';
import { useApp } from '../../context/AppContext.tsx';
import { useLabColors } from '../../theme/useLabColors.ts';
import { Sapling } from '../../domain/genetics/Sapling.ts';
import { GREEN_GENES } from '../../domain/genetics/Gene.ts';

const SAMPLE_GENES = [
  'GGYYHH',
  'YGGYHH',
  'HHGGYY',
  'WYGGYX',
  'GHGYHW',
  'YYGGHH',
  'XGYYHH'
];

const ROW_HEIGHT = 28; // Exact pixel height per line

export const GeneInputs: React.FC = () => {
  const {
    sourceSaplings,
    setSourceSaplings,
    geneInputText,
    setGeneInputText,
    runSimulation,
    cancelSimulation,
    skipCurrentGeneration,
    isCalculating,
    progress,
    setIsOptionsModalOpen,
    isScannerActive,
    isScannerInitializing,
    startScanner,
    stopScanner,
    options,
    setIsScannerGuideOpen,
    savedGeneSets,
    loadSavedGeneSet,
    deleteSavedGeneSet,
    saveCurrentGeneSet,
    results,
    highlightedGroup
  } = useApp();

  const C = useLabColors();

  const [activeSubTab, setActiveSubTab] = useState<'current' | 'saved'>('current');
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const [activeLineIdx, setActiveLineIdx] = useState<number | null>(null);
  const [clearAnchorEl, setClearAnchorEl] = useState<HTMLElement | null>(null);

  const handleClearConfirmed = () => {
    setGeneInputText('');
    setSourceSaplings([]);
    setClearAnchorEl(null);
  };

  // Compute used clone indices from the highlighted plan (Screenshot 1 & 3)
  const usedCloneIndices = useMemo(() => {
    const indices = new Set<number>();
    if (highlightedGroup && highlightedGroup.mapList.length > 0) {
      const bestMap = highlightedGroup.mapList[0];
      if (bestMap) {
        if (bestMap.baseSapling && bestMap.baseSapling.index !== undefined) {
          indices.add(bestMap.baseSapling.index);
        }
        bestMap.crossbreedingSaplings.forEach((s) => {
          if (s.index !== undefined) {
            indices.add(s.index);
          }
        });
      }
    }
    return indices;
  }, [highlightedGroup]);

  // Split text into lines
  const lines = useMemo(() => {
    const raw = geneInputText.split('\n');
    return raw;
  }, [geneInputText]);

  // Keep sourceSaplings in sync with geneInputText
  const handleTextChange = (newText: string) => {
    // Sanitize: uppercase only G, Y, H, W, X and newlines
    const linesArr = newText.toUpperCase().split('\n');
    const cleanedLines = linesArr.map((line) => line.replace(/[^GHYWX]/g, '').slice(0, 6));
    const cleanedText = cleanedLines.join('\n');
    setGeneInputText(cleanedText);

    const validSaplings: Sapling[] = [];
    cleanedLines.forEach((l, idx) => {
      if (l.length === 6 && Sapling.isValidGeneString(l)) {
        validSaplings.push(new Sapling(l, 0, idx));
      }
    });
    setSourceSaplings(validSaplings);
  };

  const handleCursorMove = () => {
    if (!textareaRef.current) return;
    const pos = textareaRef.current.selectionStart;
    const textBefore = textareaRef.current.value.slice(0, pos);
    const lineNum = textBefore.split('\n').length - 1;
    setActiveLineIdx(lineNum);
  };

  const handleLoadSample = () => {
    const text = SAMPLE_GENES.join('\n');
    handleTextChange(text);
  };

  // Determine row count to display (at least 8 rows)
  const displayRowCount = Math.max(lines.length, 8);

  return (
    <Box sx={{ width: '100%' }}>
      {/* Top Action Bar - Single Row */}
      <Box sx={{ display: 'flex', gap: '6px', mb: 2, flexWrap: 'nowrap', width: '100%' }}>
        {isCalculating ? (
          <>
            {/* CANCEL BUTTON */}
            <Button
              variant="contained"
              onClick={() => cancelSimulation()}
              startIcon={<BlockIcon sx={{ fontSize: 15 }} />}
              sx={{
                flex: 1,
                backgroundColor: C.error,
                color: '#FFFFFF',
                fontWeight: 800,
                fontSize: '0.78rem',
                py: 0.6,
                px: 1,
                whiteSpace: 'nowrap',
                minWidth: 0,
                letterSpacing: '0.3px',
                '&:hover': { backgroundColor: C.errorHover }
              }}
            >
              CANCEL
            </Button>

            {/* SKIP BUTTON */}
            <Tooltip
              title={
                <Typography sx={{ fontSize: '0.78rem', fontFamily: 'monospace', p: 0.5 }}>
                  Skip to calculating the next generation without checking all combinations for the current one.
                </Typography>
              }
              arrow
              placement="top"
            >
              <span style={{ flex: 1, display: 'inline-flex' }}>
                <Button
                  variant="contained"
                  onClick={() => skipCurrentGeneration()}
                  endIcon={<SkipNextIcon sx={{ fontSize: 15 }} />}
                  sx={{
                    width: '100%',
                    backgroundColor: C.warning,
                    color: C.isDark ? '#000000' : '#FFFFFF',
                    fontWeight: 800,
                    fontSize: '0.78rem',
                    py: 0.6,
                    px: 1,
                    whiteSpace: 'nowrap',
                    minWidth: 0,
                    letterSpacing: '0.3px',
                    '&:hover': { backgroundColor: C.warningHover }
                  }}
                >
                  SKIP
                </Button>
              </span>
            </Tooltip>

            {/* OPTIONS BUTTON (DISABLED WHILE CALCULATING) */}
            <Button
              variant="contained"
              disabled
              startIcon={<SettingsIcon sx={{ fontSize: 15 }} />}
              sx={{
                flex: 1,
                backgroundColor: C.surface,
                color: C.textFaint,
                border: `1px solid ${C.borderStrong}`,
                fontWeight: 700,
                fontSize: '0.78rem',
                py: 0.6,
                px: 1,
                whiteSpace: 'nowrap',
                minWidth: 0
              }}
            >
              OPTIONS
            </Button>
          </>
        ) : (
          <>
            {/* CALCULATE BUTTON */}
            <Button
              variant="contained"
              onClick={() => runSimulation()}
              disabled={sourceSaplings.length < 2 || isCalculating || isScannerActive || isScannerInitializing}
              sx={{
                flex: 1.1,
                backgroundColor: C.primary,
                color: C.onPrimary,
                fontWeight: 800,
                fontSize: '0.78rem',
                py: 0.6,
                px: 1,
                whiteSpace: 'nowrap',
                minWidth: 0,
                letterSpacing: '0.3px',
                '&:hover': { backgroundColor: C.primaryHover },
                '&:disabled': { backgroundColor: C.surface, color: C.textFaint }
              }}
            >
              CALCULATE
            </Button>

            {/* MANUAL SAVE SET BUTTON (when auto-save is off in Options) */}
            {!options.autoSaveInputSets && (
              <Button
                variant="contained"
                onClick={saveCurrentGeneSet}
                disabled={sourceSaplings.length === 0}
                startIcon={<SaveIcon sx={{ fontSize: 15 }} />}
                sx={{
                  flex: 0.9,
                  backgroundColor: C.surface,
                  color: C.primary,
                  border: `1px solid ${C.primaryBorder}`,
                  fontWeight: 700,
                  fontSize: '0.75rem',
                  py: 0.6,
                  px: 0.8,
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                  '&:hover': { backgroundColor: C.surfaceHover, borderColor: C.primary },
                  '&:disabled': { backgroundColor: C.inputBg, color: C.textFaint, borderColor: C.border }
                }}
              >
                SAVE
              </Button>
            )}

            {/* SCAN RUST / STOP SCANNING BUTTON */}
            {!isScannerActive && !isScannerInitializing ? (
              <Button
                variant="contained"
                onClick={() => {
                  if (options.skipScannerGuide) {
                    startScanner();
                  } else {
                    setIsScannerGuideOpen(true);
                  }
                }}
                startIcon={<DesktopWindowsIcon sx={{ fontSize: 15 }} />}
                sx={{
                  flex: 1.1,
                  backgroundColor: C.surface,
                  color: C.textPrimary,
                  border: `1px solid ${C.borderStrong}`,
                  fontWeight: 700,
                  fontSize: '0.78rem',
                  py: 0.6,
                  px: 1,
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                  '&:hover': { backgroundColor: C.surfaceHover }
                }}
              >
                SCAN RUST
              </Button>
            ) : isScannerInitializing ? (
              <Button
                variant="contained"
                disabled
                sx={{
                  flex: 1.1,
                  backgroundColor: C.surfaceHover,
                  color: C.textMuted,
                  fontWeight: 700,
                  fontSize: '0.78rem',
                  py: 0.6,
                  px: 1,
                  whiteSpace: 'nowrap',
                  minWidth: 0
                }}
              >
                INITIALIZING...
              </Button>
            ) : (
              <Button
                variant="contained"
                onClick={stopScanner}
                sx={{
                  flex: 1.1,
                  backgroundColor: C.error,
                  color: '#FFFFFF',
                  fontWeight: 800,
                  fontSize: '0.78rem',
                  py: 0.6,
                  px: 1,
                  whiteSpace: 'nowrap',
                  minWidth: 0,
                  display: 'flex',
                  alignItems: 'center',
                  '&:hover': { backgroundColor: C.errorHover }
                }}
              >
                STOP SCAN
                <span className="pulsing-dot" />
              </Button>
            )}

            {/* OPTIONS BUTTON */}
            <Button
              variant="contained"
              onClick={() => setIsOptionsModalOpen(true)}
              startIcon={<SettingsIcon sx={{ fontSize: 15 }} />}
              sx={{
                flex: 0.9,
                backgroundColor: C.surface,
                color: C.textPrimary,
                border: `1px solid ${C.borderStrong}`,
                fontWeight: 700,
                fontSize: '0.78rem',
                py: 0.6,
                px: 1,
                whiteSpace: 'nowrap',
                minWidth: 0,
                '&:hover': { backgroundColor: C.surfaceHover }
              }}
            >
              OPTIONS
            </Button>
          </>
        )}
      </Box>

      {/* Calculating Progress Bar */}
      {isCalculating && progress && (
        <Box
          sx={{
            mb: 2,
            p: 1.5,
            backgroundColor: C.cardBg,
            border: `1px solid ${C.border}`,
            borderRadius: '4px'
          }}
        >
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.75 }}>
            <Typography
              variant="caption"
              sx={{ color: C.primary, fontWeight: 800, fontFamily: '"Roboto Mono", monospace', fontSize: '0.8rem' }}
            >
              GEN. {progress.currentGeneration} OF {progress.totalGenerations}
            </Typography>
            <Typography
              variant="caption"
              sx={{ color: C.textPrimary, fontWeight: 800, fontFamily: '"Roboto Mono", monospace', fontSize: '0.8rem' }}
            >
              {(() => {
                const p = Math.min(100, Math.max(0, progress.progressPercent || 0));
                // Show fractional precision for tiny values so large searches don't look
                // frozen at "0%" while they are actually making progress.
                if (p > 0 && p < 1) return p.toFixed(2);
                if (p < 10) return p.toFixed(1);
                return Math.round(p);
              })()}%
            </Typography>
          </Box>

          <LinearProgress
            variant="determinate"
            value={Math.min(100, Math.max(0, progress.progressPercent))}
            sx={{
              height: 6,
              borderRadius: 3,
              backgroundColor: C.surface,
              '& .MuiLinearProgress-bar': {
                backgroundColor: C.primary,
                borderRadius: 3
              }
            }}
          />

          <Box sx={{ mt: 0.75, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <Typography variant="caption" sx={{ color: C.primary, fontSize: '0.74rem', fontFamily: 'monospace', fontWeight: 700 }}>
              Found: {results.length} plans
            </Typography>
            {progress.estimatedTimeRemainingSeconds !== null && progress.estimatedTimeRemainingSeconds !== undefined && (
              <Typography
                variant="caption"
                sx={{ color: C.warning, fontSize: '0.74rem', fontFamily: 'monospace', fontWeight: 700 }}
              >
                EST:{' '}
                {(() => {
                  const total = Math.round(progress.estimatedTimeRemainingSeconds);
                  if (total <= 0) return 'calculating...';
                  const m = Math.floor(total / 60);
                  const s = total % 60;
                  if (m >= 60) {
                    const h = Math.floor(m / 60);
                    const rm = m % 60;
                    return `${h}h:${rm.toString().padStart(2, '0')}m`;
                  }
                  return `${m.toString().padStart(2, '0')}m:${s.toString().padStart(2, '0')}s`;
                })()}
              </Typography>
            )}
          </Box>

          {/* What the engine is currently doing + concrete combination counts */}
          <Box sx={{ mt: 0.75, display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 1 }}>
            <Typography
              variant="caption"
              sx={{ color: C.textSecondary, fontSize: '0.72rem', fontFamily: 'monospace', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
            >
              {progress.stage || 'Working…'}
            </Typography>
            {progress.totalCombinations > 0 && (
              <Typography
                variant="caption"
                sx={{ color: C.textMuted, fontSize: '0.72rem', fontFamily: 'monospace', whiteSpace: 'nowrap' }}
              >
                {progress.processedCombinations.toLocaleString()} / {progress.totalCombinations.toLocaleString()}
              </Typography>
            )}
          </Box>
        </Box>
      )}

      {/* Tabs: CURRENT | SAVED */}
      <Box sx={{ borderBottom: `1px solid ${C.border}`, mb: 1.5 }}>
        <Tabs
          value={activeSubTab}
          onChange={(_, val) => setActiveSubTab(val)}
          textColor="inherit"
          sx={{ minHeight: 36, '& .MuiTabs-indicator': { backgroundColor: C.primary, height: 2 } }}
        >
          <Tab
            value="current"
            label="CURRENT"
            sx={{
              minHeight: 36,
              py: 0.5,
              px: 2,
              fontSize: '0.8rem',
              fontWeight: 700,
              color: activeSubTab === 'current' ? C.primary : C.textMuted
            }}
          />
          <Tab
            value="saved"
            label="SAVED"
            sx={{
              minHeight: 36,
              py: 0.5,
              px: 2,
              fontSize: '0.8rem',
              fontWeight: 700,
              color: activeSubTab === 'saved' ? C.primary : C.textMuted
            }}
          />
        </Tabs>
      </Box>

      {/* TAB 1: CURRENT (Unified Single Input with Synchronized Live Badges) */}
      {activeSubTab === 'current' && (
        <Box>
          <Paper
            variant="outlined"
            sx={{
              backgroundColor: C.inputBg,
              borderColor: C.border,
              borderRadius: '2px',
              p: '10px 14px',
              minHeight: 280,
              maxHeight: 440,
              overflowY: 'auto'
            }}
          >
            {/* Header: # Symbol on left, SAMPLE link on right */}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 1 }}>
              <Typography
                variant="caption"
                sx={{
                  color: C.textMuted,
                  fontFamily: '"Roboto Mono", monospace',
                  fontSize: '0.8rem',
                  fontWeight: 600,
                  userSelect: 'none'
                }}
              >
                #
              </Typography>

              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Button
                  size="small"
                  onClick={(e) => setClearAnchorEl(e.currentTarget)}
                  disabled={isCalculating || geneInputText.trim().length === 0}
                  sx={{
                    fontSize: '0.68rem',
                    color: C.textFaint,
                    p: 0,
                    minWidth: 'auto',
                    '&:hover': { color: C.error },
                    '&:disabled': { color: C.border }
                  }}
                >
                  CLEAR
                </Button>
                <Button
                  size="small"
                  onClick={handleLoadSample}
                  sx={{ fontSize: '0.68rem', color: C.textFaint, p: 0, minWidth: 'auto', '&:hover': { color: C.primary } }}
                >
                  SAMPLE
                </Button>
              </Box>

              <Popover
                open={Boolean(clearAnchorEl)}
                anchorEl={clearAnchorEl}
                onClose={() => setClearAnchorEl(null)}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
                transformOrigin={{ vertical: 'top', horizontal: 'right' }}
                slotProps={{
                  paper: {
                    sx: {
                      backgroundColor: C.elevatedBg,
                      border: `1px solid ${C.borderStrong}`,
                      borderRadius: '4px',
                      p: 1.5,
                      mt: 0.5,
                      maxWidth: 240
                    }
                  }
                }}
              >
                <Typography
                  sx={{ color: C.textPrimary, fontFamily: '"Roboto Mono", monospace', fontSize: '0.78rem', mb: 1.25 }}
                >
                  Clear all gene input? This removes every plant you've entered.
                </Typography>
                <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
                  <Button
                    size="small"
                    onClick={() => setClearAnchorEl(null)}
                    sx={{
                      color: C.textMuted,
                      fontFamily: 'monospace',
                      fontSize: '0.72rem',
                      fontWeight: 700,
                      '&:hover': { color: C.textPrimary }
                    }}
                  >
                    CANCEL
                  </Button>
                  <Button
                    size="small"
                    variant="contained"
                    onClick={handleClearConfirmed}
                    sx={{
                      backgroundColor: C.error,
                      color: '#FFFFFF',
                      fontFamily: 'monospace',
                      fontSize: '0.72rem',
                      fontWeight: 800,
                      boxShadow: 'none',
                      '&:hover': { backgroundColor: C.errorHover, boxShadow: 'none' }
                    }}
                  >
                    CLEAR
                  </Button>
                </Box>
              </Popover>
            </Box>

            {/* Main Interactive Grid: Line Numbers + Single Textarea + Live Badges */}
            <Box sx={{ position: 'relative', display: 'flex', alignItems: 'flex-start' }}>
              {/* Column 1: Line Numbers & Row Highlight Backgrounds */}
              <Box sx={{ display: 'flex', flexDirection: 'column', width: '100%' }}>
                {Array.from({ length: displayRowCount }).map((_, rIdx) => {
                  const lineVal = lines[rIdx] || '';
                  const chars = lineVal.toUpperCase().slice(0, 6).split('');
                  const isUsedInPlan = usedCloneIndices.has(rIdx);
                  const isActiveCursor = activeLineIdx === rIdx;

                  return (
                    <Box
                      key={rIdx}
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        height: ROW_HEIGHT,
                        px: '4px',
                        backgroundColor: isUsedInPlan
                          ? (C.isDark ? 'rgba(255, 255, 255, 0.08)' : 'rgba(2, 132, 199, 0.08)')
                          : isActiveCursor
                          ? 'rgba(255, 152, 0, 0.03)'
                          : 'transparent',
                        borderBottom: isUsedInPlan
                          ? `1.5px solid ${C.warning}`
                          : isActiveCursor
                          ? '1px solid rgba(255, 152, 0, 0.3)'
                          : '1px solid transparent',
                        borderRadius: isUsedInPlan ? '2px' : 0,
                        transition: 'all 0.15s ease'
                      }}
                    >
                      {/* Left: Row Index */}
                      <Typography
                        variant="caption"
                        sx={{
                          color: isUsedInPlan ? C.warning : C.textMuted,
                          fontFamily: '"Roboto Mono", monospace',
                          fontSize: '0.82rem',
                          minWidth: 16,
                          fontWeight: isUsedInPlan ? 800 : 500,
                          userSelect: 'none'
                        }}
                      >
                        {rIdx + 1}
                      </Typography>

                      {/* Spacer for the Textarea that floats over the middle */}
                      <Box sx={{ width: 85, flexShrink: 0 }} />

                      {/* Right: Live Circular Badges with Connectors */}
                      <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: '1px' }}>
                        {Array.from({ length: 6 }).map((_, slotIdx) => {
                          const char = chars[slotIdx];
                          const hasGene = !!char;
                          const isGreen = hasGene ? (GREEN_GENES as readonly string[]).includes(char) : false;
                          const bgColor = !hasGene ? C.inputBg : isGreen ? '#598518' : '#94382A';

                          return (
                            <React.Fragment key={slotIdx}>
                              <Box
                                sx={{
                                  width: 22,
                                  height: 22,
                                  borderRadius: '50%',
                                  display: 'flex',
                                  alignItems: 'center',
                                  justifyContent: 'center',
                                  backgroundColor: bgColor,
                                  color: hasGene ? '#FFFFFF' : 'transparent',
                                  fontWeight: 800,
                                  fontSize: '0.8rem',
                                  fontFamily: '"Roboto Mono", "Consolas", monospace',
                                  userSelect: 'none',
                                  lineHeight: 1,
                                  border: !hasGene ? `1px solid ${C.border}` : 'none'
                                }}
                              >
                                {char || ''}
                              </Box>

                              {slotIdx < 5 && (
                                <Typography
                                  component="span"
                                  sx={{
                                    color: C.textFaint,
                                    fontSize: '0.75rem',
                                    fontFamily: 'monospace',
                                    userSelect: 'none',
                                    px: '1px'
                                  }}
                                >
                                  -
                                </Typography>
                              )}
                            </React.Fragment>
                          );
                        })}
                      </Box>
                    </Box>
                  );
                })}
              </Box>

              {/* Single Unified Textarea (Floats seamlessly over the text column) */}
              <textarea
                ref={textareaRef}
                value={geneInputText}
                disabled={isCalculating}
                onChange={(e) => handleTextChange(e.target.value)}
                onKeyUp={handleCursorMove}
                onClick={handleCursorMove}
                onSelect={handleCursorMove}
                spellCheck={false}
                placeholder="GGYYHH&#10;YGGYHH&#10;HHGGYY"
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 24,
                  width: 85,
                  height: displayRowCount * ROW_HEIGHT,
                  backgroundColor: 'transparent',
                  border: 'none',
                  outline: 'none',
                  color: C.textPrimary,
                  fontFamily: '"Roboto Mono", "Consolas", monospace',
                  fontWeight: 700,
                  fontSize: '0.88rem',
                  lineHeight: `${ROW_HEIGHT}px`,
                  letterSpacing: '1px',
                  resize: 'none',
                  overflow: 'hidden',
                  whiteSpace: 'pre',
                  padding: 0,
                  margin: 0,
                  caretColor: C.primary,
                  opacity: isCalculating ? 0.6 : 1,
                  cursor: isCalculating ? 'not-allowed' : 'text'
                }}
              />
            </Box>
          </Paper>
        </Box>
      )}

      {/* TAB 2: SAVED (Screenshot) */}
      {activeSubTab === 'saved' && (
        <Box>
          {savedGeneSets.length === 0 ? (
            <Box sx={{ py: 4, textAlign: 'center' }}>
              <Typography variant="body2" sx={{ color: C.textMuted, fontFamily: 'monospace' }}>
                No saved plant sets yet.
              </Typography>
            </Box>
          ) : (
            <Paper
              variant="outlined"
              sx={{
                backgroundColor: C.inputBg,
                borderColor: C.border,
                borderRadius: '2px',
                p: '8px',
                maxHeight: 440,
                overflowY: 'auto'
              }}
            >
              <Box sx={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                {savedGeneSets.map((set) => {
                  const count = set.genes.split('\n').filter(Boolean).length;
                  const plant = set.selectedPlantType || 'mixed-berry';

                  return (
                    <Box
                      key={set.timestamp}
                      onClick={() => {
                        loadSavedGeneSet(set);
                        setActiveSubTab('current');
                      }}
                      sx={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        py: '6px',
                        px: '12px',
                        backgroundColor: C.surface,
                        border: `1px solid ${C.border}`,
                        borderRadius: '4px',
                        cursor: 'pointer',
                        transition: 'background-color 0.15s ease, border-color 0.15s ease',
                        '&:hover': {
                          backgroundColor: C.surfaceHover,
                          borderColor: C.borderStrong
                        }
                      }}
                    >
                      {/* Left: Plant Icon */}
                      <Box
                        component="img"
                        src={`./img/items/${plant}.webp`}
                        alt={plant}
                        sx={{ width: 26, height: 26, objectFit: 'contain', flexShrink: 0 }}
                      />

                      {/* Center-Left: Genes Count */}
                      <Typography
                        variant="caption"
                        sx={{
                          fontWeight: 700,
                          color: C.textPrimary,
                          fontFamily: '"Roboto Mono", monospace',
                          fontSize: '0.82rem',
                          minWidth: 80,
                          textAlign: 'left'
                        }}
                      >
                        Genes: {count}
                      </Typography>

                      {/* Center-Right: Date & Time */}
                      <Typography
                        variant="caption"
                        sx={{
                          color: C.textSecondary,
                          fontFamily: '"Roboto Mono", monospace',
                          fontSize: '0.78rem',
                          textAlign: 'right',
                          flex: 1,
                          pr: 2
                        }}
                      >
                        {new Date(set.timestamp).toLocaleString('en-US', {
                          month: 'numeric',
                          day: 'numeric',
                          year: 'numeric',
                          hour: 'numeric',
                          minute: '2-digit',
                          second: '2-digit',
                          hour12: true
                        })}
                      </Typography>

                      {/* Right: Delete (X) */}
                      <IconButton
                        size="small"
                        onClick={(e) => {
                          e.stopPropagation();
                          deleteSavedGeneSet(set.timestamp);
                        }}
                        sx={{
                          color: C.textMuted,
                          p: 0.25,
                          '&:hover': { color: C.error }
                        }}
                      >
                        <CloseIcon sx={{ fontSize: 18 }} />
                      </IconButton>
                    </Box>
                  );
                })}
              </Box>
            </Paper>
          )}
        </Box>
      )}
    </Box>
  );
};
