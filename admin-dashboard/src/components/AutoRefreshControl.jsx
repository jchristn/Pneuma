import { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';

// Auto-refresh interval options (seconds); 0 = off. Matches the table toolbar control.
const AUTO_REFRESH_OPTIONS = [
  { value: 0, label: 'None' },
  { value: 15, label: '15 seconds' },
  { value: 30, label: '30 seconds' },
  { value: 60, label: '60 seconds' },
  { value: 180, label: '180 seconds' },
  { value: 300, label: '300 seconds' }
];

// Persist the choice per surface (for example a modal) so it is kept the next time that surface opens.
const AUTO_REFRESH_STORAGE_PREFIX = 'pneuma.autoRefreshSec:';
function readStored(storageKey) {
  try {
    const val = Number(window.localStorage.getItem(AUTO_REFRESH_STORAGE_PREFIX + storageKey));
    return AUTO_REFRESH_OPTIONS.some((o) => o.value === val) ? val : 0;
  } catch {
    return 0;
  }
}

/**
 * Auto-refresh interval selector for surfaces outside a table toolbar (for example detail modals). Calls
 * onRefresh on the chosen interval; a tick is skipped while the previous refresh is still running.
 */
function AutoRefreshControl({ storageKey, onRefresh, title }) {
  const { t } = useTranslation();
  const [seconds, setSeconds] = useState(() => readStored(storageKey));
  const onRefreshRef = useRef(onRefresh);
  const busyRef = useRef(false);
  useEffect(() => { onRefreshRef.current = onRefresh; }, [onRefresh]);

  useEffect(() => {
    if (!seconds) return undefined;
    const timer = setInterval(async () => {
      if (busyRef.current || !onRefreshRef.current) return;
      busyRef.current = true;
      try { await onRefreshRef.current(); } catch { /* the surface shows its own errors */ } finally { busyRef.current = false; }
    }, seconds * 1000);
    return () => clearInterval(timer);
  }, [seconds]);

  const handleChange = (e) => {
    const val = Number(e.target.value);
    setSeconds(val);
    try { window.localStorage.setItem(AUTO_REFRESH_STORAGE_PREFIX + storageKey, String(val)); } catch { /* ignore */ }
  };

  return (
    <label className="auto-refresh-control" title={title || t('table.autoRefreshTip', 'Automatically reload on the selected interval.')}>
      <span className="auto-refresh-label">{t('table.autoRefresh', 'Auto-refresh')}</span>
      <select value={seconds} onChange={handleChange} aria-label={t('table.autoRefresh', 'Auto-refresh')}>
        {AUTO_REFRESH_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
      </select>
    </label>
  );
}

export default AutoRefreshControl;
