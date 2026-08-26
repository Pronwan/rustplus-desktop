export interface GenerationInfo {
  generationIndex: number;
  addedSaplings: number;
}

export interface WorkChunk {
  startingPositions: number[];
  combinationsToProcess: number;
  allCombinationsCount: number;
}

export interface CombinatoricsOptions {
  withRepetitions: boolean;
  minCrossbreedingSaplingsNumber: number;
  maxCrossbreedingSaplingsNumber: number;
  numberOfWorkers?: number;
}

export const WORK_CHUNKS_PER_WORKER = 50;

/**
 * Binomial coefficient n choose k
 */
export function binomialCoefficient(n: number, k: number): number {
  if (k < 0 || k > n) return 0;
  if (k === 0 || k === n) return 1;
  let c = 1;
  const kEff = k > n - k ? n - k : k;
  for (let i = 1; i <= kEff; i++) {
    c = (c * (n - (kEff - i))) / i;
  }
  return Math.round(c);
}

/**
 * Multiset combination C(n + k - 1, k)
 */
export function multisetCoefficient(n: number, k: number): number {
  if (n <= 0 || k < 0) return 0;
  if (k === 0) return 1;
  return binomialCoefficient(n + k - 1, k);
}

export function getMaxPositionsCount(
  itemsCount: number,
  maxCrossbreedingSaplings: number,
  withRepetitions: boolean
): number {
  return withRepetitions
    ? maxCrossbreedingSaplings
    : Math.min(itemsCount, maxCrossbreedingSaplings);
}

/**
 * Computes total combinations count for a given item count and options,
 * taking into account later-generation mandatory newly added plants.
 */
export function getNumberOfCrossbreedingCombinations(
  itemsCount: number,
  options: CombinatoricsOptions,
  generationInfo?: GenerationInfo
): number {
  const { withRepetitions, minCrossbreedingSaplingsNumber, maxCrossbreedingSaplingsNumber } = options;
  const maxPos = getMaxPositionsCount(itemsCount, maxCrossbreedingSaplingsNumber, withRepetitions);
  const minPos = minCrossbreedingSaplingsNumber;

  if (minPos > itemsCount && !withRepetitions) {
    return 0;
  }

  let total = 0;
  const added = generationInfo?.addedSaplings ?? 0;
  const isLaterGen = (generationInfo?.generationIndex ?? 0) > 0 && added > 0;
  const oldSaplingsCount = itemsCount - added;

  for (let k = minPos; k <= maxPos; k++) {
    if (k > itemsCount && !withRepetitions) continue;

    const countForK = withRepetitions
      ? multisetCoefficient(itemsCount, k)
      : binomialCoefficient(itemsCount, k);

    if (isLaterGen && oldSaplingsCount > 0) {
      // Subtract combinations that contain ONLY old saplings
      const oldOnlyCount = withRepetitions
        ? multisetCoefficient(oldSaplingsCount, k)
        : (k <= oldSaplingsCount ? binomialCoefficient(oldSaplingsCount, k) : 0);

      total += Math.max(0, countForK - oldOnlyCount);
    } else {
      total += countForK;
    }
  }

  return total;
}

/**
 * Builds the initial position array for enumeration.
 */
export function buildInitialSaplingPositions(minCount: number): number[] {
  const positions: number[] = [];
  for (let i = 0; i < minCount; i++) {
    positions.push(i);
  }
  return positions;
}

/**
 * Advances positions to the next combination in place.
 * Returns true if another combination was reached, false if enumeration is finished.
 */
export function setNextPosition(
  positions: number[],
  itemsCount: number,
  withRepetitions: boolean,
  minCount: number,
  maxCount: number,
  generationInfo?: GenerationInfo
): boolean {
  const k = positions.length;
  const added = generationInfo?.addedSaplings ?? 0;
  const isLaterGen = (generationInfo?.generationIndex ?? 0) > 0 && added > 0;

  // Try incrementing from right to left
  for (let i = k - 1; i >= 0; i--) {
    const maxValForPos = withRepetitions
      ? itemsCount - 1
      : itemsCount - (k - i);

    if (positions[i] < maxValForPos) {
      positions[i]++;
      // Reset subsequent positions
      for (let j = i + 1; j < k; j++) {
        positions[j] = withRepetitions ? positions[j - 1] : positions[j - 1] + 1;
      }

      // Check later generation condition: at least one index must be < addedSaplings
      // Since array is sorted, positions[0] < added guarantees at least one newly added plant.
      if (isLaterGen && positions[0] >= added) {
        // All subsequent combinations for this k will also have positions[0] >= added, so jump to next k
        break;
      }
      return true;
    }
  }

  // If we cannot advance with length k, try advancing to k + 1
  if (k < maxCount) {
    const nextK = k + 1;
    if (!withRepetitions && nextK > itemsCount) {
      return false;
    }

    positions.length = nextK;
    for (let i = 0; i < nextK; i++) {
      positions[i] = withRepetitions ? 0 : i;
    }

    // In later gen, if next initial position satisfies constraint (0 < added), return true
    if (isLaterGen && positions[0] >= added) {
      return false;
    }
    return true;
  }

  return false;
}

/**
 * Advances positions to the next combination in place within a fixed length k.
 */
export function setNextPositionInChunk(
  positions: number[],
  itemsCount: number,
  withRepetitions: boolean
): boolean {
  const k = positions.length;
  for (let i = k - 1; i >= 0; i--) {
    const maxValForPos = withRepetitions
      ? itemsCount - 1
      : itemsCount - (k - i);

    if (positions[i] < maxValForPos) {
      positions[i]++;
      for (let j = i + 1; j < k; j++) {
        positions[j] = withRepetitions ? positions[j - 1] : positions[j - 1] + 1;
      }
      return true;
    }
  }
  return false;
}

/**
 * Computes work chunks for parallel worker distribution in O(1) time without stepping loops.
 */
export function getWorkChunks(
  itemsCount: number,
  options: CombinatoricsOptions,
  generationInfo?: GenerationInfo
): WorkChunk[] {
  const allCount = getNumberOfCrossbreedingCombinations(itemsCount, options, generationInfo);
  if (allCount === 0) return [];

  const chunks: WorkChunk[] = [];
  const maxPos = getMaxPositionsCount(
    itemsCount,
    options.maxCrossbreedingSaplingsNumber,
    options.withRepetitions
  );
  const minPos = options.minCrossbreedingSaplingsNumber;

  const added = generationInfo?.addedSaplings ?? 0;
  const isLaterGen = (generationInfo?.generationIndex ?? 0) > 0 && added > 0;

  for (let k = minPos; k <= maxPos; k++) {
    if (k > itemsCount && !options.withRepetitions) continue;

    // In later generations, at least one plant must be newly added (0 <= p0 < added)
    const maxP0 = isLaterGen
      ? Math.min(added - 1, itemsCount - 1)
      : options.withRepetitions
      ? itemsCount - 1
      : itemsCount - k;

    for (let p0 = 0; p0 <= maxP0; p0++) {
      let countForSlice = 0;

      if (k === 1) {
        countForSlice = 1;
      } else {
        if (!options.withRepetitions) {
          const remainingItems = itemsCount - 1 - p0;
          const remainingSlots = k - 1;
          if (remainingItems >= remainingSlots) {
            countForSlice = binomialCoefficient(remainingItems, remainingSlots);
          }
        } else {
          const availableTypes = itemsCount - p0;
          const remainingSlots = k - 1;
          countForSlice = multisetCoefficient(availableTypes, remainingSlots);
        }
      }

      if (countForSlice > 0) {
        const startingPositions: number[] = new Array(k);
        startingPositions[0] = p0;

        for (let j = 1; j < k; j++) {
          startingPositions[j] = options.withRepetitions ? p0 : p0 + j;
        }

        chunks.push({
          startingPositions,
          combinationsToProcess: countForSlice,
          allCombinationsCount: allCount
        });
      }
    }
  }

  return chunks;
}
