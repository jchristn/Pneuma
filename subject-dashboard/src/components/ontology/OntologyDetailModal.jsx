import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { OntoModal as Modal } from './OntoKit';
import { OntoTable as DataTable } from './OntoKit';
import { OntoMenu as ActionMenu } from './OntoKit';
import StatusPill from '../crawl/CrawlPill';
import { OntoConfirm as ConfirmModal } from './OntoKit';
import VersionEditorModal from './VersionEditorModal';
import { ApproveModal, ProposeModal, TaxonomyImportModal, DiffModal, ExportVersionModal } from './OntologyToolModals';
import { versionTone, ontologyError } from './ontologyUtils';
import { formatDateTime } from '../../utils/format';
import './Ontology.css';

// One ontology: its versions (draft → approved → retired) and the subjects that pin them.
function OntologyDetailModal({ ontologyId, onClose, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modal, setModal] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setDetail(await apiClient.getOntology(ontologyId));
      setError('');
    } catch (err) {
      setError(ontologyError(err, t('ontology.loadError')));
    } finally {
      setLoading(false);
    }
  }, [apiClient, ontologyId, t]);

  useEffect(() => { load(); }, [load]);

  const changed = () => { load(); onChanged?.(); };
  const act = async (fn) => {
    setError('');
    try { await fn(); changed(); } catch (err) { setError(ontologyError(err, t('ontology.actionError'))); }
  };

  const versions = detail?.versions || [];
  const pins = detail?.pinnedSubjects || [];
  const pinnedBy = (versionId) => pins.filter((p) => p.ontologyVersionId === versionId);

  const columns = [
    { key: 'versionNumber', label: t('ontology.version'), render: (v) => `v${v.versionNumber}` },
    { key: 'status', label: t('ontology.statusLabel'), render: (v) => <StatusPill label={t(`ontology.status.${v.status}`)} tone={versionTone(v.status)} /> },
    { key: 'counts', label: t('ontology.contents'), sortable: false, render: (v) => t('ontology.countsSummary', { nodes: v.nodeTypeCount, edges: v.edgeTypeCount, rules: v.ruleCount, concepts: v.conceptCount }) },
    { key: 'pins', label: t('ontology.pinnedBy'), sortable: false, cellClass: 'wrap', render: (v) => pinnedBy(v.id).map((p) => p.displayName).join(', ') || '—' },
    { key: 'changeSummary', label: t('ontology.changeSummary'), sortable: false, cellClass: 'wrap', render: (v) => v.changeSummary || '—' },
    { key: 'lastUpdateUtc', label: t('ontology.updated'), render: (v) => formatDateTime(v.approvedUtc || v.lastUpdateUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (v) => {
      const draft = v.status === 'Draft';
      return (
        <ActionMenu items={[
          { key: 'open', label: draft ? t('common.edit') : t('common.view'), onClick: () => setModal({ type: 'edit', version: v }) },
          { key: 'approve', label: t('ontology.approve'), tip: t('ontology.approveTip'), hidden: !draft, onClick: () => setModal({ type: 'approve', version: v }) },
          { key: 'import', label: t('ontology.importTaxonomy'), tip: t('ontology.importTip'), hidden: !draft, onClick: () => setModal({ type: 'import', version: v }) },
          { key: 'draft', label: t('ontology.newDraftFrom'), tip: t('ontology.newDraftTip'), hidden: draft, onClick: () => act(() => apiClient.createOntologyDraft(ontologyId, v.id)) },
          { key: 'diff', label: t('ontology.compare'), tip: t('ontology.compareTip'), onClick: () => setModal({ type: 'diff', version: v }) },
          { key: 'export', label: t('ontology.export'), tip: t('ontology.exportTip'), onClick: () => setModal({ type: 'export', version: v }) },
          { key: 'retire', label: t('ontology.retire'), tip: t('ontology.retireTip'), hidden: v.status !== 'Approved', danger: true, onClick: () => setModal({ type: 'retire', version: v }) },
          { key: 'delete', label: t('common.delete'), tip: t('ontology.deleteDraftTip'), hidden: !draft, danger: true, onClick: () => setModal({ type: 'deleteVersion', version: v }) }
        ]} />
      );
    } }
  ];

  const headerActions = (
    <div className="onto-toolbar">
      <button type="button" className="btn btn-secondary" onClick={() => act(() => apiClient.createOntologyDraft(ontologyId, null))} title={t('ontology.newDraftTip')}>
        {t('ontology.newDraft')}
      </button>
      <button type="button" className="btn btn-secondary" onClick={() => setModal({ type: 'propose' })} title={t('ontology.proposeTip')}>
        {t('ontology.propose')}
      </button>
    </div>
  );

  return (
    <Modal title={detail?.ontology?.name || t('ontology.title')} subtitle={detail?.ontology?.description} size="xl" onClose={onClose}
      footer={<button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>}>
      <p className="field-hint">{t('ontology.lifecycleHint')}</p>
      {headerActions}
      {error && <div className="error-message onto-error">{error}</div>}
      <DataTable columns={columns} data={versions} loading={loading} onRefresh={load}
        onRowClick={(v) => setModal({ type: 'edit', version: v })} emptyMessage={t('ontology.noVersions')} />

      {modal?.type === 'edit' && <VersionEditorModal versionId={modal.version.id} onClose={() => setModal(null)} onChanged={changed} />}
      {modal?.type === 'approve' && <ApproveModal version={modal.version} onClose={() => setModal(null)} onDone={changed} />}
      {modal?.type === 'import' && <TaxonomyImportModal version={modal.version} onClose={() => setModal(null)} onDone={changed} />}
      {modal?.type === 'diff' && <DiffModal version={modal.version} versions={versions} onClose={() => setModal(null)} />}
      {modal?.type === 'export' && <ExportVersionModal ontology={detail.ontology} version={modal.version} onClose={() => setModal(null)} />}
      {modal?.type === 'propose' && (
        <ProposeModal ontology={detail.ontology} versions={versions} onClose={() => setModal(null)}
          onDone={(draft) => { changed(); if (draft?.id) setTimeout(() => setModal({ type: 'edit', version: draft }), 0); }} />
      )}
      {modal?.type === 'retire' && (
        <ConfirmModal title={t('ontology.retire')} message={t('ontology.retireConfirm', { number: modal.version.versionNumber })} confirmLabel={t('ontology.retire')}
          onConfirm={async () => { await apiClient.retireOntologyVersion(modal.version.id); changed(); }} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'deleteVersion' && (
        <ConfirmModal title={t('common.delete')} message={t('ontology.deleteDraftConfirm', { number: modal.version.versionNumber })} confirmLabel={t('common.delete')}
          onConfirm={async () => { await apiClient.deleteOntologyVersion(modal.version.id); changed(); }} onClose={() => setModal(null)} />
      )}
    </Modal>
  );
}

export default OntologyDetailModal;
