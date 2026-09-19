import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { ApiError } from '../utils/api';
import { INGESTION_TUNABLE_GROUPS, INGESTION_TUNABLE_KEYS } from '../config/ingestionTunables';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';

/**
 * Processing — system-default ingestion concurrency. Loads GET /v1.0/settings/ingestion, renders the
 * tunable knobs grouped by role (per-stage caps, job pool & summarization, timeouts), and saves via PUT.
 * Changes are applied live (no restart). Admin-only; non-admins receive a 403 handled here.
 */
function ProcessingView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();

  const [values, setValues] = useState(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(null);
  const [forbidden, setForbidden] = useState(false);

  const [saving, setSaving] = useState(false);
  const [saveMessage, setSaveMessage] = useState('');
  const [saveError, setSaveError] = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    apiClient.getIngestionSettings()
      .then((res) => {
        if (cancelled) return;
        setValues(res || {});
        setLoadError(null);
        setForbidden(false);
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && err.status === 403) setForbidden(true);
        else setLoadError(err?.message || t('processing.loadError'));
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient, t]);

  const update = (key, raw) => {
    setSaveMessage('');
    setValues((v) => ({ ...v, [key]: raw === '' ? '' : Number(raw) }));
  };

  const save = async () => {
    setSaving(true);
    setSaveMessage('');
    setSaveError('');
    try {
      // Coerce every tunable to an integer; blanks fall back to 0 rather than being dropped.
      const body = {};
      INGESTION_TUNABLE_KEYS.forEach((key) => {
        const val = values[key];
        body[key] = val === '' || val === null || val === undefined ? 0 : Number(val);
      });
      const resp = await apiClient.updateIngestionSettings(body);
      if (resp && typeof resp === 'object') setValues((v) => ({ ...v, ...resp }));
      setSaveMessage(t('processing.saved'));
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) setSaveError(t('processing.forbidden'));
      else setSaveError(err?.message || t('processing.saveError'));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div>
      <PageHeader title={t('processing.title')} subtitle={t('processing.subtitle')} />

      {loading && <div className="table-loading"><div className="loading-spinner" /> {t('common.loading')}</div>}
      {forbidden && <ErrorBanner message={t('processing.forbidden')} />}
      {loadError && <ErrorBanner message={loadError} />}

      {!loading && !forbidden && values && (
        <div className="settings-form">
          <div className="settings-restart-notice">✓ {t('processing.liveNotice')}</div>

          {INGESTION_TUNABLE_GROUPS.map((group) => (
            <div className="section" key={group.key}>
              <div className="section-title-row">
                <h2>{t(`processing.groups.${group.key}`)}</h2>
              </div>
              <p className="field-hint" style={{ marginTop: 0 }}>{t(`processing.groupHints.${group.key}`)}</p>
              <div className="settings-field-grid">
                {group.fields.map((key) => {
                  const tip = t(`processing.tips.${key}`);
                  return (
                    <div className="field" key={key}>
                      <label htmlFor={`ing-${key}`} className="has-tip" title={tip}>{t(`processing.fields.${key}`)}</label>
                      <input
                        id={`ing-${key}`}
                        type="number"
                        min="0"
                        value={values[key] ?? ''}
                        onChange={(e) => update(key, e.target.value)}
                        title={tip}
                      />
                    </div>
                  );
                })}
              </div>
            </div>
          ))}

          <div className="settings-save-bar">
            <button type="button" className="button-primary" onClick={save} disabled={saving}>
              {saving ? t('common.loading') : t('processing.save')}
            </button>
            {saveMessage && <span className="settings-save-message">{saveMessage}</span>}
            {saveError && <span className="error-message">{saveError}</span>}
          </div>
        </div>
      )}
    </div>
  );
}

export default ProcessingView;
