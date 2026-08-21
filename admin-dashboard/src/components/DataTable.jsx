import { useState, useMemo, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';

const INTERACTIVE = ['button', 'a', 'input', 'select', 'textarea', 'label', '[role="button"]', '[data-row-click-ignore="true"]'].join(', ');

const PAGE_SIZES = [10, 25, 50, 100];
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
 * Reusable data table with above-table pagination.
 * Client mode (default): pass `data`, sorting/paging handled internally.
 * Server mode: pass `server={{ totalCount, pageNumber, pageSize, onPageChange, onPageSizeChange }}`.
 */
function DataTable({
  columns = [],
  data = [],
  loading = false,
  onRowClick = null,
  toolbarLeft = null,
  toolbarRight = null,
  onRefresh = null,
  defaultPageSize = 25,
  server = null,
  emptyMessage = null,
  selection = null,
  bulkBar = null
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState(0); // zero-based, client mode
  const [pageSize, setPageSize] = useState(defaultPageSize);
  const [sort, setSort] = useState({ key: null, dir: 'asc' });
  const [pageInput, setPageInput] = useState('1');
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

  const isServer = !!server;

  const sorted = useMemo(() => {
    if (isServer || !sort.key) return data;
    const col = columns.find((c) => c.key === sort.key);
    const arr = [...data];
    arr.sort((a, b) => {
      const av = col?.sortValue ? col.sortValue(a) : a[sort.key];
      const bv = col?.sortValue ? col.sortValue(b) : b[sort.key];
      if (av === null || av === undefined) return 1;
      if (bv === null || bv === undefined) return -1;
      const cmp = String(av).localeCompare(String(bv), undefined, { numeric: true });
      return sort.dir === 'asc' ? cmp : -cmp;
    });
    return arr;
  }, [data, columns, sort, isServer]);

  const totalCount = isServer ? server.totalCount : sorted.length;
  const currentPage = isServer ? (server.pageNumber - 1) : page;
  const effPageSize = isServer ? server.pageSize : pageSize;
  const totalPages = Math.max(1, Math.ceil(totalCount / effPageSize));

  useEffect(() => { setPageInput(String(currentPage + 1)); }, [currentPage]);

  const pageRows = isServer ? data : sorted.slice(currentPage * pageSize, currentPage * pageSize + pageSize);
  const startIndex = totalCount === 0 ? 0 : currentPage * effPageSize + 1;
  const endIndex = Math.min(currentPage * effPageSize + effPageSize, totalCount);

  // Optional multi-select. The parent owns the selected-id Set; the header checkbox toggles every row on the
  // current page, while the Set persists across pages so a selection can be built up while paging.
  const selEnabled = !!selection;
  const rowId = (item) => (selection?.getRowId ? selection.getRowId(item) : (item.id ?? item.Id ?? item.guid ?? item.GUID));
  const selectedIds = selection?.selectedIds ?? null;
  const pageIds = selEnabled ? pageRows.map(rowId) : [];
  const allOnPageSelected = selEnabled && pageIds.length > 0 && pageIds.every((id) => selectedIds.has(id));
  const someOnPageSelected = selEnabled && pageIds.some((id) => selectedIds.has(id));

  const toggleAllOnPage = () => {
    const next = new Set(selectedIds);
    if (allOnPageSelected) pageIds.forEach((id) => next.delete(id));
    else pageIds.forEach((id) => next.add(id));
    selection.onChange(next);
  };

  const toggleOne = (id) => {
    const next = new Set(selectedIds);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    selection.onChange(next);
  };

  const totalColumns = columns.length + (selEnabled ? 1 : 0);

  const goTo = (p) => {
    const valid = Math.max(0, Math.min(p, totalPages - 1));
    if (isServer) server.onPageChange(valid + 1);
    else setPage(valid);
  };

  const changePageSize = (size) => {
    if (isServer) server.onPageSizeChange(size);
    else { setPageSize(size); setPage(0); }
  };

  const handleSort = (col) => {
    if (col.sortable === false || isServer) return;
    setSort((prev) => ({ key: col.key, dir: prev.key === col.key && prev.dir === 'asc' ? 'desc' : 'asc' }));
  };

  const handleRowClick = (e, item) => {
    if (!onRowClick) return;
    const target = e.target instanceof Element ? e.target : e.currentTarget;
    if (target.closest(INTERACTIVE)) return;
    onRowClick(item);
  };

  const sortIcon = (key) => {
    if (sort.key !== key) return <span style={{ opacity: 0.3 }}>↕</span>;
    return <span>{sort.dir === 'asc' ? '▲' : '▼'}</span>;
  };

  return (
    <div className="table-frame">
      <div className="table-toolbar">
        <div className="table-toolbar-left">
          <span className="pagination-info">
            {t('table.showing', { from: startIndex, to: endIndex, total: totalCount })}
          </span>
          {toolbarLeft}
        </div>
        <div className="table-toolbar-right">
          {toolbarRight}
          <div className="pagination-controls">
            <select value={effPageSize} onChange={(e) => changePageSize(Number(e.target.value))} aria-label={t('table.pageSize')}
              title="How many rows to show per page.">
              {PAGE_SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
            <button type="button" onClick={() => goTo(0)} disabled={currentPage === 0} title="Jump to the first page.">{t('table.first')}</button>
            <button type="button" onClick={() => goTo(currentPage - 1)} disabled={currentPage === 0} title="Go to the previous page.">{t('table.prev')}</button>
            <span className="page-input-container">
              {t('table.page')}{' '}
              <input className="page-input" type="text" value={pageInput} title="Type a page number and press Enter to jump to it."
                onChange={(e) => setPageInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    const n = parseInt(pageInput, 10);
                    if (!Number.isNaN(n) && n >= 1 && n <= totalPages) goTo(n - 1);
                    else setPageInput(String(currentPage + 1));
                  }
                }} />{' '}
              {t('table.of')} {totalPages}
            </span>
            <button type="button" onClick={() => goTo(currentPage + 1)} disabled={currentPage >= totalPages - 1} title="Go to the next page.">{t('table.next')}</button>
            <button type="button" onClick={() => goTo(totalPages - 1)} disabled={currentPage >= totalPages - 1} title="Jump to the last page.">{t('table.last')}</button>
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
            <button type="button" className="icon-button" onClick={onRefresh} title="Reload this table from the server." aria-label={t('common.refresh')} disabled={loading}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={loading ? { animation: 'spin 1s linear infinite' } : undefined}>
                <polyline points="23 4 23 10 17 10" />
                <polyline points="1 20 1 14 7 14" />
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
              </svg>
            </button>
          )}
        </div>
      </div>

      {selEnabled && selectedIds.size > 0 && bulkBar && (
        <div className="table-bulkbar">{bulkBar}</div>
      )}

      <div className="table-scroll">
        {loading ? (
          <div className="table-loading"><div className="loading-spinner" /><span>{t('common.loading')}</span></div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                {selEnabled && (
                  <th scope="col" className="select-col">
                    <input
                      type="checkbox"
                      checked={allOnPageSelected}
                      ref={(el) => { if (el) el.indeterminate = someOnPageSelected && !allOnPageSelected; }}
                      onChange={toggleAllOnPage}
                      aria-label={t('table.selectAll', 'Select all rows on this page')}
                      title={t('table.selectAll', 'Select all rows on this page')}
                    />
                  </th>
                )}
                {columns.map((col) => (
                  <th
                    key={col.key}
                    scope="col"
                    className={col.sortable !== false && !isServer ? 'sortable' : ''}
                    style={col.width ? { width: col.width } : undefined}
                    onClick={() => handleSort(col)}
                    title={col.tip || (col.sortable !== false && !isServer ? 'Click to sort by this column.' : undefined)}
                  >
                    <span className="th-content">
                      {col.label}
                      {col.sortable !== false && !isServer && sortIcon(col.key)}
                    </span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {pageRows.length === 0 ? (
                <tr><td className="no-data" colSpan={totalColumns}>{emptyMessage || t('common.noData')}</td></tr>
              ) : (
                pageRows.map((item, i) => {
                  const id = selEnabled ? rowId(item) : null;
                  const isSelected = selEnabled && selectedIds.has(id);
                  return (
                  <tr key={item.id || item.guid || item.Id || i}
                    className={`${onRowClick ? 'clickable' : ''}${isSelected ? ' row-selected' : ''}`}
                    onClick={(e) => handleRowClick(e, item)}>
                    {selEnabled && (
                      <td className="select-col">
                        <input
                          type="checkbox"
                          checked={isSelected}
                          onClick={(e) => e.stopPropagation()}
                          onChange={() => toggleOne(id)}
                          aria-label={t('table.selectRow', 'Select this row')}
                        />
                      </td>
                    )}
                    {columns.map((col) => (
                      <td key={col.key} className={col.cellClass || ''}>
                        {col.render ? col.render(item) : item[col.key]}
                      </td>
                    ))}
                  </tr>
                  );
                })
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

export default DataTable;
