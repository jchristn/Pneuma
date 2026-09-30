import { useState } from 'react';

// Step 1: one required text box. Reference material and advanced settings stay collapsed unless opened.
function WizardDescribeStep({ state, update, updateDraft, options, runners, collections, busy, t }) {
  const { draft, settings } = state;
  const [showReference, setShowReference] = useState(!!(draft.groundingUrl || draft.groundingText));
  const [showAdvanced, setShowAdvanced] = useState(false);
  const completion = runners.filter((r) => r.active !== false && (r.capabilities || []).includes('Completion'));
  const embedding = runners.filter((r) => r.active !== false && (r.capabilities || []).includes('Embedding'));
  const setSetting = (key, value) => update((s) => ({ ...s, settings: { ...s.settings, [key]: value } }));

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

      <button type="button" className="sw-disclosure" onClick={() => setShowReference((v) => !v)} aria-expanded={showReference}>
        {showReference ? '▾' : '▸'} {t('wizard.describe.reference')}
      </button>
      {showReference && (
        <div className="sw-panel-inset">
          <p className="sw-hint">{t('wizard.describe.referenceHint')}</p>
          {options?.groundingUrlEnabled !== false && (
            <>
              <label className="sw-label" htmlFor="sw-grounding-url">{t('wizard.describe.referenceUrl')}</label>
              <input id="sw-grounding-url" type="url" value={draft.groundingUrl} placeholder="https://..."
                onChange={(e) => updateDraft({ groundingUrl: e.target.value })} disabled={!!busy} />
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
              <option value="">{t('wizard.describe.firstAvailable')}</option>
              {completion.map((r) => <option key={r.id} value={r.id}>{r.name}{r.defaultModel ? ` (${r.defaultModel})` : ''}</option>)}
            </select>
          </div>
          <div>
            <label className="sw-label" htmlFor="sw-embedding" title={t('wizard.describe.embeddingTip')}>{t('wizard.describe.embedding')}</label>
            <select id="sw-embedding" value={settings.embeddingModel} onChange={(e) => setSetting('embeddingModel', e.target.value)} disabled={!!busy}>
              <option value="">{t('wizard.describe.firstAvailable')}</option>
              {embedding.map((r) => <option key={r.id} value={r.id}>{r.name}{r.defaultEmbeddingModel ? ` (${r.defaultEmbeddingModel})` : ''}</option>)}
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
