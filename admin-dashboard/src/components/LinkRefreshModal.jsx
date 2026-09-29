import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';

// Refresh interval presets in minutes. 'default' follows the subject's default and 'custom' takes a typed value.
const PRESETS = ['default', '0', '60', '1440', '10080', 'custom'];
const MIN_MINUTES = 60;
const MAX_MINUTES = 525600;

// Only URL links that no crawl plan manages are refreshed on a schedule (crawled links follow their plan).
export function isRefreshable(link) {
  return !!link && String(link.sourceKind || 'Url') === 'Url' && !link.crawlPlanId;
}

// Pick the preset matching a link's stored interval (null follows the subject default).
function presetFor(minutes) {
  if (minutes === null || minutes === undefined) return 'default';
  const key = String(minutes);
  return PRESETS.includes(key) ? key : 'custom';
}

// Set the refresh interval of one link (links has one entry) or several (bulk). onSave receives the request
// body: { useSubjectDefault: true } or { refreshIntervalMinutes: n }.
function LinkRefreshModal({ links, onSave, onClose }) {
  const { t } = useTranslation();
  const first = links.length === 1 ? links[0] : null;
  const [preset, setPreset] = useState(presetFor(first ? first.refreshIntervalMinutes : null));
  const [custom, setCustom] = useState(first && presetFor(first.refreshIntervalMinutes) === 'custom' ? String(first.refreshIntervalMinutes) : '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const label = (p) => t(`links.refreshPreset.${p}`);

  const save = async () => {
    let body;
    if (preset === 'default') body = { useSubjectDefault: true };
    else if (preset === 'custom') {
      const n = Number(custom);
      if (!Number.isInteger(n) || n < MIN_MINUTES || n > MAX_MINUTES) {
        setError(t('links.refreshRange', { min: MIN_MINUTES, max: MAX_MINUTES }));
        return;
      }
      body = { refreshIntervalMinutes: n };
    } else body = { refreshIntervalMinutes: Number(preset) };
    setBusy(true);
    setError('');
    try {
      await onSave(body);
    } catch (err) {
      setError(err?.message || t('links.refreshSaveFailed'));
      setBusy(false);
    }
  };

  return (
    <Modal
      title={t('links.refreshTitle')}
      subtitle={first ? first.url : t('links.refreshBulkSubtitle', { count: links.length })}
      size="sm"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className="button-primary" onClick={save} disabled={busy}>{t('common.save')}</button>
        </>
      )}
    >
      {error && <div className="form-error">{error}</div>}
      <div className="field">
        <label htmlFor="link-refresh-preset" title={t('links.refreshTip')}>{t('links.refreshInterval')}</label>
        <select id="link-refresh-preset" value={preset} onChange={(e) => setPreset(e.target.value)} title={t('links.refreshTip')}>
          {PRESETS.map((p) => <option key={p} value={p}>{label(p)}</option>)}
        </select>
      </div>
      {preset === 'custom' && (
        <div className="field">
          <label htmlFor="link-refresh-custom">{t('links.refreshCustomMinutes')}</label>
          <input id="link-refresh-custom" type="number" min={MIN_MINUTES} max={MAX_MINUTES} value={custom} onChange={(e) => setCustom(e.target.value)} />
        </div>
      )}
      <p className="field-hint">{t('links.refreshHint')}</p>
    </Modal>
  );
}

export default LinkRefreshModal;
