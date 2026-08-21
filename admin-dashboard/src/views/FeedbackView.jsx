import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeSanitize from 'rehype-sanitize';
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

// A scrollable, markdown-rendered panel for the request/response text.
function MarkdownPanel({ text, empty }) {
  if (!text) return <div className="feedback-panel feedback-panel-empty">{empty}</div>;
  return (
    <div className="feedback-panel chat-markdown">
      <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]}>{text}</ReactMarkdown>
    </div>
  );
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
        <Modal title={`${ratingIcon(detail._fb?.rating)} ${t('feedback.detailTitle', 'Feedback')}`} size="feedback"
          onClose={() => setDetail(null)}
          footer={<button type="button" className="button-secondary" onClick={() => setDetail(null)}>{t('common.close')}</button>}>
          <div className="feedback-detail">
            <div className="feedback-meta">
              <div className="feedback-meta-item"><span className="feedback-meta-label">{t('feedback.rating', 'Rating')}</span><span className="feedback-meta-value">{ratingIcon(detail._fb?.rating)} {detail._fb?.rating}</span></div>
              <div className="feedback-meta-item"><span className="feedback-meta-label">{t('feedback.subject', 'Subject')}</span><span className="feedback-meta-value">{detail._fb?.subjectId ? subjectName(detail._fb.subjectId) : '—'}</span></div>
              <div className="feedback-meta-item"><span className="feedback-meta-label">{t('feedback.user', 'User')}</span><span className="feedback-meta-value">{detail._fb?.userId ? <CopyableId value={detail._fb.userId} truncateLen={12} /> : '—'}</span></div>
              <div className="feedback-meta-item"><span className="feedback-meta-label">{t('feedback.created', 'Created')}</span><span className="feedback-meta-value">{formatDateTime(detail._fb?.createdUtc)}</span></div>
            </div>
            {detail._fb?.comment ? (
              <div className="feedback-section">
                <span className="feedback-section-label">{t('feedback.comment', 'Comment')}</span>
                <div className="feedback-panel">{detail._fb.comment}</div>
              </div>
            ) : null}
            <div className="feedback-columns">
              <div className="feedback-section">
                <span className="feedback-section-label">{t('feedback.question', 'Question')}</span>
                <MarkdownPanel text={detail._turn?.question} empty="—" />
              </div>
              <div className="feedback-section">
                <span className="feedback-section-label">{t('feedback.answer', 'Answer')}</span>
                <MarkdownPanel text={detail._turn?.answer} empty={detail._turn ? '—' : t('feedback.turnPruned', 'The rated turn has been pruned.')} />
              </div>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
}

export default FeedbackView;
