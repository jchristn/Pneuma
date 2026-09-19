import { useTranslation } from 'react-i18next';
import { NAV_GROUPS } from '../config/nav';
import { useAuth } from '../context/AuthContext';
import Icon from './Icon';

function Sidebar({ activeSection, onNavigate, open }) {
  const { t } = useTranslation();
  const { authContext } = useAuth();
  const isAdmin = !!authContext?.isAdmin;
  // Admin-only nav items (e.g. system-wide ingestion tuning) are hidden from non-system-admins; the
  // backend enforces the same restriction with a 403.
  const visibleGroups = NAV_GROUPS
    .map((group) => ({ ...group, items: group.items.filter((item) => !item.adminOnly || isAdmin) }))
    .filter((group) => group.items.length > 0);
  return (
    <aside className={`sidebar ${open ? 'open' : ''}`}>
      <nav className="sidebar-nav" aria-label="Primary">
        {visibleGroups.map((group) => (
          <div className="nav-group" key={group.labelKey}>
            <div className="nav-group-label">{t(group.labelKey)}</div>
            {group.items.map((item) => (
              <button
                key={item.section}
                type="button"
                className={`nav-item ${activeSection === item.section ? 'active' : ''}`}
                onClick={() => onNavigate(item.section)}
                aria-current={activeSection === item.section ? 'page' : undefined}
                title={item.tip}
              >
                <span className="nav-icon"><Icon name={item.icon} /></span>
                <span>{t(item.labelKey)}</span>
              </button>
            ))}
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        Pneuma Admin · v1.0.0
      </div>
    </aside>
  );
}

export default Sidebar;
