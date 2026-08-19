// Locale-aware formatting helpers. All formatting flows through these so the
// selected locale is applied explicitly rather than relying on browser defaults.
import i18n from './index';
import { normalizeLocale } from './localeRegistry';

function locale() {
  return normalizeLocale(i18n.language);
}

export function formatNumber(value, options = {}) {
  if (value === null || value === undefined || value === '') return '';
  return new Intl.NumberFormat(locale(), options).format(Number(value));
}

export function formatDate(value, options = { year: 'numeric', month: 'short', day: 'numeric' }) {
  if (!value) return '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return String(value);
  return new Intl.DateTimeFormat(locale(), options).format(d);
}

export function formatTime(value, options = { hour: '2-digit', minute: '2-digit' }) {
  if (!value) return '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return String(value);
  return new Intl.DateTimeFormat(locale(), options).format(d);
}

export function formatDateTime(value, options = {
  year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit'
}) {
  if (!value) return '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return String(value);
  return new Intl.DateTimeFormat(locale(), options).format(d);
}

export function formatRelativeTime(value) {
  if (!value) return '';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return String(value);
  const rtf = new Intl.RelativeTimeFormat(locale(), { numeric: 'auto' });
  const diffSec = Math.round((d.getTime() - Date.now()) / 1000);
  const abs = Math.abs(diffSec);
  if (abs < 60) return rtf.format(diffSec, 'second');
  if (abs < 3600) return rtf.format(Math.round(diffSec / 60), 'minute');
  if (abs < 86400) return rtf.format(Math.round(diffSec / 3600), 'hour');
  return rtf.format(Math.round(diffSec / 86400), 'day');
}

export function formatDuration(ms) {
  if (ms === null || ms === undefined || ms === '') return '';
  const n = Number(ms);
  if (Number.isNaN(n)) return String(ms);
  if (n < 1000) return `${formatNumber(Math.round(n))} ms`;
  return `${formatNumber(n / 1000, { maximumFractionDigits: 2 })} s`;
}

export function formatBytes(bytes) {
  if (bytes === null || bytes === undefined || bytes === '') return '';
  const n = Number(bytes);
  if (Number.isNaN(n)) return String(bytes);
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  let val = n;
  while (val >= 1024 && i < units.length - 1) { val /= 1024; i += 1; }
  return `${formatNumber(val, { maximumFractionDigits: i === 0 ? 0 : 1 })} ${units[i]}`;
}

export function formatPercent(value, options = { maximumFractionDigits: 1 }) {
  if (value === null || value === undefined || value === '') return '';
  return new Intl.NumberFormat(locale(), { style: 'percent', ...options }).format(Number(value));
}

export function formatList(items = []) {
  try {
    return new Intl.ListFormat(locale(), { style: 'long', type: 'conjunction' }).format(items);
  } catch {
    return items.join(', ');
  }
}
