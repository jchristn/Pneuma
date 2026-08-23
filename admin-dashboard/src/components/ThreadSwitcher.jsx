import { useCallback, useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';

// A compact conversation switcher: a dropdown listing the subject's recent threads (title + last-activity),
// with select-to-rehydrate, a "New conversation" action, inline rename, and inline delete-with-confirm. Kept
// self-contained (its own dropdown + inline confirm) so it drops into every dashboard's Ask header regardless
// of that dashboard's modal/confirm primitives; only the `.thread-*` CSS differs per dashboard.

function ChatIcon() {
  return (
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
    </svg>
  );
}
function PlusIcon() {
  return (
    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
    </svg>
  );
}
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

function asThreads(resp) {
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
 * Conversation thread switcher.
 * @param {object} props
 * @param {object} props.apiClient - client exposing listThreads/getThread/renameThread/deleteThread.
 * @param {string|null} props.subjectId - scope threads to this subject (null = all).
 * @param {string|null} props.activeThreadId - the currently-open thread (highlighted).
 * @param {(id:string)=>void} props.onSelect - called when a thread is chosen (parent rehydrates it).
 * @param {()=>void} props.onNew - called for "New conversation".
 * @param {number} props.reloadToken - bump to force a reload (e.g. after a new thread is created).
 * @param {boolean} props.disabled - disable the control while streaming.
 */
export default function ThreadSwitcher({ apiClient, subjectId = null, activeThreadId = null, onSelect, onNew, reloadToken = 0, disabled = false }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [threads, setThreads] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [renamingId, setRenamingId] = useState(null);
  const [renameValue, setRenameValue] = useState('');
  const [confirmId, setConfirmId] = useState(null);
  const [busyId, setBusyId] = useState(null);
  const rootRef = useRef(null);

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError('');
    try {
      const resp = await apiClient.listThreads(subjectId);
      setThreads(asThreads(resp));
    } catch (err) {
      setError(err?.message || 'Failed to load conversations');
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId]);

  // Load whenever the panel opens, the subject changes, or the parent bumps the reload token.
  useEffect(() => { if (open) load(); }, [open, load, reloadToken]);

  // Close on outside click / Escape.
  useEffect(() => {
    if (!open) return undefined;
    const onDoc = (e) => { if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false); };
    const onKey = (e) => { if (e.key === 'Escape') { setOpen(false); setRenamingId(null); setConfirmId(null); } };
    document.addEventListener('mousedown', onDoc);
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('mousedown', onDoc); document.removeEventListener('keydown', onKey); };
  }, [open]);

  const select = (id) => { setOpen(false); onSelect?.(id); };
  const startNew = () => { setOpen(false); onNew?.(); };

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
      setError(err?.message || 'Rename failed');
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
      if (thread.id === activeThreadId) onNew?.();
    } catch (err) {
      setError(err?.message || 'Delete failed');
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="thread-switcher" ref={rootRef}>
      <button
        type="button"
        className="thread-toggle"
        onClick={() => setOpen((o) => !o)}
        disabled={disabled}
        aria-expanded={open}
        aria-haspopup="true"
        title={t('threads.title', 'Conversations')}
      >
        <ChatIcon />
        <span>{t('threads.title', 'Conversations')}</span>
      </button>
      {open ? (
        <div className="thread-panel" role="menu">
          <button type="button" className="thread-new" onClick={startNew}>
            <PlusIcon />
            <span>{t('threads.new', 'New conversation')}</span>
          </button>
          {error ? <div className="thread-error">{error}</div> : null}
          <div className="thread-list">
            {loading ? (
              <div className="thread-empty">{t('common.loading', 'Loading…')}</div>
            ) : threads.length === 0 ? (
              <div className="thread-empty">{t('threads.empty', 'No conversations yet.')}</div>
            ) : (
              threads.map((thread) => (
                <div key={thread.id} className={`thread-row${thread.id === activeThreadId ? ' active' : ''}`}>
                  {renamingId === thread.id ? (
                    <div className="thread-rename">
                      <input
                        className="thread-rename-input"
                        value={renameValue}
                        autoFocus
                        onChange={(e) => setRenameValue(e.target.value)}
                        onKeyDown={(e) => { if (e.key === 'Enter') commitRename(thread); if (e.key === 'Escape') setRenamingId(null); }}
                      />
                      <button type="button" className="thread-icon" title={t('common.save', 'Save')} aria-label={t('common.save', 'Save')} disabled={busyId === thread.id} onClick={() => commitRename(thread)}><CheckIcon /></button>
                      <button type="button" className="thread-icon" title={t('common.cancel', 'Cancel')} aria-label={t('common.cancel', 'Cancel')} onClick={() => setRenamingId(null)}><XIcon /></button>
                    </div>
                  ) : confirmId === thread.id ? (
                    <div className="thread-confirm">
                      <span className="thread-confirm-text">{t('threads.deleteConfirm', 'Delete this conversation?')}</span>
                      <button type="button" className="thread-icon thread-danger" title={t('common.delete', 'Delete')} aria-label={t('common.delete', 'Delete')} disabled={busyId === thread.id} onClick={() => confirmDelete(thread)}><CheckIcon /></button>
                      <button type="button" className="thread-icon" title={t('common.cancel', 'Cancel')} aria-label={t('common.cancel', 'Cancel')} onClick={() => setConfirmId(null)}><XIcon /></button>
                    </div>
                  ) : (
                    <>
                      <button type="button" className="thread-select" onClick={() => select(thread.id)} title={thread.title}>
                        <span className="thread-title">{thread.title || t('threads.untitled', 'Untitled')}</span>
                        <span className="thread-time">{relativeTime(thread.lastActivityUtc || thread.createdUtc, t)}</span>
                      </button>
                      <div className="thread-actions">
                        <button type="button" className="thread-icon" title={t('threads.rename', 'Rename')} aria-label={t('threads.rename', 'Rename')} onClick={() => beginRename(thread)}><PencilIcon /></button>
                        <button type="button" className="thread-icon thread-danger" title={t('common.delete', 'Delete')} aria-label={t('common.delete', 'Delete')} onClick={() => { setRenamingId(null); setConfirmId(thread.id); }}><TrashIcon /></button>
                      </div>
                    </>
                  )}
                </div>
              ))
            )}
          </div>
        </div>
      ) : null}
    </div>
  );
}
