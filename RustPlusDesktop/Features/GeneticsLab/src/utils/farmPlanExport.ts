import { FarmPlan, PLANTER_TYPES, formatDuration, timeBasisLabel } from '../domain/planner/farmPlanning.ts';

const escapeXml = (value: string): string => value.replace(/[&<>"']/g, (character) => ({
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&apos;'
}[character]!));

export function buildFarmPlanSvg(plan: FarmPlan): string {
  const period = timeBasisLabel(plan.goal.timeBasis, plan.goal.horizonHours);
  const componentRows = plan.infrastructure.components.slice(0, 8).map((component, index) =>
    `<text x="48" y="${332 + index * 24}" fill="#d7d7d7" font-size="15">${escapeXml(component.item)}</text><text x="752" y="${332 + index * 24}" text-anchor="end" fill="#00e5ff" font-size="15" font-weight="700">${component.quantity}</text>`
  ).join('');
  const height = Math.max(560, 374 + Math.min(8, plan.infrastructure.components.length) * 24);
  const planter = PLANTER_TYPES[plan.goal.planterType].name.toLowerCase();

  return `<svg xmlns="http://www.w3.org/2000/svg" width="800" height="${height}" viewBox="0 0 800 ${height}"><rect width="800" height="${height}" fill="#0a0a0a"/><text x="48" y="54" fill="#00e5ff" font-family="monospace" font-size="26" font-weight="700">RUST FARM PLAN</text><text x="48" y="91" fill="#f5f5f5" font-family="monospace" font-size="19" font-weight="700">${escapeXml(`${plan.goal.quantity.toLocaleString()} ${plan.goal.outputItem} ${period}`)}</text><text x="48" y="126" fill="#9e9e9e" font-family="monospace" font-size="15">GENETICS ${escapeXml(plan.goal.genetics)} · ${plan.confidence === 'user' ? 'USER CALIBRATED' : 'COMMUNITY ESTIMATE'}</text><rect x="48" y="158" width="704" height="116" rx="8" fill="#141414" stroke="#333"/><text x="72" y="195" fill="#9e9e9e" font-family="monospace" font-size="13">RECOMMENDED BUILD</text><text x="72" y="228" fill="#f5f5f5" font-family="monospace" font-size="18" font-weight="700">${escapeXml(`${plan.totalPlanters} ${planter}${plan.totalPlanters === 1 ? '' : 's'} · ${plan.totalSlots} slots`)}</text><text x="72" y="254" fill="#77c843" font-family="monospace" font-size="14">First harvest ~${escapeXml(formatDuration(plan.firstHarvestMinutes))} · ${plan.goalOutputForPeriod.toLocaleString()} goal units</text><text x="48" y="306" fill="#ff9800" font-family="monospace" font-size="16" font-weight="700">COMPONENT CHECKLIST</text>${componentRows}<text x="48" y="${height - 28}" fill="#777" font-family="monospace" font-size="12">Verify sprinkler coverage and measured server rates in game.</text></svg>`;
}
