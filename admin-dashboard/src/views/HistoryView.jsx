import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ErrorBanner from '../components/ErrorBanner';
import HistoryDetailModal from '../components/HistoryDetailModal';
import { formatDateTime } from '../i18n/formatters';

function truncate(s, n) {
  const v = String(s || '');
  return v.length > n ? v.slice(0, n) + '…' : v;
}

function HistoryView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [detail, setDetail] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.listHistory(subjectId || null);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load history');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {}); }, [apiClient]);

  const openDetail = useCallback(async (row) => {
    try {
      const full = await apiClient.getHistoryTurn(row.id);
      setDetail(full?.turn ? full : { turn: row });
    } catch { setDetail({ turn: row }); }
  }, [apiClient]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || '—';

  const columns = [
    { key: 'question', label: t('history.question', 'Question'), sortable: false, cellClass: 'wrap', render: (r) => truncate(r.question, 90) },
    { key: 'subjectId', label: t('history.subject', 'Subject'), render: (r) => r.subjectId ? subjectName(r.subjectId) : '—' },
    { key: 'model', label: t('history.model', 'Model'), render: (r) => r.model || '—' },
    { key: 'totalTokens', label: t('history.totalTokens', 'Tokens'), render: (r) => r.totalTokens || ((r.promptTokens || 0) + (r.completionTokens || 0)) },
    { key: 'createdUtc', label: t('history.created', 'Created'), render: (r) => formatDateTime(r.createdUtc) }
  ];

  return (
    <div>
      <PageHeader title={t('history.title', 'History')} subtitle={t('history.subtitle', 'Every chat turn, with its timing and metadata.')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="history-subject">{t('history.subject', 'Subject')}</label>
          <select id="history-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('history.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={openDetail}
        emptyMessage={t('history.empty', 'No chat history yet.')} />

      {detail && <HistoryDetailModal detail={detail} subjectName={subjectName} onClose={() => setDetail(null)} />}
    </div>
  );
}

export default HistoryView;
