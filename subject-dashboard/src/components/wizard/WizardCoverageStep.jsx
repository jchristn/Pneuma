import { useEffect, useRef, useState } from 'react';

// Step 8: ask the accepted questions against the new subject and show which the content answers. Answers the user
// confirms become evaluation facts, a regression baseline from day one. It spends model calls, so it only runs when
// started, with the count shown on the button.
function WizardCoverageStep({ state, update, options, apiClient, goTo, t }) {
  const subject = state.created?.subject;
  const questions = state.draft.questions.filter((q) => (q.question || '').trim());
  const limit = Math.min(questions.length, options?.coverageMaxQuestions || 12);
  const results = state.coverage?.results || [];
  const [running, setRunning] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const stop = useRef(false);

  // Leaving the step (or closing the wizard) stops asking further questions.
  useEffect(() => () => { stop.current = true; }, []);

  const setResults = (fn) => update((s) => ({ ...s, coverage: { results: fn(s.coverage?.results || []) } }));

  const start = async () => {
    stop.current = false;
    setRunning(true);
    setMessage('');
    setError('');
    update((s) => ({ ...s, coverage: { results: [] } }));
    for (let i = 0; i < limit && !stop.current; i += 1) {
      const question = questions[i].question.trim();
      let row;
      try {
        const resp = await apiClient.wizardAsk(subject.id, question);
        const answered = !resp.insufficientSupport && !!(resp.answer || '').trim();
        row = { question, answer: resp.answer || '', sources: (resp.sources || []).length, answered, save: answered };
      } catch (err) {
        row = { question, answer: '', sources: 0, answered: false, error: err?.message || t('wizard.coverage.failed'), save: false };
      }
      setResults((prev) => [...prev, row]);
    }
    setRunning(false);
  };

  const toggle = (i) => setResults((prev) => prev.map((r, j) => (j === i ? { ...r, save: !r.save } : r)));

  const save = async () => {
    const facts = results.filter((r) => r.save && r.answered).map((r) => ({ subjectId: subject.id, question: r.question, expectedAnswer: r.answer, category: 'wizard' }));
    if (facts.length === 0) return;
    setSaving(true);
    setError('');
    try {
      const resp = await apiClient.bulkCreateEvalFacts(facts);
      setMessage(t('wizard.coverage.saved', { count: resp?.created ?? facts.length }));
      setResults((prev) => prev.map((r) => (r.save ? { ...r, saved: true, save: false } : r)));
    } catch (err) {
      setError(err?.message || t('wizard.coverage.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  if (!subject) return null;
  const answered = results.filter((r) => r.answered).length;
  const toSave = results.filter((r) => r.save && r.answered).length;
  return (
    <section className="sw-step">
      <h3>{t('wizard.coverage.title')}</h3>
      <p className="sw-hint">{t('wizard.coverage.intro')}</p>
      <div className="sw-row">
        {!running ? (
          <button type="button" className="sw-btn sw-btn-primary" onClick={start} disabled={limit === 0}>
            {t('wizard.coverage.start', { count: limit })}
          </button>
        ) : (
          <button type="button" className="sw-btn sw-btn-secondary" onClick={() => { stop.current = true; }}>{t('wizard.coverage.stop')}</button>
        )}
        {running && <span className="sw-muted" role="status"><span className="sw-spinner" aria-hidden="true" /> {t('wizard.coverage.progress', { done: results.length, total: limit })}</span>}
        {questions.length > limit && <span className="sw-muted">{t('wizard.coverage.capped', { count: limit, total: questions.length })}</span>}
      </div>
      {message && <div className="sw-alert sw-alert-success" role="status">{message}</div>}
      {error && <div className="sw-alert sw-alert-error" role="alert">{error}</div>}

      {results.length > 0 && (
        <>
          <p className="sw-muted">{t('wizard.coverage.summary', { answered, total: results.length })}</p>
          <ul className="sw-coverage">
            {results.map((r, i) => (
              <li key={i} className={r.answered ? 'answered' : 'unanswered'}>
                <div className="sw-row sw-between">
                  <strong>{r.question}</strong>
                  <span className={`sw-status ${r.answered ? 'sw-status-ingested' : 'sw-status-failed'}`}>
                    {r.error ? t('wizard.coverage.error') : (r.answered ? t('wizard.coverage.answered', { count: r.sources }) : t('wizard.coverage.notAnswered'))}
                  </span>
                </div>
                {r.answer && <details><summary>{t('wizard.coverage.showAnswer')}</summary><p className="sw-answer">{r.answer}</p></details>}
                {r.error && <div className="sw-muted">{r.error}</div>}
                {!r.answered && !r.error && (
                  <button type="button" className="sw-link" onClick={() => goTo('sources')}>{t('wizard.coverage.addContent')}</button>
                )}
                {r.answered && (
                  <label className="sw-check">
                    <input type="checkbox" checked={!!r.save} onChange={() => toggle(i)} disabled={r.saved || saving} />
                    {r.saved ? t('wizard.coverage.isFact') : t('wizard.coverage.keepAsFact')}
                  </label>
                )}
              </li>
            ))}
          </ul>
          <div className="sw-row sw-end">
            <button type="button" className="sw-btn sw-btn-primary" onClick={save} disabled={saving || running || toSave === 0}>
              {t('wizard.coverage.save', { count: toSave })}
            </button>
          </div>
        </>
      )}
    </section>
  );
}

export default WizardCoverageStep;
