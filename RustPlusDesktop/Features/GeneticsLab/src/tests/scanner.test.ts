import { describe, it, expect, beforeEach } from 'vitest';
import { Sapling } from '../domain/genetics/Sapling.ts';
import { TemporalVotingService } from '../services/scanner/TemporalVotingService.ts';
import { PlantScanDeduplicator } from '../services/scanner/PlantScanDeduplicator.ts';
import { RegionChangeDetector } from '../services/scanner/RegionChangeDetector.ts';
import { FrameStabilityDetector } from '../services/scanner/FrameStabilityDetector.ts';
import { GeneImagePreprocessor } from '../services/scanner/GeneImagePreprocessor.ts';
import { SCANNER_CONFIG } from '../services/scanner/scannerConfig.ts';
import { normalizeGeneGlyph } from '../services/scanner/TesseractGeneRecognizer.ts';

describe('Scanner Subsystem & Modules', () => {
  describe('Glyph Normalization', () => {
    it('normalizes OCR font confusions accurately', () => {
      expect(normalizeGeneGlyph('7')).toBe('Y');
      expect(normalizeGeneGlyph('V')).toBe('Y');
      expect(normalizeGeneGlyph('6')).toBe('G');
      expect(normalizeGeneGlyph('0')).toBe('G');
      expect(normalizeGeneGlyph('8')).toBe('X');
      expect(normalizeGeneGlyph('1')).toBe('H');
      expect(normalizeGeneGlyph('I')).toBe('H');
      expect(normalizeGeneGlyph('M')).toBe('W');
      expect(normalizeGeneGlyph('G')).toBe('G');
      expect(normalizeGeneGlyph('H')).toBe('H');
      expect(normalizeGeneGlyph('Y')).toBe('Y');
      expect(normalizeGeneGlyph('W')).toBe('W');
      expect(normalizeGeneGlyph('X')).toBe('X');
    });
  });

  describe('Gene Validation', () => {
    it('accepts valid 6-gene Rust strings', () => {
      expect(Sapling.isValidGeneString('GGGYYY')).toBe(true);
      expect(Sapling.isValidGeneString('GHYWXG')).toBe(true);
      expect(Sapling.isValidGeneString('WWXXYY')).toBe(true);
      expect(Sapling.isValidGeneString('HHHHHH')).toBe(true);
    });

    it('rejects invalid gene characters', () => {
      expect(Sapling.isValidGeneString('ABCDEF')).toBe(false);
      expect(Sapling.isValidGeneString('GGGAAY')).toBe(false);
      expect(Sapling.isValidGeneString('G12345')).toBe(false);
      expect(Sapling.isValidGeneString('G-H-Y-W')).toBe(false);
    });

    it('rejects strings that are not exactly 6 characters', () => {
      expect(Sapling.isValidGeneString('GGGYY')).toBe(false);
      expect(Sapling.isValidGeneString('GGGYYYX')).toBe(false);
      expect(Sapling.isValidGeneString('')).toBe(false);
    });
  });

  describe('Temporal Voting Service', () => {
    let votingService: TemporalVotingService;

    beforeEach(() => {
      votingService = new TemporalVotingService();
    });

    it('confirms candidate when it matches the required number of times exactly', () => {
      votingService.addCandidate('inventory', { geneString: 'GGYHYX', confidence: 85 });
      const notYet = votingService.addCandidate('inventory', { geneString: 'GGYHYX', confidence: 90 });
      // 2 matches is no longer enough to confirm (requiredMatches is now 3).
      expect(notYet).toBeNull();
      const confirmed = votingService.addCandidate('inventory', { geneString: 'GGYHYX', confidence: 88 });
      expect(confirmed).not.toBeNull();
      expect(confirmed?.geneString).toBe('GGYHYX');
    });

    it('does not confirm on only 2 agreeing torn frames', () => {
      votingService.addCandidate('inventory', { geneString: 'GGXHYX', confidence: 85 });
      const voted = votingService.addCandidate('inventory', { geneString: 'GGXHYX', confidence: 86 });
      // A mis-read that appears on 2 torn frames must not be confirmed.
      expect(voted).toBeNull();
    });

    it('resolves position-by-position majority across the sample window (single-slot noise is outvoted)', () => {
      votingService.addCandidate('inventory', { geneString: 'GGYHYX', confidence: 80 });
      votingService.addCandidate('inventory', { geneString: 'GGYHYX', confidence: 81 });
      votingService.addCandidate('inventory', { geneString: 'GGXHYX', confidence: 82 });
      const voted = votingService.addCandidate('inventory', { geneString: 'GGYHYW', confidence: 88 });

      expect(voted).not.toBeNull();
      expect(voted?.geneString).toBe('GGYHYX');
    });

    it('rejects candidate if confidence is below threshold', () => {
      const result = votingService.addCandidate('inventory', {
        geneString: 'GGYHYX',
        confidence: SCANNER_CONFIG.recognition.minAverageConfidence - 10
      });
      expect(result).toBeNull();
    });
  });

  describe('Plant Scan Deduplicator', () => {
    let deduplicator: PlantScanDeduplicator;

    beforeEach(() => {
      deduplicator = new PlantScanDeduplicator();
    });

    it('accepts first scanned plant and suppresses continuous identical scans', () => {
      const acceptFirst = deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      expect(acceptFirst).toBe(true);

      const acceptDuplicate = deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      expect(acceptDuplicate).toBe(false);
    });

    it('accepts any different genotype immediately (dedup never drops a differing read)', () => {
      deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      // Differs by several genes => a genuinely new plant.
      expect(deduplicator.shouldAccept('inventory', 'YYYYGG', 1000)).toBe(true);
      // Differs by a single gene => still accepted; duplicate mis-reads are handled by
      // temporal voting upstream, not by the deduplicator.
      expect(deduplicator.shouldAccept('inventory', 'YYYYGH', 1000)).toBe(true);
    });

    it('accepts identical genotype when ROI signature indicates a new item hover', () => {
      deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      // New item signature difference > 5%
      const acceptSecondPlant = deduplicator.shouldAccept('inventory', 'GGYYHH', 1200);
      expect(acceptSecondPlant).toBe(true);
    });

    it('allows re-acceptance after region dismissal', () => {
      deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      deduplicator.markRegionDismissed('inventory');
      const reaccept = deduplicator.shouldAccept('inventory', 'GGYYHH', 1000);
      expect(reaccept).toBe(true);
    });
  });

  describe('Region Change & Frame Stability', () => {
    it('detects significant pixel signature changes', () => {
      const changeDetector = new RegionChangeDetector();
      expect(changeDetector.hasChanged('inventory', 5000)).toBe(true);
      // Under threshold
      expect(changeDetector.hasChanged('inventory', 5001)).toBe(false);
      // Significant change
      expect(changeDetector.hasChanged('inventory', 6000)).toBe(true);
    });

    it('requires stable duration before confirming frame stability', () => {
      const stabilityDetector = new FrameStabilityDetector();
      // First frame sets baseline
      expect(stabilityDetector.registerFrame('inventory', 5000)).toBe(false);
    });
  });

  describe('Badge Binarization Logic', () => {
    it('distinguishes white letters from red and green badges', () => {
      // Mock 3-pixel RGBA buffer
      const rawData = new Uint8ClampedArray([
        // Pixel 0: White Letter (240, 240, 240) -> text (0)
        240, 240, 240, 255,
        // Pixel 1: Red Badge (210, 50, 45) -> background (255)
        210, 50, 45, 255,
        // Pixel 2: Green Badge (50, 200, 60) -> background (255)
        50, 200, 60, 255
      ]);

      GeneImagePreprocessor.binarizeBuffer(rawData);

      // Pixel 0 (White text) should become 0 (Black text)
      expect(rawData[0]).toBe(0);
      // Pixel 1 (Red badge) should become 255 (White background)
      expect(rawData[4]).toBe(255);
      // Pixel 2 (Green badge) should become 255 (White background)
      expect(rawData[8]).toBe(255);
    });
  });
});
