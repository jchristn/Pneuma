import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatRelativeTime, formatDateTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import StatusPill from '../components/StatusPill';
import IngestionLogModal from '../components/IngestionLogModal';
import LinkSubmitModal from '../components/LinkSubmitModal';
import LinkBulkSubmitModal from '../components/LinkBulkSubmitModal';
import LinkDetailModal from '../components/LinkDetailModal';

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

  const [embeddingEndpoints, setEmbeddingEndpoints] = useState([]);
  const [completionEndpoints, setCompletionEndpoints] = useState([]);
  const [collections, setCollections] = useState([]);

  const [submitOpen, setSubmitOpen] = useState(false);
  const [form, setForm] = useState({ subjectId: '', url: '', title: '', embeddingEndpointId: '', completionEndpointId: '', collectionId: '' });
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkForm, setBulkForm] = useState({ subjectId: '', urls: '', embeddingEndpointId: '', completionEndpointId: '', collectionId: '' });
  const [bulkSubmitting, setBulkSubmitting] = useState(false);
  const [bulkError, setBulkError] = useState('');
  const [notice, setNotice] = useState('');

  const [detail, setDetail] = useState(null);
  const [logTarget, setLogTarget] = useState(null);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);

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
        const [l, c, e, col] = await Promise.allSettled([
          apiClient.getLinks({ maxResults: 1000 }),
          apiClient.getSubjects({ maxResults: 1000 }),
          apiClient.listIngestionEndpoints(),
          apiClient.listCollections()
        ]);
        if (l.status === 'fulfilled') setLinks(asArray(l.value, 'links'));
        else setError(l.reason?.message || 'Failed to load links');
        if (c.status === 'fulfilled') setSubjects(asArray(c.value, 'subjects'));
        if (e.status === 'fulfilled') {
          setEmbeddingEndpoints(asArray(e.value?.embedding).filter((x) => x.active !== false));
          setCompletionEndpoints(asArray(e.value?.completion).filter((x) => x.active !== false));
        }
        if (col.status === 'fulfilled') setCollections(asArray(col.value).filter((x) => (x.active ?? x.Active) !== false));
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

  const hasEndpoints = embeddingEndpoints.length > 0 && completionEndpoints.length > 0 && collections.length > 0;

  const openSubmit = () => {
    setForm({ subjectId: subjects[0]?.id || '', url: '', title: '', embeddingEndpointId: '', completionEndpointId: '', collectionId: collections.length === 1 ? (collections[0].id ?? collections[0].Id) : '' });
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
    if (!form.embeddingEndpointId || !form.completionEndpointId) {
      setFormError(t('links.selectModelsRequired'));
      return;
    }
    if (!form.collectionId) {
      setFormError(t('links.selectCollectionRequired'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      await apiClient.submitLink(form.subjectId, {
        url: form.url.trim(),
        title: form.title.trim(),
        embeddingEndpointId: form.embeddingEndpointId,
        completionEndpointId: form.completionEndpointId,
        collectionId: form.collectionId
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
    setBulkForm({ subjectId: subjects[0]?.id || '', urls: '', embeddingEndpointId: '', completionEndpointId: '', collectionId: collections.length === 1 ? (collections[0].id ?? collections[0].Id) : '' });
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
    if (!bulkForm.embeddingEndpointId || !bulkForm.completionEndpointId) {
      setBulkError(t('links.selectModelsRequired'));
      return;
    }
    if (!bulkForm.collectionId) {
      setBulkError(t('links.selectCollectionRequired'));
      return;
    }
    setBulkSubmitting(true);
    setBulkError('');
    try {
      const result = await apiClient.bulkSubmitLinks(bulkForm.subjectId, {
        urls,
        embeddingEndpointId: bulkForm.embeddingEndpointId,
        completionEndpointId: bulkForm.completionEndpointId,
        collectionId: bulkForm.collectionId
      });
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
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setDeleting(false);
    }
  };

  const { selectedItems, clear, selection } = useTableSelection(links);
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);

  const handleBulkDelete = async () => {
    setBulkDeleting(true);
    try {
      for (const link of selectedItems) {
        await apiClient.deleteLink(link.id);
      }
      setBulkDeleteOpen(false);
      clear();
      await load(false);
    } catch (err) {
      setError(err.message);
    } finally {
      setBulkDeleting(false);
    }
  };

  const bulkBar = (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={[
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
      render: (v) => <StatusPill status={v} />
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
        embeddingEndpoints={embeddingEndpoints}
        completionEndpoints={completionEndpoints}
        collections={collections}
        hasEndpoints={hasEndpoints}
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
        embeddingEndpoints={embeddingEndpoints}
        completionEndpoints={completionEndpoints}
        collections={collections}
        hasEndpoints={hasEndpoints}
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
    </div>
  );
}

export default LinksView;
