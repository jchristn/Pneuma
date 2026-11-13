import { useEffect, useState } from 'react';
import { formatDuration } from './wizardDraft';
import { ModelPicker } from './WizardParts';

const PHASES = ['reading', 'waiting', 'writing', 'checking'];

// What a drafting step is doing while the model works: a live elapsed timer, the phase (with the phases so far),
// how much the model has written, the attempt, and how long this step took last time.
function WizardProgressPanel({ t, step, progress, lastMs, modelName, onCancel, models = null, currentModel = '', onSwitchModel = null }) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const startedAt = progress?.startedAt || now;
  const elapsed = Math.max(0, now - startedAt);
  const phase = progress?.phase || 'waiting';
  const characters = progress?.characters || 0;
  const attempt = progress?.attempt || 1;
  const phaseIndex = PHASES.indexOf(phase === 'retrying' ? 'waiting' : phase);

  return (
    <div className="sw-progress" role="status" aria-live="polite">
      <div className="sw-row sw-between">
        <div className="sw-row">
          <span className="sw-spinner" aria-hidden="true" />
          <strong>{t(`wizard.busy.${step}`)}</strong>
        </div>
        <div className="sw-row">
          <span className="sw-progress-time" title={t('wizard.progress.elapsedTip')}>{formatDuration(elapsed)}</span>
          <button type="button" className="sw-btn sw-btn-secondary sw-btn-sm" onClick={onCancel}>{t('common.cancel')}</button>
        </div>
      </div>
      <ol className="sw-phases" aria-label={t('wizard.progress.phases')}>
        {PHASES.filter((p) => p !== 'reading' || step === 'brief').map((p) => {
          const index = PHASES.indexOf(p);
          const state = index < phaseIndex ? 'done' : (index === phaseIndex ? 'active' : '');
          return <li key={p} className={state}>{t(`wizard.progress.phase.${p}`)}</li>;
        })}
      </ol>
      <div className="sw-progress-detail">
        {phase === 'writing' && characters > 0 && (
          <span>{t('wizard.progress.written', { chars: characters.toLocaleString(), tokens: Math.max(1, Math.round(characters / 4)).toLocaleString() })}</span>
        )}
        {phase === 'waiting' && <span>{t('wizard.progress.waitingHint')}</span>}
        {phase === 'reading' && progress?.message && <span>{progress.message}</span>}
        {(phase === 'retrying' || attempt > 1) && (
          <span className="sw-progress-warn">{t('wizard.progress.retry', { n: attempt })}{progress?.message ? ` ${progress.message}` : ''}</span>
        )}
      </div>
      <div className="sw-row sw-between sw-progress-foot">
        <div className="sw-muted">
          {modelName ? t('wizard.progress.model', { model: modelName }) : null}
          {lastMs ? ` · ${t('wizard.progress.lastTime', { time: formatDuration(lastMs) })}` : ''}
        </div>
        {onSwitchModel && models && models.length > 1 && (
          <div className="sw-row" title={t('wizard.progress.switchTip')}>
            <label className="sw-muted" htmlFor="sw-switch-model">{t('wizard.progress.switch')}</label>
            {/* Choosing a model stops this attempt and drafts the step again with that model. */}
            <ModelPicker t={t} id="sw-switch-model" models={models} value={currentModel} onChange={onSwitchModel} label={t('wizard.progress.switch')} />
          </div>
        )}
      </div>
    </div>
  );
}

export default WizardProgressPanel;
