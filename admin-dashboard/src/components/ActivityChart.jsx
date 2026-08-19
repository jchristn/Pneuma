import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, formatNumber } from '../i18n/formatters';

// Range presets matching the backend's supported intervals.
export const RANGES = {
  hour: { labelKey: 'chart.lastHour', hours: 1, stepMs: 60 * 1000, bucketMinutes: 1 },
  day: { labelKey: 'chart.lastDay', hours: 24, stepMs: 15 * 60 * 1000, bucketMinutes: 15 },
  week: { labelKey: 'chart.lastWeek', hours: 168, stepMs: 2 * 60 * 60 * 1000, bucketMinutes: 120 },
  month: { labelKey: 'chart.lastMonth', hours: 720, stepMs: 6 * 60 * 60 * 1000, bucketMinutes: 360 }
};

export function rangeToParams(rangeId) {
  const range = RANGES[rangeId] || RANGES.day;
  const now = Date.now();
  return {
    fromUtc: new Date(now - range.hours * 3600 * 1000).toISOString(),
    toUtc: new Date(now).toISOString(),
    bucketMinutes: range.bucketMinutes
  };
}

function pick(obj, keys) {
  for (const k of keys) {
    if (obj[k] !== undefined && obj[k] !== null) return obj[k];
  }
  return undefined;
}

function floorTo(ts, step) { return Math.floor(ts / step) * step; }

// Normalize raw summary buckets into a gap-filled array for the range.
export function normalizeBuckets(summary, rangeId) {
  const range = RANGES[rangeId] || RANGES.day;
  const end = Date.now();
  const start = end - range.hours * 3600 * 1000;
  const rawBuckets = summary?.buckets || summary?.Buckets || summary?.data || summary?.Data || [];

  const map = new Map();
  for (const b of rawBuckets) {
    const ts = pick(b, ['bucketStartUtc', 'startUtc', 'timestampUtc', 'TimestampUtc', 'BucketStartUtc', 'bucket', 'time']);
    if (!ts) continue;
    const key = floorTo(new Date(ts).getTime(), range.stepMs);
    map.set(key, {
      success: Number(pick(b, ['successCount', 'SuccessCount', 'success', 'Success', 'total2xx']) || 0),
      failure: Number(pick(b, ['failureCount', 'FailureCount', 'failure', 'Failure', 'errorCount']) || 0),
      avg: Number(pick(b, ['averageDurationMs', 'avgDurationMs', 'AverageDurationMs']) || 0)
    });
  }

  const out = [];
  for (let t = floorTo(start, range.stepMs); t < end; t += range.stepMs) {
    const match = map.get(t);
    out.push({
      startUtc: new Date(t).toISOString(),
      endUtc: new Date(t + range.stepMs).toISOString(),
      success: match?.success || 0,
      failure: match?.failure || 0,
      avg: match?.avg || 0
    });
  }
  return out;
}

const W = 800;
const H = 220;
const PT = 16;
const PB = 44;
const PL = 46;
const PR = 14;

function ActivityChart({ summary, rangeId = 'day', onBucketClick }) {
  const { t } = useTranslation();
  const [hover, setHover] = useState(null);

  const buckets = useMemo(() => normalizeBuckets(summary, rangeId), [summary, rangeId]);
  const maxCount = Math.max(1, ...buckets.map((b) => b.success + b.failure));

  const areaH = H - PT - PB;
  const areaW = W - PL - PR;
  const step = Math.max(1, Math.ceil(maxCount / 3));
  const ticks = [];
  for (let i = 0; i <= maxCount; i += step) ticks.push(i);
  if (ticks[ticks.length - 1] < maxCount) ticks.push(ticks[ticks.length - 1] + step);
  const yMax = ticks[ticks.length - 1] || 1;

  const groupW = areaW / buckets.length;
  const barW = Math.max(2, Math.min(36, groupW * 0.7));

  const labelEvery = Math.max(1, Math.ceil(buckets.length / 8));

  if (buckets.length === 0) {
    return <div className="chart-empty">{t('chart.noData')}</div>;
  }

  return (
    <div className="chart-container">
      <svg width="100%" viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="xMidYMid meet" style={{ display: 'block' }}>
        {ticks.map((tick) => {
          const y = PT + areaH - (tick / yMax) * areaH;
          return (
            <g key={tick}>
              <line x1={PL} y1={y} x2={W - PR} y2={y} stroke="var(--color-border)" strokeWidth="0.5" strokeDasharray={tick === 0 ? 'none' : '4,4'} />
              <text x={PL - 8} y={y + 3} textAnchor="end" fontSize="8" fill="var(--color-text-secondary)">{formatNumber(tick)}</text>
            </g>
          );
        })}
        {buckets.map((b, i) => {
          const total = b.success + b.failure;
          const sH = (b.success / yMax) * areaH;
          const fH = (b.failure / yMax) * areaH;
          const x = PL + i * groupW + (groupW - barW) / 2;
          const fY = PT + areaH - fH;
          const sY = fY - sH;
          return (
            <g key={i}
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}
              onClick={() => onBucketClick && total > 0 && onBucketClick(b)}
              style={{ cursor: onBucketClick && total > 0 ? 'pointer' : 'default' }}>
              <rect x={PL + i * groupW} y={PT} width={groupW} height={areaH + PB} fill="transparent" />
              {b.failure > 0 && <rect x={x} y={fY} width={barW} height={fH} rx="2" fill="var(--color-danger)" opacity={hover === i ? 1 : 0.85} />}
              {b.success > 0 && <rect x={x} y={sY} width={barW} height={sH} rx="2" fill="var(--color-success)" opacity={hover === i ? 1 : 0.85} />}
              {i % labelEvery === 0 && (
                <text x={PL + i * groupW + groupW / 2} y={H - 8} textAnchor="middle" fontSize="8" fill="var(--color-text-secondary)">
                  {formatDateTime(b.startUtc, rangeId === 'hour' || rangeId === 'day'
                    ? { hour: '2-digit', minute: '2-digit' }
                    : { month: 'short', day: 'numeric' })}
                </text>
              )}
            </g>
          );
        })}
      </svg>
      {hover !== null && buckets[hover] && (
        <div className="chart-tooltip" style={{ left: `${((hover + 0.5) / buckets.length) * 100}%` }}>
          <div style={{ fontWeight: 600, marginBottom: 4 }}>{formatDateTime(buckets[hover].startUtc, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</div>
          <div><span style={{ color: 'var(--color-success)' }}>{t('chart.success')}:</span> {formatNumber(buckets[hover].success)}</div>
          <div><span style={{ color: 'var(--color-danger)' }}>{t('chart.failed')}:</span> {formatNumber(buckets[hover].failure)}</div>
          <div>{t('chart.total')}: {formatNumber(buckets[hover].success + buckets[hover].failure)}</div>
        </div>
      )}
      <div className="chart-legend">
        <span className="legend-item"><span className="legend-color" style={{ background: 'var(--color-success)' }} />{t('chart.successLegend')}</span>
        <span className="legend-item"><span className="legend-color" style={{ background: 'var(--color-danger)' }} />{t('chart.failedLegend')}</span>
      </div>
    </div>
  );
}

export default ActivityChart;
