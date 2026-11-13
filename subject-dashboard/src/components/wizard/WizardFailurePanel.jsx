import { useState } from 'react';
import { ModelPicker } from './WizardParts';

// A drafting step that failed: what went wrong, and the way forward without losing the rest of the draft. Retry with
// the same model, pick another model and retry with it, go back and edit the description or links (brief only), or
// dismiss and carry on editing by hand.
function WizardFailurePanel({ t, failure, models, currentModel, modelName, onRetry, onEditInput, onDismiss }) {
  const [model, setModel] = useState(currentModel || '');
  const changed = (model || '') !== (currentModel || '');

  return (
    <div className="sw-failure" role="alert">
      <div className="sw-failure-head">
        <strong>{t('wizard.failure.title', { step: t(`wizard.step.${failure.step}`) })}</strong>
        <button type="button" className="sw-icon-btn" onClick={onDismiss} aria-label={t('wizard.failure.dismiss')} title={t('wizard.failure.dismiss')}>✕</button>
      </div>
      <p className="sw-failure-message">{failure.message}</p>
      {modelName && <p className="sw-muted">{t('wizard.failure.usedModel', { model: modelName })}</p>}
      <div className="sw-row sw-failure-actions">
        <ModelPicker t={t} id="sw-failure-model" models={models} value={model} onChange={setModel} label={t('wizard.failure.modelLabel')} wide />
        <button type="button" className="sw-btn sw-btn-primary" onClick={() => onRetry(changed ? model : undefined)}>
          {changed ? t('wizard.failure.retryWith') : t('wizard.failure.retry')}
        </button>
        {onEditInput && (
          <button type="button" className="sw-btn sw-btn-secondary" onClick={onEditInput}>{t('wizard.failure.editInput')}</button>
        )}
      </div>
      <p className="sw-hint">{t('wizard.failure.hint')}</p>
    </div>
  );
}

export default WizardFailurePanel;
