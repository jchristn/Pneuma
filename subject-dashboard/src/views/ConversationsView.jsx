import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import { formatDateTime } from '../utils/format';

function listOf(resp) { return resp?.objects || resp?.items || (Array.isArray(resp) ? resp : []); }

/**
 * A dedicated, fully-featured table of every conversation in the tenant: subject and user filters,
 * sortable/paginated columns (title, subject, user, id, last activity) and a per-row context menu
 * (open / rename / delete). Mirrors the other admin tables' conventions (DataTable + ActionMenu).
 */
export default function ConversationsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();

  const [threads, setThreads] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [subjectId, setSubjectId] = useState('');
  const [userId, setUserId] = useState('');

  const [renameTarget, setRenameTarget] = useState(null);
  const [renameValue, setRenameValue] = useState('');
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState(null);
  const [deleting, setDeleting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setThreads(listOf(await apiClient.listThreads(null))); }
    catch (err) { setError(err?.message || t('conversations.loadError', 'Failed to load conversations.')); setThreads([]); }
    finally { setLoading(false); }
  }, [apiClient, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.getSubjects({ maxResults: 1000 }).then((r) => setSubjects(asArray(r, 'subjects'))).catch(() => {}); }, [apiClient]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || (id ? id : '—');

  // No user directory on this dashboard, so the user filter is built from the ids present in the data.
  const userIds = useMemo(() => {
    const set = new Set();
    for (const th of threads) if (th.userId) set.add(th.userId);
    return Array.from(set);
  }, [threads]);

  const visibleRows = useMemo(() => threads.filter((th) =>
    (!subjectId || th.subjectId === subjectId) && (!userId || th.userId === userId)
  ), [threads, subjectId, userId]);

  const openThread = (thread) => navigate('/dashboard/ask?thread=' + encodeURIComponent(thread.id));
  const beginRename = (thread) => { setRenameValue(thread.title || ''); setRenameTarget(thread); };

  const commitRename = async () => {
    const title = (renameValue || '').trim();
    if (!renameTarget || !title || title === renameTarget.title) { setRenameTarget(null); return; }
    setSaving(true);
    try {
      await apiClient.renameThread(renameTarget.id, title);
      setThreads((prev) => prev.map((x) => (x.id === renameTarget.id ? { ...x, title } : x)));
      setRenameTarget(null);
    } catch (err) { setError(err?.message || t('conversations.renameError', 'Rename failed.')); }
    finally { setSaving(false); }
  };

  const confirmDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await apiClient.deleteThread(deleteTarget.id);
      setThreads((prev) => prev.filter((x) => x.id !== deleteTarget.id));
      setDeleteTarget(null);
    } catch (err) { setError(err?.message || t('conversations.deleteError', 'Delete failed.')); }
    finally { setDeleting(false); }
  };

  const columns = [
    { key: 'title', label: t('conversations.colTitle', 'Title'), render: (v) => v || t('threads.untitled', 'Untitled') },
    { key: 'subjectId', label: t('conversations.colSubject', 'Subject'), render: (v) => subjectName(v) },
    { key: 'userId', label: t('conversations.colUser', 'User'), render: (v) => v || '—' },
    { key: 'id', label: 'ID', sortable: false, render: (v) => <code className="mono-id">{v}</code> },
    { key: 'lastActivityUtc', label: t('conversations.colActivity', 'Last activity'), render: (v, r) => formatDateTime(v || r.createdUtc) },
    {
      key: '_actions', label: t('common.actions', 'Actions'), sortable: false,
      render: (v, row) => (
        <ActionMenu
          items={[
            { label: t('conversations.open', 'Open'), onClick: () => openThread(row) },
            { label: t('threads.rename', 'Rename'), onClick: () => beginRename(row) },
            { label: t('common.delete', 'Delete'), variant: 'danger', onClick: () => setDeleteTarget(row) }
          ]}
        />
      )
    }
  ];

  return (
    <div>
      <PageHeader title={t('conversations.title', 'Conversations')} subtitle={t('conversations.subtitle', 'Every conversation across all subjects.')} />
      {error && <div className="error-banner">{error}</div>}
      <DataTable columns={columns} data={visibleRows} loading={loading} onRefresh={load} onRowClick={openThread}
        toolbar={(
          <div className="pagination-group">
            <label>{t('conversations.colSubject', 'Subject')}:</label>
            <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('history.allSubjects', 'All subjects')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
            <label>{t('conversations.colUser', 'User')}:</label>
            <select value={userId} onChange={(e) => setUserId(e.target.value)} disabled={userIds.length === 0}>
              <option value="">{t('conversations.allUsers', 'All users')}</option>
              {userIds.map((uid) => <option key={uid} value={uid}>{uid}</option>)}
            </select>
          </div>
        )}
        emptyTitle={t('conversations.title', 'Conversations')}
        emptyDescription={t('conversations.empty', 'No conversations yet.')} />

      <Modal isOpen={!!renameTarget} onClose={() => setRenameTarget(null)} title={t('threads.rename', 'Rename conversation')} size="small">
        <div className="form-group">
          <label htmlFor="conv-rename">{t('conversations.colTitle', 'Title')}</label>
          <input id="conv-rename" value={renameValue} autoFocus
            onChange={(e) => setRenameValue(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') commitRename(); }} />
        </div>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={() => setRenameTarget(null)} disabled={saving}>{t('common.cancel', 'Cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={commitRename} disabled={saving}>{t('common.save', 'Save')}</button>
        </div>
      </Modal>

      <ConfirmModal
        isOpen={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={confirmDelete}
        title={t('conversations.deleteTitle', 'Delete conversation')}
        message={t('threads.deleteConfirm', 'Delete this conversation?')}
        entityName={deleteTarget?.title || ''}
        confirmLabel={t('common.delete', 'Delete')}
        variant="danger"
        isLoading={deleting} />
    </div>
  );
}
