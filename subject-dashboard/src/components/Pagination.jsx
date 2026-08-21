import { useId, useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100, 250, 500, 1000];
// Auto-refresh interval options (seconds); 0 = off.
const AUTO_REFRESH_OPTIONS = [
  { value: 0, label: 'None' },
  { value: 15, label: '15 seconds' },
  { value: 30, label: '30 seconds' },
  { value: 60, label: '60 seconds' },
  { value: 180, label: '180 seconds' },
  { value: 300, label: '300 seconds' }
];

// Persist the auto-refresh choice per route so a table keeps its interval between page visits.
const AUTO_REFRESH_STORAGE_PREFIX = 'pneuma.autoRefreshSec:';
function readStoredAutoRefresh() {
  try {
    const val = Number(window.localStorage.getItem(AUTO_REFRESH_STORAGE_PREFIX + window.location.pathname));
    return AUTO_REFRESH_OPTIONS.some((o) => o.value === val) ? val : 0;
  } catch {
    return 0;
  }
}

/**
 * Above-table pagination + control bar: total range, page size, first/prev/
 * jump/next/last, and refresh.
 */
function Pagination({
  currentPage,
  totalPages,
  pageSize,
  totalItems,
  onPageChange,
  onPageSizeChange,
  onRefresh,
  pageSizeOptions = PAGE_SIZE_OPTIONS,
  extraControls = null
}) {
  const { t } = useTranslation();
  const jumpId = useId();
  const pages = Math.max(1, totalPages || 1);
  const [autoRefreshSec, setAutoRefreshSec] = useState(readStoredAutoRefresh);

  const handleAutoRefreshChange = (e) => {
    const val = Number(e.target.value);
    setAutoRefreshSec(val);
    try { window.localStorage.setItem(AUTO_REFRESH_STORAGE_PREFIX + window.location.pathname, String(val)); } catch { /* ignore */ }
  };

  // Auto-refresh: re-run onRefresh on the chosen interval. A ref keeps the timer pointed at the latest
  // callback without restarting on every render (only a changed interval resets the timer).
  const onRefreshRef = useRef(onRefresh);
  useEffect(() => { onRefreshRef.current = onRefresh; }, [onRefresh]);
  useEffect(() => {
    if (!autoRefreshSec || !onRefresh) return undefined;
    const timer = setInterval(() => { onRefreshRef.current?.(); }, autoRefreshSec * 1000);
    return () => clearInterval(timer);
  }, [autoRefreshSec, onRefresh]);

  const startItem = totalItems === 0 ? 0 : (currentPage - 1) * pageSize + 1;
  const endItem = Math.min(currentPage * pageSize, totalItems);

  const handleJump = (e) => {
    if (e.key === 'Enter') {
      const page = parseInt(e.target.value, 10);
      if (page >= 1 && page <= pages) onPageChange(page);
      e.target.value = '';
    }
  };

  return (
    <div className="pagination">
      <div className="pagination-info">
        Showing {startItem}-{endItem} of {totalItems}
      </div>
      <div className="pagination-controls">
        {extraControls}
        {onPageSizeChange && (
          <div className="pagination-group">
            <label>Per page:</label>
            <select
              value={pageSize}
              onChange={(e) => onPageSizeChange(parseInt(e.target.value, 10))}
              title="Items per page"
            >
              {pageSizeOptions.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </select>
          </div>
        )}
        <div className="pagination-nav">
          <button
            className="pagination-btn"
            onClick={() => onPageChange(1)}
            disabled={currentPage <= 1}
            title="First page"
          >
            &laquo;
          </button>
          <button
            className="pagination-btn"
            onClick={() => onPageChange(currentPage - 1)}
            disabled={currentPage <= 1}
            title="Previous page"
          >
            &lsaquo;
          </button>
          <span className="pagination-current">
            {currentPage} / {pages}
          </span>
          <button
            className="pagination-btn"
            onClick={() => onPageChange(currentPage + 1)}
            disabled={currentPage >= pages}
            title="Next page"
          >
            &rsaquo;
          </button>
          <button
            className="pagination-btn"
            onClick={() => onPageChange(pages)}
            disabled={currentPage >= pages}
            title="Last page"
          >
            &raquo;
          </button>
        </div>
        <div className="pagination-group pagination-jump">
          <label htmlFor={jumpId}>Go to:</label>
          <input id={jumpId} type="number" min="1" max={pages} placeholder="#" onKeyDown={handleJump} />
        </div>
        {onRefresh && (
          <label className="auto-refresh-control" title="Automatically reload this table on the selected interval.">
            <span className="auto-refresh-label">{t('table.autoRefresh', 'Auto-refresh')}</span>
            <select value={autoRefreshSec} onChange={handleAutoRefreshChange}
              aria-label={t('table.autoRefresh', 'Auto-refresh')}>
              {AUTO_REFRESH_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
            </select>
          </label>
        )}
        {onRefresh && (
          <button className="pagination-btn" onClick={onRefresh} title={t('common.refresh')}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M23 4v6h-6" />
              <path d="M1 20v-6h6" />
              <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
            </svg>
          </button>
        )}
      </div>
    </div>
  );
}

export default Pagination;
