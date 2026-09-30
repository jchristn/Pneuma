import { useState } from 'react';

// A line of optional guidance for the model plus the regenerate action(s) for a step.
export function GuidanceBar({ t, busy, onRegenerate, regenerateLabel, extraAction = null, placeholder }) {
  const [guidance, setGuidance] = useState('');
  return (
    <div className="sw-guidance">
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
