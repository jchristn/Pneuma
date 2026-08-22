import { useState, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';

function ActionMenu({ items = [] }) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);

  const visible = items.filter((i) => i && i.hidden !== true);

  const place = useCallback(() => {
    const el = triggerRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const menuWidth = 180;
    const itemHeight = 38;
    const menuHeight = Math.min(visible.length * itemHeight + 8, window.innerHeight - 16);
    let left = rect.right - menuWidth;
    if (left < 8) left = 8;
    if (left + menuWidth > window.innerWidth - 8) left = window.innerWidth - menuWidth - 8;
    // Prefer dropping below the trigger; flip above (or clamp into view) when it would run off-screen.
    let top = rect.bottom + 4;
    if (top + menuHeight > window.innerHeight - 8) {
      const above = rect.top - 4 - menuHeight;
      top = above >= 8 ? above : Math.max(8, window.innerHeight - menuHeight - 8);
    }
    setCoords({ top, left, maxHeight: menuHeight });
  }, [visible.length]);

  const toggle = (e) => {
    e.stopPropagation();
    if (!open) place();
    setOpen((v) => !v);
  };

  useEffect(() => {
    if (!open) return undefined;
    const close = () => setOpen(false);
    window.addEventListener('scroll', close, true);
    window.addEventListener('resize', close);
    document.addEventListener('mousedown', close);
    return () => {
      window.removeEventListener('scroll', close, true);
      window.removeEventListener('resize', close);
      document.removeEventListener('mousedown', close);
    };
  }, [open]);

  if (visible.length === 0) return null;

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="action-menu-trigger"
        onClick={toggle}
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label="Row actions"
        title="Actions for this row — view details, edit, inspect raw JSON, or delete."
      >
        ⋯
      </button>
      {open && createPortal(
        <div
          className="action-menu-popover"
          style={{ top: coords.top, left: coords.left, maxHeight: coords.maxHeight, overflowY: 'auto' }}
          role="menu"
          onMouseDown={(e) => e.stopPropagation()}
        >
          {visible.map((item) => (
            <button
              key={item.key || item.label}
              type="button"
              className={`action-menu-item ${item.danger ? 'danger' : ''}`}
              role="menuitem"
              title={item.tip}
              onClick={(e) => { e.stopPropagation(); setOpen(false); item.onClick(); }}
            >
              {item.label}
            </button>
          ))}
        </div>,
        document.body
      )}
    </>
  );
}

export default ActionMenu;
