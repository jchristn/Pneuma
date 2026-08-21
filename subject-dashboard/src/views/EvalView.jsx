import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatDateTime } from '../utils/format';
import PageHeader from '../components/PageHeader';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';

function truncate(s, n) { const v = String(s || ''); return v.length > n ? v.slice(0, n) + '…' : v; }
function verdictClass(v) { return `ev-verdict ev-${String(v || 'unknown').toLowerCase()}`; }

export default function EvalView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [facts, setFacts] = useState([]);
  const [runs, setRuns] = useState([]);
  const [error, setError] = useState('');
  const [running, setRunning] = useState(false);
  const [form, setForm] = useState({ question: '', expectedAnswer: '', category: '' });
  const [detail, setDetail] = useState(null);
  const [confirmRun, setConfirmRun] = useState(null);

  useEffect(() => { apiClient.getSubjects().then((r) => setSubjects(asArray(r, 'objects'))).catch(() => {}); }, [apiClient]);

  const load = useCallback(async () => {
    if (!subjectId) { setFacts([]); setRuns([]); return; }
    setError('');
    try {
      setFacts(asArray(await apiClient.evalListFacts(subjectId), 'objects'));
      setRuns(asArray(await apiClient.evalListRuns(subjectId), 'objects'));
    } catch (err) { setError(err.message); }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);

  const addFact = async (e) => {
    e.preventDefault();
    if (!subjectId || !form.question.trim() || !form.expectedAnswer.trim()) return;
    try {
      await apiClient.evalCreateFact({ subjectId, question: form.question, expectedAnswer: form.expectedAnswer, category: form.category || null });
      setForm({ question: '', expectedAnswer: '', category: '' });
      await load();
    } catch (err) { setError(err.message); }
  };

  const deleteFact = async (id) => { try { await apiClient.evalDeleteFact(id); await load(); } catch (err) { setError(err.message); } };

  const startRun = async () => {
    setRunning(true); setError('');
    try { await apiClient.evalStartRun(subjectId); await load(); }
    catch (err) { setError(err.message); }
    finally { setRunning(false); }
  };

  const openRun = async (run) => {
    try { setDetail(await apiClient.evalGetRun(run.id)); }
    catch (err) { setError(err.message); }
  };

  const deleteRun = async () => {
    if (!confirmRun) return;
    try { await apiClient.evalDeleteRun(confirmRun.id); setConfirmRun(null); await load(); }
    catch (err) { setError(err.message); }
  };

  return (
    <div>
      <PageHeader title={t('eval.title', 'Evaluation')} subtitle={t('eval.subtitle', 'Grade the assistant against ground-truth facts for a subject.')} />
      <div className="filter-bar">
        <div className="pagination-group">
          <label>{t('eval.subject', 'Subject')}:</label>
          <select value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('eval.pickSubject', 'Select a subject…')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.id}</option>)}
          </select>
        </div>
        {subjectId && (
          <button className="btn btn-primary" onClick={startRun} disabled={running || facts.length === 0}
            title={facts.length === 0 ? t('eval.noFacts', 'Add facts first.') : t('eval.runTip', 'Answer every fact through the real pipeline and judge it.')}>
            {running ? t('eval.running', 'Running…') : t('eval.run', 'Run evaluation')}
          </button>
        )}
      </div>

      {error && <div className="error-banner">{error}</div>}

      {subjectId && (
        <>
          <div className="ev-section-title">{t('eval.facts', 'Ground-truth facts')} <span className="hd-count">{facts.length}</span></div>
          <form className="ev-fact-form" onSubmit={addFact}>
            <input value={form.question} onChange={(e) => setForm({ ...form, question: e.target.value })} placeholder={t('eval.question', 'Question')} />
            <input value={form.expectedAnswer} onChange={(e) => setForm({ ...form, expectedAnswer: e.target.value })} placeholder={t('eval.expected', 'Expected answer')} />
            <input value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} placeholder={t('eval.category', 'Category (optional)')} className="ev-cat" />
            <button className="btn btn-secondary" type="submit">{t('eval.addFact', 'Add')}</button>
          </form>
          <div className="hd-table-wrap">
            <table className="hd-table">
              <thead><tr><th>{t('eval.question', 'Question')}</th><th>{t('eval.expected', 'Expected')}</th><th>{t('eval.category', 'Category')}</th><th /></tr></thead>
              <tbody>
                {facts.map((f) => (
                  <tr key={f.id}>
                    <td>{truncate(f.question, 80)}</td>
                    <td>{truncate(f.expectedAnswer, 80)}</td>
                    <td className="hd-muted">{f.category || '—'}</td>
                    <td><button className="btn btn-link ev-del" onClick={() => deleteFact(f.id)}>{t('common.delete', 'Delete')}</button></td>
                  </tr>
                ))}
                {facts.length === 0 && <tr><td colSpan={4} className="hd-muted">{t('eval.noFactsYet', 'No facts yet — add one above.')}</td></tr>}
              </tbody>
            </table>
          </div>

          <div className="ev-section-title" style={{ marginTop: 20 }}>{t('eval.runs', 'Runs')} <span className="hd-count">{runs.length}</span></div>
          <div className="hd-table-wrap">
            <table className="hd-table">
              <thead><tr><th>{t('eval.started', 'Started')}</th><th>{t('common.status', 'Status')}</th><th>{t('eval.score', 'Pass / Partial / Fail')}</th><th /></tr></thead>
              <tbody>
                {runs.map((r) => (
                  <tr key={r.id}>
                    <td>{formatDateTime(r.createdUtc)}</td>
                    <td><span className={`ev-status ev-${String(r.status || '').toLowerCase()}`}>{r.status}</span></td>
                    <td>{r.passCount} / {r.partialCount} / {r.failCount} <span className="hd-muted">of {r.totalFacts}</span></td>
                    <td>
                      <button className="btn btn-link" onClick={() => openRun(r)}>{t('common.view', 'View')}</button>
                      <button className="btn btn-link ev-del" onClick={() => setConfirmRun(r)}>{t('common.delete', 'Delete')}</button>
                    </td>
                  </tr>
                ))}
                {runs.length === 0 && <tr><td colSpan={4} className="hd-muted">{t('eval.noRuns', 'No runs yet.')}</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}

      <Modal isOpen={!!detail} onClose={() => setDetail(null)} title={t('eval.runResults', 'Run results')} size="fullscreen">
        {detail && (
          <div>
            <div className="ev-run-summary">
              <span className={`ev-status ev-${String(detail.run?.status || '').toLowerCase()}`}>{detail.run?.status}</span>
              <span>{detail.run?.passCount} pass · {detail.run?.partialCount} partial · {detail.run?.failCount} fail</span>
              {detail.run?.error && <span className="ev-err">{detail.run.error}</span>}
            </div>
            <div className="hd-table-wrap">
              <table className="hd-table">
                <thead><tr><th>{t('eval.question', 'Question')}</th><th>{t('eval.verdict', 'Verdict')}</th><th>{t('eval.score', 'Score')}</th><th>{t('eval.produced', 'Produced')}</th><th>{t('eval.reason', 'Reason')}</th></tr></thead>
                <tbody>
                  {(detail.results || []).map((res) => (
                    <tr key={res.id}>
                      <td>{truncate(res.question, 60)}</td>
                      <td><span className={verdictClass(res.verdict)}>{res.verdict}</span></td>
                      <td>{typeof res.score === 'number' ? res.score.toFixed(1) : '—'}</td>
                      <td>{truncate(res.producedAnswer, 90)}</td>
                      <td className="hd-muted">{truncate(res.reason, 90)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Modal>

      <ConfirmModal isOpen={!!confirmRun} onClose={() => setConfirmRun(null)} onConfirm={deleteRun}
        title={t('eval.deleteRun', 'Delete run')} message={t('eval.deleteRunMsg', 'Delete this run and its results?')}
        confirmLabel={t('common.delete', 'Delete')} />
    </div>
  );
}
