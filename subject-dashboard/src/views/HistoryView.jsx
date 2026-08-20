import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import CopyableId from '../components/CopyableId';
import { formatDateTime } from '../utils/format';

function truncate(s, n) { const v = String(s || ''); return v.length > n ? v.slice(0, n) + '…' : v; }
function ms(v) { if (v == null) return '—'; const n = Number(v); return n < 1000 ? `${Math.round(n)} ms` : `${(n / 1000).toFixed(1)} s`; }

function listOf(resp) { return resp?.objects || resp?.items || []; }

export default function HistoryView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
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

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || '—';
  const openDetail = async (row) => {
    try { const full = await apiClient.getHistoryTurn(row.id); setDetail(full?.turn || row); }
    catch { setDetail(row); }
  };

  const columns = [
    { key: 'question', label: t('history.question', 'Question'), sortable: false, render: (v) => truncate(v, 90) },
    { key: 'subjectId', label: t('history.subject', 'Subject'), render: (v) => v ? subjectName(v) : '—' },
    { key: 'model', label: t('history.model', 'Model'), render: (v) => v || '—' },
    { key: 'totalTokens', label: t('history.totalTokens', 'Tokens'), render: (v, r) => v || ((r.promptTokens || 0) + (r.completionTokens || 0)) },
    { key: 'createdUtc', label: t('history.created', 'Created'), render: (v) => formatDateTime(v) }
  ];

  const total = detail ? (detail.totalTokens || ((detail.promptTokens || 0) + (detail.completionTokens || 0))) : 0;
  let citations = [];
  try { citations = detail?.citationsJson ? JSON.parse(detail.citationsJson) : []; } catch { citations = []; }

  return (
    <div>
      <PageHeader title={t('history.title', 'History')} subtitle={t('history.subtitle', 'Every chat turn, with its timing and metadata.')} />
      {error && <div className="error-banner">{error}</div>}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={openDetail}
        toolbar={(
          <div className="pagination-group">
            <label>{t('history.subject', 'Subject')}:</label>
            <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('history.allSubjects', 'All subjects')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>
        )}
        emptyTitle={t('history.title', 'History')} emptyDescription={t('history.empty', 'No chat history yet.')} />

      <Modal isOpen={!!detail} onClose={() => setDetail(null)} title={t('history.detailTitle', 'Chat turn')} size="large">
        {detail && (
          <div className="history-detail">
            <div className="history-block"><span className="history-label">{t('history.question', 'Question')}</span><div className="history-text">{detail.question}</div></div>
            <div className="history-block"><span className="history-label">{t('history.answer', 'Answer')}</span><div className="history-text">{detail.answer}</div></div>
            {detail.thinking ? <div className="history-block"><span className="history-label">{t('history.thinking', 'Thinking')}</span><div className="history-text">{detail.thinking}</div></div> : null}
            <div className="detail-grid">
              <div className="detail-item"><span className="detail-label">{t('history.model', 'Model')}</span><span className="detail-value">{detail.model || '—'}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.tokens', 'Tokens')}</span><span className="detail-value">{`${detail.promptTokens || 0} / ${detail.completionTokens || 0} / ${total}`}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.ttft', 'Time to first token')}</span><span className="detail-value">{ms(detail.timeToFirstTokenMs)}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.gen', 'Generation time')}</span><span className="detail-value">{ms(detail.generationMs)}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.thinkingTime', 'Thinking time')}</span><span className="detail-value">{ms(detail.thinkingMs)}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.context', 'Context window')}</span><span className="detail-value">{detail.contextSize ? `${detail.contextSize.toLocaleString()} tokens` : '—'}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.created', 'Created')}</span><span className="detail-value">{formatDateTime(detail.createdUtc)}</span></div>
              <div className="detail-item"><span className="detail-label">{t('history.id', 'Turn ID')}</span><span className="detail-value"><CopyableId value={detail.id} /></span></div>
            </div>
            {citations.length > 0 ? (
              <div className="history-block">
                <span className="history-label">{t('history.citations', 'Citations')}</span>
                <ol className="history-citations">{citations.map((c, i) => <li key={c.linkId || i}><a href={c.url} target="_blank" rel="noopener noreferrer">{c.title || c.url}</a></li>)}</ol>
              </div>
            ) : null}
          </div>
        )}
      </Modal>
    </div>
  );
}
