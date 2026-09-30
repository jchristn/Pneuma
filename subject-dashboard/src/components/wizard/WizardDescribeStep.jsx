import { useState } from 'react';
import { ModelPicker } from './WizardParts';
import { hasCapability, runnerLabel } from './wizardDraft';

// Step 1: one required text box. Reference material and advanced settings stay collapsed unless opened.
function WizardDescribeStep({ state, update, updateDraft, options, runners, collections, busy, t, completionRunners }) {
  const { draft, settings } = state;
  const urls = (draft.groundingUrls && draft.groundingUrls.length > 0) ? draft.groundingUrls : [''];
  const maxUrls = options?.maxGroundingUrls || 5;
  const [showReference, setShowReference] = useState(!!(urls.some((u) => u && u.trim()) || draft.groundingText));
  const [showAdvanced, setShowAdvanced] = useState(false);
  const completion = runners.filter((r) => hasCapability(r, 'Completion'));
  const embedding = runners.filter((r) => hasCapability(r, 'Embedding'));
  const setSetting = (key, value) => update((s) => ({ ...s, settings: { ...s.settings, [key]: value } }));
  const setUrl = (i, value) => updateDraft({ groundingUrls: urls.map((u, j) => (j === i ? value : u)) });
  const removeUrl = (i) => updateDraft({ groundingUrls: urls.length > 1 ? urls.filter((_, j) => j !== i) : [''] });
  const addUrl = () => { if (urls.length < maxUrls) updateDraft({ groundingUrls: [...urls, ''] }); };

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
        <ModelPicker t={t} id="sw-draft-model" models={completionRunners} value={settings.draftModel} onChange={(id) => setSetting('draftModel', id)} disabled={!!busy} />
      </div>
      <p className="sw-hint">{t('wizard.describe.draftModelHint')}</p>

      <button type="button" className="sw-disclosure" onClick={() => setShowReference((v) => !v)} aria-expanded={showReference}>
        {showReference ? '▾' : '▸'} {t('wizard.describe.reference')}
      </button>
      {showReference && (
        <div className="sw-panel-inset">
          <p className="sw-hint">{t('wizard.describe.referenceHint')}</p>
          {options?.groundingUrlEnabled !== false && (
            <>
              <label className="sw-label" htmlFor="sw-grounding-url-0">{t('wizard.describe.referenceUrls', { count: maxUrls })}</label>
              {urls.map((u, i) => (
                <div key={i} className="sw-row sw-url-row">
                  <input id={`sw-grounding-url-${i}`} type="url" value={u} placeholder="https://..."
                    onChange={(e) => setUrl(i, e.target.value)} disabled={!!busy} aria-label={t('wizard.describe.referenceUrlN', { n: i + 1 })} />
                  <button type="button" className="sw-icon-btn" onClick={() => removeUrl(i)} disabled={!!busy || (urls.length === 1 && !u)}
                    aria-label={t('wizard.remove')}>✕</button>
                </div>
              ))}
              <button type="button" className="sw-btn sw-btn-secondary sw-btn-sm" onClick={addUrl} disabled={!!busy || urls.length >= maxUrls}>
                {t('wizard.describe.addUrl')}
              </button>
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
