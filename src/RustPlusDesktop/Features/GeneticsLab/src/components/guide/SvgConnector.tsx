import React from 'react';

interface SvgConnectorProps {
  startX?: number;
  startY?: number;
  endX?: number;
  endY?: number;
  className?: string;
}

export const SvgConnector: React.FC<SvgConnectorProps> = ({
  startX = 0,
  startY = 0,
  endX = 100,
  endY = 100,
  className = ''
}) => {
  const dx = endX - startX;
  const dy = endY - startY;
  const cx = startX + dx / 2;
  const cy = startY + dy / 2;
  const pathD = `M ${startX} ${startY} Q ${cx} ${startY} ${endX} ${endY}`;

  return (
    <svg
      style={{
        position: 'absolute',
        top: 0,
        left: 0,
        width: '100%',
        height: '100%',
        pointerEvents: 'none',
        overflow: 'visible'
      }}
      className={className}
    >
      <defs>
        <marker
          id="arrowhead"
          markerWidth="10"
          markerHeight="7"
          refX="9"
          refY="3.5"
          orient="auto"
        >
          <polygon points="0 0, 10 3.5, 0 7" fill="currentColor" />
        </marker>
      </defs>
      <path
        d={pathD}
        fill="none"
        stroke="currentColor"
        strokeWidth="3"
        strokeDasharray="10, 5"
        markerEnd="url(#arrowhead)"
        style={{ color: 'var(--color-primary)' }}
      />
    </svg>
  );
};
