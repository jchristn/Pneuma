import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import { formatDateTime } from '../i18n/formatters';

function truncate(s, n) { const v = String(s || ''); return v.length > n ? v.slice(0, n) + '…' : v; }
function verdictClass(v) { return `ev-verdict ev-${String(v || 'unknown').toLowerCase()}`; }
function isActive(status) { const s = String(status || '').toLowerCase(); return s === 'pending' || s === 'running'; }
function passRate(pass, total) { return total > 0 ? Math.round((pass / total) * 100) : 0; }

/**
 * Live progress modal for an async eval run. Opens an SSE stream on mount, renders a progress bar,
 * running pass/partial/fail tallies, and a scrolling list of per-fact verdicts as they arrive. The
 * AbortController is torn down on unmount/close so the underlying fetch stream is cancelled.
 */
function EvalProgressModal({ run, onClose, onFinished, onViewResults }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [status, setStatus] = useState(run.status || 'Pending');
  const [total, setTotal] = useState(run.totalFacts || 0);
  const [completed, setCompleted] = useState(0);
  const [tally, setTally] = useState({ pass: 0, partial: 0, fail: 0 });
  const [results, setResults] = useState([]);
  const [finalRun, setFinalRun] = useState(null);
  const [streamError, setStreamError] = useState('');
  const [cancelling, setCancelling] = useState(false);
  const abortRef = useRef(null);
  const listRef = useRef(null);

  useEffect(() => {
    const controller = new AbortController();
    abortRef.current = controller;
    let done = false;
    apiClient.evalRunStream(run.id, {
      signal: controller.signal,
      onEvent: (ev) => {
        if (!ev || typeof ev !== 'object') return;
        switch (ev.type) {
          case 'metadata':
            if (typeof ev.total === 'number') setTotal(ev.total);
            if (ev.status) setStatus(ev.status);
            break;
          case 'result':
            setResults((prev) => [...prev, ev]);
            break;
          case 'progress':
            if (typeof ev.completed === 'number') setCompleted(ev.completed);
            if (typeof ev.total === 'number') setTotal(ev.total);
            setTally({ pass: ev.pass || 0, partial: ev.partial || 0, fail: ev.fail || 0 });
            if (ev.status) setStatus(ev.status);
            break;
          case 'complete':
            done = true;
            if (ev.run) {
              setFinalRun(ev.run);
              setStatus(ev.run.status || 'Completed');
              setTotal(ev.run.totalFacts || 0);
              setTally({ pass: ev.run.passCount || 0, partial: ev.run.partialCount || 0, fail: ev.run.failCount || 0 });
              setCompleted((ev.run.passCount || 0) + (ev.run.partialCount || 0) + (ev.run.failCount || 0));
              onFinished?.(ev.run);
            }
            break;
          case 'error':
            setStreamError(ev.message || 'Evaluation stream error');
            break;
          default:
            break;
        }
      },
    }).catch((err) => {
      if (controller.signal.aborted || done) return;
      setStreamError(err?.message || 'Evaluation stream failed');
    });
    return () => { controller.abort(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [run.id]);

  useEffect(() => {
    if (listRef.current) listRef.current.scrollTop = listRef.current.scrollHeight;
  }, [results.length]);

  const cancelRun = async () => {
    setCancelling(true);
    try {
      const updated = await apiClient.evalCancelRun(run.id);
      abortRef.current?.abort();
      setStatus(updated?.status || 'Cancelled');
      setFinalRun((prev) => prev || updated || null);
      onFinished?.(updated || run);
    } catch (err) {
      setStreamError(err?.message || 'Cancel failed');
    } finally {
      setCancelling(false);
    }
  };

  const pct = total > 0 ? Math.round((completed / total) * 100) : 0;
  const terminal = !!finalRun || ['completed', 'failed', 'cancelled'].includes(String(status).toLowerCase());

  const footer = (
    <>
      {!terminal && (
        <button type="button" className="button-danger" onClick={cancelRun} disabled={cancelling}>
          {cancelling ? t('common.loading', 'Working…') : t('eval.cancelRun', 'Cancel run')}
        </button>
      )}
      {terminal && finalRun && (
        <button type="button" className="button-primary" onClick={() => onViewResults(finalRun)}>
          {t('eval.viewResults', 'View results')}
        </button>
      )}
      <button type="button" className="button-secondary" onClick={onClose}>{t('common.close', 'Close')}</button>
    </>
  );

  return (
    <Modal title={t('eval.runProgress', 'Evaluation progress')}
      headerExtra={<StatusPill label={status} tone={toneForStatus(status)} />}
      size="wide" onClose={onClose} footer={footer}>
      <div className="ev-prog">
        <div className="ev-prog-head">
          <span>{t('eval.completedOf', '{{completed}} of {{total}} facts', { completed, total })}</span>
          <span>{pct}%</span>
        </div>
        <div className="ev-prog-bar-track"><div className="ev-prog-bar-fill" style={{ width: `${pct}%` }} /></div>
        <div className="ev-chips">
          <span className="ev-chip ev-chip-pass">{t('eval.pass', 'Pass')} {tally.pass}</span>
          <span className="ev-chip ev-chip-partial">{t('eval.partial', 'Partial')} {tally.partial}</span>
          <span className="ev-chip ev-chip-fail">{t('eval.fail', 'Fail')} {tally.fail}</span>
        </div>

        {streamError && <div className="error-message">{streamError}</div>}

        {terminal && (
          <div className="ev-run-summary">
            <span className="ev-passrate">{passRate(tally.pass, total)}%</span>
            <span className="hd-muted">{t('eval.passRate', 'pass rate')}</span>
          </div>
        )}

        <div className="ev-prog-list" ref={listRef}>
          {results.length === 0 && <div className="ev-prog-item hd-muted">{t('eval.waitingResults', 'Waiting for results…')}</div>}
          {results.map((r, i) => (
            <div className="ev-prog-item" key={r.factId || i}>
              <span className="ev-prog-item-q" title={r.question}>{truncate(r.question, 90)}</span>
              <span className={verdictClass(r.verdict)}>{r.verdict}</span>
              <span className="ev-prog-score">{typeof r.score === 'number' ? r.score.toFixed(1) : '—'}</span>
            </div>
          ))}
        </div>
      </div>
    </Modal>
  );
}

/** Run-detail modal with client-side Category / Verdict / Failure-mode filters. */
function EvalDetailModal({ detail, onClose }) {
  const { t } = useTranslation();
  const [filters, setFilters] = useState({ category: '', verdict: '', failureMode: '' });
  const results = useMemo(() => detail.results || [], [detail]);

  const categories = useMemo(() => Array.from(new Set(results.map((r) => r.category).filter(Boolean))), [results]);
  const failureModes = useMemo(() => Array.from(new Set(results.map((r) => r.failureMode).filter(Boolean))), [results]);

  const filtered = useMemo(() => results.filter((r) => {
    if (filters.category && r.category !== filters.category) return false;
    if (filters.verdict && String(r.verdict || '').toLowerCase() !== filters.verdict.toLowerCase()) return false;
    if (filters.failureMode && r.failureMode !== filters.failureMode) return false;
    return true;
  }), [results, filters]);

  const run = detail.run || {};
  const rate = passRate(run.passCount || 0, run.totalFacts || 0);

  return (
    <Modal title={t('eval.runResults', 'Run results')} size="wide" onClose={onClose}
      headerExtra={<StatusPill label={run.status} tone={toneForStatus(run.status)} />}
      footer={<button type="button" className="button-secondary" onClick={onClose}>{t('common.close', 'Close')}</button>}>
      <div className="ev-run-summary">
        <span className="ev-passrate">{rate}%</span>
        <span className="hd-muted">{t('eval.passRate', 'pass rate')}</span>
        <span>{run.passCount} {t('eval.pass', 'pass')} · {run.partialCount} {t('eval.partial', 'partial')} · {run.failCount} {t('eval.fail', 'fail')} <span className="hd-muted">{t('eval.ofN', 'of {{n}}', { n: run.totalFacts })}</span></span>
        {run.judgeModel && <span className="hd-muted">{t('eval.judge', 'Judge')}: {run.judgeModel}</span>}
        {run.error && <span className="ev-err">{run.error}</span>}
      </div>

      <div className="ev-detail-filters">
        <select value={filters.category} onChange={(e) => setFilters({ ...filters, category: e.target.value })}>
          <option value="">{t('eval.allCategories', 'All categories')}</option>
          {categories.map((c) => <option key={c} value={c}>{c}</option>)}
        </select>
        <select value={filters.verdict} onChange={(e) => setFilters({ ...filters, verdict: e.target.value })}>
          <option value="">{t('eval.allVerdicts', 'All verdicts')}</option>
          <option value="Pass">{t('eval.pass', 'Pass')}</option>
          <option value="Partial">{t('eval.partial', 'Partial')}</option>
          <option value="Fail">{t('eval.fail', 'Fail')}</option>
          <option value="Unknown">{t('eval.unknown', 'Unknown')}</option>
        </select>
        <select value={filters.failureMode} onChange={(e) => setFilters({ ...filters, failureMode: e.target.value })}>
          <option value="">{t('eval.allFailureModes', 'All failure modes')}</option>
          {failureModes.map((f) => <option key={f} value={f}>{f}</option>)}
        </select>
        <span className="hd-muted" style={{ alignSelf: 'center' }}>{t('eval.showingN', '{{shown}} of {{total}}', { shown: filtered.length, total: results.length })}</span>
      </div>

      {filtered.map((res) => (
        <div className="ev-result-card" key={res.id}>
          <div className="ev-result-head">
            <span className="ev-result-q">{res.question}</span>
            <span className={verdictClass(res.verdict)}>{res.verdict}</span>
            <span className="ev-prog-score">{typeof res.score === 'number' ? res.score.toFixed(1) : '—'}</span>
          </div>
          <div className="ev-result-ans">
            <div className="ev-ans-block">
              <div className="ev-ans-label">{t('eval.produced', 'Produced')}</div>
              {res.producedAnswer || '—'}
            </div>
            <div className="ev-ans-block">
              <div className="ev-ans-label">{t('eval.expected', 'Expected')}</div>
              {res.expectedAnswer || '—'}
            </div>
          </div>
          <div className="ev-result-meta">
            {res.category && <span>{t('eval.category', 'Category')}: {res.category}</span>}
            {res.failureMode && <span>{t('eval.failureMode', 'Failure mode')}: {res.failureMode}</span>}
            {res.reason && <span className="ev-result-reason">{res.reason}</span>}
          </div>
        </div>
      ))}
      {filtered.length === 0 && <div className="hd-muted">{t('eval.noResults', 'No results match the filters.')}</div>}
    </Modal>
  );
}

function EvalView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjects, setSubjects] = useState([]);
  const [subjectId, setSubjectId] = useState('');
  const [facts, setFacts] = useState([]);
  const [runs, setRuns] = useState([]);
  const [error, setError] = useState('');
  const [starting, setStarting] = useState(false);
  const [form, setForm] = useState({ question: '', expectedAnswer: '', category: '' });
  const [detail, setDetail] = useState(null);
  const [confirmRun, setConfirmRun] = useState(null);
  const [progressRun, setProgressRun] = useState(null);
  const [bulkDeleteOpen, setBulkDeleteOpen] = useState(false);

  useEffect(() => { apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {}); }, [apiClient]);

  const load = useCallback(async () => {
    if (!subjectId) { setFacts([]); setRuns([]); return; }
    setError('');
    try {
      setFacts(normalizeList(await apiClient.evalListFacts(subjectId)).items);
      setRuns(normalizeList(await apiClient.evalListRuns(subjectId)).items);
    } catch (err) { setError(err?.message || 'Failed to load'); }
  }, [apiClient, subjectId]);

  useEffect(() => { load(); }, [load]);

  const { selectedItems, clear, selection } = useTableSelection(runs);

  const addFact = async (e) => {
    e.preventDefault();
    if (!subjectId || !form.question.trim() || !form.expectedAnswer.trim()) return;
    try {
      await apiClient.evalCreateFact({ subjectId, question: form.question, expectedAnswer: form.expectedAnswer, category: form.category || null });
      setForm({ question: '', expectedAnswer: '', category: '' });
      await load();
    } catch (err) { setError(err?.message || 'Failed'); }
  };

  const deleteFact = async (id) => { try { await apiClient.evalDeleteFact(id); await load(); } catch (err) { setError(err?.message || 'Failed'); } };

  const startRun = async () => {
    setStarting(true); setError('');
    try {
      const run = await apiClient.evalStartRun(subjectId);
      if (run && run.id) { setProgressRun(run); await load(); }
    }
    catch (err) { setError(err?.message || 'Failed'); }
    finally { setStarting(false); }
  };

  const openRun = async (run) => { try { setDetail(await apiClient.evalGetRun(run.id)); } catch (err) { setError(err?.message || 'Failed'); } };

  const cancelRun = async (run) => { try { await apiClient.evalCancelRun(run.id); await load(); } catch (err) { setError(err?.message || 'Failed'); } };

  const deleteRun = async () => {
    if (!confirmRun) return;
    try { await apiClient.evalDeleteRun(confirmRun.id); setConfirmRun(null); await load(); }
    catch (err) { setError(err?.message || 'Failed'); }
  };

  const bulkDelete = async () => {
    try {
      for (const r of selectedItems) { await apiClient.evalDeleteRun(r.id); }
      setBulkDeleteOpen(false); clear(); await load();
    } catch (err) { setError(err?.message || 'Failed'); }
  };

  const runColumns = [
    { key: 'createdUtc', label: t('eval.started', 'Started'), render: (r) => formatDateTime(r.createdUtc) },
    { key: 'status', label: t('common.status', 'Status'), render: (r) => <StatusPill label={r.status} tone={toneForStatus(r.status)} /> },
    { key: 'score', label: t('eval.score', 'Pass / Partial / Fail'), sortable: false,
      render: (r) => <span>{r.passCount} / {r.partialCount} / {r.failCount} <span className="hd-muted">{t('eval.ofN', 'of {{n}}', { n: r.totalFacts })}</span></span> },
    { key: '_actions', label: '', sortable: false, render: (r) => (
      <ActionMenu items={[
        { key: 'view', label: t('common.view', 'View'), onClick: () => openRun(r) },
        { key: 'cancel', label: t('eval.cancelRun', 'Cancel run'), hidden: !isActive(r.status), onClick: () => cancelRun(r) },
        { key: 'delete', label: t('common.delete', 'Delete'), danger: true, onClick: () => setConfirmRun(r) },
      ]} />
    ) },
  ];

  const bulkBar = (
    <BulkActionBar count={selectedItems.length} onClear={clear}
      actions={[{ key: 'delete', label: t('common.delete', 'Delete'), danger: true, onClick: () => setBulkDeleteOpen(true) }]} />
  );

  return (
    <div>
      <PageHeader title={t('eval.title', 'Evaluation')} subtitle={t('eval.subtitle', 'Grade the assistant against ground-truth facts for a subject.')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="ev-subject">{t('eval.subject', 'Subject')}</label>
          <select id="ev-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}>
            <option value="">{t('eval.pickSubject', 'Select a subject…')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
        {subjectId && (
          <button className="button-primary" onClick={startRun} disabled={starting || facts.length === 0}
            title={facts.length === 0 ? t('eval.noFacts', 'Add facts first.') : t('eval.runTip', 'Answer every fact through the real pipeline and judge it.')}>
            {starting ? t('eval.starting', 'Starting…') : t('eval.run', 'Run evaluation')}
          </button>
        )}
      </div>

      {error && <div className="an-empty" style={{ color: 'var(--color-danger)' }}>{error}</div>}

      {subjectId && (
        <>
          <div className="ev-section-title">{t('eval.facts', 'Ground-truth facts')} <span className="hd-count">{facts.length}</span></div>
          <form className="ev-fact-form" onSubmit={addFact}>
            <input value={form.question} onChange={(e) => setForm({ ...form, question: e.target.value })} placeholder={t('eval.question', 'Question')} />
            <input value={form.expectedAnswer} onChange={(e) => setForm({ ...form, expectedAnswer: e.target.value })} placeholder={t('eval.expected', 'Expected answer')} />
            <input value={form.category} onChange={(e) => setForm({ ...form, category: e.target.value })} placeholder={t('eval.category', 'Category (optional)')} className="ev-cat" />
            <button className="button-secondary" type="submit">{t('eval.addFact', 'Add')}</button>
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
                    <td><button className="button-link ev-del" onClick={() => deleteFact(f.id)}>{t('common.delete', 'Delete')}</button></td>
                  </tr>
                ))}
                {facts.length === 0 && <tr><td colSpan={4} className="hd-muted">{t('eval.noFactsYet', 'No facts yet — add one above.')}</td></tr>}
              </tbody>
            </table>
          </div>

          <div className="ev-section-title" style={{ marginTop: 20 }}>{t('eval.runs', 'Runs')} <span className="hd-count">{runs.length}</span></div>
          <DataTable columns={runColumns} data={runs} onRefresh={load} selection={selection} bulkBar={bulkBar}
            emptyMessage={t('eval.noRuns', 'No runs yet.')} />
        </>
      )}

      {progressRun && (
        <EvalProgressModal run={progressRun}
          onClose={() => { setProgressRun(null); load(); }}
          onFinished={() => load()}
          onViewResults={(finalRun) => { setProgressRun(null); openRun(finalRun); load(); }} />
      )}

      {detail && <EvalDetailModal detail={detail} onClose={() => setDetail(null)} />}

      {confirmRun && (
        <ConfirmModal title={t('eval.deleteRun', 'Delete run')} message={t('eval.deleteRunMsg', 'Delete this run and its results?')} danger
          confirmLabel={t('common.delete', 'Delete')} onConfirm={deleteRun} onClose={() => setConfirmRun(null)} />
      )}

      {bulkDeleteOpen && (
        <ConfirmModal title={t('eval.deleteRun', 'Delete run')}
          message={t('eval.bulkDeleteMsg', 'Delete {{count}} selected run(s) and their results?', { count: selectedItems.length })}
          danger confirmLabel={t('common.delete', 'Delete')} onConfirm={bulkDelete} onClose={() => setBulkDeleteOpen(false)} />
      )}
    </div>
  );
}

export default EvalView;
