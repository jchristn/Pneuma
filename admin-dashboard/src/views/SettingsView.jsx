import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { ApiError } from '../utils/api';
import PageHeader from '../components/PageHeader';
import CopyableId from '../components/CopyableId';
import CopyButton from '../components/CopyButton';
import ErrorBanner from '../components/ErrorBanner';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import LanguageSelector from '../i18n/LanguageSelector';

// camelCase / snake_case / kebab-case -> "Title Case".
function humanize(key) {
  const spaced = String(key)
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .trim();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

// Immutable deep set by dot-path. Settings are JSON-serializable, so a JSON
// clone is both safe and portable.
function setDeep(obj, path, value) {
  const keys = path.split('.');
  const clone = JSON.parse(JSON.stringify(obj));
  let cur = clone;
  for (let i = 0; i < keys.length - 1; i += 1) cur = cur[keys[i]];
  cur[keys[keys.length - 1]] = value;
  return clone;
}

function SettingsView() {
  const { t } = useTranslation();
  const { apiClient, serverUrl, authContext, theme, toggleTheme } = useAuth();

  const [health, setHealth] = useState(null);
  const [healthError, setHealthError] = useState(false);

  const [settings, setSettings] = useState(null);
  const [meta, setMeta] = useState(null);
  const [configLoading, setConfigLoading] = useState(true);
  const [configError, setConfigError] = useState(null);
  const [configForbidden, setConfigForbidden] = useState(false);

  const [drafts, setDrafts] = useState({});       // path -> raw text while JSON is invalid
  const [jsonErrors, setJsonErrors] = useState({}); // path -> true when JSON invalid

  const [saving, setSaving] = useState(false);
  const [saveMessage, setSaveMessage] = useState('');
  const [saveError, setSaveError] = useState('');
  const [restartRequired, setRestartRequired] = useState(false);

  useEffect(() => {
    let cancelled = false;
    apiClient.health()
      .then((h) => { if (!cancelled) setHealth(h); })
      .catch(() => { if (!cancelled) setHealthError(true); });
    return () => { cancelled = true; };
  }, [apiClient]);

  useEffect(() => {
    let cancelled = false;
    setConfigLoading(true);
    apiClient.getSettings()
      .then((res) => {
        if (cancelled) return;
        const loaded = res?.settings || {};
        const m = res?.meta || {};
        const mask = m.secretMask || '********';
        // Ensure secret fields hold the mask so an unchanged value round-trips
        // and preserves the stored secret on save.
        let seeded = loaded;
        (m.secretFields || []).forEach((p) => {
          const cur = p.split('.').reduce((o, k) => (o == null ? undefined : o[k]), seeded);
          if (cur === '') seeded = setDeep(seeded, p, mask);
        });
        setSettings(seeded);
        setMeta(m);
        setConfigError(null);
        setConfigForbidden(false);
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof ApiError && err.status === 403) setConfigForbidden(true);
        else setConfigError(err?.message || 'Failed to load settings');
      })
      .finally(() => { if (!cancelled) setConfigLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient]);

  const role = authContext?.isAdmin ? t('topbar.admin')
    : authContext?.isTenantAdmin ? t('topbar.tenantAdmin') : t('topbar.user');

  const secretFields = meta?.secretFields || [];
  const secretMask = meta?.secretMask || '********';
  const sectionMeta = (key) => (meta?.sections || []).find((s) => s.key === key);

  const update = (path, value) => {
    setSettings((s) => setDeep(s, path, value));
    setSaveMessage('');
  };

  const updateJson = (path, text) => {
    setSaveMessage('');
    try {
      const parsed = JSON.parse(text);
      setSettings((s) => setDeep(s, path, parsed));
      setDrafts((d) => { const n = { ...d }; delete n[path]; return n; });
      setJsonErrors((e) => { const n = { ...e }; delete n[path]; return n; });
    } catch {
      setDrafts((d) => ({ ...d, [path]: text }));
      setJsonErrors((e) => ({ ...e, [path]: true }));
    }
  };

  // Recursively render one settings entry by JS type.
  const renderField = (path, key, value) => {
    const label = humanize(key);
    const isSecret = secretFields.includes(path);

    if (value !== null && typeof value === 'object' && !Array.isArray(value)) {
      return (
        <fieldset className="settings-subgroup" key={path}>
          <legend>{label}</legend>
          <div className="settings-field-grid">
            {Object.entries(value).map(([k, v]) => renderField(`${path}.${k}`, k, v))}
          </div>
        </fieldset>
      );
    }

    if (Array.isArray(value)) {
      const raw = drafts[path] ?? JSON.stringify(value, null, 2);
      return (
        <div className="field" style={{ gridColumn: '1 / -1' }} key={path}>
          <label htmlFor={`s-${path}`}>{label}</label>
          <textarea id={`s-${path}`} rows={Math.min(10, Math.max(3, value.length + 2))} value={raw} onChange={(e) => updateJson(path, e.target.value)} />
          {jsonErrors[path] && <div className="error-message" style={{ marginTop: '0.35rem' }}>{t('settings.invalidJson')}</div>}
        </div>
      );
    }

    if (typeof value === 'boolean') {
      return (
        <div className="checkbox-field" key={path}>
          <input id={`s-${path}`} type="checkbox" checked={value} onChange={(e) => update(path, e.target.checked)} />
          <label htmlFor={`s-${path}`} style={{ margin: 0 }}>{label}</label>
        </div>
      );
    }

    if (typeof value === 'number') {
      return (
        <div className="field" key={path}>
          <label htmlFor={`s-${path}`}>{label}</label>
          <input id={`s-${path}`} type="number" value={value} onChange={(e) => update(path, e.target.value === '' ? '' : Number(e.target.value))} />
        </div>
      );
    }

    // string or null
    const strVal = value ?? (isSecret ? secretMask : '');
    return (
      <div className="field" key={path}>
        <label htmlFor={`s-${path}`}>{label}{isSecret ? ' 🔒' : ''}</label>
        <input
          id={`s-${path}`}
          type={isSecret ? 'password' : 'text'}
          value={strVal}
          onChange={(e) => update(path, e.target.value)}
        />
      </div>
    );
  };

  const save = async () => {
    setSaving(true);
    setSaveMessage('');
    setSaveError('');
    try {
      const resp = await apiClient.updateSettings(settings);
      setSaveMessage(resp?.message || t('settings.saved'));
      setRestartRequired(!!(resp?.restartRequired));
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) setSaveError(t('settings.forbidden'));
      else setSaveError(err?.message || 'Save failed');
    } finally {
      setSaving(false);
    }
  };

  const hasJsonErrors = Object.keys(jsonErrors).length > 0;

  return (
    <div>
      <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />

      <div className="section">
        <h2>{t('settings.serverInfo')}</h2>
        <dl className="kv-grid">
          <dt>{t('settings.endpoint')}</dt>
          <dd><span className="copyable-id"><code>{serverUrl}</code><CopyButton value={serverUrl} /></span></dd>
          <dt>{t('settings.status')}</dt>
          <dd>{healthError ? <StatusPill label="Unavailable" tone="danger" />
            : health ? <StatusPill label={health.status || 'OK'} tone={toneForStatus(health.status || 'ok')} /> : '—'}</dd>
          <dt>{t('settings.serviceName')}</dt><dd>{health?.serviceName || '—'}</dd>
          <dt>{t('settings.version')}</dt><dd>{health?.version || '—'}</dd>
        </dl>
      </div>

      <div className="section">
        <h2>{t('settings.authContext')}</h2>
        <dl className="kv-grid">
          <dt>{t('settings.displayName')}</dt><dd>{authContext?.displayName || '—'}</dd>
          <dt>{t('login.email')}</dt><dd>{authContext?.email || '—'}</dd>
          <dt>{t('settings.role')}</dt><dd><span className="role-badge">{role}</span></dd>
          <dt>{t('settings.userId')}</dt><dd><CopyableId value={authContext?.userId} /></dd>
          <dt>{t('settings.tenantId')}</dt><dd><CopyableId value={authContext?.tenantId} /></dd>
        </dl>
      </div>

      <div className="section">
        <h2>{t('settings.preferences')}</h2>
        <dl className="kv-grid">
          <dt>{t('settings.theme')}</dt>
          <dd><button type="button" className="button-secondary" onClick={toggleTheme}>{theme === 'light' ? '🌙 Dark' : '☀️ Light'}</button></dd>
          <dt>{t('settings.language')}</dt>
          <dd><LanguageSelector /></dd>
        </dl>
      </div>

      <PageHeader title={t('settings.configuration')} subtitle={t('settings.configurationSubtitle')} />

      {configLoading && <div className="table-loading"><div className="loading-spinner" /> {t('common.loading')}</div>}
      {configForbidden && <ErrorBanner message={t('settings.forbidden')} />}
      {configError && <ErrorBanner message={configError} />}

      {!configLoading && !configForbidden && settings && (
        <div className="settings-form">
          {restartRequired && <div className="settings-restart-notice">⟳ {t('settings.restartNotice')}</div>}

          {Object.keys(settings).map((key) => {
            const info = sectionMeta(key);
            const label = info?.label || humanize(key);
            const value = settings[key];
            return (
              <div className="section" key={key}>
                <div className="section-title-row">
                  <h2>{label}</h2>
                  {info?.requiresRestart && (
                    <span className="requires-restart-badge">⟳ {t('settings.requiresRestart')}</span>
                  )}
                </div>
                <div className="settings-field-grid">
                  {value !== null && typeof value === 'object' && !Array.isArray(value)
                    ? Object.entries(value).map(([k, v]) => renderField(`${key}.${k}`, k, v))
                    : renderField(key, key, value)}
                </div>
              </div>
            );
          })}

          <div className="settings-save-bar">
            <button type="button" className="button-primary" onClick={save} disabled={saving || hasJsonErrors}>
              {saving ? t('common.loading') : t('settings.save')}
            </button>
            {saveMessage && <span className="settings-save-message">{saveMessage}</span>}
            {saveError && <span className="error-message">{saveError}</span>}
          </div>
        </div>
      )}
    </div>
  );
}

export default SettingsView;
