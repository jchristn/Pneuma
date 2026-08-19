const STATUS_MAP = {
  // job / generic lifecycle
  completed: 'success', complete: 'success', success: 'success', succeeded: 'success',
  active: 'success', enabled: 'success', ingested: 'success', done: 'success',
  failed: 'danger', error: 'danger', failure: 'danger', cancelled: 'danger', canceled: 'danger',
  queued: 'info', pending: 'info', running: 'warning', processing: 'warning', inprogress: 'warning',
  disabled: 'neutral', inactive: 'neutral', unknown: 'neutral'
};

export function toneForStatus(value) {
  if (value === null || value === undefined) return 'neutral';
  const key = String(value).toLowerCase().replace(/[\s_-]/g, '');
  return STATUS_MAP[key] || 'neutral';
}

export function toneForHttpStatus(code) {
  const n = Number(code);
  if (!n) return 'neutral';
  if (n >= 200 && n < 400) return 'success';
  if (n >= 400) return 'danger';
  return 'info';
}

export function toneForMethod(method) {
  switch (String(method || '').toUpperCase()) {
    case 'GET': return 'info';
    case 'POST': return 'success';
    case 'PUT': case 'PATCH': return 'warning';
    case 'DELETE': return 'danger';
    default: return 'neutral';
  }
}

function StatusPill({ label, tone = 'neutral', mono = false }) {
  if (label === null || label === undefined || label === '') return <span>—</span>;
  return <span className={`pill pill-${tone} ${mono ? 'method-pill' : ''}`}>{String(label)}</span>;
}

export default StatusPill;
