import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatRelativeTime, formatDateTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ConfirmModal from '../components/ConfirmModal';
import Modal from '../components/Modal';
import ActionMenu from '../components/ActionMenu';
import StatusPill from '../components/StatusPill';
import IngestionLogModal from '../components/IngestionLogModal';
import LinkSubmitModal from '../components/LinkSubmitModal';
import LinkBulkSubmitModal from '../components/LinkBulkSubmitModal';
import LinkDetailModal from '../components/LinkDetailModal';
import { toLabelTagPayload } from '../components/LabelTagEditor';

const AUTO_REFRESH_OPTIONS = [
  { value: 0, label: 'Off' },
  { value: 5000, label: '5s' },
  { value: 15000, label: '15s' },
  { value: 30000, label: '30s' }
];

function isFailed(status) {
  const s = (status || '').toLowerCase();
  return s === 'failed' || s === 'error';
}

function LinksView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();

  const [links, setLinks] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [autoRefreshMs, setAutoRefreshMs] = useState(0);

  const [submitOpen, setSubmitOpen] = useState(false);
  const [form, setForm] = useState({ subjectId: '', url: '', title: '', labels: [], tags: [] });
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkForm, setBulkForm] = useState({ subjectId: '', urls: '', labels: [], tags: [] });
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [bulkError, setBulkError] = useState('');
  const [notice, setNotice] = useState('');
  // Background cascade-delete notice shown as a modal (consistent with subject deletion) so it is not missed.
  const [deleteNotice, setDeleteNotice] = useState('');

  const [detail, setDetail] = useState(null);
  const [logTarget, setLogTarget] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);
  const [reingestTarget, setReingestTarget] = useState(null);
  const [reingesting, setReingesting] = useState(false);

  const loadRef = useRef();

  const subjectName = useMemo(() => {
    const map = {};
    for (const c of subjects) map[c.id] = c.displayName || c.id;
    return map;
  }, [subjects]);

  const load = useCallback(
    async (showSpinner = true) => {
      if (!apiClient) return;
      if (showSpinner) setLoading(true);
      setError('');
      try {
        const [l, c] = await Promise.allSettled([
          apiClient.getLinks({ maxResults: 1000 }),
          apiClient.getSubjects({ maxResults: 1000 })
        ]);
        if (l.status === 'fulfilled') setLinks(asArray(l.value, 'links'));
        else setError(l.reason?.message || 'Failed to load links');
        if (c.status === 'fulfilled') setSubjects(asArray(c.value, 'subjects'));
      } finally {
        if (showSpinner) setLoading(false);
      }
    },
    [apiClient]
  );

  loadRef.current = load;

  useEffect(() => {
    load();
  }, [load]);

  // Auto-refresh so status transitions are visible.
  useEffect(() => {
    if (autoRefreshMs <= 0) return undefined;
    const id = window.setInterval(() => loadRef.current?.(false), autoRefreshMs);
    return () => window.clearInterval(id);
  }, [autoRefreshMs]);

  const openSubmit = () => {
    setForm({ subjectId: subjects[0]?.id || '', url: '', title: '', labels: [], tags: [] });
    setFormError('');
    setNotice('');
    setSubmitOpen(true);
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!form.subjectId) {
      setFormError('Select a subject.');
      return;
    }
    if (!form.url.trim()) {
      setFormError('URL is required.');
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      const { labels, tags } = toLabelTagPayload(form.labels, form.tags);
      await apiClient.submitLink(form.subjectId, {
        url: form.url.trim(),
        title: form.title.trim(),
        labels,
        tags
      });
      setSubmitOpen(false);
      await load(false);
    } catch (err) {
      setFormError(err.message);
    } finally {
      setSubmitting(false);
    }
  };

  const openBulk = () => {
    setBulkForm({ subjectId: subjects[0]?.id || '', urls: '', labels: [], tags: [] });
    setBulkError('');
    setNotice('');
    setBulkOpen(true);
  };

  const handleBulkSubmit = async (e) => {
    e.preventDefault();
    if (!bulkForm.subjectId) {
      setBulkError('Select a subject.');
      return;
    }
    const urls = bulkForm.urls
      .split('\n')
      .map((u) => u.trim())
      .filter((u) => u.length > 0);
    if (urls.length === 0) {
      setBulkError(t('links.urlsRequired'));
      return;
    }
    setBulkSubmitting(true);
    setBulkError('');
    try {
      const { labels, tags } = toLabelTagPayload(bulkForm.labels, bulkForm.tags);
      const result = await apiClient.bulkSubmitLinks(bulkForm.subjectId, { urls, labels, tags });
      const created = typeof result?.created === 'number' ? result.created : asArray(result, 'links').length;
      setBulkOpen(false);
      setNotice(t('links.bulkCreated', { count: created }));
      await load(false);
    } catch (err) {
      setBulkError(err.message);
    } finally {
      setBulkSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await apiClient.deleteLink(deleteTarget.id);
      setDeleteTarget(null);
      setDeleteNotice(t('links.deletingBackground', 'We are deleting this content link and everything associated with it in the background. You may close this window.'));
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setDeleting(false);
    }
  };

  // Reingesting a link queues a FRESH ingestion job server-side (a full re-run), so it works even when the
  // link has never produced a job before.
  const handleReingest = async () => {
    if (!reingestTarget) return;
    setReingesting(true);
    setError('');
    setNotice('');
    try {
      await apiClient.reingestLink(reingestTarget.id);
      setReingestTarget(null);
      setNotice(t('links.reingestQueued', 'Reingestion queued.'));
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setReingesting(false);
    }
  };

  const { selectedItems, clear, selection } = useTableSelection(links);
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);
  const [bulkReingestOpen, setBulkReingestOpen] = useState(false);
  const [bulkReingesting, setBulkReingesting] = useState(false);

  const handleBulkDelete = async () => {
    setBulkDeleting(true);
    try {
      // One server request marks every selected link for background cascade deletion — no per-link fan-out.
      await apiClient.bulkDeleteLinks(selectedItems.map((link) => link.id));
      setBulkDeleteOpen(false);
      clear();
      setDeleteNotice(t('links.deletingBackgroundBulk', 'We are deleting the selected content links in the background. You may close this window.'));
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setBulkDeleting(false);
    }
  };

  // One server request queues a fresh ingestion job for every selected link — no per-link fan-out.
  const handleBulkReingest = async () => {
    setBulkReingesting(true);
    setError('');
    setNotice('');
    try {
      const ids = selectedItems.map((link) => link.id);
      const result = await apiClient.bulkReingestLinks(ids);
      const queued = typeof result?.queued === 'number' ? result.queued : ids.length;
      const skipped = typeof result?.skipped === 'number' ? result.skipped : 0;
      setBulkReingestOpen(false);
      clear();
      const base = t('links.bulkReingestQueued', { count: queued, defaultValue: `Reingestion queued for ${queued} link(s).` });
      setNotice(skipped > 0
        ? `${base} ${t('links.bulkReingestSkipped', { count: skipped, defaultValue: `${skipped} skipped.` })}`
        : base);
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setBulkReingesting(false);
    }
  };

  const bulkBar = (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={[
        { key: 'reingest', label: t('links.reingestMultiple', 'Reingest Links'), onClick: () => setBulkReingestOpen(true) },
        { key: 'delete', label: t('common.delete'), danger: true, onClick: () => setBulkDeleteOpen(true) }
      ]}
    />
  );

  const columns = [
    {
      key: 'url',
      label: t('links.url'),
      className: 'cell-url',
      render: (v) => (
        <a href={v} target="_blank" rel="noopener noreferrer" title={v} onClick={(e) => e.stopPropagation()}>
          {v}
        </a>
      )
    },
    {
      key: 'title',
      label: t('links.linkTitle'),
      render: (v) => v || '(untitled)'
    },
    {
      key: 'subjectId',
      label: t('links.subject'),
      render: (v, row) => subjectName[v] || subjectName[row.subjectId] || '—'
    },
    {
      key: 'status',
      label: t('common.status'),
      render: (v, row) => {
        const ds = row.deletionStatus;
        if (ds === 'Pending' || ds === 'Deleting') return <span className="hd-muted">{t('links.deletingStatus', 'deleting…')}</span>;
        if (ds === 'Failed') return <span className="hd-muted">{t('links.deletionFailed', 'deletion failed')}</span>;
        return <StatusPill status={v} />;
      }
    },
    {
      key: 'lastIngestedUtc',
      label: t('links.lastIngested'),
      sortAccessor: (row) => row.lastIngestedUtc || '',
      render: (v) =>
        v ? (
          <span title={formatDateTime(v)}>{formatRelativeTime(v)}</span>
        ) : (
          <span style={{ color: 'var(--text-muted)' }}>{t('common.never')}</span>
        )
    },
    {
      key: 'lastError',
      label: t('links.lastError'),
      sortable: false,
      className: 'cell-error',
      render: (v, row) => (isFailed(row.status) && v ? <span title={v}>{v}</span> : '—')
    },
    {
      key: '_actions',
      label: t('common.actions'),
      className: 'actions-column',
      sortable: false,
      render: (_v, row) => (
        <ActionMenu
          actions={[
            { label: t('common.view'), onClick: () => setDetail(row) },
            { label: t('links.viewIngestionLog'), onClick: () => setLogTarget(row) },
            { label: t('links.reingest', 'Reingest Link'), onClick: () => setReingestTarget(row) },
            { label: t('common.delete'), variant: 'danger', onClick: () => setDeleteTarget(row) }
          ]}
        />
      )
    }
  ];

  const toolbar = (
    <div className="pagination-group">
      <label>{t('links.autoRefresh')}:</label>
      <select value={autoRefreshMs} onChange={(e) => setAutoRefreshMs(parseInt(e.target.value, 10))}>
        {AUTO_REFRESH_OPTIONS.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  );

  return (
    <div>
      <PageHeader
        title={t('links.title')}
        subtitle={t('links.subtitle')}
        actions={
          <div style={{ display: 'flex', gap: 8 }}>
            <button className="btn btn-secondary" onClick={openBulk} disabled={subjects.length === 0}>
              {t('links.addMultiple')}
            </button>
            <button className="btn btn-primary" onClick={openSubmit} disabled={subjects.length === 0}>
              {t('links.submit')}
            </button>
          </div>
        }
      />

      {error && <div className="error-banner">{error}</div>}
      {notice && (
        <div className="error-banner" style={{ background: 'var(--color-success-bg)', color: 'var(--color-success)', borderColor: 'var(--color-success)' }}>
          {notice}
        </div>
      )}
      <Modal
        isOpen={!!deleteNotice}
        onClose={() => setDeleteNotice('')}
        title={t('links.deletingTitle', 'Deleting in the background')}
      >
        <p>{deleteNotice}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-primary" onClick={() => setDeleteNotice('')}>{t('common.close')}</button>
        </div>
      </Modal>
      {subjects.length === 0 && !loading && (
        <div className="error-banner" style={{ background: 'var(--color-warning-bg)', color: 'var(--color-warning)', borderColor: 'var(--color-warning)' }}>
          {t('subjects.empty')}
        </div>
      )}

      <DataTable
        columns={columns}
        data={links}
        loading={loading}
        onRefresh={() => load()}
        toolbar={toolbar}
        emptyTitle={t('links.title')}
        emptyDescription={t('links.empty')}
        selection={selection}
        bulkBar={bulkBar}
      />

      <LinkSubmitModal
        isOpen={submitOpen}
        onClose={() => setSubmitOpen(false)}
        subjects={subjects}
        form={form}
        setForm={setForm}
        formError={formError}
        submitting={submitting}
        onSubmit={handleSubmit}
      />

      <LinkBulkSubmitModal
        isOpen={bulkOpen}
        onClose={() => setBulkOpen(false)}
        subjects={subjects}
        bulkForm={bulkForm}
        setBulkForm={setBulkForm}
        bulkError={bulkError}
        bulkSubmitting={bulkSubmitting}
        onSubmit={handleBulkSubmit}
      />

      <LinkDetailModal detail={detail} subjectName={subjectName} onClose={() => setDetail(null)} />

      <IngestionLogModal
        isOpen={!!logTarget}
        link={logTarget}
        onClose={() => setLogTarget(null)}
      />

      <ConfirmModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={handleDelete}
        title={t('common.delete')}
        message="Delete this content link? This cannot be undone."
        entityName={deleteTarget?.url}
        confirmLabel={t('common.delete')}
        isLoading={deleting}
      />

      <ConfirmModal
        isOpen={bulkDeleteOpen}
        onClose={() => setBulkDeleteOpen(false)}
        onConfirm={handleBulkDelete}
        title={t('common.delete')}
        message={`Delete ${selectedItems.length} selected content link(s)? This cannot be undone.`}
        confirmLabel={t('common.delete')}
        isLoading={bulkDeleting}
      />

      <ConfirmModal
        isOpen={!!reingestTarget}
        onClose={() => setReingestTarget(null)}
        onConfirm={handleReingest}
        title={t('links.reingest', 'Reingest Link')}
        message={t('links.reingestConfirm', 'Reingest this content link? Its ingestion pipeline will run again from the beginning.')}
        entityName={reingestTarget?.url}
        confirmLabel={t('links.reingest', 'Reingest Link')}
        isLoading={reingesting}
      />

      <ConfirmModal
        isOpen={bulkReingestOpen}
        onClose={() => setBulkReingestOpen(false)}
        onConfirm={handleBulkReingest}
        title={t('links.reingestMultiple', 'Reingest Links')}
        message={t('links.bulkReingestConfirm', { count: selectedItems.length, defaultValue: `Reingest ${selectedItems.length} selected content link(s)? Each link's ingestion pipeline will run again from the beginning.` })}
        confirmLabel={t('links.reingestMultiple', 'Reingest Links')}
        isLoading={bulkReingesting}
      />
    </div>
  );
}

export default LinksView;
