import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import OntologyDetailModal from '../components/ontology/OntologyDetailModal';
import { ontologyError } from '../components/ontology/ontologyUtils';
import { formatDateTime } from '../i18n/formatters';

// Create an ontology (empty, from a built-in template, or copying another version), or rename one.
function OntologyFormModal({ ontology, templates, onClose, onSaved }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [name, setName] = useState(ontology?.name || '');
  const [description, setDescription] = useState(ontology?.description || '');
  const [template, setTemplate] = useState(templates[0]?.name || '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const save = async () => {
    setBusy(true);
    setError('');
    try {
      const saved = ontology
        ? await apiClient.updateOntology(ontology.id, { name, description: description || null })
        : await apiClient.createOntology({ name, description: description || null, template: template || null });
      onSaved(saved);
      onClose();
    } catch (err) {
      setError(ontologyError(err, t('ontology.saveError')));
      setBusy(false);
    }
  };
  return (
    <Modal title={ontology ? t('ontology.renameTitle') : t('ontology.createTitle')} size="lg" onClose={busy ? () => {} : onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="button-primary" onClick={save} disabled={busy || !name.trim()}>{busy ? t('common.loading') : t('common.save')}</button>
        </>
      )}>
      <div className="form-grid">
        <div className="field">
          <label htmlFor="onto-name">{t('ontology.name')} *</label>
          <input id="onto-name" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div className="field">
          <label htmlFor="onto-description">{t('ontology.description')}</label>
          <textarea id="onto-description" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </div>
        {!ontology && (
          <div className="field">
            <label htmlFor="onto-template" className="has-tip" title={t('ontology.templateTip')}>{t('ontology.template')}</label>
            <select id="onto-template" value={template} onChange={(e) => setTemplate(e.target.value)}>
              <option value="">{t('ontology.emptyTemplate')}</option>
              {templates.map((tp) => <option key={tp.name} value={tp.name}>{tp.name}</option>)}
            </select>
            {template && <small className="field-hint">{templates.find((tp) => tp.name === template)?.description}</small>}
          </div>
        )}
      </div>
      {error && <div className="error-message onto-error">{error}</div>}
    </Modal>
  );
}

// Tenant ontologies: governed, versioned definitions of the node types, edge types, rules, and taxonomy that
// subjects classify into.
function OntologiesView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [templates, setTemplates] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modal, setModal] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setRows(normalizeList(await apiClient.listOntologies()).items);
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    apiClient.listOntologyTemplates().then((r) => setTemplates(Array.isArray(r) ? r : normalizeList(r).items)).catch(() => setTemplates([]));
  }, [apiClient]);

  const columns = [
    { key: 'name', label: t('ontology.name') },
    { key: 'description', label: t('ontology.description'), cellClass: 'wrap', sortable: false, render: (r) => r.description || '—' },
    { key: 'id', label: 'ID', render: (r) => <CopyableId value={r.id} truncateLen={12} /> },
    { key: 'lastUpdateUtc', label: t('ontology.updated'), render: (r) => formatDateTime(r.lastUpdateUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (r) => (
      <ActionMenu items={[
        { key: 'open', label: t('ontology.openVersions'), onClick: () => setModal({ type: 'detail', id: r.id }) },
        { key: 'rename', label: t('common.edit'), onClick: () => setModal({ type: 'form', ontology: r }) },
        { key: 'delete', label: t('common.delete'), tip: t('ontology.deleteTip'), danger: true, onClick: () => setModal({ type: 'delete', ontology: r }) }
      ]} />
    ) }
  ];

  const createButton = (
    <button type="button" className="button-primary" onClick={() => setModal({ type: 'form' })}>{t('ontology.create')}</button>
  );

  return (
    <div>
      <PageHeader title={t('ontology.title')} subtitle={t('ontology.subtitle')} actions={createButton} />
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError('')} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load}
        onRowClick={(r) => setModal({ type: 'detail', id: r.id })} emptyMessage={t('ontology.noOntologies')} />
      {modal?.type === 'form' && (
        <OntologyFormModal ontology={modal.ontology || null} templates={templates} onClose={() => setModal(null)}
          onSaved={(saved) => { load(); if (!modal.ontology && saved?.ontology?.id) setTimeout(() => setModal({ type: 'detail', id: saved.ontology.id }), 0); }} />
      )}
      {modal?.type === 'detail' && <OntologyDetailModal ontologyId={modal.id} onClose={() => setModal(null)} onChanged={load} />}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('common.delete')} message={t('ontology.deleteConfirm', { name: modal.ontology.name })} confirmLabel={t('common.delete')}
          onConfirm={async () => { await apiClient.deleteOntology(modal.ontology.id); await load(); }} onClose={() => setModal(null)} />
      )}
    </div>
  );
}

export default OntologiesView;
