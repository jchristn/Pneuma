import { useState, useEffect, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import Icon from '../components/Icon.jsx';

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
 * A dedicated view listing every conversation the user has, across all subjects, with open / rename /
 * delete actions. Complements the in-Ask conversation switcher (which is scoped to the current subject).
 */
export default function ConversationsView() {
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
  useEffect(() => { apiClient.getSubjects().then((r) => setSubjects(listOf(r))).catch(() => {}); }, [apiClient]);

  const subjectById = useMemo(() => {
    const map = {};
    for (const s of subjects) map[s.id] = s;
    return map;
  }, [subjects]);

  const openThread = (thread) => {
    const subject = thread.subjectId ? subjectById[thread.subjectId] : null;
    const slug = subject?.urlSlug || subject?.slug;
    if (slug) navigate(`/${encodeURIComponent(slug)}?thread=${encodeURIComponent(thread.id)}`);
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

  return (
    <div className="view conversations-view">
      <div className="conv-head">
        <div>
          <h1 className="page-title">{t('conversations.title', 'Conversations')}</h1>
          <p className="page-subtitle">{t('conversations.subtitle', 'All of your conversations across every subject.')}</p>
        </div>
        <button type="button" className="button button-secondary" onClick={load} disabled={loading}>
          {t('common.refresh', 'Refresh')}
        </button>
      </div>

      {error ? <div className="error-banner" role="alert">{error}</div> : null}

      {loading ? (
        <div className="conv-empty"><div className="spinner" /></div>
      ) : threads.length === 0 ? (
        <div className="conv-empty">{t('conversations.empty', 'No conversations yet. Ask a subject a question to start one.')}</div>
      ) : (
        <ul className="conv-list">
          {threads.map((thread) => {
            const subject = thread.subjectId ? subjectById[thread.subjectId] : null;
            const slug = subject?.urlSlug || subject?.slug;
            return (
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
                    <button type="button" className="button button-primary" disabled={busyId === thread.id} onClick={() => commitRename(thread)}>{t('common.save', 'Save')}</button>
                    <button type="button" className="button button-secondary" onClick={() => setRenamingId(null)}>{t('common.cancel', 'Cancel')}</button>
                  </div>
                ) : (
                  <>
                    <button type="button" className="conv-main" onClick={() => openThread(thread)} disabled={!slug} title={slug ? t('conversations.open', 'Open conversation') : t('conversations.subjectMissing', 'This conversation’s subject is unavailable')}>
                      <span className="conv-title">{thread.title || t('threads.untitled', 'Untitled')}</span>
                      <span className="conv-meta">
                        <span className="conv-subject">{subject?.displayName || t('conversations.unknownSubject', 'Unknown subject')}</span>
                        <span className="conv-dot" aria-hidden="true">·</span>
                        <span className="conv-time">{relativeTime(thread.lastActivityUtc || thread.createdUtc, t)}</span>
                      </span>
                    </button>
                    {confirmId === thread.id ? (
                      <div className="conv-actions">
                        <span className="conv-confirm-text">{t('threads.deleteConfirm', 'Delete this conversation?')}</span>
                        <button type="button" className="button button-danger" disabled={busyId === thread.id} onClick={() => confirmDelete(thread)}>{t('common.delete', 'Delete')}</button>
                        <button type="button" className="button button-secondary" onClick={() => setConfirmId(null)}>{t('common.cancel', 'Cancel')}</button>
                      </div>
                    ) : (
                      <div className="conv-actions">
                        <button type="button" className="button button-secondary conv-btn" onClick={() => beginRename(thread)}>
                          <Icon name="edit" size={14} /><span>{t('threads.rename', 'Rename')}</span>
                        </button>
                        <button type="button" className="button button-danger conv-btn" onClick={() => { setRenamingId(null); setConfirmId(thread.id); }}>
                          <Icon name="trash" size={14} /><span>{t('common.delete', 'Delete')}</span>
                        </button>
                      </div>
                    )}
                  </>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
