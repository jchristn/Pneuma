import { useState } from 'react';
import { runnerLabel } from './wizardDraft';

// Pick a completion model. An empty value means the wizard's default (the drafting model, else the first available).
export function ModelPicker({ t, models, value, onChange, disabled, id, defaultLabel }) {
  return (
    <select id={id} className="sw-model" value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled}
      aria-label={t('wizard.modelLabel')} title={t('wizard.modelTip')}>
      <option value="">{defaultLabel || t('wizard.describe.firstAvailable')}</option>
      {(models || []).map((r) => <option key={r.id} value={r.id}>{runnerLabel(r)}</option>)}
    </select>
  );
}

// A line of optional guidance for the model, the model to draft with, and the regenerate action(s) for a step.
export function GuidanceBar({ t, busy, onRegenerate, regenerateLabel, extraAction = null, placeholder, models = null, model = '', onModelChange = null, defaultModelLabel = null }) {
  const [guidance, setGuidance] = useState('');
  return (
    <div className="sw-guidance">
      {models && onModelChange && (
        <ModelPicker t={t} models={models} value={model} onChange={onModelChange} disabled={!!busy} defaultLabel={defaultModelLabel} />
      )}
      <input
        type="text"
        value={guidance}
        maxLength={1000}
        onChange={(e) => setGuidance(e.target.value)}
        placeholder={placeholder || t('wizard.guidancePlaceholder')}
        aria-label={t('wizard.guidanceLabel')}
        title={t('wizard.guidanceTip')}
        disabled={!!busy}
      />
      <button type="button" className="sw-btn sw-btn-secondary" onClick={() => onRegenerate(guidance)} disabled={!!busy} title={t('wizard.regenerateTip')}>
        {regenerateLabel || t('wizard.regenerate')}
      </button>
      {extraAction && (
        <button type="button" className="sw-btn sw-btn-secondary" onClick={() => extraAction.onClick(guidance)} disabled={!!busy} title={extraAction.tip}>
          {extraAction.label}
        </button>
      )}
    </div>
  );
}

// A lock toggle: locked items are kept verbatim when the rest of the step is regenerated.
export function LockButton({ t, locked, onToggle, disabled }) {
  return (
    <button
      type="button"
      className={`sw-lock${locked ? ' locked' : ''}`}
      onClick={onToggle}
      disabled={disabled}
      aria-pressed={locked}
      title={locked ? t('wizard.unlockTip') : t('wizard.lockTip')}
    >
      {locked ? t('wizard.locked') : t('wizard.lock')}
    </button>
  );
}
