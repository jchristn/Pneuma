import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatDateTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ActionMenu from '../components/ActionMenu';
import JsonViewer from '../components/JsonViewer';
import CrawlPill from '../components/crawl/CrawlPill';
import CrawlPlanFormModal from '../components/crawl/CrawlPlanFormModal';
import CrawlPreviewModal from '../components/crawl/CrawlPreviewModal';
import CrawlOperationModal from '../components/crawl/CrawlOperationModal';
import { planStatusTone, operationTone, scheduleSummary } from '../components/crawl/crawlUtils';

// Delete a plan, optionally with the links it created.
function DeletePlanModal({ plan, onConfirm, onClose }) {
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
    <Modal isOpen title={t('crawl.deleteTitle')} size="small" onClose={busy ? () => {} : onClose}>
      <p>{t('crawl.deleteMessage', { count: 1 })} <strong>{plan.name}</strong></p>
      <label className="checkbox-field" title={t('crawl.deleteLinksTip')}>
        <input type="checkbox" checked={deleteLinks} onChange={(e) => setDeleteLinks(e.target.checked)} />
        <span>{t('crawl.deleteLinks')}</span>
      </label>
      {error && <div className="form-error">{error}</div>}
      <div className="form-actions">
        <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
        <button type="button" className="btn btn-danger" onClick={go} disabled={busy}>{t('common.delete')}</button>
      </div>
    </Modal>
  );
}

// Crawlers: the crawl plans that keep the user's subjects in sync with web sites, sitemaps, buckets, and shares,
// with their operations. Scoped by subject.
function CrawlersView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [plans, setPlans] = useState([]);
  const [operations, setOperations] = useState([]);
  const [types, setTypes] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [modal, setModal] = useState(null);
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError('');
    try {
      const planList = asArray(await apiClient.listCrawlPlans(subjectId || null));
      setPlans(planList);
      const ops = asArray(await apiClient.listCrawlOperations());
      const ids = new Set(planList.map((p) => p.id));
      setOperations(ops.filter((o) => ids.has(o.planId)).slice(0, 200));
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    if (!apiClient) return;
    apiClient.listCrawlPlanTypes().then((r) => setTypes(Array.isArray(r) ? r : asArray(r))).catch(() => setTypes([]));
    apiClient.getSubjects({ maxResults: 1000 }).then((r) => setSubjects(asArray(r))).catch(() => setSubjects([]));
  }, [apiClient]);

  const planName = useMemo(() => Object.fromEntries(plans.map((p) => [p.id, p.name])), [plans]);
  const subjectName = useMemo(() => Object.fromEntries(subjects.map((s) => [s.id, s.displayName || s.id])), [subjects]);

  const openEdit = async (plan, duplicate) => {
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

  const planColumns = [
    { key: 'name', label: t('crawl.name') },
    { key: 'type', label: t('crawl.type'), render: (v) => <CrawlPill label={v} tone="info" /> },
    { key: 'subjectId', label: t('crawl.subject'), render: (v) => subjectName[v] || v },
    { key: 'status', label: t('crawl.status'), render: (v, r) => (
      <span style={{ display: 'inline-flex', gap: 6 }}>
        <CrawlPill label={v} tone={planStatusTone(v)} />
        {r.enabled === false && <CrawlPill label={t('crawl.disabled')} tone="neutral" />}
      </span>
    ) },
    { key: 'schedule', label: t('crawl.schedule'), sortable: false, render: (v) => scheduleSummary(v, t) },
    { key: 'lastRunUtc', label: t('crawl.lastRun'), render: (v) => (v ? formatDateTime(v) : '-') },
    { key: 'nextRunUtc', label: t('crawl.nextRun'), render: (v) => (v ? formatDateTime(v) : '-') },
    { key: 'actions', label: '', sortable: false, render: (_, plan) => {
      const running = plan.status !== 'Idle';
      const items = [
        { label: t('common.edit'), onClick: () => openEdit(plan, false) },
        { label: t('crawl.viewJson'), onClick: () => setModal({ type: 'json', plan }) },
        { label: t('crawl.duplicate'), onClick: () => openEdit(plan, true) },
        running ? { label: t('crawl.stop'), variant: 'danger', onClick: () => stop(plan) } : { label: t('crawl.start'), onClick: () => start(plan) },
        { label: t('crawl.test'), onClick: () => setModal({ type: 'test', plan }) },
        { label: t('crawl.previewAction'), onClick: () => setModal({ type: 'preview', plan }) },
        ...(running ? [] : [{ label: t('common.delete'), variant: 'danger', onClick: () => setModal({ type: 'delete', plan }) }])
      ];
      return <ActionMenu actions={items} />;
    } }
  ];

  const opColumns = [
    { key: 'startedUtc', label: t('crawl.started'), render: (v) => formatDateTime(v) },
    { key: 'planId', label: t('crawl.plan'), render: (v) => planName[v] || v },
    { key: 'status', label: t('crawl.status'), render: (v) => <CrawlPill label={v} tone={operationTone(v)} /> },
    { key: 'added', label: t('crawl.count.added') },
    { key: 'updated', label: t('crawl.count.updated') },
    { key: 'deleted', label: t('crawl.count.deleted') },
    { key: 'failed', label: t('crawl.count.failed') }
  ];

  return (
    <div>
      <PageHeader title={t('crawl.plansTitle')} subtitle={t('crawl.plansSubtitle')}
        actions={(
          <button type="button" className="btn btn-primary" disabled={types.length === 0 || subjects.length === 0}
            title={types.length === 0 ? t('crawl.noTypes') : t('crawl.createTip')} onClick={() => setModal({ type: 'form' })}>
            {t('crawl.createPlan')}
          </button>
        )} />
      <div className="filter-bar">
        <label htmlFor="crawlers-subject">{t('crawl.subject')}</label>
        <select id="crawlers-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} title={t('crawl.subjectFilterTip')}>
          <option value="">{t('crawl.allSubjects')}</option>
          {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
        </select>
      </div>
      {error && <div className="error-banner">{error}</div>}
      <DataTable columns={planColumns} data={plans} loading={loading} onRefresh={load} emptyTitle={t('crawl.noPlans')}
        onRowClick={(plan) => setModal({ type: 'preview', plan })} />

      <h3 style={{ marginTop: 24 }}>{t('crawl.operationsTitle')}</h3>
      <DataTable columns={opColumns} data={operations} loading={loading} onRefresh={load} emptyTitle={t('crawl.noOperations')}
        onRowClick={(op) => setModal({ type: 'operation', id: op.id })} />

      {modal?.type === 'form' && (
        <CrawlPlanFormModal plan={modal.plan || null} duplicateOf={modal.duplicateOf || null} types={types} subjects={subjects}
          fixedSubjectId={subjectId || null} onClose={() => setModal(null)} onSaved={() => load()} />
      )}
      {(modal?.type === 'test' || modal?.type === 'preview') && <CrawlPreviewModal plan={modal.plan} mode={modal.type} onClose={() => setModal(null)} />}
      {modal?.type === 'operation' && <CrawlOperationModal operationId={modal.id} onClose={() => setModal(null)} onChanged={load} />}
      {modal?.type === 'json' && (
        <Modal isOpen title={modal.plan.name} size="large" onClose={() => setModal(null)}>
          <JsonViewer value={modal.plan} label="JSON" />
        </Modal>
      )}
      {modal?.type === 'delete' && (
        <DeletePlanModal plan={modal.plan} onClose={() => setModal(null)}
          onConfirm={async (deleteLinks) => { await apiClient.deleteCrawlPlan(modal.plan.id, deleteLinks); await load(); }} />
      )}
      {notice && (
        <Modal isOpen title={t('crawl.plansTitle')} size="small" onClose={() => setNotice('')}>
          <p>{notice}</p>
          <div className="form-actions">
            <button type="button" className="btn btn-primary" onClick={() => setNotice('')}>{t('common.close')}</button>
          </div>
        </Modal>
      )}
    </div>
  );
}

export default CrawlersView;
