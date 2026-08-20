import { useState, useMemo, useEffect } from 'react';
import { useTranslation } from 'react-i18next';

const INTERACTIVE = ['button', 'a', 'input', 'select', 'textarea', 'label', '[role="button"]', '[data-row-click-ignore="true"]'].join(', ');

const PAGE_SIZES = [10, 25, 50, 100];

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
  emptyMessage = null
}) {
  const { t } = useTranslation();
  const [page, setPage] = useState(0); // zero-based, client mode
  const [pageSize, setPageSize] = useState(defaultPageSize);
  const [sort, setSort] = useState({ key: null, dir: 'asc' });
  const [pageInput, setPageInput] = useState('1');

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
            <select value={effPageSize} onChange={(e) => changePageSize(Number(e.target.value))} aria-label={t('table.pageSize')}>
              {PAGE_SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
            <button type="button" onClick={() => goTo(0)} disabled={currentPage === 0}>{t('table.first')}</button>
            <button type="button" onClick={() => goTo(currentPage - 1)} disabled={currentPage === 0}>{t('table.prev')}</button>
            <span className="page-input-container">
              {t('table.page')}{' '}
              <input className="page-input" type="text" value={pageInput}
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
            <button type="button" onClick={() => goTo(currentPage + 1)} disabled={currentPage >= totalPages - 1}>{t('table.next')}</button>
            <button type="button" onClick={() => goTo(totalPages - 1)} disabled={currentPage >= totalPages - 1}>{t('table.last')}</button>
          </div>
          {onRefresh && (
            <button type="button" className="icon-button" onClick={onRefresh} title={t('common.refresh')} aria-label={t('common.refresh')} disabled={loading}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" style={loading ? { animation: 'spin 1s linear infinite' } : undefined}>
                <polyline points="23 4 23 10 17 10" />
                <polyline points="1 20 1 14 7 14" />
                <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
              </svg>
            </button>
          )}
        </div>
      </div>

      <div className="table-scroll">
        {loading ? (
          <div className="table-loading"><div className="loading-spinner" /><span>{t('common.loading')}</span></div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
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
                <tr><td className="no-data" colSpan={columns.length}>{emptyMessage || t('common.noData')}</td></tr>
              ) : (
                pageRows.map((item, i) => (
                  <tr key={item.id || item.guid || item.Id || i}
                    className={onRowClick ? 'clickable' : ''}
                    onClick={(e) => handleRowClick(e, item)}>
                    {columns.map((col) => (
                      <td key={col.key} className={col.cellClass || ''}>
                        {col.render ? col.render(item) : item[col.key]}
                      </td>
                    ))}
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}

export default DataTable;
