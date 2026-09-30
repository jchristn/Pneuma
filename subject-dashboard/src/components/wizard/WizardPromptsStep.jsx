import { useEffect, useState } from 'react';
import { PROMPT_KEYS, PROMPT_GLOBAL_KEYS, listOf, promptValue } from './wizardDraft';
import { GuidanceBar, LockButton } from './WizardParts';

// Step 5: the subject's additions to four prompts, each shown with the global prompt it is appended to. Editing an
// addition locks it; "use the default" leaves the global prompt alone for that one.
function WizardPromptsStep({ state, updateDraft, generate, busy, apiClient, t, completionRunners, stepModelFor, setStepModel, defaultModelLabel }) {
  const prompts = state.draft.prompts || { locked: [] };
  const locked = new Set(prompts.locked || []);
  const [globals, setGlobals] = useState({});
  const [open, setOpen] = useState({});

  useEffect(() => {
    let cancelled = false;
    apiClient.wizardPrompts().then((resp) => {
      if (cancelled) return;
      const map = {};
      listOf(resp).forEach((p) => { if (p && p.key) map[p.key] = p.content; });
      setGlobals(map);
    }).catch(() => {});
    return () => { cancelled = true; };
  }, [apiClient]);

  const set = (key, value, lock = true) => {
    const nextLocked = new Set(locked);
    if (lock) nextLocked.add(key); else nextLocked.delete(key);
    updateDraft({ prompts: { ...prompts, [key]: value, locked: Array.from(nextLocked) } });
  };
  const toggleLock = (key) => set(key, promptValue(prompts, key), !locked.has(key));

  return (
    <section className="sw-step">
      <h3>{t('wizard.prompts.title')}</h3>
      <p className="sw-hint">{t('wizard.prompts.intro')}</p>
      {PROMPT_KEYS.map((key) => {
        const value = promptValue(prompts, key);
        const useDefault = locked.has(key) && !value.trim();
        const globalText = globals[PROMPT_GLOBAL_KEYS[key]];
        return (
          <div key={key} className={`sw-card${locked.has(key) ? ' locked' : ''}`}>
            <div className="sw-row sw-between">
              <label className="sw-label" htmlFor={`sw-prompt-${key}`}>{t(`wizard.prompts.${key}`)}</label>
              <div className="sw-row">
                <label className="sw-check" title={t('wizard.prompts.useDefaultTip')}>
                  <input type="checkbox" checked={useDefault} onChange={(e) => set(key, e.target.checked ? '' : value, e.target.checked)} disabled={!!busy} />
                  {t('wizard.prompts.useDefault')}
                </label>
                <LockButton t={t} locked={locked.has(key)} onToggle={() => toggleLock(key)} disabled={!!busy} />
              </div>
            </div>
            <p className="sw-hint">{t(`wizard.prompts.${key}Hint`)}</p>
            <textarea id={`sw-prompt-${key}`} rows={4} value={value} onChange={(e) => set(key, e.target.value)} disabled={!!busy || useDefault}
              placeholder={useDefault ? t('wizard.prompts.defaultOnly') : ''} />
            {globalText && (
              <>
                <button type="button" className="sw-disclosure" onClick={() => setOpen((o) => ({ ...o, [key]: !o[key] }))} aria-expanded={!!open[key]}>
                  {open[key] ? '▾' : '▸'} {t('wizard.prompts.showGlobal')}
                </button>
                {open[key] && <pre className="sw-pre">{globalText}</pre>}
              </>
            )}
          </div>
        );
      })}
      <GuidanceBar t={t} busy={busy} models={completionRunners} model={stepModelFor('prompts')} onModelChange={(id) => setStepModel('prompts', id)} defaultModelLabel={defaultModelLabel} regenerateLabel={t('wizard.regenerateRest')} onRegenerate={(guidance) => generate('prompts', { guidance })}
        placeholder={t('wizard.prompts.guidancePlaceholder')} />
    </section>
  );
}

export default WizardPromptsStep;
