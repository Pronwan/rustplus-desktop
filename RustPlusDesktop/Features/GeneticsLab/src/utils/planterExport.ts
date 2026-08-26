export interface PlanterExportInput {
  target: string;
  center?: string;
  surrounding: string[];
}

export const buildPlanterSvg = ({ target, center, surrounding }: PlanterExportInput): string => {
  const plants = [
    surrounding[0], surrounding[1], surrounding[2],
    surrounding[3], center, surrounding[4],
    surrounding[5], surrounding[6], surrounding[7]
  ];

  const cells = plants.map((genes, index) => {
    const x = 28 + (index % 3) * 124;
    const y = 72 + Math.floor(index / 3) * 94;
    const isCenter = index === 4;
    const dots = genes
      ? genes.split('').map((gene, geneIndex) =>
          `<text x="${x + 14 + geneIndex * 15}" y="${y + 48}" fill="${'GYH'.includes(gene) ? '#7cb342' : '#ef5350'}" font-size="13" font-weight="700">${gene}</text>`
        ).join('')
      : `<text x="${x + (isCenter ? 15 : 34)}" y="${y + 48}" fill="#777" font-size="11">${isCenter ? 'ANY RECEIVER' : 'EMPTY'}</text>`;
    const label = isCenter ? 'CENTER · PLANT 1ST' : `SURROUNDING #${index < 4 ? index + 1 : index}`;
    return `<rect x="${x}" y="${y}" width="108" height="72" rx="6" fill="${isCenter ? '#0d3338' : '#171717'}" stroke="${isCenter ? '#00e5ff' : '#444'}"/><text x="${x + 8}" y="${y + 20}" fill="#aaa" font-size="11">${label}</text>${dots}`;
  }).join('');

  return `<svg xmlns="http://www.w3.org/2000/svg" width="400" height="390" viewBox="0 0 400 390"><rect width="400" height="390" fill="#0a0a0a"/><text x="28" y="34" fill="#00e5ff" font-family="monospace" font-size="18" font-weight="700">RUST BREEDING PLANTER</text><text x="28" y="55" fill="#ddd" font-family="monospace" font-size="13">TARGET ${target}</text>${cells}<text x="28" y="374" fill="#aaa" font-family="monospace" font-size="11">Center first, then plant the surrounding clones.</text></svg>`;
};
