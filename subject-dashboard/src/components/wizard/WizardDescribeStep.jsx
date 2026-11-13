import { useState } from 'react';
import { ModelPicker } from './WizardParts';
import { hasCapability, runnerLabel, parseLinks } from './wizardDraft';

// Step 1: one required text box. Reference material and advanced settings stay collapsed unless opened.
function WizardDescribeStep({ state, update, updateDraft, options, runners, runnersLoaded, collections, busy, t, completionRunners }) {
  const { draft, settings } = state;
  const linksText = state.linksText || '';
  const parsed = parseLinks(linksText);
  const maxUrls = options?.maxGroundingUrls || 10;
  const [showReference, setShowReference] = useState(!!(linksText.trim() || draft.groundingText));
  const [showAdvanced, setShowAdvanced] = useState(false);
  const completion = runners.filter((r) => hasCapability(r, 'Completion'));
  const embedding = runners.filter((r) => hasCapability(r, 'Embedding'));
  const setSetting = (key, value) => update((s) => ({ ...s, settings: { ...s.settings, [key]: value } }));
  // The links box takes a pasted batch; the draft keeps the parsed links (at most maxUrls are sent).
  const setLinks = (text) => update((s) => ({ ...s, linksText: text, draft: { ...s.draft, groundingUrls: parseLinks(text).urls } }));

  return (
    <section className="sw-step">
      <h3>{t('wizard.describe.title')}</h3>
      <p className="sw-hint">{t('wizard.describe.intro')}</p>
      {options && options.hasCompletionModel === false && (
        <div className="sw-alert sw-alert-warning">{t('wizard.describe.noModel')}</div>
      )}
      <label className="sw-label" htmlFor="sw-description">{t('wizard.describe.label')}</label>
      <textarea
        id="sw-description"
        rows={4}
        maxLength={4000}
        value={draft.description}
        onChange={(e) => updateDraft({ description: e.target.value })}
        placeholder={t('wizard.describe.placeholder')}
        disabled={!!busy}
        autoFocus
      />

      <label className="sw-label" htmlFor="sw-draft-model" title={t('wizard.describe.draftModelTip')}>{t('wizard.describe.draftModel')}</label>
      <div className="sw-row">
        <ModelPicker t={t} id="sw-draft-model" models={completionRunners} value={settings.draftModel} onChange={(id) => setSetting('draftModel', id)} disabled={!!busy} wide />
      </div>
      {runnersLoaded && completionRunners.length === 0 && options?.hasCompletionModel !== false && (
        <p className="sw-hint sw-warn-text">{t('wizard.describe.noModelList')}</p>
      )}
      <p className="sw-hint">{t('wizard.describe.draftModelHint')}</p>

      <button type="button" className="sw-disclosure" onClick={() => setShowReference((v) => !v)} aria-expanded={showReference}>
        {showReference ? '▾' : '▸'} {t('wizard.describe.reference')}
      </button>
      {showReference && (
        <div className="sw-panel-inset">
          <p className="sw-hint">{t('wizard.describe.referenceHint')}</p>
          {options?.groundingUrlEnabled !== false && (
            <>
              <label className="sw-label" htmlFor="sw-grounding-urls">{t('wizard.describe.referenceUrls', { count: maxUrls })}</label>
              <textarea id="sw-grounding-urls" className="sw-links" rows={5} value={linksText} spellCheck={false}
                placeholder={'https://example.com/about\nhttps://en.wikipedia.org/wiki/...'}
                onChange={(e) => setLinks(e.target.value)} disabled={!!busy} />
              <div className="sw-links-summary" aria-live="polite">
                {parsed.urls.length > 0 && <span>{t('wizard.describe.linksFound', { count: parsed.urls.length })}</span>}
                {parsed.urls.length > maxUrls && <span className="sw-warn-text">{t('wizard.describe.linksOverMax', { max: maxUrls })}</span>}
                {parsed.invalid.length > 0 && (
                  <span className="sw-warn-text">{t('wizard.describe.linksInvalid', { list: parsed.invalid.slice(0, 5).join(', ') + (parsed.invalid.length > 5 ? ', ...' : '') })}</span>
                )}
                {linksText.trim() && (
                  <button type="button" className="sw-link" onClick={() => setLinks('')} disabled={!!busy}>{t('wizard.describe.linksClear')}</button>
                )}
              </div>
            </>
          )}
          <label className="sw-label" htmlFor="sw-grounding-text">{t('wizard.describe.referenceText')}</label>
          <textarea id="sw-grounding-text" rows={4} value={draft.groundingText}
            onChange={(e) => updateDraft({ groundingText: e.target.value })} disabled={!!busy} />
        </div>
      )}

      <button type="button" className="sw-disclosure" onClick={() => setShowAdvanced((v) => !v)} aria-expanded={showAdvanced}>
        {showAdvanced ? '▾' : '▸'} {t('wizard.describe.advanced')}
      </button>
      {showAdvanced && (
        <div className="sw-panel-inset sw-grid">
          <div>
            <label className="sw-label" htmlFor="sw-model" title={t('wizard.describe.modelTip')}>{t('wizard.describe.model')}</label>
            <select id="sw-model" value={settings.modelRunnerId} onChange={(e) => setSetting('modelRunnerId', e.target.value)} disabled={!!busy}>
              <option value="">{t('wizard.describe.sameAsDrafting')}</option>
              {completion.map((r) => <option key={r.id} value={r.id}>{runnerLabel(r)}</option>)}
            </select>
          </div>
          <div>
            <label className="sw-label" htmlFor="sw-embedding" title={t('wizard.describe.embeddingTip')}>{t('wizard.describe.embedding')}</label>
            <select id="sw-embedding" value={settings.embeddingModel} onChange={(e) => setSetting('embeddingModel', e.target.value)} disabled={!!busy}>
              <option value="">{t('wizard.describe.firstAvailable')}</option>
              {embedding.map((r) => <option key={r.id} value={r.id}>{runnerLabel(r)}</option>)}
            </select>
          </div>
          <div>
            <label className="sw-label" htmlFor="sw-collection" title={t('wizard.describe.collectionTip')}>{t('wizard.describe.collection')}</label>
            <select id="sw-collection" value={settings.collection} onChange={(e) => setSetting('collection', e.target.value)} disabled={!!busy}>
              <option value="">{t('wizard.describe.defaultCollection')}</option>
              {collections.map((c) => <option key={c.id} value={c.id}>{c.name || c.id}{c.dimensionality ? ` (${c.dimensionality})` : ''}</option>)}
            </select>
          </div>
        </div>
      )}
    </section>
  );
}

export default WizardDescribeStep;
