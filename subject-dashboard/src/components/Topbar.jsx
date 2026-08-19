import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import CopyButton from './CopyButton';
import LanguageSelector from './LanguageSelector';

const GITHUB_URL = 'https://github.com/jchristn/pneuma';

function Topbar() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { serverUrl, principal, theme, toggleTheme, logout } = useAuth();

  const handleLogout = async () => {
    await logout();
    navigate('/');
  };

  const roleLabel = principal?.isAdmin
    ? t('topbar.admin')
    : principal?.isTenantAdmin
    ? t('topbar.tenantAdmin')
    : t('topbar.subject');
  const roleClass = principal?.isAdmin || principal?.isTenantAdmin ? 'role-badge admin' : 'role-badge';

  return (
    <header className="topbar">
      <div className="topbar-left">
        <img src="/logo.png" alt="Pneuma" className="topbar-logo" />
        <div className="topbar-endpoint" title={serverUrl}>
          <span className="copyable-id-value">{serverUrl}</span>
          <CopyButton value={serverUrl} title="Copy endpoint" />
        </div>
      </div>

      <div className="topbar-right">
        {principal && (
          <div className="principal-chip">
            <span className="principal-name">{principal.displayName || principal.email}</span>
            <span className={roleClass}>{roleLabel}</span>
          </div>
        )}
        <LanguageSelector />
        <a
          className="btn-icon"
          href={GITHUB_URL}
          target="_blank"
          rel="noopener noreferrer"
          title={t('topbar.github')}
          aria-label={t('topbar.github')}
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor">
            <path d="M12 0C5.37 0 0 5.37 0 12c0 5.31 3.435 9.795 8.205 11.385.6.105.825-.255.825-.57 0-.285-.015-1.23-.015-2.235-3.015.555-3.795-.735-4.035-1.41-.135-.345-.72-1.41-1.23-1.695-.42-.225-1.02-.78-.015-.795.945-.015 1.62.87 1.845 1.23 1.08 1.815 2.805 1.305 3.495.99.105-.78.42-1.305.765-1.605-2.67-.3-5.46-1.335-5.46-5.925 0-1.305.465-2.385 1.23-3.225-.12-.3-.54-1.53.12-3.18 0 0 1.005-.315 3.3 1.23.96-.27 1.98-.405 3-.405s2.04.135 3 .405c2.295-1.56 3.3-1.23 3.3-1.23.66 1.65.24 2.88.12 3.18.765.84 1.23 1.905 1.23 3.225 0 4.605-2.805 5.625-5.475 5.925.435.375.81 1.095.81 2.22 0 1.605-.015 2.895-.015 3.3 0 .315.225.69.825.57A12.02 12.02 0 0 0 24 12c0-6.63-5.37-12-12-12z" />
          </svg>
        </a>
        <button className="btn-icon" onClick={toggleTheme} title={t('topbar.theme')} aria-label={t('topbar.theme')}>
          {theme === 'light' ? (
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
            </svg>
          ) : (
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="5" />
              <line x1="12" y1="1" x2="12" y2="3" />
              <line x1="12" y1="21" x2="12" y2="23" />
              <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
              <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
              <line x1="1" y1="12" x2="3" y2="12" />
              <line x1="21" y1="12" x2="23" y2="12" />
              <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
              <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
            </svg>
          )}
        </button>
        <button className="btn btn-secondary btn-sm" onClick={handleLogout} title={t('topbar.logout')}>
          {t('topbar.logout')}
        </button>
      </div>
    </header>
  );
}

export default Topbar;
