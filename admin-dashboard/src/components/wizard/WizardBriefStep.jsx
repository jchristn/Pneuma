import { GuidanceBar } from './WizardParts';

const FIELDS = [
  { key: 'displayName', rows: 0, max: 200 },
  { key: 'type', rows: 0, max: 100 },
  { key: 'description', rows: 4, max: 2000 },
  { key: 'tagline', rows: 0, max: 200 },
  { key: 'audience', rows: 0, max: 300 },
  { key: 'tone', rows: 0, max: 300 }
];

// Step 2: the model's brief as a small editable form. Mostly a chance to catch a misunderstanding early.
function WizardBriefStep({ state, updateDraft, generate, busy, t }) {
  const brief = state.draft.brief;
  const set = (key, value) => updateDraft({ brief: { ...(brief || {}), [key]: value } });

  return (
    <section className="sw-step">
      <h3>{t('wizard.brief.title')}</h3>
      <p className="sw-hint">{t('wizard.brief.intro')}</p>
      {brief && (
        <div className="sw-grid">
          {FIELDS.map((f) => (
            <div key={f.key} className={f.rows ? 'sw-span-2' : ''}>
              <label className="sw-label" htmlFor={`sw-brief-${f.key}`}>{t(`wizard.brief.${f.key}`)}</label>
              {f.rows ? (
                <textarea id={`sw-brief-${f.key}`} rows={f.rows} maxLength={f.max} value={brief[f.key] || ''}
                  onChange={(e) => set(f.key, e.target.value)} disabled={!!busy} />
              ) : (
                <input id={`sw-brief-${f.key}`} type="text" maxLength={f.max} value={brief[f.key] || ''}
                  onChange={(e) => set(f.key, e.target.value)} disabled={!!busy} required={f.key === 'displayName'} />
              )}
            </div>
          ))}
        </div>
      )}
      <GuidanceBar t={t} busy={busy} onRegenerate={(guidance) => generate('brief', { guidance })}
        placeholder={t('wizard.brief.guidancePlaceholder')} />
    </section>
  );
}

export default WizardBriefStep;
