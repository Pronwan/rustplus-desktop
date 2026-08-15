import './setupLocalStorage.ts';
import { describe, it, expect, beforeEach } from 'vitest';
import { StorageService, BreedingSession } from '../services/storageService.ts';

describe('Breeding Sessions & Persistence', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('should save and load active breeding session', () => {
    const session: BreedingSession = {
      id: 'test_session_1',
      cropType: 'hemp',
      targetGenetics: 'GGGYYY',
      steps: [
        {
          generationIndex: 1,
          targetGeneString: 'GGGYYY',
          surroundingSaplingsStrings: ['GGYYHH', 'YYGGHH'],
          chance: 1.0,
          isCenterPlanted: false,
          isSurroundingPlanted: false,
          isCompleted: false
        }
      ],
      currentStepIndex: 0,
      status: 'active',
      startedAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };

    StorageService.saveActiveBreedingSession(session);
    const loaded = StorageService.getActiveBreedingSession();

    expect(loaded).toBeDefined();
    expect(loaded?.id).toBe('test_session_1');
    expect(loaded?.targetGenetics).toBe('GGGYYY');
    expect(loaded?.steps.length).toBe(1);
  });

  it('should archive sessions to history', () => {
    const session: BreedingSession = {
      id: 'session_done',
      cropType: 'hemp',
      targetGenetics: 'GGGYYY',
      steps: [],
      currentStepIndex: 0,
      status: 'completed',
      startedAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      completedAt: new Date().toISOString()
    };

    StorageService.addBreedingSessionToHistory(session);
    const history = StorageService.getBreedingSessionHistory();

    expect(history.length).toBe(1);
    expect(history[0].id).toBe('session_done');
    expect(history[0].status).toBe('completed');
  });
});
