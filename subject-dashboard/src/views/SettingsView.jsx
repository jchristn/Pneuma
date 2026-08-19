import { useState, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/PageHeader';
import CopyableId from '../components/CopyableId';
import StatusPill from '../components/StatusPill';
import LanguageSelector from '../components/LanguageSelector';

function SettingsView() {
  const { t } = useTranslation();
  const { apiClient, serverUrl, principal, theme, toggleTheme } = useAuth();
  const [health, setHealth] = useState(null);
  const [healthError, setHealthError] = useState('');

  useEffect(() => {
    if (!apiClient) return;
    apiClient
      .health()
      .then(setHealth)
      .catch((err) => setHealthError(err.message));
  }, [apiClient]);

  return (
    <div>
      <PageHeader title={t('settings.title')} subtitle={t('settings.subtitle')} />

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('settings.service')}</h3>
        </div>
        <div className="card-body">
          <div className="detail-grid">
            <div className="detail-item">
              <span className="detail-label">{t('settings.endpoint')}</span>
              <span className="detail-value"><CopyableId value={serverUrl} title="Copy endpoint" /></span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.health')}</span>
              <span className="detail-value">
                {healthError ? (
                  <StatusPill status="Failed" />
                ) : health ? (
                  <StatusPill status={health.status || 'Completed'} />
                ) : (
                  '...'
                )}
              </span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.version')}</span>
              <span className="detail-value">{health?.version || 'n/a'}</span>
            </div>
            <div className="detail-item">
              <span className="detail-label">Service Name</span>
              <span className="detail-value">{health?.serviceName || 'Pneuma'}</span>
            </div>
          </div>
          {healthError && <div className="form-error" style={{ marginTop: 14 }}>{healthError}</div>}
        </div>
      </div>

      <div className="card section-band">
        <div className="card-header">
          <h3>{t('settings.authContext')}</h3>
        </div>
        <div className="card-body">
          <div className="detail-grid">
            <div className="detail-item">
              <span className="detail-label">{t('settings.principal')}</span>
              <span className="detail-value">{principal?.displayName || '—'}</span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.email')}</span>
              <span className="detail-value">{principal?.email || '—'}</span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.role')}</span>
              <span className="detail-value">
                {principal?.isAdmin ? t('topbar.admin') : principal?.isTenantAdmin ? t('topbar.tenantAdmin') : t('topbar.subject')}
              </span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.tenant')}</span>
              <span className="detail-value"><CopyableId value={principal?.tenantId} title="Copy tenant ID" /></span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.userId')}</span>
              <span className="detail-value"><CopyableId value={principal?.userId} title="Copy user ID" /></span>
            </div>
          </div>
        </div>
      </div>

      <div className="card">
        <div className="card-header">
          <h3>{t('settings.title')}</h3>
        </div>
        <div className="card-body">
          <div className="detail-grid">
            <div className="detail-item">
              <span className="detail-label">{t('settings.theme')}</span>
              <span className="detail-value">
                <button className="btn btn-secondary btn-sm" onClick={toggleTheme}>
                  {theme === 'light' ? 'Light' : 'Dark'}
                </button>
              </span>
            </div>
            <div className="detail-item">
              <span className="detail-label">{t('settings.language')}</span>
              <span className="detail-value"><LanguageSelector /></span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export default SettingsView;
