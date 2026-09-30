import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from '../Modal';
import DataTable from '../DataTable';
import ActionMenu from '../ActionMenu';

// Thin adapters giving the ontology components one small, stable surface over this dashboard's shared
// Modal, DataTable, ActionMenu, and confirm dialog (a footer slot, row-first renderers, hidden menu items, and
// an async confirm with busy/error state).

const SIZES = { sm: 'small', lg: 'large', xl: 'large onto-modal-xl' };

export function OntoModal({ title, subtitle, headerExtra, size = 'lg', onClose, footer, children }) {
  return (
    <Modal isOpen title={title} size={SIZES[size] || 'medium'} onClose={onClose} headerAction={headerExtra}>
      {subtitle && <p className="field-hint">{subtitle}</p>}
      {children}
      {footer && <div className="form-actions">{footer}</div>}
    </Modal>
  );
}

export function OntoTable({ columns, data, loading = false, onRefresh = null, onRowClick = null, emptyMessage = '' }) {
  const mapped = columns.map((c) => ({
    key: c.key,
    label: c.label,
    sortable: c.sortable !== false,
    className: c.cellClass,
    render: c.render ? (_value, row) => c.render(row) : undefined
  }));
  return <DataTable columns={mapped} data={data} loading={loading} onRefresh={onRefresh} onRowClick={onRowClick} emptyTitle={emptyMessage} />;
}

export function OntoMenu({ items }) {
  const actions = items.filter((i) => i && i.hidden !== true).map((i) => ({ label: i.label, onClick: i.onClick, variant: i.danger ? 'danger' : undefined }));
  return <ActionMenu actions={actions} />;
}

export function OntoConfirm({ title, message, confirmLabel, onConfirm, onClose }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const go = async () => {
    setBusy(true);
    setError('');
    try {
      await onConfirm();
      onClose();
    } catch (err) {
      setError(err?.message || t('ontology.actionError'));
      setBusy(false);
    }
  };
  return (
    <OntoModal title={title} size="sm" onClose={busy ? () => {} : onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="btn btn-danger" onClick={go} disabled={busy}>{busy ? t('common.loading') : confirmLabel}</button>
        </>
      )}>
      <p>{message}</p>
      {error && <div className="error-message onto-error">{error}</div>}
    </OntoModal>
  );
}
