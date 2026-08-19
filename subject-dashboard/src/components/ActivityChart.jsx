import { useState } from 'react';
import { createPortal } from 'react-dom';
import { getRangeWindow, normalizeBuckets } from '../utils/activity';
import { formatDateTime } from '../utils/format';

const CHART_HEIGHT = 220;
const PAD_TOP = 16;
const PAD_BOTTOM = 34;
const PAD_LEFT = 44;
const PAD_RIGHT = 14;
const VIEW_WIDTH = 800;
const MAX_X_LABELS = 8;

function formatAxisLabel(iso, rangeId) {
  const d = new Date(iso);
  if (rangeId === 'hour' || rangeId === 'day') {
    return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }
  if (rangeId === 'week') {
    return d.toLocaleString([], { weekday: 'short', hour: 'numeric' });
  }
  return d.toLocaleString([], { month: 'short', day: 'numeric' });
}

function computeYTicks(max) {
  if (max <= 0) return [0];
  const step = Math.max(1, Math.ceil(max / 3));
  const ticks = [];
  for (let v = 0; v <= max; v += step) ticks.push(v);
  if (ticks[ticks.length - 1] < max) ticks.push(ticks[ticks.length - 1] + step);
  return ticks;
}

/**
 * Hand-rolled stacked SVG bar chart (success stacked on failure). Normalizes
 * gaps to zero, renders a Y axis (~3 ticks), ~8 X labels, and a portal tooltip.
 */
function ActivityChart({ summary, rangeId = 'day', onBucketClick }) {
  const [hover, setHover] = useState(null);
  const range = getRangeWindow(rangeId);
  const buckets = normalizeBuckets(summary, range);

  if (!summary) {
    return (
      <div className="empty-state">
        <p className="empty-state-description">No request activity for the selected range.</p>
      </div>
    );
  }

  const maxCount = Math.max(1, ...buckets.map((b) => b.totalCount));
  const yTicks = computeYTicks(maxCount);
  const yMax = yTicks[yTicks.length - 1] || 1;
  const barAreaHeight = CHART_HEIGHT - PAD_TOP - PAD_BOTTOM;
  const barAreaWidth = VIEW_WIDTH - PAD_LEFT - PAD_RIGHT;
  const groupWidth = barAreaWidth / buckets.length;
  const barWidth = Math.max(2, Math.min(34, groupWidth * 0.72));

  const labelInterval = Math.max(1, Math.ceil(buckets.length / MAX_X_LABELS));

  return (
    <div style={{ position: 'relative', width: '100%' }}>
      <svg
        width="100%"
        viewBox={`0 0 ${VIEW_WIDTH} ${CHART_HEIGHT}`}
        preserveAspectRatio="xMidYMid meet"
        style={{ display: 'block' }}
        role="img"
        aria-label="Request activity chart"
      >
        {yTicks.map((tick) => {
          const y = PAD_TOP + barAreaHeight - (tick / yMax) * barAreaHeight;
          return (
            <g key={tick}>
              <line
                x1={PAD_LEFT}
                y1={y}
                x2={VIEW_WIDTH - PAD_RIGHT}
                y2={y}
                stroke="var(--border-color)"
                strokeDasharray={tick === 0 ? 'none' : '4,4'}
                strokeWidth={0.5}
              />
              <text x={PAD_LEFT - 8} y={y + 3} textAnchor="end" fontSize="8" fill="var(--text-secondary)">
                {tick}
              </text>
            </g>
          );
        })}

        {buckets.map((bucket, i) => {
          const successH = (bucket.successCount / yMax) * barAreaHeight;
          const failureH = (bucket.failureCount / yMax) * barAreaHeight;
          const x = PAD_LEFT + i * groupWidth + (groupWidth - barWidth) / 2;
          const failureY = PAD_TOP + barAreaHeight - failureH;
          const successY = failureY - successH;
          const showLabel = i % labelInterval === 0;

          return (
            <g
              key={bucket.bucketStartUtc}
              onMouseEnter={(e) => setHover({ bucket, x: e.clientX, y: e.clientY })}
              onMouseMove={(e) => setHover({ bucket, x: e.clientX, y: e.clientY })}
              onMouseLeave={() => setHover(null)}
              onClick={() => onBucketClick && onBucketClick(bucket)}
              style={{ cursor: onBucketClick ? 'pointer' : 'default' }}
            >
              <rect
                x={PAD_LEFT + i * groupWidth}
                y={PAD_TOP}
                width={groupWidth}
                height={barAreaHeight + PAD_BOTTOM}
                fill="transparent"
              />
              {bucket.failureCount > 0 && (
                <rect x={x} y={failureY} width={barWidth} height={failureH} rx={2} fill="var(--color-danger)" />
              )}
              {bucket.successCount > 0 && (
                <rect x={x} y={successY} width={barWidth} height={successH} rx={2} fill="var(--color-success)" />
              )}
              {bucket.totalCount === 0 && (
                <rect
                  x={x}
                  y={PAD_TOP + barAreaHeight - 2}
                  width={barWidth}
                  height={2}
                  fill="var(--border-color)"
                />
              )}
              {showLabel && (
                <text
                  x={PAD_LEFT + i * groupWidth + groupWidth / 2}
                  y={CHART_HEIGHT - 6}
                  textAnchor="middle"
                  fontSize="8"
                  fill="var(--text-secondary)"
                >
                  {formatAxisLabel(bucket.bucketStartUtc, rangeId)}
                </text>
              )}
            </g>
          );
        })}
      </svg>

      <div style={{ display: 'flex', gap: 16, marginTop: 8, fontSize: '0.76rem', color: 'var(--text-secondary)' }}>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: 'var(--color-success)' }} /> Success
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
          <span style={{ width: 10, height: 10, borderRadius: 2, background: 'var(--color-danger)' }} /> Failed
        </span>
      </div>

      {hover &&
        createPortal(
          <div
            style={{
              position: 'fixed',
              left: Math.min(hover.x + 14, window.innerWidth - 240),
              top: Math.min(hover.y + 14, window.innerHeight - 130),
              background: 'var(--bg-primary)',
              border: '1px solid var(--border-color)',
              borderRadius: 8,
              boxShadow: 'var(--shadow-lg)',
              padding: '10px 12px',
              fontSize: '0.76rem',
              zIndex: 4000,
              pointerEvents: 'none',
              minWidth: 200
            }}
          >
            <div style={{ fontWeight: 600, marginBottom: 4 }}>
              {formatDateTime(hover.bucket.bucketStartUtc)}
            </div>
            <div>Total: {hover.bucket.totalCount}</div>
            <div style={{ color: 'var(--color-success)' }}>Success: {hover.bucket.successCount}</div>
            <div style={{ color: 'var(--color-danger)' }}>Failed: {hover.bucket.failureCount}</div>
            <div>Avg: {hover.bucket.averageDurationMs.toFixed(1)} ms</div>
          </div>,
          document.body
        )}
    </div>
  );
}

export default ActivityChart;
