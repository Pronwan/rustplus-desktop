import React, { useState, useMemo, useRef, useEffect, useCallback } from 'react';
import {
  Paper,
  Box,
  Typography,
  Button,
  Tabs,
  Tab,
  IconButton,
  Popover,
  Tooltip
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import SaveIcon from '@mui/icons-material/Save';
import { useWorkspace } from '../../../context/WorkspaceContext.tsx';
import { useCalculation } from '../../../context/CalculationContext.tsx';
import { useScanner } from '../../../context/ScannerContext.tsx';
import { GREEN_GENES } from '../../../domain/genetics/Gene.ts';

const ROW_HEIGHT = 28; // Exact pixel height per line for perfect vertical alignment

export const CloneBank: React.FC = () => {
  const {
    selectedPlant,
    geneInputText,
    setGeneInputText,
    sourceSaplings,
    clearGeneInput,
    loadSampleGenes,
    savedGeneSets,
    saveCurrentGeneSet,
    loadSavedGeneSet,
    deleteSavedGeneSet
  } = useWorkspace();

  const { isCalculating, highlightedGroup, selectedGroup } = useCalculation();
  const { isScannerActive, startScanner } = useScanner();

  const [activeTab, setActiveTab] = useState<'current' | 'saved'>('current');
  const [clearAnchorEl, setClearAnchorEl] = useState<HTMLElement | null>(null);
  const [activeLineIdx, setActiveLineIdx] = useState<number | null>(null);

  // Local text state for instantaneous zero-latency typing
  const [localText, setLocalText] = useState(geneInputText);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const debounceTimerRef = useRef<any>(null);

  // Sync localText when external geneInputText changes (e.g. sample loaded, saved set loaded, scanner added)
  useEffect(() => {
    setLocalText(geneInputText);
  }, [geneInputText]);

  // Compute used donor clone indices from the active or hovered route
  const usedCloneIndices = useMemo(() => {
    const indices = new Set<number>();
    const activeRouteGroup = highlightedGroup || selectedGroup;
    if (activeRouteGroup && activeRouteGroup.mapList.length > 0) {
      const bestMap = activeRouteGroup.mapList[0];
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
  }, [highlightedGroup, selectedGroup]);

  // Split text into lines
  const lines = useMemo(() => {
    return localText.split('\n');
  }, [localText]);

  const handleTextChange = (newText: string) => {
    // Sanitize: uppercase only G, Y, H, W, X and newlines
    const linesArr = newText.toUpperCase().split('\n');
    const cleanedLines = linesArr.map((line) => line.replace(/[^GHYWX]/g, '').slice(0, 6));
    const cleanedText = cleanedLines.join('\n');
    
    // Immediate local update (0ms typing latency)
    setLocalText(cleanedText);

    // Sync to workspace context
    if (debounceTimerRef.current) clearTimeout(debounceTimerRef.current);
    debounceTimerRef.current = setTimeout(() => {
      setGeneInputText(cleanedText);
    }, 40);
  };

  const handleCursorMove = () => {
    if (!textareaRef.current) return;
    const pos = textareaRef.current.selectionStart;
    const textBefore = textareaRef.current.value.slice(0, pos);
    const lineNum = textBefore.split('\n').length - 1;
    setActiveLineIdx(lineNum);
  };

  const handleClearConfirmed = () => {
    clearGeneInput();
    setLocalText('');
    setClearAnchorEl(null);
  };

  // Always show at least 10 rows or lines + 2 extra rows for smooth entry
  const displayRowCount = Math.max(lines.length + 1, 10);

  return (
    <Paper
      variant="outlined"
      sx={{
        backgroundColor: '#121212',
        borderColor: '#242424',
        borderRadius: '6px',
        p: 1.5,
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        maxHeight: 'calc(100vh - 100px)',
        minWidth: 285,
        boxSizing: 'border-box'
      }}
    >
      {/* Header: Title & Plant Count Badge */}
      <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
          <Typography
            variant="subtitle1"
            sx={{
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.85rem',
              color: '#FFFFFF',
              letterSpacing: '0.5px'
            }}
          >
            GENE INPUTS
          </Typography>
          <Typography
            variant="caption"
            sx={{
              backgroundColor: 'rgba(0, 229, 255, 0.12)',
              color: '#00E5FF',
              fontWeight: 800,
              px: 0.75,
              py: 0.15,
              borderRadius: '3px',
              border: '1px solid rgba(0, 229, 255, 0.3)',
              fontFamily: 'monospace',
              fontSize: '0.72rem'
            }}
          >
            {sourceSaplings.length} plant{sourceSaplings.length === 1 ? '' : 's'}
          </Typography>
        </Box>

        {!isScannerActive && (
          <Button
            variant="outlined"
            size="small"
            onClick={() => startScanner()}
            startIcon={<AutoAwesomeIcon sx={{ fontSize: 13 }} />}
            sx={{
              py: 0.15,
              px: 0.75,
              fontSize: '0.68rem',
              fontWeight: 700,
              borderColor: '#333',
              color: '#AAA',
              minWidth: 'auto'
            }}
          >
            Scan
          </Button>
        )}
      </Box>

      {/* Tabs: CURRENT | SAVED */}
      <Box sx={{ borderBottom: '1px solid #242424', mb: 1 }}>
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          sx={{ minHeight: 28, '& .MuiTabs-indicator': { backgroundColor: '#00E5FF', height: 2 } }}
        >
          <Tab
            value="current"
            label="MANUAL INPUT"
            sx={{
              minHeight: 28,
              py: 0.15,
              px: 1,
              fontSize: '0.72rem',
              fontWeight: 800,
              color: activeTab === 'current' ? '#00E5FF' : '#888888'
            }}
          />
          <Tab
            value="saved"
            label={`SAVED (${savedGeneSets.length})`}
            sx={{
              minHeight: 28,
              py: 0.15,
              px: 1,
              fontSize: '0.72rem',
              fontWeight: 800,
              color: activeTab === 'saved' ? '#00E5FF' : '#888888'
            }}
          />
        </Tabs>
      </Box>

      {/* TAB 1: CURRENT MANUAL INPUT */}
      {activeTab === 'current' && (
        <Box sx={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
          {/* Action Row: # symbol on left, CLEAR / SAMPLE / SAVE buttons on right */}
          <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 0.75, px: 0.25 }}>
            <Typography
              variant="caption"
              sx={{
                color: '#666666',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.72rem',
                fontWeight: 700,
                userSelect: 'none'
              }}
            >
              #
            </Typography>

            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
              <Button
                size="small"
                onClick={(e) => setClearAnchorEl(e.currentTarget)}
                disabled={isCalculating || localText.trim().length === 0}
                sx={{
                  fontSize: '0.65rem',
                  color: '#666666',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: '#E53935' },
                  '&:disabled': { color: '#333333' }
                }}
              >
                CLEAR
              </Button>

              <Button
                size="small"
                onClick={loadSampleGenes}
                disabled={isCalculating}
                sx={{
                  fontSize: '0.65rem',
                  color: '#666666',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: '#00E5FF' }
                }}
              >
                SAMPLE
              </Button>

              <Button
                size="small"
                onClick={saveCurrentGeneSet}
                disabled={sourceSaplings.length === 0}
                sx={{
                  fontSize: '0.65rem',
                  color: '#666666',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: '#4CAF50' },
                  '&:disabled': { color: '#333333' }
                }}
              >
                SAVE
              </Button>
            </Box>

            {/* Clear Confirmation Popover */}
            <Popover
              open={Boolean(clearAnchorEl)}
              anchorEl={clearAnchorEl}
              onClose={() => setClearAnchorEl(null)}
              anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
              transformOrigin={{ vertical: 'top', horizontal: 'right' }}
              slotProps={{
                paper: {
                  sx: {
                    backgroundColor: '#1C1C1C',
                    border: '1px solid #333333',
                    borderRadius: '4px',
                    p: 1.5,
                    mt: 0.5,
                    maxWidth: 220
                  }
                }
              }}
            >
              <Typography sx={{ color: '#E0E0E0', fontSize: '0.75rem', mb: 1.25 }}>
                Clear all manual gene inputs?
              </Typography>
              <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
                <Button size="small" onClick={() => setClearAnchorEl(null)} sx={{ color: '#888', fontSize: '0.7rem' }}>
                  Cancel
                </Button>
                <Button
                  size="small"
                  variant="contained"
                  color="error"
                  onClick={handleClearConfirmed}
                  sx={{ fontSize: '0.7rem', fontWeight: 800 }}
                >
                  Clear
                </Button>
              </Box>
            </Popover>
          </Box>

          {/* Interactive Multi-Line Editor Container */}
          <Paper
            variant="outlined"
            sx={{
              flex: 1,
              backgroundColor: '#0E0E0E',
              borderColor: '#202020',
              borderRadius: '4px',
              p: '6px 8px',
              overflowY: 'auto',
              overflowX: 'hidden',
              position: 'relative',
              '&::-webkit-scrollbar': { width: 5 },
              '&::-webkit-scrollbar-thumb': { backgroundColor: '#333', borderRadius: 3 }
            }}
          >
            <Box sx={{ position: 'relative', display: 'flex', alignItems: 'flex-start', minHeight: displayRowCount * ROW_HEIGHT }}>
              {/* Row Visuals: Line Index + Gene Badges */}
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
                        px: '2px',
                        backgroundColor: isUsedInPlan
                          ? 'rgba(255, 152, 0, 0.12)'
                          : isActiveCursor
                          ? 'rgba(0, 229, 255, 0.04)'
                          : 'transparent',
                        borderBottom: isUsedInPlan
                          ? '1.5px solid #FF9800'
                          : isActiveCursor
                          ? '1px solid rgba(0, 229, 255, 0.25)'
                          : '1px solid transparent',
                        borderRadius: isUsedInPlan ? '3px' : 0,
                        transition: 'all 0.1s ease'
                      }}
                    >
                      {/* Left: Row Index & Used Badge */}
                      <Box sx={{ display: 'flex', alignItems: 'center', minWidth: 32, gap: 0.25 }}>
                        <Typography
                          variant="caption"
                          sx={{
                            color: isUsedInPlan ? '#FFA726' : '#555555',
                            fontFamily: '"Roboto Mono", monospace',
                            fontSize: '0.74rem',
                            fontWeight: isUsedInPlan ? 800 : 500,
                            userSelect: 'none'
                          }}
                        >
                          #{rIdx + 1}
                        </Typography>

                        {isUsedInPlan && (
                          <Typography
                            variant="caption"
                            sx={{
                              color: '#FF9800',
                              fontSize: '0.55rem',
                              fontWeight: 800,
                              fontFamily: 'monospace'
                            }}
                          >
                            *
                          </Typography>
                        )}
                      </Box>

                      {/* Spacer for the Textarea that floats over the middle */}
                      <Box sx={{ width: 78, flexShrink: 0 }} />

                      {/* Right: Live Circular Badges (17px with 2px gap) */}
                      <Box sx={{ display: 'inline-flex', alignItems: 'center', gap: '2px', flexShrink: 0, pr: 0.5 }}>
                        {Array.from({ length: 6 }).map((_, slotIdx) => {
                          const char = chars[slotIdx];
                          const hasGene = !!char;
                          const isGreen = hasGene ? (GREEN_GENES as readonly string[]).includes(char) : false;
                          const bgColor = !hasGene ? '#181818' : isGreen ? '#4A7C17' : '#8A2E22';

                          return (
                            <Box
                              key={slotIdx}
                              sx={{
                                width: 17,
                                height: 17,
                                borderRadius: '50%',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center',
                                backgroundColor: bgColor,
                                color: hasGene ? '#FFFFFF' : 'transparent',
                                fontWeight: 800,
                                fontSize: '0.7rem',
                                fontFamily: '"Roboto Mono", "Consolas", monospace',
                                userSelect: 'none',
                                lineHeight: 1,
                                border: !hasGene ? '1px solid #222222' : 'none'
                              }}
                            >
                              {char || ''}
                            </Box>
                          );
                        })}
                      </Box>
                    </Box>
                  );
                })}
              </Box>

              {/* Single Unified Floating Textarea */}
              <textarea
                ref={textareaRef}
                value={localText}
                disabled={isCalculating}
                onChange={(e) => handleTextChange(e.target.value)}
                onKeyUp={handleCursorMove}
                onClick={handleCursorMove}
                onSelect={handleCursorMove}
                spellCheck={false}
                placeholder=""
                style={{
                  position: 'absolute',
                  top: 0,
                  left: 36,
                  width: 78,
                  height: displayRowCount * ROW_HEIGHT,
                  backgroundColor: 'transparent',
                  border: 'none',
                  outline: 'none',
                  color: '#FFFFFF',
                  fontFamily: '"Roboto Mono", "Consolas", monospace',
                  fontWeight: 700,
                  fontSize: '0.82rem',
                  lineHeight: `${ROW_HEIGHT}px`,
                  letterSpacing: '1.2px',
                  resize: 'none',
                  overflow: 'hidden',
                  whiteSpace: 'pre',
                  padding: 0,
                  margin: 0,
                  caretColor: '#00E5FF',
                  opacity: isCalculating ? 0.6 : 1,
                  cursor: isCalculating ? 'not-allowed' : 'text'
                }}
              />
            </Box>
          </Paper>

          <Typography variant="caption" sx={{ color: '#555', fontSize: '0.65rem', mt: 0.75, display: 'block', textAlign: 'center' }}>
            Type or paste 6-gene lines. Auto-saved on calculate.
          </Typography>
        </Box>
      )}

      {/* TAB 2: SAVED SETS HISTORY */}
      {activeTab === 'saved' && (
        <Box sx={{ flex: 1, overflowY: 'auto' }}>
          {savedGeneSets.length === 0 ? (
            <Box sx={{ py: 4, textAlign: 'center' }}>
              <Typography variant="caption" sx={{ color: '#666666', fontFamily: 'monospace' }}>
                No saved plant sets yet.
              </Typography>
            </Box>
          ) : (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75 }}>
              {savedGeneSets.map((set) => {
                const count = set.genes.split('\n').filter(Boolean).length;
                const plant = set.selectedPlantType || 'mixed-berry';

                return (
                  <Box
                    key={set.timestamp}
                    onClick={() => {
                      loadSavedGeneSet(set);
                      setActiveTab('current');
                    }}
                    sx={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      py: '5px',
                      px: '8px',
                      backgroundColor: '#161616',
                      border: '1px solid #242424',
                      borderRadius: '4px',
                      cursor: 'pointer',
                      transition: 'all 0.15s ease',
                      '&:hover': {
                        backgroundColor: '#202020',
                        borderColor: '#00E5FF'
                      }
                    }}
                  >
                    {/* Left: Plant Icon & Count */}
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                      <Box
                        component="img"
                        src={`./img/items/${plant}.webp`}
                        alt={plant}
                        sx={{ width: 20, height: 20, objectFit: 'contain', flexShrink: 0 }}
                        onError={(e) => {
                          (e.target as HTMLElement).style.display = 'none';
                        }}
                      />

                      <Typography
                        variant="caption"
                        sx={{
                          fontWeight: 800,
                          color: '#FFFFFF',
                          fontFamily: 'monospace',
                          fontSize: '0.72rem'
                        }}
                      >
                        {count} plants
                      </Typography>
                    </Box>

                    {/* Center: Timestamp */}
                    <Typography
                      variant="caption"
                      sx={{
                        color: '#777777',
                        fontSize: '0.68rem',
                        fontFamily: 'monospace'
                      }}
                    >
                      {new Date(set.timestamp).toLocaleDateString()} {new Date(set.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </Typography>

                    {/* Right: Delete */}
                    <IconButton
                      size="small"
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteSavedGeneSet(set.timestamp);
                      }}
                      sx={{
                        color: '#666',
                        p: 0.2,
                        '&:hover': { color: '#E53935' }
                      }}
                    >
                      <CloseIcon sx={{ fontSize: 15 }} />
                    </IconButton>
                  </Box>
                );
              })}
            </Box>
          )}
        </Box>
      )}
    </Paper>
  );
};
