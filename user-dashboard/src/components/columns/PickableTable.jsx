import { useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useColumnVisibility } from './useColumnVisibility';
import ColumnPicker from './ColumnPicker';

// A plain <table> with a column picker. The columns are read from the header row after each render and hidden
// with a generated stylesheet scoped to this table, so any hand-built table (including tables in rendered
// Markdown) gets the picker without restructuring its rows. A header cell with no text (row actions) is always
// shown, as is the first column. Tables with fewer than three columns get no picker. Kept identical in the admin,
// subject, and user dashboards.
//
// Props: tableId (storage key; defaults to the page path plus the header labels), maxDefault (optional columns
// shown before the rest start hidden; pass Infinity to show everything by default), barClassName, and any
// <table> attributes.
function PickableTable({ tableId = null, maxDefault, barClassName = '', children, ...tableProps }) {
  const rawId = useId();
  const scope = `pt${rawId.replace(/[^a-zA-Z0-9]/g, '')}`;
  const tableRef = useRef(null);
  const [headers, setHeaders] = useState([]);

  // Read the header cells after every render; only a change in the labels or spans updates state (the setter
  // returns the previous value otherwise), so this cannot loop.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useLayoutEffect(() => {
    const table = tableRef.current;
    const row = table?.tHead?.rows?.[0];
    const cells = row ? Array.from(row.cells) : [];
    const next = cells.map((c) => ({ label: (c.textContent || '').trim(), span: Math.max(1, c.colSpan || 1) }));
    const sig = JSON.stringify(next);
    setHeaders((prev) => (JSON.stringify(prev) === sig ? prev : next));
  });

  const columns = useMemo(() => headers.map((h, i) => ({
    key: `${i}:${h.label}`,
    label: h.label,
    required: i === 0 || !h.label ? true : undefined
  })), [headers]);

  const { picker } = useColumnVisibility(columns, {
    tableId: tableId || (headers.length ? `table:${headers.map((h) => h.label).join('|')}` : null),
    maxDefault
  });

  // Hide the header cell and the body cells under it (a header spanning several body columns hides them all).
  const css = useMemo(() => {
    const rules = [];
    let bodyIndex = 1;
    headers.forEach((h, i) => {
      const key = columns[i]?.key;
      if (key && !picker.visible.has(key)) {
        rules.push(`[data-pick="${scope}"] > thead > tr > :nth-child(${i + 1})`);
        for (let k = 0; k < h.span; k += 1) {
          rules.push(`[data-pick="${scope}"] > tbody > tr > :nth-child(${bodyIndex + k})`);
          rules.push(`[data-pick="${scope}"] > tfoot > tr > :nth-child(${bodyIndex + k})`);
        }
      }
      bodyIndex += h.span;
    });
    return rules.length ? `${rules.join(',\n')} { display: none; }` : '';
  }, [headers, columns, picker.visible, scope]);

  const showPicker = headers.length >= 3;

  return (
    <>
      {showPicker && (
        <div className={`pick-table-bar ${barClassName}`.trim()}>
          <ColumnPicker picker={picker} />
        </div>
      )}
      {css && <style>{css}</style>}
      <table ref={tableRef} data-pick={scope} {...tableProps}>{children}</table>
    </>
  );
}

export default PickableTable;
