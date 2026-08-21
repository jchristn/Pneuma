import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import PageHeader from '../components/PageHeader';

function fmtMs(v) {
  const n = Number(v);
  if (!isFinite(n) || n <= 0) return '—';
  return n < 1000 ? `${Math.round(n)} ms` : `${(n / 1000).toFixed(2)} s`;
}
function fmtNum(v) {
  const n = Number(v);
  return isFinite(n) ? Math.round(n).toLocaleString() : '—';
}

// A metric tile.
function Tile({ label, value, hint }) {
  return (
    <div className="an-tile" title={hint}>
      <span className="an-tile-label">{label}</span>
      <span className="an-tile-value">{value}</span>
    </div>
  );
}

// Hand-rolled SVG vertical bar chart of turn volume per day (no chart library).
function VolumeChart({ buckets }) {
  if (!buckets || buckets.length === 0) return <div className="an-empty">No activity in this window.</div>;
  const w = 640, h = 160, pad = 24;
  const max = Math.max(1, ...buckets.map((b) => b.count));
  const bw = (w - pad * 2) / buckets.length;
  return (
    <svg className="an-chart" viewBox={`0 0 ${w} ${h}`} preserveAspectRatio="none" role="img" aria-label="Turns per day">
      {buckets.map((b, i) => {
        const bh = Math.max(1, ((h - pad * 2) * b.count) / max);
        return (
          <rect key={i} x={pad + i * bw + 1} y={h - pad - bh} width={Math.max(1, bw - 2)} height={bh}
            fill="var(--color-primary)" rx="2">
            <title>{`${new Date(b.bucketUtc).toLocaleDateString()}: ${b.count} turns, avg ${fmtMs(b.avgGenerationMs)}`}</title>
          </rect>
        );
      })}
      <line x1={pad} y1={h - pad} x2={w - pad} y2={h - pad} stroke="var(--border-color)" strokeWidth="1" />
    </svg>
  );
}

// Hand-rolled SVG horizontal bar chart of per-stage average latency.
function StageChart({ stages }) {
  if (!stages || stages.length === 0) return <div className="an-empty">No stage telemetry in this window.</div>;
  const max = Math.max(1, ...stages.map((s) => s.avgDurationMs));
  return (
    <div className="an-stages">
      {stages.map((s, i) => (
        <div className="an-stage-row" key={i} title={`${s.stage}: avg ${fmtMs(s.avgDurationMs)}, p95 ${fmtMs(s.p95DurationMs)}, ${s.count} runs`}>
          <span className="an-stage-name">{s.stage}</span>
          <span className="an-stage-track">
            <span className="an-stage-fill" style={{ width: `${Math.max(2, (s.avgDurationMs / max) * 100)}%` }} />
          </span>
          <span className="an-stage-val">{fmtMs(s.avgDurationMs)}</span>
        </div>
      ))}
    </div>
  );
}

export default function AnalyticsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [days, setDays] = useState(30);
  const [report, setReport] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => { apiClient.getSubjects().then((r) => setSubjects(asArray(r, 'objects'))).catch(() => {}); }, [apiClient]);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setReport(await apiClient.getAnalytics(subjectId || null, days)); }
    catch (err) { setError(err.message); setReport(null); }
    finally { setLoading(false); }
  }, [apiClient, subjectId, days]);

  useEffect(() => { load(); }, [load]);

  const o = report?.overview || {};
  const up = o.thumbsUp || 0, down = o.thumbsDown || 0;

  return (
    <div>
      <PageHeader title={t('analytics.title', 'Analytics')} subtitle={t('analytics.subtitle', 'Chat volume, latency, and feedback for a subject over time.')} />
      <div className="filter-bar">
        <div className="pagination-group">
          <label>{t('analytics.subject', 'Subject')}:</label>
          <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('analytics.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
          </select>
        </div>
        <div className="pagination-group">
          <label>{t('analytics.window', 'Window')}:</label>
          <select value={days} onChange={(e) => setDays(Number(e.target.value))}>
            <option value={7}>{t('analytics.days7', '7 days')}</option>
            <option value={30}>{t('analytics.days30', '30 days')}</option>
            <option value={90}>{t('analytics.days90', '90 days')}</option>
          </select>
        </div>
        <button className="btn btn-secondary" onClick={load} disabled={loading}>{t('common.refresh', 'Refresh')}</button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      <div className="an-tiles">
        <Tile label={t('analytics.turns', 'Turns')} value={fmtNum(o.turnCount)} hint="Answered chat turns in the window." />
        <Tile label={t('analytics.avgGen', 'Avg generation')} value={fmtMs(o.avgGenerationMs)} hint="Average answer generation time." />
        <Tile label={t('analytics.p95', 'p95 generation')} value={fmtMs(o.p95GenerationMs)} hint="95th-percentile generation time." />
        <Tile label={t('analytics.p99', 'p99 generation')} value={fmtMs(o.p99GenerationMs)} hint="99th-percentile generation time." />
        <Tile label={t('analytics.ttft', 'Avg TTFT')} value={fmtMs(o.avgTimeToFirstTokenMs)} hint="Average time to first token." />
        <Tile label={t('analytics.tps', 'Avg throughput')} value={o.avgTokensPerSecond > 0 ? `${o.avgTokensPerSecond.toFixed(1)} tok/s` : '—'} hint="Average completion tokens per second." />
        <Tile label={t('analytics.tokens', 'Avg tokens')} value={`${fmtNum(o.avgPromptTokens)} / ${fmtNum(o.avgCompletionTokens)}`} hint="Average prompt / completion tokens per turn." />
        <Tile label={t('analytics.feedback', 'Feedback ↑ / ↓')} value={`${up} / ${down}`} hint="Thumbs up / down in the window." />
      </div>

      <div className="an-section">
        <div className="an-section-title">{t('analytics.volume', 'Turns per day')}</div>
        <VolumeChart buckets={report?.timeseries} />
      </div>

      <div className="an-section">
        <div className="an-section-title">{t('analytics.stageLatency', 'Average stage latency')}</div>
        <StageChart stages={report?.stages} />
      </div>
    </div>
  );
}
