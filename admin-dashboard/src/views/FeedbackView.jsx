import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import { formatDateTime } from '../i18n/formatters';

function truncate(s, n) {
  const v = String(s || '');
  return v.length > n ? v.slice(0, n) + '…' : v;
}

function ratingIcon(rating) {
  if (rating === 'Up') return '👍';
  if (rating === 'Down') return '👎';
  return '💬';
}

function FeedbackView() {
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
      const resp = await apiClient.listFeedback(subjectId || null);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load feedback');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {}); }, [apiClient]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || '—';

  // Each row is { feedback, turn }; flatten a display shape for the table.
  const flat = rows.map((r) => ({ ...r, id: r.feedback?.id, _fb: r.feedback, _turn: r.turn }));

  const columns = [
    { key: 'rating', label: t('feedback.rating', 'Rating'), sortable: false, render: (r) => <span title={r._fb?.rating}>{ratingIcon(r._fb?.rating)}</span> },
    { key: 'comment', label: t('feedback.comment', 'Comment'), sortable: false, cellClass: 'wrap', render: (r) => r._fb?.comment ? truncate(r._fb.comment, 80) : '—' },
    { key: 'question', label: t('feedback.question', 'Question'), sortable: false, cellClass: 'wrap', render: (r) => truncate(r._turn?.question, 70) },
    { key: 'subjectId', label: t('feedback.subject', 'Subject'), render: (r) => r._fb?.subjectId ? subjectName(r._fb.subjectId) : '—' },
    { key: 'createdUtc', label: t('feedback.created', 'Created'), render: (r) => formatDateTime(r._fb?.createdUtc) }
  ];

  return (
    <div>
      <PageHeader title={t('feedback.title', 'Feedback')} subtitle={t('feedback.subtitle', 'Thumbs up/down and comments left on chat answers.')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="feedback-subject">{t('feedback.subject', 'Subject')}</label>
          <select id="feedback-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('feedback.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={flat} loading={loading} onRefresh={load} onRowClick={(r) => setDetail(r)}
        emptyMessage={t('feedback.empty', 'No feedback yet.')} />

      {detail && (
        <Modal title={`${ratingIcon(detail._fb?.rating)} ${t('feedback.detailTitle', 'Feedback')}`} size="lg"
          onClose={() => setDetail(null)}
          footer={<button type="button" className="button-secondary" onClick={() => setDetail(null)}>{t('common.close')}</button>}>
          <div className="history-detail">
            <dl className="kv-grid">
              <dt>{t('feedback.rating', 'Rating')}</dt><dd>{ratingIcon(detail._fb?.rating)} {detail._fb?.rating}</dd>
              <dt>{t('feedback.subject', 'Subject')}</dt><dd>{detail._fb?.subjectId ? subjectName(detail._fb.subjectId) : '—'}</dd>
              <dt>{t('feedback.user', 'User')}</dt><dd>{detail._fb?.userId ? <CopyableId value={detail._fb.userId} truncateLen={12} /> : '—'}</dd>
              <dt>{t('feedback.created', 'Created')}</dt><dd>{formatDateTime(detail._fb?.createdUtc)}</dd>
            </dl>
            {detail._fb?.comment ? (
              <div className="history-block">
                <span className="history-label">{t('feedback.comment', 'Comment')}</span>
                <div className="history-text">{detail._fb.comment}</div>
              </div>
            ) : null}
            <div className="history-block">
              <span className="history-label">{t('feedback.question', 'Question')}</span>
              <div className="history-text">{detail._turn?.question || '—'}</div>
            </div>
            <div className="history-block">
              <span className="history-label">{t('feedback.answer', 'Answer')}</span>
              <div className="history-text">{detail._turn?.answer || (detail._turn ? '—' : t('feedback.turnPruned', 'The rated turn has been pruned.'))}</div>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

export default FeedbackView;
