import { useState, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';

/**
 * Row action menu. The dropdown is portalled to document.body and positioned
 * with fixed coordinates so it is never clipped by table overflow.
 */
function ActionMenu({ actions = [] }) {
  const [open, setOpen] = useState(false);
  const [coords, setCoords] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);

  const close = useCallback(() => setOpen(false), []);

  useEffect(() => {
    if (!open) return undefined;
    const handleClick = (e) => {
      if (triggerRef.current && !triggerRef.current.contains(e.target)) {
        close();
      }
    };
    const handleScroll = () => close();
    document.addEventListener('mousedown', handleClick);
    window.addEventListener('scroll', handleScroll, true);
    window.addEventListener('resize', handleScroll);
    return () => {
      document.removeEventListener('mousedown', handleClick);
      window.removeEventListener('scroll', handleScroll, true);
      window.removeEventListener('resize', handleScroll);
    };
  }, [open, close]);

  const toggle = (e) => {
    e.stopPropagation();
    if (!open && triggerRef.current) {
      const rect = triggerRef.current.getBoundingClientRect();
      const menuWidth = 170;
      const itemHeight = 38;
      const menuHeight = Math.min(actions.length * itemHeight + 8, window.innerHeight - 16);
      // Prefer dropping below the trigger; flip above (or clamp) when it would run off the bottom.
      let top = rect.bottom + 4;
      if (top + menuHeight > window.innerHeight - 8) {
        const above = rect.top - 4 - menuHeight;
        top = above >= 8 ? above : Math.max(8, window.innerHeight - menuHeight - 8);
      }
      setCoords({
        top,
        left: Math.max(8, Math.min(rect.right - menuWidth, window.innerWidth - menuWidth - 8)),
        maxHeight: menuHeight
      });
    }
    setOpen((v) => !v);
  };

  if (!actions.length) return null;

  return (
    <div className="action-menu" ref={triggerRef}>
      <button
        type="button"
        className="action-menu-trigger"
        onClick={toggle}
        title="Actions"
        aria-label="Actions"
        aria-haspopup="menu"
        aria-expanded={open}
      >
        &#8942;
      </button>
      {open &&
        createPortal(
          <div
            className="action-menu-dropdown"
            role="menu"
            style={{ top: coords.top, left: coords.left, maxHeight: coords.maxHeight, overflowY: 'auto' }}
          >
            {actions.map((action, idx) => (
              <button
                key={idx}
                type="button"
                role="menuitem"
                className={`action-menu-item ${action.variant === 'danger' ? 'danger' : ''}`}
                onClick={(e) => {
                  e.stopPropagation();
                  close();
                  action.onClick();
                }}
              >
                {action.label}
              </button>
            ))}
          </div>,
          document.body
        )}
    </div>
  );
}

export default ActionMenu;
