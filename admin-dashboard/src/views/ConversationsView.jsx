import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';

// Self-contained conversations management view: lists every conversation across all subjects with
// open / inline-rename / inline-delete actions. Complements the in-Ask conversation switcher (which is
// scoped to the current subject). Uses small inline SVGs so it carries no shared Icon dependency.

function PencilIcon() {
  return (
    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M12 20h9" /><path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4z" />
    </svg>
  );
}
function TrashIcon() {
  return (
    <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <polyline points="3 6 5 6 21 6" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" />
    </svg>
  );
}
function CheckIcon() {
  return (<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><polyline points="20 6 9 17 4 12" /></svg>);
}
function XIcon() {
  return (<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></svg>);
}

function listOf(resp) {
  if (Array.isArray(resp)) return resp;
  if (resp && Array.isArray(resp.objects)) return resp.objects;
  if (resp && Array.isArray(resp.items)) return resp.items;
  return [];
}

function relativeTime(iso, t) {
  if (!iso) return '';
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return '';
  const secs = Math.max(0, Math.floor((Date.now() - then) / 1000));
  if (secs < 60) return t('threads.justNow', 'just now');
  const mins = Math.floor(secs / 60);
  if (mins < 60) return t('threads.minutesAgo', '{{count}}m ago', { count: mins });
  const hours = Math.floor(mins / 60);
  if (hours < 24) return t('threads.hoursAgo', '{{count}}h ago', { count: hours });
  const days = Math.floor(hours / 24);
  return t('threads.daysAgo', '{{count}}d ago', { count: days });
}

/**
 * A dedicated view listing every conversation, across all subjects, with open / rename / delete actions.
 */
function ConversationsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const navigate = useNavigate();

  const [threads, setThreads] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [renamingId, setRenamingId] = useState(null);
  const [renameValue, setRenameValue] = useState('');
  const [confirmId, setConfirmId] = useState(null);
  const [busyId, setBusyId] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setThreads(listOf(await apiClient.listThreads(null)));
    } catch (err) {
      setError(err?.message || t('conversations.loadError', 'Failed to load conversations.'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    apiClient.list('subjects', { maxResults: 1000 })
      .then((resp) => setSubjects(normalizeList(resp).items))
      .catch(() => {});
  }, [apiClient]);

  const subjectById = useMemo(() => {
    const map = {};
    for (const s of subjects) map[s.id] = s;
    return map;
  }, [subjects]);

  const openThread = (thread) => {
    navigate('/dashboard/ask?thread=' + encodeURIComponent(thread.id));
  };

  const beginRename = (thread) => { setConfirmId(null); setRenamingId(thread.id); setRenameValue(thread.title || ''); };
  const commitRename = async (thread) => {
    const title = (renameValue || '').trim();
    if (!title || title === thread.title) { setRenamingId(null); return; }
    setBusyId(thread.id);
    try {
      await apiClient.renameThread(thread.id, title);
      setThreads((prev) => prev.map((x) => (x.id === thread.id ? { ...x, title } : x)));
      setRenamingId(null);
    } catch (err) {
      setError(err?.message || t('conversations.renameError', 'Rename failed.'));
    } finally {
      setBusyId(null);
    }
  };
  const confirmDelete = async (thread) => {
    setBusyId(thread.id);
    try {
      await apiClient.deleteThread(thread.id);
      setThreads((prev) => prev.filter((x) => x.id !== thread.id));
      setConfirmId(null);
    } catch (err) {
      setError(err?.message || t('conversations.deleteError', 'Delete failed.'));
    } finally {
      setBusyId(null);
    }
  };

  const subjectName = (thread) => {
    const subject = thread.subjectId ? subjectById[thread.subjectId] : null;
    return subject?.displayName || subject?.name || t('conversations.unknownSubject', 'Unknown subject');
  };

  return (
    <div className="view conv-view">
      <PageHeader
        title={t('conversations.title', 'Conversations')}
        subtitle={t('conversations.subtitle', 'All conversations across every subject.')}
        actions={(
          <button type="button" className="conv-btn" onClick={load} disabled={loading}>
            {t('common.refresh', 'Refresh')}
          </button>
        )}
      />

      {error ? <div className="error-banner" role="alert">{error}</div> : null}

      {loading ? (
        <div className="conv-empty">{t('common.loading', 'Loading…')}</div>
      ) : threads.length === 0 ? (
        <div className="conv-empty">{t('conversations.empty', 'No conversations yet. Ask a subject a question to start one.')}</div>
      ) : (
        <ul className="conv-list">
          {threads.map((thread) => (
            <li key={thread.id} className="conv-item">
              {renamingId === thread.id ? (
                <div className="conv-rename">
                  <input
                    className="conv-rename-input"
                    value={renameValue}
                    autoFocus
                    onChange={(e) => setRenameValue(e.target.value)}
                    onKeyDown={(e) => { if (e.key === 'Enter') commitRename(thread); if (e.key === 'Escape') setRenamingId(null); }}
                  />
                  <button type="button" className="conv-btn" disabled={busyId === thread.id} onClick={() => commitRename(thread)}><CheckIcon /><span>{t('common.save', 'Save')}</span></button>
                  <button type="button" className="conv-btn" onClick={() => setRenamingId(null)}><XIcon /><span>{t('common.cancel', 'Cancel')}</span></button>
                </div>
              ) : (
                <>
                  <button type="button" className="conv-main" onClick={() => openThread(thread)} title={t('conversations.open', 'Open conversation')}>
                    <span className="conv-title">{thread.title || t('threads.untitled', 'Untitled')}</span>
                    <span className="conv-meta">
                      <span className="conv-subject">{subjectName(thread)}</span>
                      <span className="conv-dot" aria-hidden="true">·</span>
                      <span className="conv-time">{relativeTime(thread.lastActivityUtc || thread.createdUtc, t)}</span>
                    </span>
                  </button>
                  {confirmId === thread.id ? (
                    <div className="conv-actions">
                      <span className="conv-confirm-text">{t('threads.deleteConfirm', 'Delete this conversation?')}</span>
                      <button type="button" className="conv-btn conv-btn-danger" disabled={busyId === thread.id} onClick={() => confirmDelete(thread)}>{t('common.delete', 'Delete')}</button>
                      <button type="button" className="conv-btn" onClick={() => setConfirmId(null)}>{t('common.cancel', 'Cancel')}</button>
                    </div>
                  ) : (
                    <div className="conv-actions">
                      <button type="button" className="conv-btn" onClick={() => openThread(thread)}>{t('conversations.openAction', 'Open')}</button>
                      <button type="button" className="conv-btn" onClick={() => beginRename(thread)}><PencilIcon /><span>{t('threads.rename', 'Rename')}</span></button>
                      <button type="button" className="conv-btn conv-btn-danger" onClick={() => { setRenamingId(null); setConfirmId(thread.id); }}><TrashIcon /><span>{t('common.delete', 'Delete')}</span></button>
                    </div>
                  )}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default ConversationsView;
