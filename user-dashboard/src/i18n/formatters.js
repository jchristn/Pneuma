/**
 * Locale-aware formatting helpers. All formatting goes through explicit-locale
 * Intl wrappers rather than relying on the browser default, so output stays
 * consistent with the selected UI locale.
 */
import i18n from './index.js';
import { normalizeLocale } from './localeRegistry.js';

function activeLocale(locale) {
  return normalizeLocale(locale || i18n.language);
}

export function formatNumber(value, locale, options = {}) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(activeLocale(locale), options).format(Number(value));
}

export function formatPercent(value, locale, fractionDigits = 0) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(activeLocale(locale), {
    style: 'percent',
    minimumFractionDigits: fractionDigits,
    maximumFractionDigits: fractionDigits
  }).format(Number(value));
}

export function formatDate(utc, locale, options = { dateStyle: 'medium' }) {
  if (!utc) return '';
  const date = new Date(utc);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(activeLocale(locale), options).format(date);
}

export function formatTime(utc, locale, options = { timeStyle: 'short' }) {
  if (!utc) return '';
  const date = new Date(utc);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(activeLocale(locale), options).format(date);
}

export function formatDateTime(utc, locale, options = { dateStyle: 'medium', timeStyle: 'short' }) {
  if (!utc) return '';
  const date = new Date(utc);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(activeLocale(locale), options).format(date);
}

export function formatRelativeTime(utc, locale) {
  if (!utc) return '';
  const then = new Date(utc).getTime();
  if (Number.isNaN(then)) return '';
  const rtf = new Intl.RelativeTimeFormat(activeLocale(locale), { numeric: 'auto' });
  const diffSeconds = Math.round((then - Date.now()) / 1000);
  const abs = Math.abs(diffSeconds);
  if (abs < 60) return rtf.format(diffSeconds, 'second');
  const minutes = Math.round(diffSeconds / 60);
  if (Math.abs(minutes) < 60) return rtf.format(minutes, 'minute');
  const hours = Math.round(minutes / 60);
  if (Math.abs(hours) < 24) return rtf.format(hours, 'hour');
  const days = Math.round(hours / 24);
  return rtf.format(days, 'day');
}

export function formatList(items, locale, options = { style: 'long', type: 'conjunction' }) {
  const values = (items || []).filter(Boolean).map(String);
  if (values.length === 0) return '';
  if (typeof Intl.ListFormat !== 'function') return values.join(', ');
  return new Intl.ListFormat(activeLocale(locale), options).format(values);
}
