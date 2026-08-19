import Modal from './Modal';
import StatusPill from './StatusPill';
import { formatDateTime } from '../i18n/formatters';
import './HealthHistogram.css';

// Bucket a flat list of {timestampUtc, success} records into evenly-sized time buckets so a long
// history renders as a compact fixed-width strip. Each bucket carries success/fail counts.
function bucketHistory(history) {
  const points = (history || [])
    .map((r) => ({ t: new Date(r.timestampUtc || r.TimestampUtc).getTime(), ok: (r.success ?? r.Success) === true }))
    .filter((p) => !Number.isNaN(p.t))
    .sort((a, b) => a.t - b.t);
  if (points.length === 0) return [];

  const span = points[points.length - 1].t - points[0].t;
  const bucketMs = span < 3600000 ? 0 : span <= 21600000 ? 60000 : 300000; // raw / 1-min / 5-min
  if (bucketMs === 0) {
    return points.map((p) => ({ t: p.t, success: p.ok ? 1 : 0, fail: p.ok ? 0 : 1 }));
  }

  const map = new Map();
  for (const p of points) {
    const key = Math.floor(p.t / bucketMs);
    const b = map.get(key) || { t: key * bucketMs, success: 0, fail: 0 };
    if (p.ok) b.success += 1; else b.fail += 1;
    map.set(key, b);
  }
  return Array.from(map.values()).sort((a, b) => a.t - b.t);
}

function barTone(b) {
  if (b.fail === 0) return 'ok';
  if (b.success === 0) return 'fail';
  return 'mixed';
}

export function HealthHistogram({ history, height = 18, maxBars = 0 }) {
  let buckets = bucketHistory(history);
  // Cap the number of bars, keeping the most recent: the table strip is compact, the modal shows more.
  if (maxBars > 0 && buckets.length > maxBars) buckets = buckets.slice(buckets.length - maxBars);
  if (buckets.length === 0) {
    return <span className="health-histogram-empty">No data</span>;
  }
  return (
    <div className="health-histogram" style={{ height: `${height}px` }} aria-hidden="true">
      {buckets.map((b, i) => (
        <span
          key={i}
          className={`hh-bar hh-${barTone(b)}`}
          title={`${formatDateTime(new Date(b.t).toISOString())} — ${b.success} ok, ${b.fail} fail`}
        />
      ))}
    </div>
  );
}

function humanizeSpan(history) {
  const points = (history || [])
    .map((r) => new Date(r.timestampUtc || r.TimestampUtc).getTime())
    .filter((t) => !Number.isNaN(t));
  if (points.length < 2) return '—';
  const ms = Math.max(...points) - Math.min(...points);
  const mins = Math.round(ms / 60000);
  if (mins < 60) return `${mins} min`;
  const hrs = ms / 3600000;
  return `${hrs.toFixed(1)} h`;
}

function get(obj, ...keys) {
  for (const k of keys) if (obj && obj[k] !== undefined && obj[k] !== null) return obj[k];
  return undefined;
}

// Detail modal mirroring the reference dashboards: status/uptime/consecutive stat cards, a last-error
// box, the full-width history strip, and the key state-transition timestamps, plus base URL / latency.
export function HealthDetailModal({ title, health, loading, onClose }) {
  const h = health || {};
  const isHealthy = get(h, 'isHealthy', 'IsHealthy') === true;
  const hasData = get(h, 'lastCheckUtc', 'LastCheckUtc');
  const history = get(h, 'history', 'History') || [];
  const uptime = Number(get(h, 'uptimePercentage', 'UptimePercentage') ?? 0);
  const latency = get(h, 'latencyMs', 'LatencyMs');
  const statusCode = get(h, 'statusCode', 'StatusCode');
  const lastError = get(h, 'lastError', 'LastError');

  return (
    <Modal title={title || 'Endpoint Health'} size="lg" onClose={onClose}
      footer={<button type="button" className="button-secondary" onClick={onClose}>Close</button>}>
      {loading ? (
        <div className="table-loading"><div className="loading-spinner" /></div>
      ) : (
        <div className="health-detail">
          <div className="health-meta">
            {get(h, 'baseUrl', 'BaseUrl') && <span><strong>Base URL:</strong> <code>{get(h, 'baseUrl', 'BaseUrl')}</code></span>}
            {statusCode != null && <span><strong>HTTP:</strong> {statusCode}</span>}
            {latency != null && <span><strong>Latency:</strong> {Math.round(latency)} ms</span>}
          </div>

          <div className="health-stat-row">
            <div className="health-stat-card">
              <div className="health-stat-label">Status</div>
              <div className="health-stat-value">
                <StatusPill label={!hasData ? 'Pending' : (isHealthy ? 'Healthy' : 'Unhealthy')} tone={!hasData ? 'neutral' : (isHealthy ? 'success' : 'danger')} />
              </div>
            </div>
            <div className="health-stat-card">
              <div className="health-stat-label">Uptime</div>
              <div className="health-stat-value">{hasData ? `${uptime.toFixed(2)}%` : '—'}</div>
            </div>
            <div className="health-stat-card">
              <div className="health-stat-label">History Span</div>
              <div className="health-stat-value">{humanizeSpan(history)}</div>
            </div>
            <div className="health-stat-card">
              <div className="health-stat-label">Consecutive OK</div>
              <div className="health-stat-value health-ok">{get(h, 'consecutiveSuccesses', 'ConsecutiveSuccesses') ?? '—'}</div>
            </div>
            <div className="health-stat-card">
              <div className="health-stat-label">Consecutive Fail</div>
              <div className="health-stat-value health-fail">{get(h, 'consecutiveFailures', 'ConsecutiveFailures') ?? '—'}</div>
            </div>
          </div>

          {lastError && <div className="health-error-box">{lastError}</div>}

          <div className="health-section-label">Health History</div>
          <HealthHistogram history={history} height={36} maxBars={50} />

          <div className="health-timestamps">
            <div><span>First check</span><strong>{formatDateTime(get(h, 'firstCheckUtc', 'FirstCheckUtc')) || '—'}</strong></div>
            <div><span>Last check</span><strong>{formatDateTime(get(h, 'lastCheckUtc', 'LastCheckUtc')) || '—'}</strong></div>
            <div><span>Last healthy</span><strong>{formatDateTime(get(h, 'lastHealthyUtc', 'LastHealthyUtc')) || '—'}</strong></div>
            <div><span>Last unhealthy</span><strong>{formatDateTime(get(h, 'lastUnhealthyUtc', 'LastUnhealthyUtc')) || '—'}</strong></div>
          </div>
        </div>
      )}
    </Modal>
  );
}

export default HealthHistogram;
