import { useEffect, useState } from 'react';
import { PROMPT_KEYS, promptValue } from './wizardDraft';

// Step 6: everything in one place, with the ontology rendered exactly as the classifier will see it. "Create subject"
// (in the footer) commits it all in one request.
function WizardReviewStep({ state, update, busy, options, runners, collections, apiClient, t }) {
  const { draft, settings } = state;
  const [rendered, setRendered] = useState(null);
  const [renderError, setRenderError] = useState('');
  const modes = options?.ontologyModes || ['Prompt'];
  const mode = settings.ontologyMode && modes.includes(settings.ontologyMode) ? settings.ontologyMode : (options?.defaultOntologyMode || 'Prompt');
  const name = (id, list) => (list.find((x) => x.id === id)?.name) || null;

  useEffect(() => {
    let cancelled = false;
    if (!draft.ontology) return undefined;
    apiClient.wizardRenderOntology(draft).then((resp) => { if (!cancelled) setRendered(resp); })
      .catch((err) => { if (!cancelled) setRenderError(err?.message || ''); });
    return () => { cancelled = true; };
  }, [apiClient, draft]);

  const brief = draft.brief || {};
  const prompts = draft.prompts || {};
  return (
    <section className="sw-step">
      <h3>{t('wizard.review.title')}</h3>
      <p className="sw-hint">{t('wizard.review.intro')}</p>

      <div className="sw-card">
        <h4>{brief.displayName}</h4>
        <div className="sw-muted">{brief.type}{brief.audience ? ` · ${brief.audience}` : ''}</div>
        {brief.description && <p>{brief.description}</p>}
        {brief.tagline && <p className="sw-muted">“{brief.tagline}”</p>}
      </div>

      <div className="sw-card">
        <h4>{t('wizard.review.questions', { count: draft.questions.length })}</h4>
        <ol className="sw-plain-list">{draft.questions.map((q, i) => <li key={i}><span className="sw-tag">{t(`wizard.kind.${q.kind}`)}</span> {q.question}</li>)}</ol>
      </div>

      <div className="sw-card">
        <h4>{t('wizard.review.ontology')}</h4>
        {rendered?.warnings?.length > 0 && <ul className="sw-muted">{rendered.warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>}
        {renderError && <div className="sw-alert sw-alert-error">{renderError}</div>}
        <pre className="sw-pre">{rendered ? rendered.rendered : t('common.loading')}</pre>
        <label className="sw-label" htmlFor="sw-ontology-mode">{t('wizard.review.ontologyMode')}</label>
        <select id="sw-ontology-mode" value={mode} onChange={(e) => update((s) => ({ ...s, settings: { ...s.settings, ontologyMode: e.target.value } }))} disabled={!!busy}>
          {modes.map((m) => <option key={m} value={m}>{t(`wizard.review.mode.${m}`)}</option>)}
        </select>
        <p className="sw-hint">{t(`wizard.review.modeHint.${mode}`)}</p>
      </div>

      <div className="sw-card">
        <h4>{t('wizard.review.prompts')}</h4>
        {PROMPT_KEYS.map((key) => (
          <div key={key} className="sw-review-prompt">
            <div className="sw-label">{t(`wizard.prompts.${key}`)}</div>
            <div className={promptValue(prompts, key) ? '' : 'sw-muted'}>{promptValue(prompts, key) || t('wizard.prompts.defaultOnly')}</div>
          </div>
        ))}
      </div>

      <div className="sw-card">
        <h4>{t('wizard.review.settings')}</h4>
        <dl className="sw-dl">
          <dt>{t('wizard.describe.model')}</dt><dd>{name(settings.modelRunnerId, runners) || t('wizard.describe.firstAvailable')}</dd>
          <dt>{t('wizard.describe.embedding')}</dt><dd>{name(settings.embeddingModel, runners) || t('wizard.describe.firstAvailable')}</dd>
          <dt>{t('wizard.describe.collection')}</dt><dd>{name(settings.collection, collections) || t('wizard.describe.defaultCollection')}</dd>
        </dl>
      </div>

    </section>
  );
}

export default WizardReviewStep;
