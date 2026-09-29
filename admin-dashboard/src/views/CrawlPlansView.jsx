import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import JsonViewer from '../components/JsonViewer';
import CrawlPlanFormModal from '../components/crawl/CrawlPlanFormModal';
import CrawlPreviewModal from '../components/crawl/CrawlPreviewModal';
import CrawlOperationModal from '../components/crawl/CrawlOperationModal';
import { planStatusTone, operationTone, scheduleSummary } from '../components/crawl/crawlUtils';
import { formatDateTime } from '../i18n/formatters';

// Delete one or more plans, optionally with the links they created.
function DeletePlansModal({ count, onConfirm, onClose }) {
  const { t } = useTranslation();
  const [deleteLinks, setDeleteLinks] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const go = async () => {
    setBusy(true);
    setError('');
    try {
      await onConfirm(deleteLinks);
      onClose();
    } catch (err) {
      setError(err?.message || t('crawl.deleteError'));
      setBusy(false);
    }
  };
  return (
    <Modal title={t('crawl.deleteTitle', { count })} size="sm" onClose={busy ? () => {} : onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="button-danger" onClick={go} disabled={busy}>{t('common.delete')}</button>
        </>
      )}>
      <p className="confirm-text">{t('crawl.deleteMessage', { count })}</p>
      <label className="checkbox-field" title={t('crawl.deleteLinksTip')}>
        <input type="checkbox" checked={deleteLinks} onChange={(e) => setDeleteLinks(e.target.checked)} />
        <span>{t('crawl.deleteLinks')}</span>
      </label>
      {error && <div className="error-message" style={{ marginTop: '0.5rem' }}>{error}</div>}
    </Modal>
  );
}

// A plan's operations, newest first; a row opens the operation.
function PlanOperationsModal({ plan, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [openId, setOpenId] = useState(null);
  const load = useCallback(async () => {
    setLoading(true);
    try { setRows(normalizeList(await apiClient.listCrawlOperations(plan.id)).items); } catch { setRows([]); } finally { setLoading(false); }
  }, [apiClient, plan.id]);
  useEffect(() => { load(); }, [load]);
  const columns = [
    { key: 'startedUtc', label: t('crawl.started'), render: (r) => formatDateTime(r.startedUtc) },
    { key: 'status', label: t('crawl.status'), render: (r) => <StatusPill label={r.status} tone={operationTone(r.status)} /> },
    { key: 'trigger', label: t('crawl.trigger') },
    { key: 'added', label: t('crawl.count.added') },
    { key: 'updated', label: t('crawl.count.updated') },
    { key: 'deleted', label: t('crawl.count.deleted') },
    { key: 'failed', label: t('crawl.count.failed') }
  ];
  return (
    <Modal title={t('crawl.operationsFor', { name: plan.name })} size="xl" onClose={onClose}
      footer={<button type="button" className="button-primary" onClick={onClose}>{t('common.close')}</button>}>
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={(r) => setOpenId(r.id)}
        emptyMessage={t('crawl.noOperations')} />
      {openId && <CrawlOperationModal operationId={openId} onClose={() => setOpenId(null)} onChanged={load} />}
    </Modal>
  );
}

// Crawl plans: sources kept in sync with a subject. Pass subjectId to scope the view to one subject.
function CrawlPlansView({ subjectId: fixedSubjectId = null }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [types, setTypes] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [subjectFilter, setSubjectFilter] = useState(fixedSubjectId || '');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modal, setModal] = useState(null);
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setRows(normalizeList(await apiClient.listCrawlPlans(subjectFilter || null)).items);
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectFilter, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    apiClient.listCrawlPlanTypes().then((r) => setTypes(Array.isArray(r) ? r : normalizeList(r).items)).catch(() => setTypes([]));
    apiClient.list('subjects', { maxResults: 1000 }).then((r) => setSubjects(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  const subjectName = useMemo(() => {
    const map = {};
    for (const s of subjects) map[s.id] = s.displayName || s.id;
    return map;
  }, [subjects]);

  const { selectedItems, clear, selection } = useTableSelection(rows);

  const openEdit = async (plan, duplicate = false) => {
    try {
      const fresh = await apiClient.getCrawlPlan(plan.id);
      setModal(duplicate ? { type: 'form', duplicateOf: fresh } : { type: 'form', plan: fresh });
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    }
  };

  const start = async (plan) => {
    try {
      await apiClient.startCrawlPlan(plan.id);
      setNotice(t('crawl.startedNotice', { name: plan.name }));
    } catch (err) {
      setNotice(err?.status === 409 ? t('crawl.alreadyRunning') : (err?.message || t('crawl.startError')));
    }
    await load();
  };

  const stop = async (plan) => {
    try {
      await apiClient.stopCrawlPlan(plan.id);
      setNotice(t('crawl.stopNotice', { name: plan.name }));
    } catch (err) {
      setNotice(err?.status === 409 ? t('crawl.notRunning') : (err?.message || t('crawl.stopError')));
    }
    await load();
  };

  const removePlans = async (plans, deleteLinks) => {
    const failed = [];
    for (const p of plans) {
      try { await apiClient.deleteCrawlPlan(p.id, deleteLinks); } catch (err) { failed.push(`${p.name}: ${err?.message || err}`); }
    }
    clear();
    await load();
    if (failed.length) throw new Error(failed.join('\n'));
  };

  const columns = [
    { key: 'name', label: t('crawl.name'), render: (r) => r.name || '-' },
    { key: 'type', label: t('crawl.type'), render: (r) => <StatusPill label={r.type} tone="info" /> },
    ...(fixedSubjectId ? [] : [{ key: 'subjectId', label: t('crawl.subject'), render: (r) => subjectName[r.subjectId] || <CopyableId value={r.subjectId} truncateLen={12} /> }]),
    { key: 'status', label: t('crawl.status'), render: (r) => (
      <span className="status-with-badges">
        <StatusPill label={r.status} tone={planStatusTone(r.status)} />
        {r.enabled === false && <StatusPill label={t('crawl.disabled')} tone="neutral" />}
      </span>
    ) },
    { key: 'schedule', label: t('crawl.schedule'), sortable: false, render: (r) => scheduleSummary(r.schedule, t) },
    { key: 'lastRunUtc', label: t('crawl.lastRun'), render: (r) => (r.lastRunUtc ? formatDateTime(r.lastRunUtc) : '-') },
    { key: 'nextRunUtc', label: t('crawl.nextRun'), render: (r) => (r.nextRunUtc ? formatDateTime(r.nextRunUtc) : '-') },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (plan) => {
      const running = plan.status !== 'Idle';
      return (
        <ActionMenu items={[
          { key: 'view', label: t('crawl.view'), tip: t('crawl.viewTip'), onClick: () => setModal({ type: 'operations', plan }) },
          { key: 'edit', label: t('common.edit'), tip: t('crawl.editTip'), onClick: () => openEdit(plan) },
          { key: 'json', label: t('crawl.viewJson'), tip: t('crawl.viewJsonTip'), onClick: () => setModal({ type: 'json', plan }) },
          { key: 'duplicate', label: t('crawl.duplicate'), tip: t('crawl.duplicateTip'), onClick: () => openEdit(plan, true) },
          { key: 'start', label: t('crawl.start'), tip: t('crawl.startTip'), hidden: running, onClick: () => start(plan) },
          { key: 'stop', label: t('crawl.stop'), tip: t('crawl.stopTip'), hidden: !running, danger: true, onClick: () => stop(plan) },
          { key: 'test', label: t('crawl.test'), tip: t('crawl.testTip'), onClick: () => setModal({ type: 'test', plan }) },
          { key: 'preview', label: t('crawl.previewAction'), tip: t('crawl.previewTip'), onClick: () => setModal({ type: 'preview', plan }) },
          { key: 'operations', label: t('crawl.operations'), tip: t('crawl.operationsTip'), onClick: () => setModal({ type: 'operations', plan }) },
          { key: 'delete', label: t('common.delete'), tip: t('crawl.deleteTip'), hidden: running, danger: true, onClick: () => setModal({ type: 'delete', plans: [plan] }) }
        ]} />
      );
    } }
  ];

  const bulkBar = (
    <BulkActionBar count={selectedItems.length} onClear={clear} actions={[
      { key: 'delete', label: t('common.delete'), danger: true, tip: t('crawl.bulkDeleteTip'),
        disabled: selectedItems.some((p) => p.status !== 'Idle'),
        onClick: () => setModal({ type: 'delete', plans: selectedItems }) }
    ]} />
  );

  const createButton = (
    <button type="button" className="button-primary" disabled={types.length === 0} title={types.length === 0 ? t('crawl.noTypes') : t('crawl.createTip')}
      onClick={() => setModal({ type: 'form' })}>{t('crawl.createPlan')}</button>
  );

  return (
    <div>
      <PageHeader title={t('crawl.plansTitle')} subtitle={t('crawl.plansSubtitle')} actions={createButton} />
      {!fixedSubjectId && (
        <div className="filter-bar">
          <div className="field">
            <label htmlFor="crawl-plan-subject" className="has-tip" title={t('crawl.subjectFilterTip')}>{t('crawl.subject')}</label>
            <select id="crawl-plan-subject" value={subjectFilter} onChange={(e) => setSubjectFilter(e.target.value)}>
              <option value="">{t('crawl.allSubjects')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>
        </div>
      )}
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError('')} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load}
        onRowClick={(plan) => setModal({ type: 'operations', plan })}
        selection={selection} bulkBar={bulkBar} emptyMessage={t('crawl.noPlans')} />

      {modal?.type === 'form' && (
        <CrawlPlanFormModal plan={modal.plan || null} duplicateOf={modal.duplicateOf || null} types={types} subjects={subjects}
          fixedSubjectId={fixedSubjectId} onClose={() => setModal(null)} onSaved={() => load()} />
      )}
      {(modal?.type === 'test' || modal?.type === 'preview') && (
        <CrawlPreviewModal plan={modal.plan} mode={modal.type} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'operations' && <PlanOperationsModal plan={modal.plan} onClose={() => { setModal(null); load(); }} />}
      {modal?.type === 'json' && <JsonViewer title={modal.plan.name} data={modal.plan} onClose={() => setModal(null)} />}
      {modal?.type === 'delete' && (
        <DeletePlansModal count={modal.plans.length} onConfirm={(deleteLinks) => removePlans(modal.plans, deleteLinks)} onClose={() => setModal(null)} />
      )}
      {notice && (
        <Modal title={t('common.notice')} size="sm" onClose={() => setNotice('')}
          footer={<button type="button" className="button-primary" onClick={() => setNotice('')}>{t('common.close')}</button>}>
          <p className="confirm-text">{notice}</p>
        </Modal>
      )}
    </div>
  );
}

export default CrawlPlansView;
