import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import HistoryDetailModal from '../components/HistoryDetailModal';
import { formatDateTime } from '../utils/format';

function truncate(s, n) { const v = String(s || ''); return v.length > n ? v.slice(0, n) + '…' : v; }

function listOf(resp) { return resp?.objects || resp?.items || []; }

export default function HistoryView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [threads, setThreads] = useState([]);
  const [threadId, setThreadId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [detail, setDetail] = useState(null);

  const load = useCallback(async () => {
    setLoading(true); setError('');
    try { setRows(listOf(await apiClient.listHistory(subjectId || null))); }
    catch (err) { setError(err.message); setRows([]); }
    finally { setLoading(false); }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.getSubjects().then((r) => setSubjects(listOf(r))).catch(() => {}); }, [apiClient]);
  // Load the thread list for the selected subject so turns can show/filter by their conversation.
  useEffect(() => {
    setThreadId('');
    apiClient.listThreads(subjectId || null).then((r) => setThreads(listOf(r))).catch(() => setThreads([]));
  }, [apiClient, subjectId]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || '—';
  const threadTitleById = useMemo(() => {
    const map = {};
    for (const th of threads) map[th.id] = th.title || t('threads.untitled', 'Untitled');
    return map;
  }, [threads, t]);
  const threadTitle = (id) => (id ? (threadTitleById[id] || truncate(id, 12)) : '—');

  // Thread filter is applied client-side over the loaded turns (each turn carries its threadId).
  const visibleRows = useMemo(() => (threadId ? rows.filter((r) => r.threadId === threadId) : rows), [rows, threadId]);

  const openDetail = async (row) => {
    try { const full = await apiClient.getHistoryTurn(row.id); setDetail(full?.turn ? full : { turn: row }); }
    catch { setDetail({ turn: row }); }
  };

  const columns = [
    { key: 'question', label: t('history.question', 'Question'), sortable: false, render: (v) => truncate(v, 80) },
    { key: 'subjectId', label: t('history.subject', 'Subject'), render: (v) => v ? subjectName(v) : '—' },
    { key: 'threadId', label: t('history.thread', 'Conversation'), sortable: false, render: (v) => threadTitle(v) },
    { key: 'model', label: t('history.model', 'Model'), render: (v) => v || '—' },
    { key: 'totalTokens', label: t('history.totalTokens', 'Tokens'), render: (v, r) => v || ((r.promptTokens || 0) + (r.completionTokens || 0)) },
    { key: 'createdUtc', label: t('history.created', 'Created'), render: (v) => formatDateTime(v) }
  ];

  return (
    <div>
      <PageHeader title={t('history.title', 'History')} subtitle={t('history.subtitle', 'Every chat turn, with its timing and metadata.')} />
      {error && <div className="error-banner">{error}</div>}
      <DataTable columns={columns} data={visibleRows} loading={loading} onRefresh={load} onRowClick={openDetail}
        toolbar={(
          <div className="pagination-group">
            <label>{t('history.subject', 'Subject')}:</label>
            <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('history.allSubjects', 'All subjects')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
            <label>{t('history.thread', 'Conversation')}:</label>
            <select value={threadId} onChange={(e) => setThreadId(e.target.value)} disabled={threads.length === 0}>
              <option value="">{t('history.allThreads', 'All conversations')}</option>
              {threads.map((th) => <option key={th.id} value={th.id}>{th.title || t('threads.untitled', 'Untitled')}</option>)}
            </select>
          </div>
        )}
        emptyTitle={t('history.title', 'History')} emptyDescription={t('history.empty', 'No chat history yet.')} />

      {detail && <HistoryDetailModal detail={detail} subjectName={subjectName} onClose={() => setDetail(null)} />}
    </div>
  );
}
