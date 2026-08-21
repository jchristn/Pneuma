import { useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';

function Modal({ title, subtitle, headerExtra, children, footer, onClose, size = '' }) {
  const { t } = useTranslation();
  const panelRef = useRef(null);
  // Keep the latest onClose in a ref so the mount effect below can run exactly once. Depending on onClose
  // directly re-runs the effect on every parent render (callers pass a fresh arrow each time), which would
  // re-focus the panel on each keystroke and steal focus from inputs inside the modal.
  const onCloseRef = useRef(onClose);
  useEffect(() => { onCloseRef.current = onClose; });

  useEffect(() => {
    const onKey = (e) => { if (e.key === 'Escape') onCloseRef.current?.(); };
    document.addEventListener('keydown', onKey);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    // focus the panel for accessibility (once, on open)
    const timer = setTimeout(() => { panelRef.current?.focus(); }, 0);
    return () => {
      document.removeEventListener('keydown', onKey);
      document.body.style.overflow = prevOverflow;
      clearTimeout(timer);
    };
  }, []);

  const handleBackdrop = (e) => {
    if (e.target === e.currentTarget) onClose();
  };

  return createPortal(
    <div className="modal-backdrop" onMouseDown={handleBackdrop}>
      <div
        ref={panelRef}
        className={`modal ${size ? `modal-${size}` : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={typeof title === 'string' ? title : undefined}
        tabIndex={-1}
      >
        <div className="modal-header">
          <div>
            {title && <h2>{title}</h2>}
            {subtitle && <div className="modal-subtitle">{subtitle}</div>}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            {headerExtra}
            <button type="button" className="icon-button" onClick={onClose} aria-label={t('common.close')}>✕</button>
          </div>
        </div>
        <div className="modal-body">{children}</div>
        {footer && <div className="modal-footer">{footer}</div>}
      </div>
    </div>,
    document.body
  );
}

export default Modal;
