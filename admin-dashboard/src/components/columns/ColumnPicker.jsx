import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { columnLabel, isRequired } from './useColumnVisibility';
import './ColumnPicker.css';

// The "Columns" control in a table's toolbar: a checkbox per column (required ones are checked and locked), plus
// Show all and Reset. Kept identical in the admin, subject, and user dashboards.
function ColumnPicker({ picker }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const rootRef = useRef(null);
  const menuRef = useRef(null);
  const [pos, setPos] = useState(null);
  const { columns, visible, toggle, showAll, reset, isDefault } = picker;

  // The menu is rendered in a portal (tables and modals clip their overflow), placed under the button, flipped
  // above it when there is no room below, and kept in place as the page scrolls or resizes.
  const place = useCallback(() => {
    if (!rootRef.current) return;
    const r = rootRef.current.getBoundingClientRect();
    const below = window.innerHeight - r.bottom;
    const right = Math.max(8, window.innerWidth - r.right);
    setPos(below >= 240 || below >= r.top ? { top: r.bottom + 4, right } : { bottom: window.innerHeight - r.top + 4, right });
  }, []);

  useLayoutEffect(() => { if (open) place(); }, [open, place]);

  useEffect(() => {
    if (!open) return undefined;
    const onDown = (e) => {
      if (rootRef.current?.contains(e.target) || menuRef.current?.contains(e.target)) return;
      setOpen(false);
    };
    const onKey = (e) => { if (e.key === 'Escape') { e.stopPropagation(); setOpen(false); } };
    const onMove = (e) => { if (!menuRef.current?.contains(e.target)) place(); };
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey, true);
    window.addEventListener('resize', onMove);
    window.addEventListener('scroll', onMove, true);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey, true);
      window.removeEventListener('resize', onMove);
      window.removeEventListener('scroll', onMove, true);
    };
  }, [open, place]);

  const listed = columns.map((c, i) => ({ c, required: isRequired(c, i) })).filter(({ c, required }) => !required || columnLabel(c) !== c.key);
  const optional = listed.filter((x) => !x.required);
  if (optional.length === 0) return null;
  const shownOptional = optional.filter((x) => visible.has(x.c.key)).length;

  return (
    <div className="colpick" ref={rootRef}>
      <button type="button" className="colpick-button" aria-haspopup="true" aria-expanded={open}
        onClick={() => setOpen((o) => !o)} title={t('table.columnsTip', 'Choose which columns this table shows')}>
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <rect x="3" y="4" width="18" height="16" rx="2" /><path d="M9 4v16M15 4v16" />
        </svg>
        <span>{t('table.columns', 'Columns')}</span>
        <span className="colpick-count">{shownOptional}/{optional.length}</span>
      </button>
      {open && pos && createPortal(
        <div className="colpick-menu" role="menu" ref={menuRef} style={pos}>
          <div className="colpick-list">
            {listed.map(({ c, required }) => (
              <label key={c.key} className={`colpick-item${required ? ' required' : ''}`}
                title={required ? t('table.columnRequired', 'Always shown') : undefined}>
                <input type="checkbox" checked={visible.has(c.key)} disabled={required} onChange={() => toggle(c.key)} />
                <span>{columnLabel(c)}</span>
                {required && <span className="colpick-req">{t('table.required', 'required')}</span>}
              </label>
            ))}
          </div>
          <div className="colpick-actions">
            <button type="button" onClick={showAll}>{t('table.showAllColumns', 'Show all')}</button>
            <button type="button" onClick={reset} disabled={isDefault}>{t('table.resetColumns', 'Reset')}</button>
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}

export default ColumnPicker;
