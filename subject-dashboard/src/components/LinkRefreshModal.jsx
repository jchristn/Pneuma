import { useState, useEffect } from 'react';
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
function LinkRefreshModal({ isOpen, links, onSave, onClose }) {
  const { t } = useTranslation();
  const [preset, setPreset] = useState('default');
  const [custom, setCustom] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const list = links || [];
  const first = list.length === 1 ? list[0] : null;

  useEffect(() => {
    if (!isOpen) return;
    const start = presetFor(first ? first.refreshIntervalMinutes : null);
    setPreset(start);
    setCustom(start === 'custom' ? String(first.refreshIntervalMinutes) : '');
    setError('');
    setBusy(false);
  }, [isOpen, first]);

  const save = async (e) => {
    e.preventDefault();
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
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={t('links.refreshTitle')} size="small">
      <form onSubmit={save}>
        <p className="field-hint">{first ? first.url : t('links.refreshBulkSubtitle', { count: list.length })}</p>
        {error && <div className="form-error">{error}</div>}
        <div className="form-group">
          <label htmlFor="lr-preset" title={t('links.refreshTip')}>{t('links.refreshInterval')}</label>
          <select id="lr-preset" value={preset} onChange={(e) => setPreset(e.target.value)} title={t('links.refreshTip')}>
            {PRESETS.map((p) => <option key={p} value={p}>{t(`links.refreshPreset.${p}`)}</option>)}
          </select>
        </div>
        {preset === 'custom' && (
          <div className="form-group">
            <label htmlFor="lr-custom">{t('links.refreshCustomMinutes')}</label>
            <input id="lr-custom" type="number" min={MIN_MINUTES} max={MAX_MINUTES} value={custom} onChange={(e) => setCustom(e.target.value)} />
          </div>
        )}
        <p className="field-hint">{t('links.refreshHint')}</p>
        <div className="form-actions">
          <button type="button" className="btn btn-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" className="btn btn-primary" disabled={busy}>{t('common.save')}</button>
        </div>
      </form>
    </Modal>
  );
}

export default LinkRefreshModal;
