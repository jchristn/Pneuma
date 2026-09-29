// Shared helpers for the crawl plan screens: status tones, schedule summaries, and the conversion between a
// plan's settings object and the schema-driven form's text values.

export const PLAN_TYPES = ['Web', 'Sitemap', 'S3', 'Cifs', 'Nfs', 'GitHub', 'AzureBlob', 'GoogleCloud', 'LocalFolder'];
export const OPERATION_STATUSES = ['Running', 'Ingesting', 'Succeeded', 'PartiallySucceeded', 'Failed', 'Cancelled', 'Held'];
export const OBJECT_STATUSES = ['Active', 'Missing', 'Failed', 'Excluded'];
export const ACTIONS = ['Add', 'Update', 'Retry', 'Delete', 'Skip', 'Fail'];

export function settingsProperty(type) {
  if (!type) return 'web';
  return type === 'S3' ? 's3' : type.charAt(0).toLowerCase() + type.slice(1);
}

export function operationTone(status) {
  switch (status) {
    case 'Succeeded': return 'success';
    case 'PartiallySucceeded': return 'warning';
    case 'Held': return 'warning';
    case 'Failed': return 'danger';
    case 'Cancelled': return 'neutral';
    case 'Running': case 'Ingesting': return 'info';
    default: return 'neutral';
  }
}

export function planStatusTone(status) {
  if (status === 'Running') return 'info';
  if (status === 'Stopping') return 'warning';
  return 'neutral';
}

export function objectTone(status) {
  if (status === 'Active') return 'success';
  if (status === 'Failed') return 'danger';
  if (status === 'Missing') return 'warning';
  return 'neutral';
}

export function scheduleSummary(schedule, t) {
  if (!schedule || schedule.type === 'Manual' || !schedule.type) return t('crawl.scheduleManual');
  if (schedule.type === 'Interval') return t('crawl.scheduleEvery', { minutes: schedule.intervalMinutes });
  return `${schedule.cronExpression || ''} (${schedule.timeZone || 'UTC'})`;
}

// Settings object -> form values (lists as newline text, everything else as-is; secrets always start empty).
export function settingsToForm(fields, settings) {
  const values = {};
  for (const f of fields || []) {
    if (f.kind === 'secret') { values[f.name] = ''; continue; }
    const v = settings ? settings[f.name] : undefined;
    if (f.kind === 'list') values[f.name] = Array.isArray(v) ? v.join('\n') : (f.default ?? '');
    else if (f.kind === 'boolean') values[f.name] = v === undefined || v === null ? f.default === 'true' : !!v;
    else if (v === undefined || v === null) values[f.name] = f.default ?? '';
    else values[f.name] = String(v);
  }
  return values;
}

// Form values -> settings object. Empty secrets are omitted so the server keeps the stored value.
export function formToSettings(fields, values) {
  const out = {};
  for (const f of fields || []) {
    const v = values[f.name];
    if (f.kind === 'secret') { if (v) out[f.name] = v; continue; }
    if (f.kind === 'list') { out[f.name] = String(v || '').split('\n').map((s) => s.trim()).filter(Boolean); continue; }
    if (f.kind === 'boolean') { out[f.name] = !!v; continue; }
    if (f.kind === 'integer') { if (v !== '' && v !== null && v !== undefined) out[f.name] = Number(v); continue; }
    out[f.name] = v === '' ? null : v;
  }
  return out;
}

export function linesToList(text) {
  return String(text || '').split('\n').map((s) => s.trim()).filter(Boolean);
}
