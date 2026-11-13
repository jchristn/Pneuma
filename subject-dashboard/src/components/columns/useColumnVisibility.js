import { useMemo, useState, useCallback } from 'react';

// Which columns a table shows. Kept identical in the admin, subject, and user dashboards.
//
// Column flags: `required` (always shown, cannot be hidden), `defaultHidden` (hidden until the user turns it on),
// `pickerLabel` (the name in the picker when `label` is not plain text). A table that sets `defaultHidden` on any
// column has chosen its own defaults; otherwise the cap below applies. The first column (the row's identity) and
// columns with no label (actions, row menus) are required unless they say `required: false`. When a table has more
// optional columns than `maxDefault`, the ones past that start hidden so the table fits without a horizontal
// scrollbar. Each viewer's choice is remembered per table in this browser; "Reset" returns to the defaults.

const STORAGE_PREFIX = 'pneuma.columns:';
export const DEFAULT_MAX_OPTIONAL = 5;

function plainText(label) {
  if (typeof label === 'string' || typeof label === 'number') return String(label);
  return '';
}

export function columnLabel(col) {
  return col.pickerLabel || plainText(col.label) || col.key;
}

// Row-action columns (the "..." menu) are always shown, whatever their label.
const ACTION_KEYS = ['actions', '_actions', 'action', '_action', 'menu', '_menu'];

export function isRequired(col, index) {
  if (col.required === true || col.hideable === false) return true;
  if (col.required === false) return false;
  if (index === 0) return true;
  if (ACTION_KEYS.includes(String(col.key).toLowerCase())) return true;
  return !plainText(col.label) && !col.pickerLabel;
}

function defaultVisible(columns, maxOptional) {
  const visible = new Set();
  let optional = 0;
  // A table that sets defaultHidden on any column has chosen its defaults; the automatic cap does not apply.
  const explicit = columns.some((c) => Object.prototype.hasOwnProperty.call(c, 'defaultHidden'));
  const cap = explicit ? Infinity : maxOptional;
  columns.forEach((c, i) => {
    if (isRequired(c, i)) { visible.add(c.key); return; }
    if (c.defaultHidden) return;
    if (optional < cap) { visible.add(c.key); optional += 1; }
  });
  return visible;
}

function readStored(key) {
  try {
    const raw = window.localStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    return parsed && Array.isArray(parsed.hidden) && Array.isArray(parsed.shown) ? parsed : null;
  } catch {
    return null;
  }
}

function writeStored(key, value) {
  try {
    if (value === null) window.localStorage.removeItem(key);
    else window.localStorage.setItem(key, JSON.stringify(value));
  } catch { /* storage unavailable: the choice lasts until reload */ }
}

export function useColumnVisibility(columns, { tableId = null, maxDefault = DEFAULT_MAX_OPTIONAL } = {}) {
  const keys = columns.map((c) => c.key);
  const keySig = keys.join('|');
  const storageKey = STORAGE_PREFIX + (tableId || `${typeof window !== 'undefined' ? window.location.pathname : ''}#${keySig}`);
  // The user's explicit choices: columns they turned on and off. Anything not chosen follows the defaults, so a
  // column added in a later release appears (or not) by its default.
  const [choice, setChoice] = useState(() => readStored(storageKey) || { hidden: [], shown: [] });
  const [loadedKey, setLoadedKey] = useState(storageKey);
  if (loadedKey !== storageKey) {
    setLoadedKey(storageKey);
    setChoice(readStored(storageKey) || { hidden: [], shown: [] });
  }

  const defaults = useMemo(() => defaultVisible(columns, maxDefault), [columns, maxDefault]);

  const visible = useMemo(() => {
    const set = new Set();
    columns.forEach((c, i) => {
      if (isRequired(c, i)) set.add(c.key);
      else if (choice.shown.includes(c.key)) set.add(c.key);
      else if (!choice.hidden.includes(c.key) && defaults.has(c.key)) set.add(c.key);
    });
    return set;
  }, [columns, choice, defaults]);

  const save = useCallback((next) => { setChoice(next); writeStored(storageKey, next); }, [storageKey]);

  const toggle = useCallback((key) => {
    const on = visible.has(key);
    const hidden = choice.hidden.filter((k) => k !== key);
    const shown = choice.shown.filter((k) => k !== key);
    if (on) hidden.push(key); else shown.push(key);
    save({ hidden, shown });
  }, [visible, choice, save]);

  const showAll = useCallback(() => save({ hidden: [], shown: keys.slice() }), [keySig, save]); // eslint-disable-line react-hooks/exhaustive-deps
  const reset = useCallback(() => { setChoice({ hidden: [], shown: [] }); writeStored(storageKey, null); }, [storageKey]);

  const visibleColumns = useMemo(() => columns.filter((c) => visible.has(c.key)), [columns, visible]);
  const isDefault = choice.hidden.length === 0 && choice.shown.length === 0;

  return { visibleColumns, picker: { columns, visible, toggle, showAll, reset, isDefault } };
}
