import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDateTime, formatNumber } from '../i18n/formatters';
import { normalizeIngestionBuckets, stagesPresent, stageColor, stageLabel } from '../utils/ingestionActivity';

const W = 800;
const H = 220;
const PT = 16;
const PB = 44;
const PL = 46;
const PR = 14;

/**
 * Hand-rolled SVG stacked bar chart of ingestion activity over time, one stacked series per pipeline
 * stage. Shape and sizing mirror the request-activity ActivityChart so the two read as a pair.
 */
function IngestionActivityChart({ summary, rangeId = 'day', onBucketClick }) {
  const { t } = useTranslation();
  const [hover, setHover] = useState(null);

  const buckets = useMemo(() => normalizeIngestionBuckets(summary), [summary]);
  const stages = useMemo(() => stagesPresent(buckets), [buckets]);
  const maxCount = Math.max(1, ...buckets.map((b) => b.total));

  const areaH = H - PT - PB;
  const areaW = W - PL - PR;
  const step = Math.max(1, Math.ceil(maxCount / 3));
  const ticks = [];
  for (let i = 0; i <= maxCount; i += step) ticks.push(i);
  if (ticks[ticks.length - 1] < maxCount) ticks.push(ticks[ticks.length - 1] + step);
  const yMax = ticks[ticks.length - 1] || 1;

  const groupW = areaW / Math.max(1, buckets.length);
  const barW = Math.max(2, Math.min(36, groupW * 0.7));
  const labelEvery = Math.max(1, Math.ceil(buckets.length / 8));

  if (buckets.length === 0 || stages.length === 0) {
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
          const x = PL + i * groupW + (groupW - barW) / 2;
          let yCursor = PT + areaH; // stack upward from the baseline
          return (
            <g key={i}
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}
              onClick={() => onBucketClick && b.total > 0 && onBucketClick(b)}
              style={{ cursor: onBucketClick && b.total > 0 ? 'pointer' : 'default' }}>
              <rect x={PL + i * groupW} y={PT} width={groupW} height={areaH + PB} fill="transparent" />
              {stages.map((stage) => {
                const count = b.stages[stage] || 0;
                if (count <= 0) return null;
                const segH = (count / yMax) * areaH;
                yCursor -= segH;
                return (
                  <rect key={stage} x={x} y={yCursor} width={barW} height={segH} fill={stageColor(stage)} opacity={hover === i ? 1 : 0.85} />
                );
              })}
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
      {hover !== null && buckets[hover] && buckets[hover].total > 0 && (
        <div className="chart-tooltip" style={{ left: `${((hover + 0.5) / buckets.length) * 100}%` }}>
          <div style={{ fontWeight: 600, marginBottom: 4 }}>{formatDateTime(buckets[hover].startUtc, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })}</div>
          {stages.filter((s) => (buckets[hover].stages[s] || 0) > 0).map((s) => (
            <div key={s}><span style={{ color: stageColor(s) }}>{stageLabel(s)}:</span> {formatNumber(buckets[hover].stages[s])}</div>
          ))}
          <div style={{ marginTop: 2 }}>{t('chart.total')}: {formatNumber(buckets[hover].total)}</div>
        </div>
      )}
      <div className="chart-legend chart-legend-wrap">
        {stages.map((s) => (
          <span key={s} className="legend-item"><span className="legend-color" style={{ background: stageColor(s) }} />{stageLabel(s)}</span>
        ))}
      </div>
    </div>
  );
}

export default IngestionActivityChart;
