import React, { useState, useMemo, useRef, useEffect, useLayoutEffect, useCallback } from 'react';
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
import { AudioService } from '../../../services/audioService.ts';

const ROW_HEIGHT = 28; // Exact pixel height per line for perfect vertical alignment

/**
 * Sanitize to gene letters and wrap each line to 6 genes — overflow cascades onto
 * new lines instead of being dropped, so typing/pasting past 6 flows to the next
 * plant. Empty lines are preserved.
 */
const sanitizeAndReflow = (str: string): string => {
  const lines = str.toUpperCase().split('\n').map((l) => l.replace(/[^GHYWX]/g, ''));
  const out: string[] = [];
  for (const line of lines) {
    if (line.length === 0) {
      out.push('');
      continue;
    }
    for (let i = 0; i < line.length; i += 6) out.push(line.slice(i, i + 6));
  }
  return out.join('\n');
};

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

  const { isCalculating, highlightedGroup, selectedGroup, options } = useCalculation();
  const { isScannerActive, startScanner } = useScanner();

  const [activeTab, setActiveTab] = useState<'current' | 'saved'>('current');
  const [clearAnchorEl, setClearAnchorEl] = useState<HTMLElement | null>(null);
  const [activeLineIdx, setActiveLineIdx] = useState<number | null>(null);

  // Local text state for instantaneous zero-latency typing
  const [localText, setLocalText] = useState(geneInputText);
  const textareaRef = useRef<HTMLTextAreaElement | null>(null);
  const debounceTimerRef = useRef<any>(null);
  // Caret position to restore after a change reflows the text (so the cursor
  // follows the typed gene onto the next line instead of jumping to the end).
  const pendingCaretRef = useRef<number | null>(null);

  useLayoutEffect(() => {
    if (pendingCaretRef.current !== null && textareaRef.current) {
      const pos = pendingCaretRef.current;
      textareaRef.current.selectionStart = pos;
      textareaRef.current.selectionEnd = pos;
      pendingCaretRef.current = null;
    }
  }, [localText]);

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

  const handleTextChange = (newText: string, caretPos?: number) => {
    // "Wrong key" feedback: the user typed a character that isn't a gene letter
    // (G/Y/H/W/X) or whitespace. Only fire when adding text (not on delete/paste-shrink).
    const typedInvalidLetter =
      newText.length > localText.length && /[^GYHWX\s]/i.test(newText);
    if (typedInvalidLetter) {
      AudioService.playWrongKey(options.sounds);
    }

    let cleanedText = sanitizeAndReflow(newText);

    // Map the caret through the same transform so it lands right after the gene
    // the user just typed (even after invalid chars are stripped / lines wrap).
    let newCaret = caretPos != null ? sanitizeAndReflow(newText.slice(0, caretPos)).length : cleanedText.length;

    // Auto-advance: completing a 6-gene line at the end drops the cursor onto a
    // fresh next line so you can type the next plant without pressing Enter.
    const isTyping = newText.length > localText.length;
    const appendedAtEnd = caretPos != null && caretPos === newText.length;
    const lastLine = cleanedText.split('\n').pop() || '';
    if (isTyping && appendedAtEnd && lastLine.length === 6) {
      cleanedText += '\n';
      newCaret = cleanedText.length;
    }

    pendingCaretRef.current = newCaret;

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
        backgroundColor: 'var(--gl-panel-bg)',
        borderColor: 'var(--gl-surface)',
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
              color: 'var(--gl-text-primary)',
              letterSpacing: '0.5px'
            }}
          >
            GENE INPUTS
          </Typography>
          <Typography
            variant="caption"
            sx={{
              backgroundColor: 'rgba(0, 229, 255, 0.12)',
              color: 'var(--gl-primary)',
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
              borderColor: 'var(--gl-surface-hover)',
              color: 'var(--gl-text-secondary)',
              minWidth: 'auto'
            }}
          >
            Scan
          </Button>
        )}
      </Box>

      {/* Tabs: CURRENT | SAVED */}
      <Box sx={{ borderBottom: '1px solid var(--gl-surface)', mb: 1 }}>
        <Tabs
          value={activeTab}
          onChange={(_, val) => setActiveTab(val)}
          sx={{ minHeight: 28, '& .MuiTabs-indicator': { backgroundColor: 'var(--gl-primary)', height: 2 } }}
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
              color: activeTab === 'current' ? 'var(--gl-primary)' : 'var(--gl-text-muted)'
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
              color: activeTab === 'saved' ? 'var(--gl-primary)' : 'var(--gl-text-muted)'
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
                color: 'var(--gl-text-muted)',
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
                  color: 'var(--gl-text-muted)',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: 'var(--gl-error)' },
                  '&:disabled': { color: 'var(--gl-surface-hover)' }
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
                  color: 'var(--gl-text-muted)',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: 'var(--gl-primary)' }
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
                  color: 'var(--gl-text-muted)',
                  p: 0,
                  minWidth: 'auto',
                  fontWeight: 700,
                  '&:hover': { color: 'var(--gl-success)' },
                  '&:disabled': { color: 'var(--gl-surface-hover)' }
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
                    backgroundColor: 'var(--gl-card-hover-bg)',
                    border: '1px solid var(--gl-surface-hover)',
                    borderRadius: '4px',
                    p: 1.5,
                    mt: 0.5,
                    maxWidth: 220
                  }
                }
              }}
            >
              <Typography sx={{ color: 'var(--gl-text-primary)', fontSize: '0.75rem', mb: 1.25 }}>
                Clear all manual gene inputs?
              </Typography>
              <Box sx={{ display: 'flex', justifyContent: 'flex-end', gap: 1 }}>
                <Button size="small" onClick={() => setClearAnchorEl(null)} sx={{ color: 'var(--gl-text-muted)', fontSize: '0.7rem' }}>
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
              backgroundColor: 'var(--gl-app-bg)',
              borderColor: 'var(--gl-border-subtle)',
              borderRadius: '4px',
              p: '6px 8px',
              overflowY: 'auto',
              overflowX: 'hidden',
              position: 'relative',
              '&::-webkit-scrollbar': { width: 5 },
              '&::-webkit-scrollbar-thumb': { backgroundColor: 'var(--gl-surface-hover)', borderRadius: 3 }
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
                          ? '1.5px solid var(--gl-warning)'
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
                            color: isUsedInPlan ? 'var(--gl-warning)' : 'var(--gl-text-faint)',
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
                              color: 'var(--gl-warning)',
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
                          const bgColor = !hasGene ? 'var(--gl-panel-header-bg)' : isGreen ? '#4A7C17' : '#8A2E22';

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
                                border: !hasGene ? '1px solid var(--gl-surface)' : 'none'
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
                aria-label="Clone genetics, one six-gene sequence per line"
                ref={textareaRef}
                value={localText}
                disabled={isCalculating}
                onChange={(e) => handleTextChange(e.target.value, e.target.selectionStart ?? undefined)}
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
                  color: 'var(--gl-text-primary)',
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
                  caretColor: 'var(--gl-primary)',
                  opacity: isCalculating ? 0.6 : 1,
                  cursor: isCalculating ? 'not-allowed' : 'text'
                }}
              />
            </Box>
          </Paper>

          <Typography variant="caption" sx={{ color: 'var(--gl-text-faint)', fontSize: '0.65rem', mt: 0.75, display: 'block', textAlign: 'center' }}>
            Type or paste 6-gene lines. Auto-saved on calculate.
          </Typography>
        </Box>
      )}

      {/* TAB 2: SAVED SETS HISTORY */}
      {activeTab === 'saved' && (
        <Box sx={{ flex: 1, overflowY: 'auto' }}>
          {savedGeneSets.length === 0 ? (
            <Box sx={{ py: 4, textAlign: 'center' }}>
              <Typography variant="caption" sx={{ color: 'var(--gl-text-muted)', fontFamily: 'monospace' }}>
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
                      backgroundColor: 'var(--gl-card-bg)',
                      border: '1px solid var(--gl-surface)',
                      borderRadius: '4px',
                      cursor: 'pointer',
                      transition: 'all 0.15s ease',
                      '&:hover': {
                        backgroundColor: 'var(--gl-border-subtle)',
                        borderColor: 'var(--gl-primary)'
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
                          color: 'var(--gl-text-primary)',
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
                        color: 'var(--gl-text-muted)',
                        fontSize: '0.68rem',
                        fontFamily: 'monospace'
                      }}
                    >
                      {new Date(set.timestamp).toLocaleDateString()} {new Date(set.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </Typography>

                    {/* Right: Delete */}
                    <IconButton
                      aria-label={`Delete saved gene set from ${new Date(set.timestamp).toLocaleString()}`}
                      size="small"
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteSavedGeneSet(set.timestamp);
                      }}
                      sx={{
                        color: 'var(--gl-text-muted)',
                        p: 0.2,
                        '&:hover': { color: 'var(--gl-error)' }
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
