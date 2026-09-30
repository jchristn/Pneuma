import { useState } from 'react';
import { KINDS, MIN_QUESTIONS } from './wizardDraft';
import { GuidanceBar, LockButton } from './WizardParts';

// Step 3: the example questions, the spine of the wizard. Editing a question makes it the user's (kept on
// regeneration); locking keeps a model question as it is.
function WizardQuestionsStep({ state, updateDraft, generate, busy, options, t, completionRunners, stepModelFor, setStepModel, defaultModelLabel }) {
  const questions = state.draft.questions;
  const [adding, setAdding] = useState('');
  const max = options?.maxQuestions || 40;

  const setQuestions = (next) => updateDraft({ questions: next });
  const change = (i, patch) => setQuestions(questions.map((q, j) => (j === i ? { ...q, ...patch } : q)));
  const move = (i, delta) => {
    const j = i + delta;
    if (j < 0 || j >= questions.length) return;
    const next = [...questions];
    [next[i], next[j]] = [next[j], next[i]];
    setQuestions(next);
  };
  const add = () => {
    const text = adding.trim();
    if (!text || questions.length >= max) return;
    setQuestions([...questions, { question: text, kind: 'Fact', origin: 'User', locked: false }]);
    setAdding('');
  };
  const filled = questions.filter((q) => (q.question || '').trim()).length;

  return (
    <section className="sw-step">
      <h3>{t('wizard.questions.title')}</h3>
      <p className="sw-hint">{t('wizard.questions.intro')}</p>
      {filled < MIN_QUESTIONS && questions.length > 0 && (
        <div className="sw-alert sw-alert-info">{t('wizard.questions.needMore', { count: MIN_QUESTIONS })}</div>
      )}
      <ol className="sw-list">
        {questions.map((q, i) => (
          <li key={i} className={`sw-list-row${q.locked ? ' locked' : ''}`}>
            <span className="sw-num">{i + 1}</span>
            <select value={q.kind} onChange={(e) => change(i, { kind: e.target.value, origin: 'User' })} disabled={!!busy}
              aria-label={t('wizard.questions.kind')} title={t('wizard.questions.kindTip')}>
              {KINDS.map((k) => <option key={k} value={k}>{t(`wizard.kind.${k}`)}</option>)}
            </select>
            <input type="text" value={q.question} maxLength={500} aria-label={t('wizard.questions.question', { n: i + 1 })}
              onChange={(e) => change(i, { question: e.target.value, origin: 'User' })} disabled={!!busy} />
            {q.origin === 'User' && <span className="sw-tag" title={t('wizard.questions.yoursTip')}>{t('wizard.questions.yours')}</span>}
            <LockButton t={t} locked={!!q.locked} onToggle={() => change(i, { locked: !q.locked })} disabled={!!busy} />
            <button type="button" className="sw-icon-btn" onClick={() => move(i, -1)} disabled={!!busy || i === 0} aria-label={t('wizard.moveUp')}>↑</button>
            <button type="button" className="sw-icon-btn" onClick={() => move(i, 1)} disabled={!!busy || i === questions.length - 1} aria-label={t('wizard.moveDown')}>↓</button>
            <button type="button" className="sw-icon-btn" onClick={() => setQuestions(questions.filter((_, j) => j !== i))} disabled={!!busy} aria-label={t('wizard.remove')}>✕</button>
          </li>
        ))}
      </ol>
      <div className="sw-row">
        <input type="text" value={adding} maxLength={500} placeholder={t('wizard.questions.addPlaceholder')}
          onChange={(e) => setAdding(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(); } }}
          disabled={!!busy || questions.length >= max} aria-label={t('wizard.questions.add')} />
        <button type="button" className="sw-btn sw-btn-secondary" onClick={add} disabled={!!busy || !adding.trim() || questions.length >= max}>{t('wizard.questions.add')}</button>
      </div>
      <GuidanceBar t={t} busy={busy} models={completionRunners} model={stepModelFor('questions')} onModelChange={(id) => setStepModel('questions', id)} defaultModelLabel={defaultModelLabel}
        regenerateLabel={questions.length === 0 ? t('wizard.questions.draft') : t('wizard.questions.regenerate')}
        onRegenerate={(guidance) => generate('questions', { guidance, mode: 'replace', count: Math.max(questions.length, options?.defaultQuestionCount || 12) })}
        extraAction={{ label: t('wizard.questions.more'), tip: t('wizard.questions.moreTip'), onClick: (guidance) => generate('questions', { guidance, mode: 'more' }) }}
        placeholder={t('wizard.questions.guidancePlaceholder')} />
    </section>
  );
}

export default WizardQuestionsStep;
