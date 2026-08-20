import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import CopyButton from '../components/CopyButton';
import { formatDateTime } from '../i18n/formatters';

function truncate(s, n) {
  const v = String(s || '');
  return v.length > n ? v.slice(0, n) + '…' : v;
}

function ms(v) {
  if (v == null) return '—';
  const n = Number(v);
  return n < 1000 ? `${Math.round(n)} ms` : `${(n / 1000).toFixed(1)} s`;
}

/** A row of the turn-detail key/value grid. */
function Row({ label, children }) {
  return (<><dt>{label}</dt><dd>{children}</dd></>);
}

function TurnDetail({ turn }) {
  const { t } = useTranslation();
  if (!turn) return null;
  let citations = [];
  try { citations = turn.citationsJson ? JSON.parse(turn.citationsJson) : []; } catch { citations = []; }
  const total = turn.totalTokens || ((turn.promptTokens || 0) + (turn.completionTokens || 0));
  return (
    <div className="history-detail">
      <div className="history-block">
        <span className="history-label">{t('history.question', 'Question')}</span>
        <div className="history-text">{turn.question}</div>
      </div>
      <div className="history-block">
        <span className="history-label">{t('history.answer', 'Answer')}</span>
        <div className="history-text">{turn.answer}</div>
      </div>
      {turn.thinking ? (
        <details className="history-block">
          <summary className="history-label">{t('history.thinking', 'Thinking')}</summary>
          <div className="history-text" style={{ whiteSpace: 'pre-wrap' }}>{turn.thinking}</div>
        </details>
      ) : null}
      <dl className="kv-grid">
        <Row label={t('history.id', 'Turn ID')}><CopyableId value={turn.id} /></Row>
        <Row label={t('history.subject', 'Subject')}>{turn.subjectId ? <CopyableId value={turn.subjectId} truncateLen={12} /> : '—'}</Row>
        <Row label={t('history.user', 'User')}>{turn.userId ? <CopyableId value={turn.userId} truncateLen={12} /> : '—'}</Row>
        <Row label={t('history.model', 'Model')}>{turn.model || '—'}</Row>
        <Row label={t('history.tokens', 'Tokens')}>{`${turn.promptTokens || 0} prompt / ${turn.completionTokens || 0} completion / ${total} total`}</Row>
        <Row label={t('history.ttft', 'Time to first token')}>{ms(turn.timeToFirstTokenMs)}</Row>
        <Row label={t('history.gen', 'Generation time')}>{ms(turn.generationMs)}</Row>
        <Row label={t('history.thinkingTime', 'Thinking time')}>{ms(turn.thinkingMs)}</Row>
        <Row label={t('history.context', 'Context window')}>{turn.contextSize ? `${turn.contextSize.toLocaleString()} tokens` : '—'}</Row>
        <Row label={t('history.created', 'Created')}>{formatDateTime(turn.createdUtc)}</Row>
      </dl>
      {citations.length > 0 ? (
        <div className="history-block">
          <span className="history-label">{t('history.citations', 'Citations')}</span>
          <ol className="history-citations">
            {citations.map((c, i) => (
              <li key={c.linkId || i}><a href={c.url} target="_blank" rel="noopener noreferrer">{c.title || c.url}</a></li>
            ))}
          </ol>
        </div>
      ) : null}
    </div>
  );
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
      setDetail(full?.turn || row);
    } catch { setDetail(row); }
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

      {detail && (
        <Modal title={t('history.detailTitle', 'Chat turn')} size="lg"
          headerExtra={<CopyButton value={String(detail.id)} label="ID" />}
          onClose={() => setDetail(null)}
          footer={<button type="button" className="button-secondary" onClick={() => setDetail(null)}>{t('common.close')}</button>}>
          <TurnDetail turn={detail} />
        </Modal>
      )}
    </div>
  );
}

export default HistoryView;
