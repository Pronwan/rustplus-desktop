import React, { useState, useEffect, useMemo, useRef } from 'react';
import { Box, Typography, Button, Divider } from '@mui/material';
import { useApp } from '../../context/AppContext.tsx';
import { SimulationMapCard } from './SimulationMapCard.tsx';
import { SinglePlanCard, PlanDetailModal } from './PlanDetailModal.tsx';
import { GeneticsMapGroup } from '../../domain/genetics/GeneticsMapGroup.ts';
import { GeneticsMap } from '../../domain/genetics/GeneticsMap.ts';
import { Sapling } from '../../domain/genetics/Sapling.ts';

const PAGE_SIZE = 12;

export const ResultsPanel: React.FC = () => {
  const { results, isCalculating, highlightedGroup, setHighlightedGroup } = useApp();
  const [highlightedMapIndex, setHighlightedMapIndex] = useState(0);

  // Sub-modal for clicking GEN.X in highlighted result
  const [parentModalOpen, setParentModalOpen] = useState(false);
  const [parentSapling, setParentSapling] = useState<Sapling | null>(null);
  const [parentMap, setParentMap] = useState<GeneticsMap | null>(null);

  const topRef = useRef<HTMLDivElement | null>(null);
  const observerRef = useRef<HTMLDivElement | null>(null);

  // Filters
  const [minGs, setMinGs] = useState<string>('');
  const [minYs, setMinYs] = useState<string>('');
  const [minHs, setMinHs] = useState<string>('');

  const [slot1, setSlot1] = useState<string>('');
  const [slot2, setSlot2] = useState<string>('');
  const [slot3, setSlot3] = useState<string>('');
  const [slot4, setSlot4] = useState<string>('');
  const [slot5, setSlot5] = useState<string>('');
  const [slot6, setSlot6] = useState<string>('');

  const [visibleCount, setVisibleCount] = useState<number>(PAGE_SIZE);

  // Filter evaluation
  const filteredResults = useMemo(() => {
    const targetGs = parseInt(minGs, 10);
    const targetYs = parseInt(minYs, 10);
    const targetHs = parseInt(minHs, 10);

    const slotFilters = [
      slot1.trim().toUpperCase(),
      slot2.trim().toUpperCase(),
      slot3.trim().toUpperCase(),
      slot4.trim().toUpperCase(),
      slot5.trim().toUpperCase(),
      slot6.trim().toUpperCase()
    ];

    return results.filter((group) => {
      const s = new Sapling(group.resultSaplingGeneString);

      if (!isNaN(targetGs) && s.numberOfGs() < targetGs) return false;
      if (!isNaN(targetYs) && s.numberOfYs() < targetYs) return false;
      if (!isNaN(targetHs) && s.numberOfHs() < targetHs) return false;

      for (let i = 0; i < 6; i++) {
        const req = slotFilters[i];
        if (req && req !== '?' && req !== '*' && s.genes[i].type !== req) {
          return false;
        }
      }

      return true;
    });
  }, [results, minGs, minYs, minHs, slot1, slot2, slot3, slot4, slot5, slot6]);

  useEffect(() => {
    if (!observerRef.current) return;
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && visibleCount < filteredResults.length) {
          setVisibleCount((c) => Math.min(c + PAGE_SIZE, filteredResults.length));
        }
      },
      { threshold: 0.1, rootMargin: '300px' }
    );

    observer.observe(observerRef.current);
    return () => observer.disconnect();
  }, [filteredResults.length, visibleCount]);

  const visibleResults = filteredResults.slice(0, visibleCount);

  const handleSelectGroup = (group: GeneticsMapGroup) => {
    setHighlightedGroup(group);
    setHighlightedMapIndex(0);
    topRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  // Selecting an alternative plan highlights that specific map at the top as the
  // "Highlighted Result" instead of replacing the plan shown on the source card.
  const handleHighlightMap = (group: GeneticsMapGroup, mapIndex: number) => {
    setHighlightedGroup(group);
    setHighlightedMapIndex(mapIndex);
    topRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  const handleClearHighlight = () => {
    setHighlightedGroup(null);
  };

  const handleOpenParentPlan = (sapling: Sapling, map?: GeneticsMap) => {
    setParentSapling(sapling);
    setParentMap(map || null);
    setParentModalOpen(true);
  };

  const highlightedMap = highlightedGroup ? highlightedGroup.mapList[highlightedMapIndex] || highlightedGroup.mapList[0] : null;
  const genNum = highlightedMap ? Math.max(1, highlightedMap.resultSapling.generationIndex || 1) : 1;
  const genOrdinal = genNum === 1 ? '1st' : genNum === 2 ? '2nd' : '3rd';

  return (
    <Box ref={topRef} sx={{ width: '100%' }}>
      {/* TOP PINNED HIGHLIGHTED RESULT (Screenshot 1) */}
      {highlightedGroup && highlightedMap && (
        <Box sx={{ mb: 4, display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Typography
            variant="caption"
            sx={{
              fontWeight: 800,
              color: '#FFFFFF',
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.85rem',
              mb: 1.5,
              letterSpacing: '0.5px'
            }}
          >
            Highlighted Result
          </Typography>

          <SinglePlanCard
            map={highlightedMap}
            isHighlightedView={true}
            onOpenParentPlan={handleOpenParentPlan}
          />

          {/* Clear Highlight Button */}
          <Button
            size="small"
            onClick={handleClearHighlight}
            sx={{
              mt: 1.5,
              mb: 2.5,
              backgroundColor: '#262626',
              color: '#FFFFFF',
              border: '1px solid #383838',
              fontFamily: 'monospace',
              fontSize: '0.75rem',
              fontWeight: 700,
              px: 2,
              '&:hover': { backgroundColor: '#333333', color: '#00E5FF' }
            }}
          >
            CLEAR HIGHLIGHT
          </Button>

          {/* Guidance Explanations */}
          <Box sx={{ maxWidth: 640, textAlign: 'center', mb: 2 }}>
            <Typography
              variant="body2"
              sx={{
                color: '#CCCCCC',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.82rem',
                lineHeight: 1.6,
                mb: 1
              }}
            >
              The Plant you selected comes from the <strong>{genOrdinal}</strong> generation. To be able to crossbreed it, first you will need to acquire Plants that it requires. Click on <span style={{ color: '#FFA726', textDecoration: 'underline' }}>highlighted</span> Plants to see how to crossbreed them.
            </Typography>

            <Typography
              variant="caption"
              sx={{
                color: '#8E8E8E',
                fontFamily: '"Roboto Mono", monospace',
                fontSize: '0.78rem'
              }}
            >
              <strong>#NUMBER</strong> indicates position of the Plant in the gene list you provided.
            </Typography>
          </Box>

          <Divider sx={{ width: '100%', borderColor: '#282828', my: 2 }} />
        </Box>
      )}

      {/* Top Filter Bar (Underline style matching Rust Breeder design) */}
      <Box sx={{ mb: 2.5 }}>
        {/* Row 1: Counts */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: 'repeat(3, 1fr)',
            gap: 3,
            mb: 2
          }}
        >
          <Box>
            <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
              No. of Gs
            </Typography>
            <input
              type="number"
              min={0}
              max={6}
              value={minGs}
              onChange={(e) => setMinGs(e.target.value)}
              className="filter-underline-input"
            />
          </Box>

          <Box>
            <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
              No. of Ys
            </Typography>
            <input
              type="number"
              min={0}
              max={6}
              value={minYs}
              onChange={(e) => setMinYs(e.target.value)}
              className="filter-underline-input"
            />
          </Box>

          <Box>
            <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
              No. of Hs
            </Typography>
            <input
              type="number"
              min={0}
              max={6}
              value={minHs}
              onChange={(e) => setMinHs(e.target.value)}
              className="filter-underline-input"
            />
          </Box>
        </Box>

        {/* Row 2: Slots */}
        <Box
          sx={{
            display: 'grid',
            gridTemplateColumns: 'repeat(6, 1fr)',
            gap: 1.5
          }}
        >
          {[
            { label: 'Gene 1', val: slot1, set: setSlot1 },
            { label: 'Gene 2', val: slot2, set: setSlot2 },
            { label: 'Gene 3', val: slot3, set: setSlot3 },
            { label: 'Gene 4', val: slot4, set: setSlot4 },
            { label: 'Gene 5', val: slot5, set: setSlot5 },
            { label: 'Gene 6', val: slot6, set: setSlot6 }
          ].map((item, idx) => (
            <Box key={idx}>
              <Typography variant="caption" sx={{ color: '#AAAAAA', fontSize: '0.75rem', fontFamily: 'monospace', display: 'block', mb: 0.25 }}>
                {item.label}
              </Typography>
              <input
                type="text"
                maxLength={1}
                value={item.val}
                onChange={(e) => item.set(e.target.value.toUpperCase())}
                className="filter-underline-input"
                placeholder="-"
              />
            </Box>
          ))}
        </Box>
      </Box>

      {/* Total Results Badge */}
      {results.length > 0 && (
        <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', gap: 1.5, mb: 1.5 }}>
          <Typography
            variant="caption"
            sx={{
              color: '#00E5FF',
              fontWeight: 800,
              fontFamily: '"Roboto Mono", monospace',
              fontSize: '0.82rem',
              backgroundColor: 'rgba(0, 229, 255, 0.06)',
              border: '1px solid rgba(0, 229, 255, 0.25)',
              borderRadius: '4px',
              px: 1.5,
              py: 0.35,
              letterSpacing: '0.5px'
            }}
          >
            {filteredResults.length} {filteredResults.length === 1 ? 'PLAN FOUND' : 'PLANS FOUND'}
            {results.length !== filteredResults.length && ` (${results.length} TOTAL)`}
          </Typography>
        </Box>
      )}

      {/* Center Informational Subtitle */}
      <Box sx={{ textAlign: 'center', mb: 3 }}>
        <Typography
          variant="body2"
          sx={{
            color: '#B0B0B0',
            fontSize: '0.85rem',
            fontFamily: '"Roboto Mono", monospace',
            letterSpacing: '0.2px'
          }}
        >
          Click on a Card to see more details about generations and crossbreeding!
        </Typography>
      </Box>

      {/* Results List */}
      {filteredResults.length === 0 ? (
        <Box sx={{ textAlign: 'center', py: 6 }}>
          <Typography variant="body2" sx={{ color: '#666666', fontFamily: 'monospace' }}>
            {isCalculating ? 'Simulating crossbreeding combinations...' : 'No breeding results to display.'}
          </Typography>
        </Box>
      ) : (
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Box sx={{ width: 310, maxWidth: 310 }}>
            {visibleResults.map((group) => (
              <SimulationMapCard
                key={group.resultSaplingGeneString}
                group={group}
                onSelectGroup={() => handleSelectGroup(group)}
                onHighlightMap={(mapIndex) => handleHighlightMap(group, mapIndex)}
              />
            ))}
          </Box>

          {/* Infinite Scroll Sentinel */}
          {filteredResults.length > visibleCount && (
            <Box
              ref={observerRef}
              sx={{
                py: 3,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: '100%'
              }}
            >
              <Typography variant="caption" sx={{ color: '#666666', fontFamily: 'monospace' }}>
                Loading more plans ({visibleCount} of {filteredResults.length})...
              </Typography>
            </Box>
          )}
        </Box>
      )}

      {/* Parent GEN.X Plan Detail Modal */}
      <PlanDetailModal
        open={parentModalOpen}
        onClose={() => setParentModalOpen(false)}
        parentSapling={parentSapling}
        parentMap={parentMap}
      />
    </Box>
  );
};
