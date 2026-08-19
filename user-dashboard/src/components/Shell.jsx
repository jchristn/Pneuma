import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext.jsx';
import ThemeToggle from './ThemeToggle.jsx';
import LanguageSelector from './LanguageSelector.jsx';
import Icon from './Icon.jsx';

export default function Shell() {
  const { t } = useTranslation();
  const { logout, user } = useAuth();
  const navigate = useNavigate();

  function handleLogout() {
    logout();
    navigate('/login', { replace: true });
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header-inner">
          <button
            type="button"
            className="brand"
            onClick={() => navigate('/')}
            aria-label={`${t('app.name')} — ${t('app.explore')}`}
          >
            <img src="/logo.png" alt="Pneuma" className="brand-logo" />
          </button>

          <nav className="app-nav" aria-label="Primary">
            <NavLink to="/" end className={({ isActive }) => `nav-link${isActive ? ' is-active' : ''}`}>
              <Icon name="search" size={16} />
              <span>{t('nav.search')}</span>
            </NavLink>
            <NavLink to="/ask" className={({ isActive }) => `nav-link${isActive ? ' is-active' : ''}`}>
              <Icon name="ask" size={16} />
              <span>{t('nav.ask')}</span>
            </NavLink>
          </nav>

          <div className="app-header-actions">
            {user?.displayName ? <span className="user-chip" title={user.email}>{user.displayName}</span> : null}
            <LanguageSelector />
            <ThemeToggle />
            <button type="button" className="icon-button" onClick={handleLogout} aria-label={t('nav.logout', 'Log out')} title={t('nav.logout', 'Log out')}>
              <Icon name="logout" size={18} />
            </button>
          </div>
        </div>
      </header>

      <main className="app-main">
        <Outlet />
      </main>
    </div>
  );
}
