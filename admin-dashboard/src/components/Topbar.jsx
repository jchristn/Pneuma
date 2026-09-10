import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import LanguageSelector from '../i18n/LanguageSelector';
import Icon from './Icon';
import CopyButton from './CopyButton';
import logo from '../assets/logo.png';

const GITHUB_URL = 'https://github.com/jchristn/pneuma';
const DISCORD_URL = 'https://discord.gg/tRAN8HgvK5';

function Topbar({ onToggleSidebar }) {
  const { t } = useTranslation();
  const { serverUrl, authContext, theme, toggleTheme, logout } = useAuth();

  const roleLabel = authContext?.isAdmin ? t('topbar.admin')
    : authContext?.isTenantAdmin ? t('topbar.tenantAdmin')
      : t('topbar.user');
  const roleClass = authContext?.isAdmin ? '' : authContext?.isTenantAdmin ? 'tenant' : 'user';

  return (
    <header className="topbar">
      <div className="topbar-left">
        <button type="button" className="icon-button hamburger" onClick={onToggleSidebar} aria-label="Menu" title="Show or hide the navigation sidebar.">☰</button>
        <img className="topbar-logo" src={logo} alt="Pneuma" />
        <span className="chip" title={`API server this dashboard is talking to: ${serverUrl}`}>
          <span>{t('topbar.endpoint')}</span>
          <span className="chip-value">{serverUrl}</span>
        </span>
        <CopyButton value={serverUrl} label={null} title="Copy the API server URL to the clipboard." />
      </div>
      <div className="topbar-right">
        <span className={`role-badge ${roleClass}`} title="Your access level in this session — it determines which actions you can perform.">{roleLabel}</span>
        {authContext?.displayName && (
          <span className="chip" title="The account you are signed in as."><span className="chip-value">{authContext.displayName}</span></span>
        )}
        <LanguageSelector />
        <button type="button" className="icon-button" onClick={toggleTheme} aria-label={t('topbar.theme')} title="Switch between light and dark appearance.">
          {theme === 'light' ? '🌙' : '☀️'}
        </button>
        <a className="icon-button" href={GITHUB_URL} target="_blank" rel="noopener noreferrer" aria-label={t('topbar.github')} title="Open the Pneuma source repository on GitHub (new tab).">
          <Icon name="github" />
        </a>
        <a className="icon-button" href={DISCORD_URL} target="_blank" rel="noopener noreferrer" aria-label={t('topbar.discord', 'Discord')} title="Join the Pneuma community on Discord (new tab).">
          <Icon name="discord" />
        </a>
        <button type="button" className="icon-button" onClick={logout} aria-label={t('topbar.logout')} title="Sign out and return to the login screen.">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
            <path d="M16 17l5-5-5-5M21 12H9" />
          </svg>
        </button>
      </div>
    </header>
  );
}

export default Topbar;
