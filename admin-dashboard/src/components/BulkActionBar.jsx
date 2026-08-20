import { useState, useMemo, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';

const defaultGetRowId = (item) => item.id ?? item.Id ?? item.guid ?? item.GUID;

/**
 * Multi-select state for a DataTable. Owns the selected-id Set, derives the selected items from the current
 * rows, and prunes ids that are no longer present after a reload. Returns a `selection` object ready to pass
 * straight to DataTable's `selection` prop.
 * @param {Array} rows the full row set currently loaded (client-mode DataTable holds every page)
 * @param {Function} [getRowId] maps a row to its stable id; defaults to id/Id/guid/GUID
 */
export function useTableSelection(rows, getRowId = defaultGetRowId) {
  const [selectedIds, setSelectedIds] = useState(() => new Set());

  useEffect(() => {
    setSelectedIds((prev) => {
      if (prev.size === 0) return prev;
      const present = new Set(rows.map(getRowId));
      const next = new Set();
      let changed = false;
      prev.forEach((id) => { if (present.has(id)) next.add(id); else changed = true; });
      return changed ? next : prev;
    });
  }, [rows, getRowId]);

  const selectedItems = useMemo(
    () => rows.filter((r) => selectedIds.has(getRowId(r))),
    [rows, selectedIds, getRowId]
  );
  const clear = useCallback(() => setSelectedIds(new Set()), []);

  return {
    selectedIds,
    selectedItems,
    clear,
    selection: { selectedIds, onChange: setSelectedIds, getRowId }
  };
}

/**
 * A bar shown above a DataTable when one or more rows are selected. Renders the selection count, a set of
 * bulk-action buttons, and a "clear selection" control.
 * @param {number} count number of selected rows
 * @param {Array} actions [{ key, label, onClick, danger, disabled, hidden, tip }]
 * @param {Function} onClear clears the current selection
 */
function BulkActionBar({ count, actions = [], onClear }) {
  const { t } = useTranslation();
  return (
    <div className="bulk-action-bar">
      <span className="bulk-count">{t('table.selectedCount', { count, defaultValue: `${count} selected` })}</span>
      <div className="bulk-actions">
        {actions.filter((a) => !a.hidden).map((a) => (
          <button
            key={a.key || a.label}
            type="button"
            className={a.danger ? 'button-danger' : 'button-secondary'}
            onClick={a.onClick}
            disabled={a.disabled}
            title={a.tip}
          >
            {a.label}
          </button>
        ))}
        <button type="button" className="button-secondary bulk-clear" onClick={onClear}>
          {t('table.clearSelection', 'Clear')}
        </button>
      </div>
    </div>
  );
}

export default BulkActionBar;
