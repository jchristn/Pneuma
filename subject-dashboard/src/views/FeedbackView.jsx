import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import { formatDateTime } from '../utils/format';

function truncate(s, n) { const v = String(s || ''); return v.length > n ? v.slice(0, n) + '…' : v; }
function listOf(resp) { return resp?.objects || resp?.items || []; }
function ratingIcon(r) { return r === 'Up' ? '👍' : r === 'Down' ? '👎' : '💬'; }

export default function FeedbackView() {
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
    try { setRows(listOf(await apiClient.listFeedback(subjectId || null))); }
    catch (err) { setError(err.message); setRows([]); }
    finally { setLoading(false); }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { apiClient.getSubjects().then((r) => setSubjects(listOf(r))).catch(() => {}); }, [apiClient]);

  const subjectName = (id) => subjects.find((s) => s.id === id)?.displayName || '—';
  const flat = rows.map((r) => ({ ...r, id: r.feedback?.id, _fb: r.feedback, _turn: r.turn }));

  const columns = [
    { key: 'rating', label: t('feedback.rating', 'Rating'), sortable: false, render: (v, r) => ratingIcon(r._fb?.rating) },
    { key: 'comment', label: t('feedback.comment', 'Comment'), sortable: false, render: (v, r) => r._fb?.comment ? truncate(r._fb.comment, 80) : '—' },
    { key: 'question', label: t('feedback.question', 'Question'), sortable: false, render: (v, r) => truncate(r._turn?.question, 70) },
    { key: 'subjectId', label: t('feedback.subject', 'Subject'), render: (v, r) => r._fb?.subjectId ? subjectName(r._fb.subjectId) : '—' },
    { key: 'createdUtc', label: t('feedback.created', 'Created'), render: (v, r) => formatDateTime(r._fb?.createdUtc) }
  ];

  return (
    <div>
      <PageHeader title={t('feedback.title', 'Feedback')} subtitle={t('feedback.subtitle', 'Thumbs up/down and comments left on chat answers.')} />
      {error && <div className="error-banner">{error}</div>}
      <DataTable columns={columns} data={flat} loading={loading} onRefresh={load} onRowClick={(r) => setDetail(r)}
        toolbar={(
          <div className="pagination-group">
            <label>{t('feedback.subject', 'Subject')}:</label>
            <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('feedback.allSubjects', 'All subjects')}</option>
              {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
            </select>
          </div>
        )}
        emptyTitle={t('feedback.title', 'Feedback')} emptyDescription={t('feedback.empty', 'No feedback yet.')} />

      <Modal isOpen={!!detail} onClose={() => setDetail(null)} title={t('feedback.detailTitle', 'Feedback')} size="large">
        {detail && (
          <div className="history-detail">
            <div className="detail-grid">
              <div className="detail-item"><span className="detail-label">{t('feedback.rating', 'Rating')}</span><span className="detail-value">{ratingIcon(detail._fb?.rating)} {detail._fb?.rating}</span></div>
              <div className="detail-item"><span className="detail-label">{t('feedback.subject', 'Subject')}</span><span className="detail-value">{detail._fb?.subjectId ? subjectName(detail._fb.subjectId) : '—'}</span></div>
              <div className="detail-item"><span className="detail-label">{t('feedback.created', 'Created')}</span><span className="detail-value">{formatDateTime(detail._fb?.createdUtc)}</span></div>
            </div>
            {detail._fb?.comment ? <div className="history-block"><span className="history-label">{t('feedback.comment', 'Comment')}</span><div className="history-text">{detail._fb.comment}</div></div> : null}
            <div className="history-block"><span className="history-label">{t('feedback.question', 'Question')}</span><div className="history-text">{detail._turn?.question || '—'}</div></div>
            <div className="history-block"><span className="history-label">{t('feedback.answer', 'Answer')}</span><div className="history-text">{detail._turn?.answer || (detail._turn ? '—' : t('feedback.turnPruned', 'The rated turn has been pruned.'))}</div></div>
          </div>
        )}
      </Modal>
    </div>
  );
}
