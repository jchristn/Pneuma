import { useMemo, useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import './DataGrid.css';

// A client-side results grid for modals that show many rows (crawl operation objects, previews, ingestion
// progress): fuzzy search with column:value and -exclude terms, sortable headers, facet chips with counts, CSV
// export, and incremental rendering. It scrolls inside its own frame (sticky header) and wraps long values, so the
// surrounding modal never scrolls horizontally. Kept identical in the admin and subject dashboards.
//
// columns: [{ key, label, value(row) -> string|number (search, sort, CSV, facet), render?(row), width?, facet?,
//             sortable? (default true), fuzzy? (subsequence matching for this column), mono?, align? }]

const PAGE = 200;

function text(v) {
  if (v === null || v === undefined) return '';
  return String(v);
}

function subsequence(needle, hay) {
  let i = 0;
  for (let j = 0; j < hay.length && i < needle.length; j += 1) if (hay[j] === needle[i]) i += 1;
  return i === needle.length;
}

// Parse the search box: plain terms, "column:value" terms, and "-term" exclusions. Quoted phrases stay together.
export function parseQuery(query) {
  const parts = String(query || '').match(/-?(?:[^\s:"]+:)?(?:"[^"]*"|\S+)/g) || [];
  return parts.map((raw) => {
    let p = raw;
    const negate = p.startsWith('-') && p.length > 1;
    if (negate) p = p.slice(1);
    let field = null;
    const colon = p.indexOf(':');
    if (colon > 0 && !p.startsWith('"') && !/^[a-z]+:\/\//i.test(p)) {
      field = p.slice(0, colon).toLowerCase();
      p = p.slice(colon + 1);
    }
    p = p.replace(/^"|"$/g, '').toLowerCase();
    return { term: p, field, negate };
  }).filter((q) => q.term.length > 0);
}

function columnFor(columns, field) {
  return columns.find((c) => c.key.toLowerCase() === field)
    || columns.find((c) => String(c.label || '').toLowerCase() === field)
    || columns.find((c) => c.key.toLowerCase().startsWith(field) || String(c.label || '').toLowerCase().startsWith(field));
}

function termMatches(term, hay, fuzzy) {
  if (hay.includes(term)) return true;
  return fuzzy && term.length >= 3 && subsequence(term, hay);
}

function rowMatches(row, terms, columns) {
  return terms.every(({ term, field, negate }) => {
    let hit;
    const col = field ? columnFor(columns, field) : null;
    if (col) hit = termMatches(term, text(col.value(row)).toLowerCase(), !!col.fuzzy);
    else hit = columns.some((c) => termMatches(term, text(c.value(row)).toLowerCase(), !!c.fuzzy));
    return negate ? !hit : hit;
  });
}

function compare(a, b) {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return text(a).localeCompare(text(b), undefined, { numeric: true, sensitivity: 'base' });
}

function csvCell(v) {
  const s = text(v);
  return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
}

function DataGrid({
  rows,
  columns,
  rowKey,
  initialSort = null,
  facets: controlledFacets,
  onFacetsChange,
  emptyText,
  exportName = 'results',
  toolbarExtra = null,
  loading = false,
  note = null
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const [sort, setSort] = useState(initialSort);
  const [ownFacets, setOwnFacets] = useState({});
  const [limit, setLimit] = useState(PAGE);
  const searchRef = useRef(null);
  const facets = controlledFacets || ownFacets;
  const setFacets = onFacetsChange || setOwnFacets;
  const list = useMemo(() => (Array.isArray(rows) ? rows : []), [rows]);

  const facetColumns = useMemo(() => columns.filter((c) => c.facet), [columns]);
  const terms = useMemo(() => parseQuery(query), [query]);

  // Rows that pass the search (facet counts are computed on these, so the chips show what a click would leave).
  const searched = useMemo(() => (terms.length ? list.filter((r) => rowMatches(r, terms, columns)) : list), [list, terms, columns]);

  const filtered = useMemo(() => searched.filter((r) => facetColumns.every((c) => {
    const chosen = facets[c.key];
    return !chosen || chosen.length === 0 || chosen.includes(text(c.value(r)));
  })), [searched, facets, facetColumns]);

  const sorted = useMemo(() => {
    if (!sort) return filtered;
    const col = columns.find((c) => c.key === sort.key);
    if (!col) return filtered;
    const dir = sort.dir === 'desc' ? -1 : 1;
    return [...filtered].sort((a, b) => dir * compare(col.value(a), col.value(b)));
  }, [filtered, sort, columns]);

  useEffect(() => { setLimit(PAGE); }, [query, facets, sort]);

  const facetCounts = useMemo(() => {
    const out = {};
    facetColumns.forEach((c) => {
      const counts = new Map();
      list.forEach((r) => { const v = text(c.value(r)); if (!counts.has(v)) counts.set(v, 0); });
      searched.forEach((r) => { const v = text(c.value(r)); counts.set(v, (counts.get(v) || 0) + 1); });
      out[c.key] = Array.from(counts.entries()).sort((a, b) => b[1] - a[1] || compare(a[0], b[0]));
    });
    return out;
  }, [facetColumns, list, searched]);

  const toggleFacet = (key, value) => {
    const current = facets[key] || [];
    const next = current.includes(value) ? current.filter((v) => v !== value) : [...current, value];
    setFacets({ ...facets, [key]: next });
  };
  const anyFacet = facetColumns.some((c) => (facets[c.key] || []).length > 0);
  const clearAll = () => { setQuery(''); setFacets({}); };

  const toggleSort = (col) => {
    if (col.sortable === false) return;
    setSort((s) => {
      if (!s || s.key !== col.key) return { key: col.key, dir: 'asc' };
      if (s.dir === 'asc') return { key: col.key, dir: 'desc' };
      return null;
    });
  };

  const exportCsv = () => {
    const header = columns.map((c) => csvCell(c.label || c.key)).join(',');
    const body = sorted.map((r) => columns.map((c) => csvCell(c.value(r))).join(',')).join('\r\n');
    const blob = new Blob([`${header}\r\n${body}\r\n`], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${exportName}.csv`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };

  const onSearchKey = (e) => {
    if (e.key === 'Escape' && query) { e.stopPropagation(); setQuery(''); }
  };

  const visible = sorted.slice(0, limit);

  return (
    <div className="dg">
      <div className="dg-toolbar">
        <div className="dg-search">
          <input
            ref={searchRef}
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onSearchKey}
            placeholder={t('grid.searchPlaceholder', 'Filter: words, fuzzy key match, column:value, -exclude')}
            aria-label={t('grid.search', 'Filter rows')}
            title={t('grid.searchTip', 'Every word must match. Words of 3+ letters also fuzzy-match keys (letters in order). Use column:value for one column (for example action:fail or outcome:failed) and -word to exclude. Esc clears.')}
          />
        </div>
        <div className="dg-toolbar-right">
          {/* Narrow screens hide the header row, so sorting moves here. */}
          <select className="dg-sort-select" aria-label={t('grid.sortBy', 'Sort by')}
            value={sort ? `${sort.key}:${sort.dir}` : ''}
            onChange={(e) => { const [key, dir] = e.target.value.split(':'); setSort(key ? { key, dir } : null); }}>
            <option value="">{t('grid.sortNone', 'Sort: default')}</option>
            {columns.filter((c) => c.sortable !== false).flatMap((c) => [
              <option key={`${c.key}:asc`} value={`${c.key}:asc`}>{`${c.label} ▲`}</option>,
              <option key={`${c.key}:desc`} value={`${c.key}:desc`}>{`${c.label} ▼`}</option>
            ])}
          </select>
          {toolbarExtra}
          <button type="button" className="dg-btn" onClick={exportCsv} disabled={sorted.length === 0} title={t('grid.exportTip', 'Download the filtered rows as CSV')}>
            {t('grid.export', 'Export CSV')}
          </button>
        </div>
      </div>

      {facetColumns.length > 0 && (
        <div className="dg-facets">
          {facetColumns.map((c) => (
            <div className="dg-facet" key={c.key} role="group" aria-label={c.label}>
              <span className="dg-facet-label">{c.label}</span>
              {(facetCounts[c.key] || []).map(([value, count]) => {
                const on = (facets[c.key] || []).includes(value);
                return (
                  <button type="button" key={value || '(none)'} className={`dg-chip${on ? ' on' : ''}`} aria-pressed={on}
                    onClick={() => toggleFacet(c.key, value)} disabled={!on && count === 0}>
                    {value || t('grid.none', '(none)')} <span className="dg-chip-count">{count.toLocaleString()}</span>
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      )}

      <div className="dg-summary" aria-live="polite">
        <span>
          {sorted.length === list.length
            ? t('grid.count', { count: list.length, n: list.length.toLocaleString(), defaultValue: '{{n}} rows' })
            : t('grid.countFiltered', { shown: sorted.length.toLocaleString(), total: list.length.toLocaleString(), defaultValue: '{{shown}} of {{total}} rows match' })}
        </span>
        {(query || anyFacet) && <button type="button" className="dg-link" onClick={clearAll}>{t('grid.clear', 'Clear filters')}</button>}
        {note && <span className="dg-note">{note}</span>}
      </div>

      <div className="dg-scroll">
        <table className="dg-table">
          <colgroup>
            {columns.map((c) => <col key={c.key} style={c.width ? { width: c.width } : undefined} />)}
          </colgroup>
          <thead>
            <tr>
              {columns.map((c) => {
                const active = sort && sort.key === c.key;
                const ariaSort = active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none';
                return (
                  <th key={c.key} aria-sort={ariaSort} style={c.align ? { textAlign: c.align } : undefined}>
                    {c.sortable === false ? c.label : (
                      <button type="button" className={`dg-sort${active ? ' active' : ''}`} onClick={() => toggleSort(c)}
                        title={t('grid.sortTip', 'Sort (click again to reverse, a third time to clear)')}>
                        {c.label}<span className="dg-sort-mark" aria-hidden="true">{active ? (sort.dir === 'asc' ? '▲' : '▼') : '↕'}</span>
                      </button>
                    )}
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {visible.map((r, i) => (
              <tr key={rowKey ? rowKey(r) : i}>
                {columns.map((c) => (
                  <td key={c.key} data-label={c.label} className={c.mono ? 'dg-mono' : undefined} style={c.align ? { textAlign: c.align } : undefined}>
                    {c.render ? c.render(r) : (text(c.value(r)) || '-')}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
        {!loading && sorted.length === 0 && (
          <div className="dg-empty">
            {list.length === 0 ? (emptyText || t('grid.empty', 'Nothing to show.')) : t('grid.noMatch', 'No rows match the filters.')}
          </div>
        )}
        {loading && list.length === 0 && <div className="dg-empty">{t('common.loading', 'Loading...')}</div>}
      </div>

      {sorted.length > limit && (
        <div className="dg-more">
          <span>{t('grid.showing', { shown: limit.toLocaleString(), total: sorted.length.toLocaleString(), defaultValue: 'Showing {{shown}} of {{total}}' })}</span>
          <button type="button" className="dg-btn" onClick={() => setLimit((l) => l + PAGE * 5)}>{t('grid.more', 'Show more')}</button>
          <button type="button" className="dg-btn" onClick={() => setLimit(sorted.length)}>{t('grid.all', 'Show all')}</button>
        </div>
      )}
    </div>
  );
}

export default DataGrid;
