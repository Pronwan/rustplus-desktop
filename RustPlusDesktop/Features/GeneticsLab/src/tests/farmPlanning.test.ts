import { describe, expect, it } from 'vitest';
import {
  DEFAULT_AUDIT_INPUT,
  DEFAULT_CONDITIONS,
  DEFAULT_GOAL_INPUT,
  auditFarmSetup,
  estimateCropCycle,
  planFarmFromGoal
} from '../domain/planner/farmPlanning.ts';
import { buildFarmPlanSvg } from '../utils/farmPlanExport.ts';

describe('farm planning engine', () => {
  it('models genes, the limiting condition, and calibrated values explicitly', () => {
    const estimate = estimateCropCycle({
      cropId: 'hemp',
      genetics: 'GGGYYY',
      conditions: { ...DEFAULT_CONDITIONS, water: 80 }
    });

    expect(estimate).not.toBeNull();
    expect(estimate?.gCount).toBe(3);
    expect(estimate?.yCount).toBe(3);
    expect(estimate?.clonesPerPlant).toBe(3);
    expect(estimate?.limitingCondition).toBe('water');
    expect(estimate?.effectiveConditionPercent).toBe(80);
    expect(estimate?.cycleMinutes).toBeGreaterThan(51);
    expect(estimate?.yieldPerPlant).toBe(61.3);
    expect(estimate?.confidence).toBe('community');

    const calibrated = estimateCropCycle({
      cropId: 'hemp',
      genetics: 'GGGYYY',
      conditions: DEFAULT_CONDITIONS,
      measuredYieldPerPlant: 50,
      measuredCycleMinutes: 75
    });
    expect(calibrated?.yieldPerPlant).toBe(50);
    expect(calibrated?.cycleMinutes).toBe(75);
    expect(calibrated?.confidence).toBe('user');
  });

  it('expands recipe goals into farmable crop requirements', () => {
    const plan = planFarmFromGoal({
      ...DEFAULT_GOAL_INPUT,
      outputItem: 'Pure Ore Tea',
      quantity: 1,
      timeBasis: 'harvest'
    });

    expect(plan.supported).toBe(true);
    expect(plan.crops).toHaveLength(1);
    expect(plan.crops[0].cropId).toBe('yellow-berry');
    expect(plan.crops[0].requiredPerGoalUnit).toBe(64);
    expect(plan.goalOutputForPeriod).toBeGreaterThanOrEqual(1);
  });

  it('reports output in requested goal units and exports a usable checklist image', () => {
    const plan = planFarmFromGoal({
      ...DEFAULT_GOAL_INPUT,
      outputItem: 'Pure Ore Tea',
      quantity: 4,
      timeBasis: 'day'
    });

    expect(plan.goalOutputForPeriod).toBeGreaterThanOrEqual(4);
    expect(buildFarmPlanSvg(plan)).toContain('RUST FARM PLAN');
    expect(buildFarmPlanSvg(plan)).toContain('Pure Ore Tea');
  });

  it('accounts for clone reserve when sizing a farm', () => {
    const withReserve = planFarmFromGoal({
      ...DEFAULT_GOAL_INPUT,
      quantity: 1000,
      reserveClones: true
    });
    const withoutReserve = planFarmFromGoal({
      ...DEFAULT_GOAL_INPUT,
      quantity: 1000,
      reserveClones: false
    });

    expect(withReserve.totalCloneReservePlants).toBeGreaterThan(0);
    expect(withReserve.totalPlanters).toBeGreaterThanOrEqual(withoutReserve.totalPlanters);
  });

  it('derives a build list and keeps water flow separate from power', () => {
    const plan = planFarmFromGoal({
      ...DEFAULT_GOAL_INPUT,
      quantity: 1,
      timeBasis: 'harvest'
    });

    expect(plan.infrastructure.lights).toBe(plan.totalPlanters);
    expect(plan.infrastructure.sprinklers).toBe(Math.ceil(plan.totalPlanters / 4));
    expect(plan.infrastructure.powerDrawRw).toBe(
      plan.infrastructure.lights * 2 +
      plan.infrastructure.waterPumps * 5 +
      plan.infrastructure.poweredPurifiers * 5
    );
    expect(plan.infrastructure.recommendedWaterFlowMlPerMinute).toBeGreaterThan(
      plan.infrastructure.waterDemandMlPerMinute
    );
  });

  it('reports the primary limiting system for an existing setup', () => {
    const audit = auditFarmSetup({
      ...DEFAULT_AUDIT_INPUT,
      availableWaterMlPerMinute: 1,
      availablePowerRw: 1000
    });

    expect(audit.supported).toBe(true);
    expect(audit.status).toBe('unsustainable');
    expect(audit.bottleneck).toBe('water');
    expect(audit.waterMarginMlPerMinute).toBeLessThan(0);
    expect(audit.recommendation).toContain('ml/min');
  });

  it('never falls back to hemp for unsupported crops or outputs', () => {
    expect(estimateCropCycle({
      cropId: 'not-a-crop',
      genetics: 'GGGYYY',
      conditions: DEFAULT_CONDITIONS
    })).toBeNull();

    const plan = planFarmFromGoal({ ...DEFAULT_GOAL_INPUT, outputItem: 'Not an item' });
    expect(plan.supported).toBe(false);
    expect(plan.totalPlanters).toBe(0);

    const audit = auditFarmSetup({ ...DEFAULT_AUDIT_INPUT, cropId: 'not-a-crop' });
    expect(audit.supported).toBe(false);
  });
});
