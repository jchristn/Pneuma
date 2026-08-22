import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from './PageHeader';
import DataTable from './DataTable';
import BulkActionBar, { useTableSelection } from './BulkActionBar';
import ActionMenu from './ActionMenu';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import JsonViewer, { JsonBlock } from './JsonViewer';
import ErrorBanner from './ErrorBanner';
import CopyButton from './CopyButton';

function getId(item, idField) {
  if (idField && item[idField] !== undefined) return item[idField];
  return item.id ?? item.guid ?? item.Id ?? item.GUID ?? item.GUID ?? item.userId ?? item.name;
}

function FieldInput({ field, value, onChange }) {
  const tip = field.tip || undefined;
  const labelClass = tip ? 'has-tip' : undefined;
  const fullClass = field.fullWidth ? ' field-full' : '';
  const common = {
    id: `field-${field.name}`,
    value: value ?? '',
    onChange: (e) => onChange(field.name, field.type === 'checkbox' ? e.target.checked : e.target.value),
    disabled: field.readOnly,
    required: !!field.required,
    placeholder: field.placeholder || '',
    title: tip
  };
  if (field.type === 'checkbox') {
    return (
      <div className={`checkbox-field${fullClass}`} title={tip}>
        <input id={common.id} type="checkbox" checked={!!value} onChange={common.onChange} disabled={field.readOnly} title={tip} />
        <label htmlFor={common.id} className={labelClass} style={{ margin: 0 }} title={tip}>{field.label}</label>
      </div>
    );
  }
  if (field.type === 'select') {
    return (
      <div className={`field${fullClass}`}>
        <label htmlFor={common.id} className={labelClass} title={tip}>{field.label}</label>
        <select {...common}>
          {field.placeholder && <option value="">{field.placeholder}</option>}
          {(field.options || []).map((o) => (
            <option key={o.value} value={o.value} title={o.tip}>{o.label}</option>
          ))}
        </select>
      </div>
    );
  }
  if (field.type === 'multicheck') {
    const arr = Array.isArray(value) ? value : [];
    const toggle = (optVal, checked) => {
      const next = checked
        ? Array.from(new Set([...arr, optVal]))
        : arr.filter((v) => v !== optVal);
      onChange(field.name, next);
    };
    return (
      <div className={`field${fullClass}`}>
        <label className={labelClass} title={tip}>{field.label}</label>
        <div className="checkbox-group">
          {(field.options || []).map((o) => {
            const cid = `field-${field.name}-${o.value}`;
            return (
              <div className="checkbox-field" key={o.value} title={o.tip}>
                <input id={cid} type="checkbox" checked={arr.includes(o.value)} onChange={(e) => toggle(o.value, e.target.checked)} disabled={field.readOnly} />
                <label htmlFor={cid} style={{ margin: 0 }} title={o.tip}>{o.label}</label>
              </div>
            );
          })}
        </div>
      </div>
    );
  }
  if (field.type === 'custom' && typeof field.render === 'function') {
    return (
      <div className={`field${fullClass}`}>
        <label className={labelClass} title={tip}>{field.label}</label>
        {field.render(value, (v) => onChange(field.name, v))}
      </div>
    );
  }
  if (field.type === 'textarea') {
    return (
      <div className={`field${fullClass}`}>
        <label htmlFor={common.id} className={labelClass} title={tip}>{field.label}</label>
        <textarea {...common} rows={field.rows || 6} />
      </div>
    );
  }
  return (
    <div className="field">
      <label htmlFor={common.id} className={labelClass} title={tip}>{field.label}</label>
      <input {...common} type={field.type || 'text'} />
    </div>
  );
}

function emptyDefault(field) {
  if (field.type === 'checkbox') return false;
  if (field.type === 'multicheck') return [];
  return '';
}

function ResourceForm({ fields, initial, onSubmit, onCancel, submitLabel, disabled = false, notice = null, twoColumn = false }) {
  const { t } = useTranslation();
  const [values, setValues] = useState(() => {
    const v = {};
    fields.forEach((f) => {
      v[f.name] = initial ? (initial[f.name] ?? f.default ?? emptyDefault(f)) : (f.default ?? emptyDefault(f));
    });
    return v;
  });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  // Tracks fields the user has manually edited so derived fields stop auto-filling.
  const editedRef = useRef(new Set());

  const change = (name, value) => {
    editedRef.current.add(name);
    setValues((prev) => {
      const next = { ...prev, [name]: value };
      // Auto-populate any field that derives from the one just changed, unless
      // the user has already edited that derived field by hand.
      fields.forEach((f) => {
        if (f.deriveFrom === name && typeof f.derive === 'function' && !editedRef.current.has(f.name)) {
          next[f.name] = f.derive(value);
        }
      });
      return next;
    });
  };

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const body = {};
      fields.forEach((f) => {
        if (f.readOnly && !f.includeReadOnly) return;
        let val = values[f.name];
        if (f.type === 'number' && val !== '' && val !== null) val = Number(val);
        if (val === '' && f.omitIfEmpty !== false) return;
        body[f.name] = val;
      });
      await onSubmit(body);
    } catch (err) {
      setError(err?.message || 'Save failed');
      setBusy(false);
    }
  };

  return (
    <form onSubmit={submit}>
      <div className={twoColumn ? 'form-grid form-grid-2col' : 'form-grid'}>
        {fields.map((f) => (
          <FieldInput key={f.name} field={f} value={values[f.name]} onChange={change} />
        ))}
      </div>
      {notice && <div className="error-message" style={{ marginTop: '1rem' }}>{notice}</div>}
      {error && <div className="error-message" style={{ marginTop: '1rem' }}>{error}</div>}
      <div className="modal-footer" style={{ padding: '1rem 0 0', borderTop: 'none' }}>
        <button type="button" className="button-secondary" onClick={onCancel} disabled={busy}>{t('common.cancel')}</button>
        <button type="submit" className="button-primary" disabled={busy || disabled}>{busy ? t('common.loading') : submitLabel}</button>
      </div>
    </form>
  );
}

function DetailView({ fields, item }) {
  return (
    <dl className="kv-grid">
      {fields.map((f) => (
        <div key={f.name} style={{ display: 'contents' }}>
          <dt className={f.tip ? 'has-tip' : undefined} title={f.tip}>{f.label}</dt>
          <dd>{f.render ? f.render(item) : (item[f.name] === undefined || item[f.name] === null || item[f.name] === '' ? '—' : String(item[f.name]))}</dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * Generic resource view: list table + view/edit/create/delete/JSON modals.
 */
function ResourceView({
  resourceKey,
  singular,
  title,
  subtitle,
  columns,
  formFields = [],
  detailFields = null,
  idField = null,
  capabilities = { create: true, edit: true, delete: true, viewJson: true },
  fetcher = null,
  subject = null,
  updater = null,
  deleter = null,
  fetchDetail = false,
  onCreated = null,
  listParams = null,
  extraActions = [],
  toolbar = null,
  headerActions = null,
  createDisabled = false,
  createNotice = null,
  modalSize = 'lg',
  selectable = false,
  bulkActions = null,
  postDeleteNotice = null,
  twoColumnForm = false
}) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [modal, setModal] = useState(null); // { type, item }
  const [pendingBulk, setPendingBulk] = useState(null); // { action, items }
  const [noticeOpen, setNoticeOpen] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = fetcher ? await fetcher(apiClient) : await apiClient.list(resourceKey, listParams);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load data');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, resourceKey, fetcher, listParams]);

  useEffect(() => { load(); }, [load]);

  const openView = useCallback(async (item) => {
    if (fetchDetail) {
      try {
        const full = await apiClient.get(resourceKey, getId(item, idField));
        setModal({ type: 'view', item: full || item });
        return;
      } catch { /* fall back to row */ }
    }
    setModal({ type: 'view', item });
  }, [apiClient, resourceKey, idField, fetchDetail]);

  // Row click opens the Edit modal when the resource is editable; otherwise it opens View.
  const openRow = useCallback((item) => {
    if (capabilities.edit && formFields.length > 0) {
      setModal({ type: 'edit', item });
      return;
    }
    openView(item);
  }, [capabilities.edit, formFields.length, openView]);

  const doCreate = async (body) => {
    const resp = subject ? await subject(apiClient, body) : await apiClient.create(resourceKey, body);
    setModal(null);
    await load();
    if (onCreated) onCreated(resp, setModal);
  };

  const doUpdate = async (item, body) => {
    if (updater) await updater(apiClient, getId(item, idField), body);
    else await apiClient.update(resourceKey, getId(item, idField), body);
    setModal(null);
    await load();
  };

  const doDelete = async (item) => {
    if (deleter) await deleter(apiClient, getId(item, idField));
    else await apiClient.remove(resourceKey, getId(item, idField));
    await load();
    // For long-running (background) deletes, surface a dismissible notice instead of silently returning.
    if (postDeleteNotice) setNoticeOpen(true);
  };

  // Optional multi-select with bulk actions. Enabled when a caller opts in via `selectable` or by supplying
  // `bulkActions`. A default "delete selected" is offered whenever the resource is deletable.
  const rowIdFn = useCallback((item) => getId(item, idField), [idField]);
  const bulkEnabled = selectable || !!bulkActions;
  const { selectedItems, clear, selection } = useTableSelection(rows, rowIdFn);

  const runBulk = async (action, items) => {
    await action.run(items);
    setPendingBulk(null);
    clear();
    await load();
  };

  const bulkDeleteRun = async (items) => {
    for (const item of items) {
      if (deleter) await deleter(apiClient, getId(item, idField));
      else await apiClient.remove(resourceKey, getId(item, idField));
    }
  };

  const customBulk = bulkEnabled && bulkActions ? (bulkActions(selectedItems) || []) : [];
  const allBulk = [
    ...customBulk,
    ...(bulkEnabled && capabilities.delete ? [{
      key: 'delete', label: t('common.delete'), danger: true,
      confirm: {
        title: t('common.delete'),
        message: t('resource.bulkDeleteConfirm', { count: selectedItems.length, name: singular, defaultValue: `Delete ${selectedItems.length} selected ${singular}(s)? This cannot be undone.` }),
        confirmLabel: t('common.delete')
      },
      run: bulkDeleteRun
    }] : [])
  ];

  const bulkBar = bulkEnabled && allBulk.length > 0 ? (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={allBulk.map((a) => ({
        key: a.key, label: a.label, danger: a.danger, disabled: a.disabled, hidden: a.hidden, tip: a.tip,
        onClick: () => { if (a.confirm) setPendingBulk({ action: a, items: selectedItems }); else runBulk(a, selectedItems); }
      }))}
    />
  ) : null;

  const actionColumn = {
    key: '_actions',
    label: t('common.actions'),
    sortable: false,
    width: '56px',
    render: (item) => (
      <ActionMenu items={[
        { key: 'view', label: t('common.view'), tip: t('common.viewTip', { name: singular, defaultValue: `Open a read-only detail view of this ${singular}.` }), onClick: () => openView(item) },
        ...extraActions.map((a) => ({
          key: a.key || a.label,
          label: a.label,
          tip: a.tip,
          danger: a.danger,
          hidden: typeof a.hidden === 'function' ? a.hidden(item) : a.hidden,
          onClick: () => a.onClick(item)
        })),
        { key: 'edit', label: t('common.edit'), tip: t('common.editTip', { name: singular, defaultValue: `Edit this ${singular}'s fields.` }), hidden: !capabilities.edit || formFields.length === 0, onClick: () => setModal({ type: 'edit', item }) },
        { key: 'json', label: t('common.viewJson'), tip: t('common.viewJsonTip', { defaultValue: 'Inspect the raw JSON returned by the API for this record.' }), hidden: !capabilities.viewJson, onClick: () => setModal({ type: 'json', item }) },
        { key: 'delete', label: t('common.delete'), tip: t('common.deleteTip', { name: singular, defaultValue: `Permanently delete this ${singular}. You'll be asked to confirm.` }), danger: true, hidden: !capabilities.delete, onClick: () => setModal({ type: 'delete', item }) }
      ]} />
    )
  };

  const tableColumns = [...columns, actionColumn];
  const effectiveDetailFields = detailFields || formFields;

  return (
    <div>
      <PageHeader
        title={title}
        subtitle={subtitle}
        actions={(capabilities.create && formFields.length > 0) || headerActions ? (
          <>
            {headerActions}
            {capabilities.create && formFields.length > 0 && (
              <button type="button" className="button-primary" onClick={() => setModal({ type: 'create' })}
                title={t('resource.addTip', { name: singular, defaultValue: `Create a new ${singular}. Opens a form; nothing is saved until you submit.` })}>
                + {t('common.add')}
              </button>
            )}
          </>
        ) : null}
      />
      {toolbar}
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={tableColumns} data={rows} loading={loading} onRefresh={load} onRowClick={openRow}
        selection={bulkEnabled ? selection : null} bulkBar={bulkBar} />

      {modal?.type === 'create' && (
        <Modal title={t('resource.addTitle', { name: singular })} size={modalSize} onClose={() => setModal(null)}>
          <ResourceForm fields={formFields} onSubmit={doCreate} onCancel={() => setModal(null)} submitLabel={t('common.create')} disabled={createDisabled} notice={createNotice} twoColumn={twoColumnForm} />
        </Modal>
      )}
      {modal?.type === 'edit' && (
        <Modal title={t('resource.editTitle', { name: singular })} size={modalSize}
          subtitle={<span className="copyable-id"><code>{String(getId(modal.item, idField))}</code><CopyButton value={String(getId(modal.item, idField))} label={null} /></span>}
          onClose={() => setModal(null)}>
          <ResourceForm fields={formFields} initial={modal.item} onSubmit={(body) => doUpdate(modal.item, body)} onCancel={() => setModal(null)} submitLabel={t('common.save')} twoColumn={twoColumnForm} />
        </Modal>
      )}
      {modal?.type === 'view' && (
        <Modal title={t('resource.viewTitle', { name: singular })} size={modalSize}
          headerExtra={<CopyButton value={String(getId(modal.item, idField))} label="ID" />}
          onClose={() => setModal(null)}
          footer={<button type="button" className="button-secondary" onClick={() => setModal(null)}>{t('common.close')}</button>}>
          <DetailView fields={effectiveDetailFields} item={modal.item} />
        </Modal>
      )}
      {modal?.type === 'json' && (
        <JsonViewer data={modal.item} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'delete' && (
        <ConfirmModal
          title={t('common.delete')}
          message={t('resource.deleteConfirm', { name: singular })}
          confirmLabel={t('common.delete')}
          onConfirm={() => doDelete(modal.item)}
          onClose={() => setModal(null)}
        />
      )}
      {pendingBulk && (
        <ConfirmModal
          title={pendingBulk.action.confirm.title}
          message={pendingBulk.action.confirm.message}
          danger={!!pendingBulk.action.danger}
          confirmLabel={pendingBulk.action.confirm.confirmLabel}
          onConfirm={() => runBulk(pendingBulk.action, pendingBulk.items)}
          onClose={() => setPendingBulk(null)}
        />
      )}
      {noticeOpen && (
        <Modal
          title={t('common.notice', 'Notice')}
          size="sm"
          onClose={() => setNoticeOpen(false)}
          footer={<button type="button" className="button-primary" onClick={() => setNoticeOpen(false)}>{t('common.close')}</button>}
        >
          <p className="confirm-text">{postDeleteNotice}</p>
        </Modal>
      )}
    </div>
  );
}

export { getId, JsonBlock };
export default ResourceView;
