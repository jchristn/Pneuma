import { useState, useEffect, useRef, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { STEPS, MIN_QUESTIONS, loadState, saveState, clearState, emptyState, listOf, questionsKey } from './wizardDraft';
import WizardDescribeStep from './WizardDescribeStep';
import WizardBriefStep from './WizardBriefStep';
import WizardQuestionsStep from './WizardQuestionsStep';
import WizardOntologyStep from './WizardOntologyStep';
import WizardPromptsStep from './WizardPromptsStep';
import WizardReviewStep from './WizardReviewStep';
import WizardSourcesStep from './WizardSourcesStep';
import WizardCoverageStep from './WizardCoverageStep';
import './SubjectWizard.css';

const DRAFT_STEPS = STEPS.slice(0, STEPS.indexOf('review') + 1);

// The new subject wizard: the user describes the subject in a sentence or two, a completion model drafts the brief,
// example questions, ontology, and prompts, and the user edits, locks, or regenerates each before creating the subject,
// adding content, and checking that the content answers the questions.
function SubjectWizard({ onClose, onCreated }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [state, setState] = useState(loadState);
  const [options, setOptions] = useState(null);
  const [runners, setRunners] = useState([]);
  const [collections, setCollections] = useState([]);
  const [busy, setBusy] = useState(null);
  const [error, setError] = useState('');
  const [warnings, setWarnings] = useState([]);
  const [lastModel, setLastModel] = useState(null);
  const [confirmReset, setConfirmReset] = useState(false);
  const stateRef = useRef(state);
  const requestSeq = useRef(0);
  const autoTried = useRef(new Set());

  stateRef.current = state;
  useEffect(() => { saveState(state); }, [state]);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([apiClient.wizardOptions(), apiClient.wizardModelRunners(), apiClient.wizardCollections()]).then(([o, r, c]) => {
      if (cancelled) return;
      if (o.status === 'fulfilled') setOptions(o.value);
      else setError(o.reason?.message || t('wizard.optionsFailed'));
      if (r.status === 'fulfilled') setRunners(listOf(r.value));
      if (c.status === 'fulfilled') setCollections(listOf(c.value));
    });
    return () => { cancelled = true; };
  }, [apiClient, t]);

  const update = useCallback((fn) => setState((s) => fn(s)), []);
  const updateDraft = useCallback((patch) => setState((s) => ({ ...s, draft: { ...s.draft, ...patch } })), []);

  const apply = useCallback((step, result) => {
    const value = result.value;
    setState((s) => {
      const draft = { ...s.draft };
      const next = { ...s, draft };
      if (step === 'brief') {
        draft.brief = value;
        if (result.groundingExcerpt) {
          draft.groundingText = [draft.groundingText, result.groundingExcerpt].filter((x) => x && x.trim()).join('\n\n');
          draft.groundingUrl = '';
        }
      } else if (step === 'questions') draft.questions = value || [];
      else if (step === 'ontology') {
        draft.ontology = value;
        next.ontologyQuestionsKey = questionsKey(draft.questions);
      } else if (step === 'prompts') draft.prompts = value;
      else if (step === 'sources') next.sources = value || [];
      return next;
    });
  }, []);

  const generate = useCallback(async (step, { guidance, mode, count } = {}) => {
    const seq = requestSeq.current + 1;
    requestSeq.current = seq;
    setBusy(step);
    setError('');
    setWarnings([]);
    try {
      const current = stateRef.current;
      const body = {
        draft: current.draft,
        modelRunnerId: current.settings.modelRunnerId || null,
        guidance: guidance || null,
        mode: mode || null,
        count: count || 0
      };
      const result = await apiClient.wizardGenerate(step, body);
      if (seq !== requestSeq.current) return;
      apply(step, result);
      setWarnings(result.warnings || []);
      setLastModel({ model: result.model, elapsedMs: result.elapsedMs });
    } catch (err) {
      if (seq === requestSeq.current) setError(err?.message || t('wizard.generateFailed'));
    } finally {
      if (seq === requestSeq.current) setBusy(null);
    }
  }, [apiClient, apply, t]);

  const cancel = () => {
    requestSeq.current += 1;
    setBusy(null);
  };

  // Draft a step automatically the first time it is opened with nothing in it.
  useEffect(() => {
    const { step, draft, created, sources } = state;
    if (busy || autoTried.current.has(step)) return;
    const empty = (step === 'brief' && !draft.brief)
      || (step === 'questions' && draft.questions.length === 0)
      || (step === 'ontology' && !draft.ontology)
      || (step === 'prompts' && !draft.prompts)
      || (step === 'sources' && created && sources === null);
    if (!empty) return;
    autoTried.current.add(step);
    generate(step);
  }, [state, busy, generate]);

  const index = STEPS.indexOf(state.step);
  const created = state.created;
  const goTo = (step) => {
    setError('');
    setWarnings([]);
    update((s) => ({ ...s, step, maxVisited: Math.max(s.maxVisited || 0, STEPS.indexOf(step)) }));
  };
  const reachable = (i) => {
    if (created) return i >= STEPS.indexOf('sources');
    return i <= STEPS.indexOf('review') && i <= Math.max(state.maxVisited || 0, index);
  };

  const canContinue = () => {
    const d = state.draft;
    switch (state.step) {
      case 'describe': return !!d.description.trim() && options?.hasCompletionModel !== false;
      case 'brief': return !!(d.brief && (d.brief.displayName || '').trim());
      case 'questions': return d.questions.filter((q) => (q.question || '').trim()).length >= MIN_QUESTIONS;
      case 'ontology': return (d.ontology?.nodeTypes || []).length > 0;
      default: return true;
    }
  };

  const commit = async () => {
    setBusy('commit');
    setError('');
    try {
      const s = stateRef.current;
      const result = await apiClient.wizardCommit({
        draft: s.draft,
        inferenceModel: s.settings.modelRunnerId || null,
        embeddingModel: s.settings.embeddingModel || null,
        collection: s.settings.collection || null,
        ontologyMode: s.settings.ontologyMode || options?.defaultOntologyMode || 'Prompt'
      });
      update((prev) => ({ ...prev, created: result, step: 'sources', maxVisited: STEPS.indexOf('sources') }));
      setWarnings(result.warnings || []);
      if (onCreated) onCreated(result.subject);
    } catch (err) {
      setError(err?.message || t('wizard.commitFailed'));
    } finally {
      setBusy(null);
    }
  };

  const close = () => {
    cancel();
    if (stateRef.current.created) clearState();
    onClose();
  };

  const startOver = () => {
    cancel();
    clearState();
    autoTried.current = new Set();
    setState(emptyState());
    setConfirmReset(false);
    setError('');
    setWarnings([]);
  };

  const stepProps = { state, update, updateDraft, generate, busy, options, runners, collections, apiClient, t, goTo };
  const renderStep = () => {
    switch (state.step) {
      case 'brief': return <WizardBriefStep {...stepProps} />;
      case 'questions': return <WizardQuestionsStep {...stepProps} />;
      case 'ontology': return <WizardOntologyStep {...stepProps} />;
      case 'prompts': return <WizardPromptsStep {...stepProps} />;
      case 'review': return <WizardReviewStep {...stepProps} />;
      case 'sources': return <WizardSourcesStep {...stepProps} />;
      case 'coverage': return <WizardCoverageStep {...stepProps} />;
      default: return <WizardDescribeStep {...stepProps} />;
    }
  };

  const next = () => {
    if (state.step === 'describe' && !state.draft.brief) autoTried.current.delete('brief');
    goTo(STEPS[index + 1]);
  };

  return createPortal(
    <div className="sw-backdrop" role="presentation">
      <div className="sw-panel" role="dialog" aria-modal="true" aria-label={t('wizard.title')}>
        <div className="sw-header">
          <div>
            <h2>{created ? (created.subject?.displayName || t('wizard.title')) : t('wizard.title')}</h2>
            <div className="sw-subtitle">
              {lastModel?.model ? t('wizard.draftedWith', { model: lastModel.model, seconds: Math.max(1, Math.round((lastModel.elapsedMs || 0) / 1000)) }) : t('wizard.subtitle')}
            </div>
          </div>
          <button type="button" className="sw-icon-btn" onClick={close} aria-label={t('common.close')} title={created ? t('wizard.closeDone') : t('wizard.closeKeep')}>✕</button>
        </div>
        <div className="sw-body">
          <nav className="sw-rail" aria-label={t('wizard.steps')}>
            <ol>
              {STEPS.map((step, i) => (
                <li key={step}>
                  <button type="button" className={`sw-rail-item${step === state.step ? ' active' : ''}${i < index ? ' done' : ''}`}
                    disabled={!reachable(i) || !!busy} onClick={() => goTo(step)} aria-current={step === state.step ? 'step' : undefined}>
                    <span className="sw-rail-num">{i + 1}</span>
                    <span>{t(`wizard.step.${step}`)}</span>
                  </button>
                </li>
              ))}
            </ol>
            {!created && (
              <button type="button" className="sw-link" onClick={() => setConfirmReset(true)} disabled={!!busy}>{t('wizard.startOver')}</button>
            )}
          </nav>
          <main className="sw-content">
            {error && <div className="sw-alert sw-alert-error" role="alert">{error}</div>}
            {warnings.length > 0 && (
              <div className="sw-alert sw-alert-warning">
                <ul>{warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>
              </div>
            )}
            {busy && busy !== 'commit' && (
              <div className="sw-busy" role="status">
                <span className="sw-spinner" aria-hidden="true" />
                <span>{t(`wizard.busy.${busy}`)}</span>
                <button type="button" className="sw-btn sw-btn-secondary sw-btn-sm" onClick={cancel}>{t('common.cancel')}</button>
              </div>
            )}
            {renderStep()}
          </main>
        </div>
        <div className="sw-footer">
          <button type="button" className="sw-btn sw-btn-secondary" onClick={() => goTo(STEPS[index - 1])}
            disabled={index === 0 || !!busy || (created && state.step === 'sources')}>{t('wizard.back')}</button>
          {DRAFT_STEPS.includes(state.step) && state.step !== 'review' && (
            <button type="button" className="sw-btn sw-btn-primary" onClick={next} disabled={!canContinue() || !!busy}
              title={state.step === 'questions' && !canContinue() ? t('wizard.questions.needMore', { count: MIN_QUESTIONS }) : undefined}>
              {t('wizard.continue')}
            </button>
          )}
          {state.step === 'review' && (
            <button type="button" className="sw-btn sw-btn-primary" onClick={commit} disabled={!!busy || !((state.draft.brief?.displayName || '').trim())}>
              {busy === 'commit' ? t('wizard.review.creating') : t('wizard.review.create')}
            </button>
          )}
          {state.step === 'sources' && (
            <button type="button" className="sw-btn sw-btn-primary" onClick={() => goTo('coverage')} disabled={!!busy}>{t('wizard.continue')}</button>
          )}
          {state.step === 'coverage' && (
            <button type="button" className="sw-btn sw-btn-primary" onClick={close} disabled={!!busy}>{t('wizard.finish')}</button>
          )}
        </div>
        {confirmReset && (
          <div className="sw-confirm" role="alertdialog" aria-label={t('wizard.startOver')}>
            <div className="sw-confirm-box">
              <p>{t('wizard.startOverConfirm')}</p>
              <div className="sw-row sw-end">
                <button type="button" className="sw-btn sw-btn-secondary" onClick={() => setConfirmReset(false)}>{t('common.cancel')}</button>
                <button type="button" className="sw-btn sw-btn-danger" onClick={startOver}>{t('wizard.startOver')}</button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>,
    document.body
  );
}

export default SubjectWizard;
