/**
 * Locale-aware formatting helpers. These wrap Intl with an explicit locale so
 * output is deterministic and can follow the selected UI language.
 */

let currentLocale = 'en';

export function setFormatterLocale(locale) {
  currentLocale = locale || 'en';
}

export function formatNumber(value, options = {}) {
  if (value === null || value === undefined || Number.isNaN(value)) return '0';
  return new Intl.NumberFormat(currentLocale, options).format(value);
}

export function formatDateTime(value) {
  if (!value) return 'n/a';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return 'n/a';
  return new Intl.DateTimeFormat(currentLocale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  }).format(d);
}

export function formatDate(value) {
  if (!value) return 'n/a';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return 'n/a';
  return new Intl.DateTimeFormat(currentLocale, {
    year: 'numeric',
    month: 'short',
    day: 'numeric'
  }).format(d);
}

const RELATIVE_DIVISIONS = [
  { amount: 60, unit: 'second' },
  { amount: 60, unit: 'minute' },
  { amount: 24, unit: 'hour' },
  { amount: 7, unit: 'day' },
  { amount: 4.34524, unit: 'week' },
  { amount: 12, unit: 'month' },
  { amount: Number.POSITIVE_INFINITY, unit: 'year' }
];

/**
 * Human-friendly relative time, e.g. "3 minutes ago" / "in 2 days".
 */
export function formatRelativeTime(value) {
  if (!value) return 'never';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return 'never';

  const rtf = new Intl.RelativeTimeFormat(currentLocale, { numeric: 'auto' });
  let duration = (d.getTime() - Date.now()) / 1000;

  for (const division of RELATIVE_DIVISIONS) {
    if (Math.abs(duration) < division.amount) {
      return rtf.format(Math.round(duration), division.unit);
    }
    duration /= division.amount;
  }
  return rtf.format(Math.round(duration), 'year');
}

export function formatDurationMs(ms) {
  if (ms === null || ms === undefined || Number.isNaN(ms)) return 'n/a';
  if (ms < 1000) return `${Math.round(ms)} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(2)} s`;
  const minutes = Math.floor(seconds / 60);
  const rem = Math.round(seconds % 60);
  return `${minutes}m ${rem}s`;
}

export function formatBytes(bytes) {
  if (!bytes) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let value = bytes;
  let index = 0;
  while (value >= 1024 && index < units.length - 1) {
    value /= 1024;
    index += 1;
  }
  return `${value.toFixed(value >= 10 || index === 0 ? 0 : 1)} ${units[index]}`;
}

export function formatPercent(value, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(value)) return '0%';
  return `${value.toFixed(digits)}%`;
}
