import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import ErrorBanner from '../components/ErrorBanner';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import { formatDateTime } from '../i18n/formatters';

/**
 * A dedicated, fully-featured table of every conversation in the tenant: subject and user filters,
 * sortable/paginated columns (title, subject, user, id, last activity) and a per-row context menu
 * (open / rename / delete). Mirrors the other admin tables' conventions (DataTable + ActionMenu).
 */
function ConversationsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();

  const [threads, setThreads] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const [subjectId, setSubjectId] = useState('');
  const [userId, setUserId] = useState('');

  const [renameTarget, setRenameTarget] = useState(null);
  const [renameValue, setRenameValue] = useState('');
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setThreads(normalizeList(await apiClient.listThreads(null)).items); }
    catch (err) { setError(err?.message || 'Failed to load conversations'); setThreads([]); }
    finally { setLoading(false); }
  }, [apiClient]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.list('subjects', { maxResults: 1000 }).then((r) => setSubjects(normalizeList(r).items)).catch(() => {}); }, [apiClient]);
  useEffect(() => { apiClient.list('users', { maxResults: 1000 }).then((r) => setUsers(normalizeList(r).items)).catch(() => {}); }, [apiClient]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || (id || '—');
  const userName = (id) => { const u = users.find((x) => x.id === id); return u ? (u.email || u.fullName || u.firstName || u.id) : (id || '—'); };

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
    } catch (err) { setError(err?.message || 'Rename failed'); }
    finally { setSaving(false); }
  };

  const columns = [
    { key: 'title', label: t('conversations.colTitle', 'Title'), render: (r) => r.title || t('threads.untitled', 'Untitled') },
    { key: 'subjectId', label: t('conversations.colSubject', 'Subject'), render: (r) => subjectName(r.subjectId) },
    { key: 'userId', label: t('conversations.colUser', 'User'), render: (r) => userName(r.userId) },
    { key: 'id', label: 'ID', sortable: false, render: (r) => <code className="mono-id">{r.id}</code> },
    { key: 'lastActivityUtc', label: t('conversations.colActivity', 'Last activity'), render: (r) => formatDateTime(r.lastActivityUtc || r.createdUtc) },
    {
      key: '_actions', label: t('common.actions', 'Actions'), sortable: false,
      render: (r) => (
        <ActionMenu
          items={[
            { label: t('conversations.open', 'Open'), onClick: () => openThread(r) },
            { label: t('threads.rename', 'Rename'), onClick: () => beginRename(r) },
            { label: t('common.delete', 'Delete'), variant: 'danger', onClick: () => setDeleteTarget(r) }
          ]}
        />
      )
    }
  ];

  return (
    <div>
      <PageHeader title={t('conversations.title', 'Conversations')} subtitle={t('conversations.subtitle', 'Every conversation across all subjects and users.')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="conv-subject">{t('conversations.colSubject', 'Subject')}</label>
          <select id="conv-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('history.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="conv-user">{t('conversations.colUser', 'User')}</label>
          <select id="conv-user" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">{t('conversations.allUsers', 'All users')}</option>
            {users.map((u) => <option key={u.id} value={u.id}>{u.email || u.fullName || u.id}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={visibleRows} loading={loading} onRefresh={load} onRowClick={openThread}
        emptyMessage={t('conversations.empty', 'No conversations yet.')} />

      {renameTarget && (
        <Modal
          title={t('threads.rename', 'Rename conversation')}
          size="sm"
          onClose={() => setRenameTarget(null)}
          footer={(
            <>
              <button type="button" className="button-secondary" onClick={() => setRenameTarget(null)} disabled={saving}>{t('common.cancel', 'Cancel')}</button>
              <button type="button" className="button-primary" onClick={commitRename} disabled={saving}>{t('common.save', 'Save')}</button>
            </>
          )}>
          <div className="field">
            <label htmlFor="conv-rename">{t('conversations.colTitle', 'Title')}</label>
            <input id="conv-rename" value={renameValue} autoFocus
              onChange={(e) => setRenameValue(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') commitRename(); }} />
          </div>
        </Modal>
      )}

      {deleteTarget && (
        <ConfirmModal
          title={t('conversations.deleteTitle', 'Delete conversation')}
          message={t('threads.deleteConfirm', 'Delete this conversation?') + (deleteTarget.title ? ` (“${deleteTarget.title}”)` : '')}
          confirmLabel={t('common.delete', 'Delete')}
          danger
          onConfirm={async () => {
            await apiClient.deleteThread(deleteTarget.id);
            setThreads((prev) => prev.filter((x) => x.id !== deleteTarget.id));
          }}
          onClose={() => setDeleteTarget(null)} />
      )}
    </div>
  );
}

export default ConversationsView;
