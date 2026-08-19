import { useState, useMemo, useCallback, useEffect } from 'react';
import Pagination from './Pagination';

/**
 * Reusable client-side data table with sortable headers and an above-table
 * pagination/control bar. For remote/server pagination, pass controlled
 * `page`, `totalItems`, `onPageChange`, `onPageSizeChange` and set `manual`.
 *
 * columns: [{ key, label, sortable, render(value, row), className, sortAccessor }]
 */
function DataTable({
  columns = [],
  data = [],
  loading = false,
  emptyTitle = 'No data',
  emptyDescription = '',
  onRowClick = null,
  rowKey = (row, i) => row.id || row.guid || i,
  initialPageSize = 25,
  onRefresh = null,
  toolbar = null,
  manual = false,
  page: controlledPage,
  totalItems: controlledTotal,
  onPageChange: controlledPageChange,
  onPageSizeChange: controlledPageSizeChange
}) {
  const [internalPage, setInternalPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [sortKey, setSortKey] = useState(null);
  const [sortDir, setSortDir] = useState('asc');

  useEffect(() => {
    if (!manual) setInternalPage(1);
  }, [data, manual]);

  const sorted = useMemo(() => {
    if (manual || !sortKey) return data;
    const col = columns.find((c) => c.key === sortKey);
    const accessor = col?.sortAccessor || ((row) => row[sortKey]);
    const copy = [...data];
    copy.sort((a, b) => {
      const av = accessor(a);
      const bv = accessor(b);
      if (av == null && bv == null) return 0;
      if (av == null) return 1;
      if (bv == null) return -1;
      if (av < bv) return sortDir === 'asc' ? -1 : 1;
      if (av > bv) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return copy;
  }, [data, columns, sortKey, sortDir, manual]);

  const page = manual ? controlledPage : internalPage;
  const totalItems = manual ? controlledTotal ?? 0 : sorted.length;
  const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));

  const pageRows = useMemo(() => {
    if (manual) return sorted;
    const start = (internalPage - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, internalPage, pageSize, manual]);

  const handleSort = useCallback(
    (col) => {
      if (col.sortable === false || manual) return;
      if (sortKey === col.key) {
        setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'));
      } else {
        setSortKey(col.key);
        setSortDir('asc');
      }
    },
    [sortKey, manual]
  );

  const handlePageChange = (p) => {
    if (manual) controlledPageChange?.(p);
    else setInternalPage(p);
  };

  const handlePageSizeChange = (size) => {
    setPageSize(size);
    if (manual) controlledPageSizeChange?.(size);
    else setInternalPage(1);
  };

  return (
    <div className="table-frame">
      <Pagination
        currentPage={page}
        totalPages={totalPages}
        pageSize={pageSize}
        totalItems={totalItems}
        onPageChange={handlePageChange}
        onPageSizeChange={handlePageSizeChange}
        onRefresh={onRefresh}
        extraControls={toolbar}
      />
      <div className="table-scroll">
        <table className="data-table">
          <thead>
            <tr>
              {columns.map((col) => (
                <th
                  key={col.key}
                  scope="col"
                  className={`${col.sortable !== false && !manual ? 'sortable' : ''} ${col.className || ''}`}
                  onClick={() => handleSort(col)}
                  aria-sort={
                    sortKey === col.key ? (sortDir === 'asc' ? 'ascending' : 'descending') : 'none'
                  }
                >
                  {col.label}
                  {sortKey === col.key && <span> {sortDir === 'asc' ? '▲' : '▼'}</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={columns.length} className="table-loading">
                  <span className="loading-spinner" />
                </td>
              </tr>
            ) : pageRows.length === 0 ? (
              <tr>
                <td colSpan={columns.length} className="table-empty">
                  <div className="empty-state-title">{emptyTitle}</div>
                  {emptyDescription && (
                    <div className="empty-state-description">{emptyDescription}</div>
                  )}
                </td>
              </tr>
            ) : (
              pageRows.map((row, i) => (
                <tr
                  key={rowKey(row, i)}
                  className={onRowClick ? 'clickable-row' : ''}
                  onClick={onRowClick ? () => onRowClick(row) : undefined}
                >
                  {columns.map((col) => (
                    <td key={col.key} className={col.className || ''}>
                      {col.render ? col.render(row[col.key], row) : row[col.key]}
                    </td>
                  ))}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default DataTable;
